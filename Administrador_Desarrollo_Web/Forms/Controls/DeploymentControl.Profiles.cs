using Administrador_Desarrollo_Web.Models;
using Administrador_Desarrollo_Web.Forms.Details;
using Microsoft.EntityFrameworkCore;

namespace Administrador_Desarrollo_Web.Forms.Controls;

public partial class DeploymentControl
{
    private DataGridView _gridProfiles = null!;
    private List<DeploymentProfile> _profiles = [];

    private TabPage BuildProfilesTab()
    {
        var (tab, body) = NewTab("  🔧  Perfiles  ");

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false, Padding = new Padding(10, 8, 10, 5), BackColor = AppTheme.ContentBg
        };
        var bNew  = AppTheme.MakePrimaryButton("➕ Nuevo perfil", 140); bNew.Margin = new Padding(0, 2, 6, 0); bNew.Click += ProfNew_Click;
        var bEdit = AppTheme.MakeSecondaryButton("✏ Editar", 100); bEdit.Margin = new Padding(0, 2, 6, 0); bEdit.Click += ProfEdit_Click;
        var bDel  = AppTheme.MakeDangerButton("🗑 Eliminar", 100); bDel.Margin = new Padding(0, 2, 0, 0); bDel.Click += ProfDelete_Click;
        toolbar.Controls.AddRange([bNew, bEdit, bDel]);

        _gridProfiles = AppTheme.MakeGrid();
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ID",          Name = "Id",    Width = 40, FillWeight = 5 });
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Perfil",       Name = "Name",  FillWeight = 25 });
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Descripción",  Name = "Desc",  FillWeight = 30 });
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "# Servidores", Name = "Count", FillWeight = 13 });
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Operaciones",  Name = "Ops",   FillWeight = 15 });
        _gridProfiles.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Servidores",   Name = "Srv",   FillWeight = 27 });
        _gridProfiles.CellDoubleClick += (_, _) => ProfEdit_Click(null, EventArgs.Empty);

        var pGrid = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 4, 10, 10), BackColor = AppTheme.ContentBg };
        pGrid.Controls.Add(_gridProfiles);

        body.Controls.Add(toolbar, 0, 0);
        body.Controls.Add(pGrid, 0, 1);
        return tab;
    }

    private void LoadProfiles()
    {
        _profiles = _db.DeploymentProfiles
            .Where(p => !p.IsAdHoc)
            .Include(p => p.ProfileTargets).ThenInclude(pt => pt.Target)
            .OrderBy(p => p.Name).ToList();
        _gridProfiles.Rows.Clear();
        foreach (var p in _profiles)
        {
            var names = string.Join(", ", p.ProfileTargets.OrderBy(pt => pt.Order).Select(pt => pt.Target.Nombre));
            if (names.Length > 70) names = names[..70] + "…";
            _gridProfiles.Rows.Add(p.Id, p.Name, p.Description ?? "—", p.ProfileTargets.Count,
                p.AllowedForOperaciones ? "✓ Sí" : "—", names);
        }
    }

    private DeploymentProfile? SelectedProfile()
    {
        if (_gridProfiles.CurrentRow?.Cells["Id"].Value is not int id) return null;
        return _profiles.FirstOrDefault(p => p.Id == id);
    }

    private void ProfNew_Click(object? s, EventArgs e)
    {
        using var frm = new DeploymentProfileDetailForm(_db);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        var p = frm.Result; p.CreatedAt = DateTime.UtcNow;
        _db.DeploymentProfiles.Add(p); _db.SaveChanges();
        SyncProfileTargets(p.Id, frm.SelectedTargetIds);
        _audit.Record(AuditAction.Create, "DeploymentProfile", p.Id.ToString(), p.Name);
        LoadProfiles();
    }

    private void ProfEdit_Click(object? s, EventArgs e)
    {
        var p = SelectedProfile();
        if (p == null) { MessageBox.Show("Selecciona un perfil.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        using var frm = new DeploymentProfileDetailForm(_db, p);
        if (frm.ShowDialog(this) != DialogResult.OK) return;
        _db.SaveChanges();
        SyncProfileTargets(p.Id, frm.SelectedTargetIds);
        _audit.Record(AuditAction.Update, "DeploymentProfile", p.Id.ToString(), p.Name);
        LoadProfiles();
    }

    private void SyncProfileTargets(int profileId, List<int> targetIds)
    {
        var old = _db.DeploymentProfileTargets.Where(pt => pt.ProfileId == profileId).ToList();
        _db.DeploymentProfileTargets.RemoveRange(old);
        int order = 0;
        foreach (var tid in targetIds)
            _db.DeploymentProfileTargets.Add(new DeploymentProfileTarget { ProfileId = profileId, TargetId = tid, Order = order++ });
        _db.SaveChanges();
    }

    private void ProfDelete_Click(object? s, EventArgs e)
    {
        var p = SelectedProfile();
        if (p == null) { MessageBox.Show("Selecciona un perfil.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Information); return; }
        if (_db.DeploymentJobs.Any(j => j.DeploymentProfileId == p.Id))
        { MessageBox.Show("No se puede eliminar: el perfil tiene despliegues en el historial.", "Aviso", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (MessageBox.Show($"¿Eliminar el perfil '{p.Name}'?", "Confirmar", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _db.DeploymentProfiles.Remove(p); _db.SaveChanges();
        _audit.Record(AuditAction.Delete, "DeploymentProfile", p.Id.ToString(), p.Name);
        LoadProfiles();
    }
}
