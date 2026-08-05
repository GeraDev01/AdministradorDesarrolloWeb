using Administrador_Desarrollo_Web.Data;
using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Services;
using Administrador_Desarrollo_Web.Forms.Details;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public class UserManagementControl : UserControl
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private DataGridView _grid = null!;
    private List<User> _allUsers = [];
    private readonly ToolTip _tip = new() { AutoPopDelay = 15000 };

    public UserManagementControl(AppDbContext db, AuthService auth) { _db = db; _auth = auth; BuildUI(); LoadData(); }

    private void BuildUI()
    {
        BackColor = AppTheme.ContentBg;
        Dock = DockStyle.Fill;

        var tbl = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, CellBorderStyle = TableLayoutPanelCellBorderStyle.None
        };
        tbl.RowStyles.Add(new RowStyle(SizeType.Absolute, 52f));
        tbl.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        tbl.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));

        // Toolbar
        var btnFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, BackColor = AppTheme.ContentBg,
            Margin = Padding.Empty, Padding = new Padding(10, 8, 10, 5)
        };
        var btnNew    = AppTheme.MakePrimaryButton("➕ Nuevo usuario", 140);
        var btnEdit   = AppTheme.MakeSecondaryButton("✏ Editar", 108);
        var btnReset  = AppTheme.MakeSecondaryButton("🔑 Reset pwd", 120);
        var btnUnlock = AppTheme.MakeSecondaryButton("🔓 Desbloquear", 140);
        var btnToggle = AppTheme.MakeSecondaryButton("⚡ Activar/Desact.", 150);
        var btnDelete = AppTheme.MakeDangerButton("🗑 Eliminar", 120);
        foreach (var b in new[] { btnNew, btnEdit, btnReset, btnUnlock, btnToggle, btnDelete })
            b.Margin = new Padding(0, 2, 8, 0);
        btnNew.Click    += BtnNew_Click;
        btnEdit.Click   += BtnEdit_Click;
        btnReset.Click  += BtnReset_Click;
        btnUnlock.Click += BtnUnlock_Click;
        btnToggle.Click += BtnToggle_Click;
        btnDelete.Click += BtnDelete_Click;
        _tip.SetToolTip(btnUnlock,
            $"Levanta el bloqueo por intentos fallidos ({AuthService.MaxFailedAttempts} errores bloquean " +
            $"{AuthService.LockoutMinutes} minutos).\nNo cambia la contraseña: para eso usa «Reset pwd».");
        btnFlow.Controls.AddRange([btnNew, btnEdit, btnReset, btnUnlock, btnToggle, btnDelete]);

        // Grid
        _grid = AppTheme.MakeGrid();
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",      Name = "Id",      Width = 45, FillWeight = 5  });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Usuario",  Name = "Username", FillWeight = 20 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Nombre",   Name = "Name",     FillWeight = 30 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Rol",      Name = "Role",     FillWeight = 15 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Activo",   Name = "Active",   FillWeight = 10 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Acceso",   Name = "Lock",     FillWeight = 22 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Alta",     Name = "Created",  FillWeight = 20 });
        _grid.CellDoubleClick += (_, _) => BtnEdit_Click(null, EventArgs.Empty);

        var pnlGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg, Margin = Padding.Empty };
        pnlGrid.Controls.Add(_grid);

        tbl.Controls.Add(btnFlow,   0, 0);
        tbl.Controls.Add(pnlGrid,   0, 1);
        Controls.Add(tbl);
    }

    private void LoadData()
    {
        _allUsers = _auth.GetAllUsers();
        _grid.Rows.Clear();
        foreach (var u in _allUsers)
        {
            var roleLabel = u.Role switch
            {
                UserRole.Admin        => "Admin",
                UserRole.Operaciones  => "Operaciones",
                UserRole.Desarrollador => "Desarrollador",
                _                     => u.Role.ToString()
            };
            bool bloqueado = AuthService.EstaBloqueado(u);
            string acceso = bloqueado
                ? $"🔒 Hasta {u.LockoutUntil!.Value.ToLocalTime():HH:mm}"
                : u.FailedLoginCount > 0
                    ? $"⚠ {u.FailedLoginCount}/{AuthService.MaxFailedAttempts} fallidos"
                    : "";

            int i = _grid.Rows.Add(u.Id, u.Username, u.FullName, roleLabel,
                u.IsActive ? "✓" : "✗",
                acceso,
                u.CreatedAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm"));
            if (!u.IsActive) _grid.Rows[i].DefaultCellStyle.ForeColor = AppTheme.TextSecondary;

            var celdaAcceso = _grid.Rows[i].Cells["Lock"];
            if (bloqueado)
            {
                celdaAcceso.Style.ForeColor = AppTheme.Danger;
                celdaAcceso.Style.Font = AppTheme.BoldFont;
                celdaAcceso.ToolTipText =
                    $"Bloqueada por {AuthService.MaxFailedAttempts} intentos fallidos hasta las " +
                    $"{u.LockoutUntil!.Value.ToLocalTime():HH:mm}. Usa «🔓 Desbloquear» para levantarlo ya.";
            }
            else if (u.FailedLoginCount > 0)
            {
                celdaAcceso.Style.ForeColor = AppTheme.Warning;
                celdaAcceso.ToolTipText =
                    $"Lleva {u.FailedLoginCount} intento(s) fallido(s). A los {AuthService.MaxFailedAttempts} " +
                    $"la cuenta se bloquea {AuthService.LockoutMinutes} minutos.";
            }
        }
    }

    private User? SelectedUser()
    {
        if (_grid.CurrentRow == null) return null;
        if (_grid.CurrentRow.Cells["Id"].Value is not int id) return null;
        return _allUsers.FirstOrDefault(u => u.Id == id);
    }

    private void BtnNew_Click(object? s, EventArgs e)
    {
        using var frm = new UserDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var (ok, msg) = _auth.CreateUser(frm.Result, frm.NewPassword);
        if (!ok) MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        else LoadData();
    }

    private void BtnEdit_Click(object? s, EventArgs e)
    {
        var u = SelectedUser();
        if (u == null) { MessageBox.Show("Selecciona un usuario.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new UserDetailForm(_db, u);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        if (!string.IsNullOrEmpty(frm.NewPassword)) _auth.ResetPassword(u.Id, frm.NewPassword);
        var (ok, msg) = _auth.UpdateUser(frm.Result);
        if (!ok) MessageBox.Show(msg, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        else LoadData();
    }

    private void BtnReset_Click(object? s, EventArgs e)
    {
        var u = SelectedUser();
        if (u == null) { MessageBox.Show("Selecciona un usuario.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new ChangePasswordForm();
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var (ok, msg) = _auth.ResetPassword(u.Id, frm.NewPassword);
        MessageBox.Show(msg, ok ? "Listo" : "Error", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        if (ok) LoadData();
    }

    /// <summary>
    /// Levanta el bloqueo por intentos fallidos. Antes solo caducaba por tiempo, así que el
    /// administrador no podía hacer nada por alguien que estuviera esperando junto a él.
    /// </summary>
    private void BtnUnlock_Click(object? s, EventArgs e)
    {
        var u = SelectedUser();
        if (u == null) { MessageBox.Show("Selecciona un usuario.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        try
        {
            var (ok, msg) = _auth.DesbloquearCuenta(u.Id);
            LoadData();   // el estado cambió (o se descubrió que ya no estaba bloqueado): refrescar
            MessageBox.Show(msg, ok ? "Listo" : "Sin cambios",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Information);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void BtnToggle_Click(object? s, EventArgs e)
    {
        var u = SelectedUser();
        if (u == null) { MessageBox.Show("Selecciona un usuario.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        u.IsActive = !u.IsActive; _db.SaveChanges(); LoadData();
    }

    private void BtnDelete_Click(object? s, EventArgs e)
    {
        var u = SelectedUser();
        if (u == null) { MessageBox.Show("Selecciona un usuario.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        // Se avisa ANTES de confirmar, no después: son las dos razones por las que alguien
        // se arrepentiría de pulsar el botón.
        if (u.Role == UserRole.Admin)
        {
            MessageBox.Show(
                $"«{u.Username}» es líder y no se puede eliminar.\n\n" +
                "Si de verdad quieres darlo de baja, cámbiale antes el rol o desactívalo.",
                "No permitido", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        int referencias = _auth.ContarReferencias(u.Id);
        var aviso = referencias > 0
            ? $"\n\n⚠  {referencias} registro(s) del histórico quedarán sin atribución " +
              "(puntos asignados, revisiones, despliegues iniciados…). La bitácora conserva su nombre."
            : "";

        if (MessageBox.Show(
                $"¿Eliminar al usuario «{u.Username}» ({u.FullName})?{aviso}\n\n" +
                "Si solo quieres quitarle el acceso temporalmente, usa «Activar/Desact.» en su lugar.\n\n" +
                "Esta acción no se puede deshacer.",
                "Eliminar usuario", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        try
        {
            var (ok, msg) = _auth.DeleteUser(u.Id);
            LoadData();
            MessageBox.Show(msg, ok ? "Listo" : "No se pudo",
                MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (AuthorizationException ex)
        {
            MessageBox.Show(ex.Message, "Sin permiso", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (Visible) LoadData(); }
}
