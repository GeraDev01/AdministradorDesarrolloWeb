using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Services;

/// <summary>Qué hacer si la base destino ya tiene filas.</summary>
public enum MigrationMode
{
    /// <summary>No tocar nada y reportar qué tablas vienen con datos.</summary>
    AbortarSiHayDatos,
    /// <summary>Borrar todo lo del destino y volver a cargar desde SQLite.</summary>
    ReemplazarDestino
}

public sealed record TableResult(string Table, int Rows, string? Note = null);

public sealed record MigrationReport(
    bool Success,
    IReadOnlyList<TableResult> Tables,
    int TotalRows,
    string? Error = null,
    string? FailedTable = null,
    IReadOnlyList<string>? NonEmptyTables = null);

/// <summary>
/// Copia la base SQLite local a SQL Server.
///
/// Decisiones de diseño, todas consecuencia de fallas reales de la versión anterior:
///
///  · <b>SqlBulkCopy con KeepIdentity</b> en vez de EF + <c>SET IDENTITY_INSERT</c>. Aquel ajuste
///    vive en la sesión de SQL Server, y EF abría y cerraba conexión en cada comando, así que se
///    perdía antes del INSERT y la migración moría en la primera tabla con el error 544.
///
///  · <b>La lista de tablas sale del modelo de EF</b>, no de una lista escrita a mano. La versión
///    anterior copiaba 19 de 37 tablas y aun así reportaba éxito.
///
///  · <b>Una sola transacción</b> para toda la carga: o queda completa o no queda nada. Antes un
///    fallo a media copia dejaba el destino a medio poblar y sin forma de reintentar.
///
///  · <b>Sin navegaciones cargadas</b>: se lee por ADO.NET plano, columna por columna. El
///    <c>Include(u =&gt; u.Developer)</c> anterior arrastraba Developers al lote de Users y
///    reventaba por PK duplicada o IDENTITY_INSERT sobre la tabla equivocada.
///
///  · <b>Sin UI</b>: el servicio no abre MessageBox. Devuelve un <see cref="MigrationReport"/> y
///    quien llama decide qué mostrar. Antes un diálogo dentro del servicio abortaba con un
///    <c>return</c> que el llamador leía como éxito.
///
///  · Las restricciones de FK <b>no se verifican durante la carga</b> (comportamiento por omisión
///    de SqlBulkCopy), lo que permite ignorar el orden y tolerar el ciclo Teams↔Developers; al
///    final se revalidan todas con WITH CHECK, de modo que una inconsistencia real sí aborta.
/// </summary>
public class DataMigrationService
{
    /// <summary>
    /// Todo el trabajo se despacha a un hilo del pool. La creación del esquema y la construcción
    /// del modelo de EF son síncronas y bloquean varios segundos: si corrieran en el hilo de UI la
    /// ventana se quedaría congelada, que es exactamente el defecto que ya tenía «Probar conexión».
    /// El <see cref="IProgress{T}"/> se crea en la UI, así que los avances vuelven solos a ese hilo.
    /// </summary>
    public Task<MigrationReport> MigrateSqliteToSqlServerAsync(
        string sqlitePath,
        string sqlServerConn,
        MigrationMode mode,
        IProgress<string> progress,
        CancellationToken ct)
        => Task.Run(() => MigrateCoreAsync(sqlitePath, sqlServerConn, mode, progress, ct), ct);

