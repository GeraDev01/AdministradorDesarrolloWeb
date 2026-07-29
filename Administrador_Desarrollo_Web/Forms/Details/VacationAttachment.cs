using Administrador_Desarrollo_Web.Data;

namespace Administrador_Desarrollo_Web.Forms.Details;

/// <summary>Utilería para abrir el documento adjunto de una solicitud de vacaciones (se lee el BLOB bajo demanda).</summary>
public static class VacationAttachment
{
    public static void Open(AppDbContext db, int vacationRequestId, IWin32Window owner)
    {
        var data = db.VacationRequests
            .Where(v => v.Id == vacationRequestId)
            .Select(v => new { v.AttachmentBytes, v.AttachmentFileName })
            .FirstOrDefault();

        if (data?.AttachmentBytes == null || data.AttachmentBytes.Length == 0)
        { MessageBox.Show(owner, "Esta solicitud no tiene documento adjunto.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }

        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "advweb_vac_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var name = string.IsNullOrWhiteSpace(data.AttachmentFileName) ? "adjunto" : data.AttachmentFileName!;
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            var path = Path.Combine(dir, name);
            File.WriteAllBytes(path, data.AttachmentBytes);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, $"No se pudo abrir el adjunto:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
