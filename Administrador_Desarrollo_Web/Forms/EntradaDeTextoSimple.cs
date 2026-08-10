namespace Administrador_Desarrollo_Web.Forms;

/// <summary>
/// Cuadro para pedir una frase: un motivo, un nombre, una nota.
///
/// Existe porque el mismo diálogo estaba escrito a mano —con sus coordenadas y sus dos botones— en
/// varias pantallas, cada una con su propia idea de qué devolver al cancelar. Aquí la distinción es
/// explícita y única: <c>null</c> es «canceló», cadena vacía es «aceptó sin escribir nada». Quien
/// exija texto comprueba lo segundo; quien lo tenga por opcional, solo lo primero.
/// </summary>
public static class EntradaDeTextoSimple
{
    /// <summary>
    /// Muestra el cuadro y devuelve lo escrito, ya recortado. <c>null</c> si se canceló.
    /// </summary>
    public static string? Pedir(IWin32Window? padre, string titulo, string mensaje,
        string valorInicial = "", int maxLength = 500)
    {
        using var dlg = new ResponsiveForm
        {
            Text = titulo,
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false,
            BackColor = AppTheme.CardBg, Font = AppTheme.DefaultFont,
            ClientSize = new Size(460, 210)
        };

        dlg.Controls.Add(new Label
        {
            Left = 20, Top = 16, Width = 420, Height = 46, AutoSize = false,
            Text = mensaje, ForeColor = AppTheme.TextSecondary
        });

        // Multilínea: casi siempre lo que se pide es una explicación, y una sola línea invita a
        // responder con tres palabras. El Enter lo respeta ResponsiveForm.
        var txt = new TextBox
        {
            Left = 20, Top = 66, Width = 420, Height = 80, Multiline = true,
            ScrollBars = ScrollBars.Vertical, MaxLength = maxLength, Text = valorInicial
        };
        dlg.Controls.Add(txt);

        var btnAceptar = AppTheme.MakePrimaryButton("Aceptar", 110);
        btnAceptar.Left = 210; btnAceptar.Top = 158;
        btnAceptar.DialogResult = DialogResult.OK;

        var btnCancelar = AppTheme.MakeSecondaryButton("Cancelar", 110);
        btnCancelar.Left = 330; btnCancelar.Top = 158;
        btnCancelar.DialogResult = DialogResult.Cancel;

        dlg.Controls.AddRange([btnAceptar, btnCancelar]);
        dlg.AcceptButton = btnAceptar;
        dlg.CancelButton = btnCancelar;

        return dlg.ShowDialog(padre) == DialogResult.OK ? txt.Text.Trim() : null;
    }
}