    private static async Task<MigrationReport> MigrateCoreAsync(
        string sqlitePath,
        string sqlServerConn,
        MigrationMode mode,
        IProgress<string> progress,
        CancellationToken ct)
    {
        var resultados = new List<TableResult>();

        var srcOpts = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={sqlitePath}").Options;
        var dstOpts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(SqlConnectionStringHelper.NormalizeOrOriginal(sqlServerConn)).Options;

        await using var src = new AppDbContext(srcOpts);
        await using var dst = new AppDbContext(dstOpts);

        // 1. Esquema del destino. Es DDL: va fuera de la transacción de datos.
        progress.Report("Preparando el esquema en SQL Server...");
        DatabaseMigrator.EnsureUpToDate(dst);

        // 2. Plan de copia derivado del modelo.
        var plan = BuildPlan(src.Model, dst.Model);
        progress.Report($"{plan.Count} tablas en el modelo.");

        var sinOrigen = plan.Where(t => t.Columns.Count == 0).Select(t => t.Table).ToList();
        foreach (var t in sinOrigen)
            progress.Report($"  · {t}: no existe en SQLite, se omite.");

        await using var conn = new SqlConnection(SqlConnectionStringHelper.NormalizeOrOriginal(sqlServerConn));
        await conn.OpenAsync(ct);

        // 3. Estado del destino antes de tocar nada.
        var conDatos = new List<string>();
        foreach (var t in plan)
            if (await CountRowsAsync(conn, null, t.Table, ct) > 0)
                conDatos.Add(t.Table);

        if (conDatos.Count > 0 && mode == MigrationMode.AbortarSiHayDatos)
        {
            return new MigrationReport(false, resultados, 0,
                $"El destino ya tiene datos en {conDatos.Count} tabla(s). No se copió nada.",
                NonEmptyTables: conDatos);
        }

        await using var tx = (SqlTransaction)await conn.BeginTransactionAsync(ct);
        var tablaActual = "";
        try
        {
            // 4. Reemplazo: vaciar el destino dentro de la misma transacción.
            if (conDatos.Count > 0)
            {
                progress.Report($"Vaciando {conDatos.Count} tabla(s) del destino...");
                // En orden inverso al de dependencias, y con las FK sin verificar durante la carga,
                // DELETE basta; no se usa TRUNCATE porque falla si hay FK apuntando a la tabla.
                foreach (var t in Enumerable.Reverse(plan))
                {
                    if (!conDatos.Contains(t.Table)) continue;
                    tablaActual = t.Table;
                    await ExecAsync(conn, tx, $"DELETE FROM [{t.Table}]", ct);
                }
            }

            // 5. Copia tabla por tabla.
            int total = 0;
            foreach (var t in plan)
            {
                ct.ThrowIfCancellationRequested();
                tablaActual = t.Table;

                if (t.Columns.Count == 0)
                {
                    resultados.Add(new TableResult(t.Table, 0, "sin origen en SQLite"));
                    continue;
                }

                var tabla = await ReadFromSqliteAsync(src, t, ct);
                if (tabla.Rows.Count == 0)
                {
                    resultados.Add(new TableResult(t.Table, 0, "vacía en origen"));
                    progress.Report($"  · {t.Table}: vacía.");
                    continue;
                }

                using var bulk = new SqlBulkCopy(conn, SqlBulkCopyOptions.KeepIdentity, tx)
                {
                    DestinationTableName = $"[{t.Table}]",
                    BatchSize = 2000,
                    BulkCopyTimeout = 0   // los BLOB de adjuntos y firmas pueden tardar
                };
                foreach (var c in t.Columns)
                    bulk.ColumnMappings.Add(c.Destino, c.Destino);

                await bulk.WriteToServerAsync(tabla, ct);

                total += tabla.Rows.Count;
                resultados.Add(new TableResult(t.Table, tabla.Rows.Count));
                progress.Report($"  ✓ {t.Table}: {tabla.Rows.Count} fila(s).");
            }

            // 6. Revalidar las FK que SqlBulkCopy no verificó. Si el origen tenía referencias
            //    rotas, aquí se detecta y la transacción se deshace entera.
            progress.Report("Verificando integridad referencial...");
            foreach (var t in plan)
            {
                tablaActual = t.Table;
                await ExecAsync(conn, tx, $"ALTER TABLE [{t.Table}] WITH CHECK CHECK CONSTRAINT ALL", ct);
            }

            await tx.CommitAsync(ct);
            progress.Report($"Confirmado. {total} fila(s) en {resultados.Count(r => r.Rows > 0)} tabla(s).");
            return new MigrationReport(true, resultados, total);
        }
        catch (OperationCanceledException)
        {
            await SafeRollbackAsync(tx, progress);
            return new MigrationReport(false, resultados, 0, "Migración cancelada. No se modificó el destino.", tablaActual);
        }
        catch (Exception ex)
        {
            await SafeRollbackAsync(tx, progress);
            return new MigrationReport(false, resultados, 0, Describe(ex), tablaActual);
        }
    }

