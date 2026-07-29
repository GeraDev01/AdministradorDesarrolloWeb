using ClosedXML.Excel;

namespace Administrador_Desarrollo_Web.Services;

public class ReportService
{
    public void ExportToExcel<T>(
        IEnumerable<T> data,
        string[] headers,
        Func<T, object?[]> rowMapper,
        string sheetName,
        string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sheetName);

        // Header row
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromArgb(37, 99, 235);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        // Data rows
        int row = 2;
        bool alt = false;
        foreach (var item in data)
        {
            var values = rowMapper(item);
            for (int i = 0; i < values.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                var val = values[i];
                cell.Value = val switch
                {
                    null            => XLCellValue.FromObject(""),
                    string s        => XLCellValue.FromObject(s),
                    int n           => XLCellValue.FromObject(n),
                    decimal d       => XLCellValue.FromObject(d),
                    double db       => XLCellValue.FromObject(db),
                    bool b          => XLCellValue.FromObject(b ? "Sí" : "No"),
                    DateTime dt     => XLCellValue.FromObject(dt.ToString("dd/MM/yyyy")),
                    _               => XLCellValue.FromObject(val.ToString() ?? "")
                };
                if (alt)
                    cell.Style.Fill.BackgroundColor = XLColor.FromArgb(240, 245, 255);
            }
            alt = !alt;
            row++;
        }

        ws.ColumnsUsed().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public string? PromptSaveDialog(string defaultName)
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Excel (*.xlsx)|*.xlsx",
            FileName = $"{defaultName}_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
            Title = "Guardar reporte como..."
        };
        return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
    }
}
