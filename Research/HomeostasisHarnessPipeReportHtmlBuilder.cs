using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ISIDA.Research
{
  /// <summary>HTML-отчёт по pipe-прогону: карточка метода и таблица строк с OK/NO.</summary>
  public static class ResearchHarnessPipeReportHtmlBuilder
  {
    /// <summary>Записывает HTML-отчёт: карточка метода, сводка по манифесту, таблица строк с фактом и ожиданием.</summary>
    /// <param name="method">Описание метода (заголовок, подсказка формата, подписи колонок).</param>
    /// <param name="rows">Разобранные строки с результатами сравнения выходов.</param>
    /// <param name="manifest">Сводка прогона или null.</param>
    /// <param name="reportHtmlPath">Путь к создаваемому report.html.</param>
    public static void WriteReport(
        ResearchHarnessPipeMethodInfo method,
        List<ResearchHarnessPipePreparedRow> rows,
        PipeHarnessManifest manifest,
        string reportHtmlPath)
    {
      var sb = new StringBuilder();
      sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
      sb.AppendLine("<title>Прогон: " + EscapeHtml(method.Title) + "</title>");
      sb.AppendLine("<style>");
      sb.AppendLine("body{font-family:Segoe UI,sans-serif;margin:16px;}");
      sb.AppendLine(".card{border:1px solid #ccc;border-radius:8px;padding:12px 16px;margin-bottom:16px;background:#fafafa;}");
      sb.AppendLine(".card h2{margin:0 0 8px 0;font-size:18px;}");
      sb.AppendLine(".card pre{white-space:pre-wrap;margin:0;font-size:13px;color:#333;}");
      sb.AppendLine("table{border-collapse:collapse;width:100%;font-size:12px;}");
      sb.AppendLine("th,td{border:1px solid #ccc;padding:6px 8px;text-align:left;}");
      sb.AppendLine("th{background:#eee;}");
      sb.AppendLine(".ok{color:#1b5e20;font-weight:600;}");
      sb.AppendLine(".no{color:#b71c1c;font-weight:600;}");
      sb.AppendLine(".cell-ok{background:#e8f5e9;}");
      sb.AppendLine(".cell-no{background:#ffebee;}");
      sb.AppendLine(".row-ok{background:#f1f8e9;}");
      sb.AppendLine(".row-no{background:#fce4ec;}");
      sb.AppendLine(".muted{color:#666;font-size:13px;}");
      sb.AppendLine("</style></head><body>");

      sb.AppendLine("<h1>Отчёт исследовательского прогона</h1>");

      sb.AppendLine("<div class=\"card\">");
      sb.AppendLine("<h2>" + EscapeHtml(method.Title) + "</h2>");
      sb.AppendLine("<p class=\"muted\">Идентификатор: <code>" + EscapeHtml(method.HarnessId) + "</code></p>");
      sb.AppendLine("<pre>" + EscapeHtml(method.CardDescription) + "</pre>");
      sb.AppendLine("<p class=\"muted\">Формат одной строки (разделитель «|»):</p>");
      sb.AppendLine("<pre>" + EscapeHtml(method.PipeFormatLine) + "</pre>");
      sb.AppendLine("</div>");

      if (manifest != null)
      {
        sb.AppendLine("<p class=\"muted\">Строк в прогоне: " + manifest.row_count
            + " · Расхождений (NO): " + manifest.mismatch_count
            + " · Время: " + manifest.elapsed_ms + " мс</p>");
        sb.AppendLine("<p class=\"muted\">Файлы: <code>" + EscapeHtml(manifest.results_csv) + "</code></p>");
      }

      sb.AppendLine("<table><thead><tr>");
      foreach (var h in method.ColumnLabels)
        sb.AppendLine("<th>" + EscapeHtml(h) + "</th>");
      foreach (var rc in method.ResultColumns)
        sb.AppendLine("<th>" + EscapeHtml(rc.Label + " факт") + "</th>");
      sb.AppendLine("<th>Итог строки</th>");
      sb.AppendLine("</tr></thead><tbody>");

      foreach (var r in rows)
      {
        string rowClass = r.Match ? "row-ok" : "row-no";
        sb.AppendLine("<tr class=\"" + rowClass + "\">");
        foreach (var c in r.RawCells)
          sb.AppendLine("<td>" + EscapeHtml(c) + "</td>");
        foreach (var slot in r.OutputSlots)
        {
          string cellClass = slot.Match ? "cell-ok" : "cell-no";
          string detail = EscapeHtml(slot.ActualText);
          if (!slot.Match)
            detail += " <span class=\"no\">(ожид. " + EscapeHtml(slot.ExpectedText) + ")</span>";
          sb.AppendLine("<td class=\"" + cellClass + "\">" + detail + "</td>");
        }

        string res = r.Match ? "<span class=\"ok\">OK</span>" : "<span class=\"no\">NO</span>";
        sb.AppendLine("<td>" + res + "</td>");
        sb.AppendLine("</tr>");
      }

      sb.AppendLine("</tbody></table>");
      sb.AppendLine("</body></html>");

      File.WriteAllText(reportHtmlPath, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeHtml(string s)
    {
      if (string.IsNullOrEmpty(s)) return "";
      return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
  }
}