    // ── Plan ────────────────────────────────────────────────────────────────────

    private sealed record ColumnPlan(string Origen, string Destino, Type ClrType);
    private sealed record TablePlan(string Table, IReadOnlyList<ColumnPlan> Columns);

    /// <summary>
    /// Cruza el modelo SQLite con el de SQL Server: se copian las columnas que existen en ambos.
    /// Eso descarta sola a RowVersion, que solo existe en SQL Server y la genera el motor.
    /// </summary>
    private static List<TablePlan> BuildPlan(IModel srcModel, IModel dstModel)
    {
        var srcPorTabla = srcModel.GetEntityTypes()
            .Where(e => e.GetTableName() != null)
            .ToDictionary(e => e.GetTableName()!, ColumnasDe, StringComparer.OrdinalIgnoreCase);

        var planes = new Dictionary<string, TablePlan>(StringComparer.OrdinalIgnoreCase);
        foreach (var dstEntity in dstModel.GetEntityTypes())
        {
            var tabla = dstEntity.GetTableName();
            if (tabla == null || planes.ContainsKey(tabla)) continue;

            var dstCols = ColumnasDe(dstEntity);
            var columnas = new List<ColumnPlan>();

            if (srcPorTabla.TryGetValue(tabla, out var srcCols))
                foreach (var (col, clr) in dstCols)
                    if (srcCols.ContainsKey(col))
                        columnas.Add(new ColumnPlan(col, col, clr));

            planes[tabla] = new TablePlan(tabla, columnas);
        }

        return OrdenarPorDependencias(dstModel, planes);
    }

    private static Dictionary<string, Type> ColumnasDe(IEntityType entity)
    {
        var store = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
        var cols = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        if (store == null) return cols;

        foreach (var p in entity.GetProperties())
        {
            var col = p.GetColumnName(store.Value);
            if (col != null) cols[col] = p.ClrType;
        }
        return cols;
    }

    /// <summary>
    /// Orden topológico por llaves foráneas: los padres antes que los hijos. El modelo tiene un
    /// ciclo real (Teams.LeadDeveloperId ↔ Developers.TeamId); al detectarlo se emite el resto en
    /// orden estable en lugar de fallar. La carga no depende de este orden — SqlBulkCopy no
    /// verifica FK — pero hace el log legible y ayuda al DELETE del reemplazo.
    /// </summary>
    private static List<TablePlan> OrdenarPorDependencias(IModel model, Dictionary<string, TablePlan> planes)
    {
        var dependencias = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var e in model.GetEntityTypes())
        {
            var tabla = e.GetTableName();
            if (tabla == null || !planes.ContainsKey(tabla)) continue;

            var deps = dependencias.TryGetValue(tabla, out var d) ? d : dependencias[tabla] = new(StringComparer.OrdinalIgnoreCase);
            foreach (var fk in e.GetForeignKeys())
            {
                var padre = fk.PrincipalEntityType.GetTableName();
                if (padre != null && !string.Equals(padre, tabla, StringComparison.OrdinalIgnoreCase) && planes.ContainsKey(padre))
                    deps.Add(padre);
            }
        }

        var orden = new List<TablePlan>();
        var puestas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendientes = planes.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        while (pendientes.Count > 0)
        {
            var listas = pendientes
                .Where(t => dependencias[t].All(puestas.Contains))
                .ToList();

            // Ciclo: nadie tiene todas sus dependencias resueltas. Se emite el resto tal cual.
            if (listas.Count == 0) listas = pendientes.ToList();

            foreach (var t in listas)
            {
                orden.Add(planes[t]);
                puestas.Add(t);
                pendientes.Remove(t);
            }
        }

