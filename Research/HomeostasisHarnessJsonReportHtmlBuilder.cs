using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace ISIDA.Research
{
  /// <summary>Краткий HTML-отчёт по JSON-прогону: manifest + превью results.jsonl (все поля <see cref="HomeostasisHarnessResultRow"/>).</summary>
  public static class HomeostasisHarnessJsonReportHtmlBuilder
  {
    /// <param name="manifestPath">Путь к manifest.json.</param>
    /// <param name="jsonlPath">Путь к results.jsonl.</param>
    /// <param name="reportHtmlPath">Путь к создаваемому report.html.</param>
    /// <param name="previewMaxLines">Максимум строк превью из jsonl.</param>
    public static void WriteReport(string manifestPath, string jsonlPath, string reportHtmlPath, int previewMaxLines = 12)
    {
      HomeostasisHarnessManifest manifest = null;
      try
      {
        if (File.Exists(manifestPath))
          manifest = JsonConvert.DeserializeObject<HomeostasisHarnessManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
      }
      catch
      {
        // manifest остаётся null
      }

      const int ColCount = 12;
      var previewRows = new List<string[]>();
      if (File.Exists(jsonlPath))
      {
        foreach (var line in File.ReadLines(jsonlPath).Take(previewMaxLines))
        {
          if (string.IsNullOrWhiteSpace(line))
            continue;
          try
          {
            var row = JsonConvert.DeserializeObject<HomeostasisHarnessResultRow>(line);
            if (row != null)
            {
              previewRows.Add(new[]
              {
                EscapeHtml(row.CaseId),
                EscapeHtml(row.HarnessId),
                FormatBool(row.HasCritical),
                FormatBool(row.AnyVitalHarmful),
                FormatBool(row.HasExternalThreshold),
                FormatBool(row.IsExternalOrientationCritical),
                FormatFloat(row.Urgency),
                FormatInt(row.OperatorAssessment),
                FormatInt(row.DominantParamId),
                FormatInt(row.DominantZone),
                FormatFloat(row.DominanceScore),
                EscapeHtml(row.Error ?? "")
              });
            }
          }
          catch
          {
            previewRows.Add(new[] { EscapeHtml(line) }.Concat(Enumerable.Repeat("", ColCount - 1)).ToArray());
          }
        }
      }

      var sb = new StringBuilder();
      sb.AppendLine("<!DOCTYPE html><html><head><meta charset=\"utf-8\"/>");
      sb.AppendLine("<title>Прогон гомеостаза (JSON)</title>");
      sb.AppendLine("<style>");
      sb.AppendLine("body{font-family:Segoe UI,sans-serif;margin:16px;}");
      sb.AppendLine(".wrap{overflow-x:auto;max-width:100%;}");
      sb.AppendLine("table{border-collapse:collapse;font-size:12px;}");
      sb.AppendLine("th,td{border:1px solid #ccc;padding:6px 8px;white-space:nowrap;}");
      sb.AppendLine("th{background:#eee;}");
      sb.AppendLine(".muted{color:#666;}");
      sb.AppendLine(".bad{color:#b71c1c;}");
      sb.AppendLine("</style>");
      sb.AppendLine("</head><body>");
      sb.AppendLine("<h1>Исследовательский прогон гомеостаза (вход JSON)</h1>");
      if (manifest != null)
      {
        sb.AppendLine("<p class=\"muted\">manifest.json</p><table>");
        sb.AppendLine("<tr><th>harness_id</th><td>" + EscapeHtml(manifest.HarnessId) + "</td></tr>");
        sb.AppendLine("<tr><th>row_count</th><td>" + manifest.RowCount + "</td></tr>");
        sb.AppendLine("<tr><th>errors_count</th><td class=\"" + (manifest.ErrorsCount > 0 ? "bad" : "") + "\">" + manifest.ErrorsCount + "</td></tr>");
        sb.AppendLine("<tr><th>elapsed_ms</th><td>" + manifest.ElapsedMs + "</td></tr>");
        sb.AppendLine("<tr><th>schema_version</th><td>" + EscapeHtml(manifest.SchemaVersion) + "</td></tr>");
        sb.AppendLine("</table>");
        sb.AppendLine("<p><strong>Файлы:</strong></p><ul>");
        sb.AppendLine("<li><code>" + EscapeHtml(manifest.OutputJsonl) + "</code> — JSON Lines</li>");
        sb.AppendLine("<li><code>" + EscapeHtml(manifest.OutputCsv) + "</code> — CSV</li>");
        sb.AppendLine("<li><code>" + EscapeHtml(manifest.OutputReportHtml) + "</code> — этот отчёт</li>");
        sb.AppendLine("<li><code>" + EscapeHtml(manifest.InputPath) + "</code> — входной JSON</li>");
        sb.AppendLine("</ul>");
      }
      else
      {
        sb.AppendLine("<p class=\"bad\">Не удалось прочитать manifest.json</p>");
      }

      sb.AppendLine("<h2>Превью results.jsonl</h2>");
      sb.AppendLine("<p class=\"muted\">Для каждого прогона заполняются только релевантные колонки; остальные — «—».</p>");
      sb.AppendLine("<div class=\"wrap\"><table><tr>");
      sb.AppendLine("<th>case_id</th><th>harness_id</th>");
      sb.AppendLine("<th>has_critical</th><th>any_vital_harmful</th>");
      sb.AppendLine("<th>has_external_threshold</th><th>is_external_orientation_critical</th>");
      sb.AppendLine("<th>urgency</th><th>operator_assessment</th>");
      sb.AppendLine("<th>dominant_param_id</th><th>dominant_zone</th><th>dominance_score</th>");
      sb.AppendLine("<th>error</th>");
      sb.AppendLine("</tr>");
      foreach (var cells in previewRows)
        sb.AppendLine("<tr><td>" + string.Join("</td><td>", cells) + "</td></tr>");
      if (previewRows.Count == 0)
        sb.AppendLine("<tr><td colspan=\"" + ColCount + "\">(пусто)</td></tr>");
      sb.AppendLine("</table></div>");
      sb.AppendLine("<p class=\"muted\">Полный разбор — по jsonl/csv во внешних инструментах.</p>");
      sb.AppendLine("</body></html>");

      File.WriteAllText(reportHtmlPath, sb.ToString(), Encoding.UTF8);
    }

    private static string FormatBool(bool? v)
    {
      if (!v.HasValue)
        return "—";
      return v.Value ? "true" : "false";
    }

    private static string FormatFloat(float? v)
    {
      if (!v.HasValue)
        return "—";
      return v.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatInt(int? v)
    {
      if (!v.HasValue)
        return "—";
      return v.Value.ToString(CultureInfo.InvariantCulture);
    }

    private static string EscapeHtml(string s)
    {
      if (string.IsNullOrEmpty(s))
        return "";
      return s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
    }
  }
}