        return orden;
    }

    // ── Lectura desde SQLite ────────────────────────────────────────────────────

    private static async Task<DataTable> ReadFromSqliteAsync(AppDbContext src, TablePlan plan, CancellationToken ct)
    {
        var tabla = new DataTable(plan.Table);
        foreach (var c in plan.Columns)
            tabla.Columns.Add(c.Destino, TipoDeColumna(c.ClrType));

        var conn = src.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);

        var cols = string.Join(", ", plan.Columns.Select(c => $"\"{c.Origen}\""));
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {cols} FROM \"{plan.Table}\"";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var fila = tabla.NewRow();
            for (int i = 0; i < plan.Columns.Count; i++)
                fila[i] = Convertir(reader.IsDBNull(i) ? null : reader.GetValue(i), plan.Columns[i].ClrType);
            tabla.Rows.Add(fila);
        }
        return tabla;
    }

    private static Type TipoDeColumna(Type clr)
    {
        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        return t.IsEnum ? Enum.GetUnderlyingType(t) : t;
    }

    /// <summary>
    /// SQLite solo distingue INTEGER/REAL/TEXT/BLOB, así que las fechas llegan como texto y los
    /// booleanos como enteros. Se convierte al tipo que espera la columna de SQL Server.
    /// </summary>
    private static object Convertir(object? raw, Type clr)
    {
        if (raw is null or DBNull) return DBNull.Value;

        var t = Nullable.GetUnderlyingType(clr) ?? clr;
        if (t.IsEnum) t = Enum.GetUnderlyingType(t);

        if (t == typeof(byte[]))   return raw as byte[] ?? (object)DBNull.Value;
        if (t == typeof(string))   return Convert.ToString(raw, CultureInfo.InvariantCulture) ?? (object)DBNull.Value;
        if (t == typeof(bool))     return raw is bool b ? b : Convert.ToInt64(raw, CultureInfo.InvariantCulture) != 0;
        if (t == typeof(Guid))     return raw is Guid g ? g : Guid.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!);
        if (t == typeof(TimeSpan)) return raw is TimeSpan ts ? ts : TimeSpan.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

        if (t == typeof(DateTime))
            return raw is DateTime d ? d
                 : DateTime.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!,
                                  CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        if (t == typeof(DateTimeOffset))
            return raw is DateTimeOffset dto ? dto
                 : DateTimeOffset.Parse(Convert.ToString(raw, CultureInfo.InvariantCulture)!, CultureInfo.InvariantCulture);

        return Convert.ChangeType(raw, t, CultureInfo.InvariantCulture);
    }

    // ── Utilidades ──────────────────────────────────────────────────────────────

    private static async Task<int> CountRowsAsync(SqlConnection conn, SqlTransaction? tx, string tabla, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT COUNT_BIG(1) FROM [{tabla}]";
        var n = await cmd.ExecuteScalarAsync(ct);
        return n is long l ? (int)Math.Min(l, int.MaxValue) : 0;
    }

    private static async Task ExecAsync(SqlConnection conn, SqlTransaction tx, string sql, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.CommandTimeout = 0;
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task SafeRollbackAsync(SqlTransaction tx, IProgress<string> progress)
    {
        try
        {
            await tx.RollbackAsync();
            progress.Report("Se deshizo todo: el destino quedó como estaba.");
        }
        catch (Exception ex)
        {
            // Si ni el rollback funciona, el usuario tiene que saberlo: el destino quedó indefinido.
            progress.Report($"ATENCIÓN: no se pudo deshacer la transacción ({ex.Message}). Revisa el destino antes de reintentar.");
        }
    }

    /// <summary>Mensaje con toda la cadena de excepciones: antes se perdía la InnerException.</summary>
    private static string Describe(Exception ex)
    {
        var partes = new List<string>();
        for (var e = ex; e != null; e = e.InnerException)
            partes.Add(e is SqlException sql ? SqlConnectionStringHelper.Explain(sql) : e.Message);
        return string.Join("\n  → ", partes.Distinct());
    }
}
