using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ISIDA.Common;
using ISIDA.Gomeostas;
using Newtonsoft.Json;

namespace ISIDA.Research
{
  /// <summary>
  /// Пакетный прогон методов <see cref="HomeostasisCalculator"/> по JSON-входу;
  /// выход: manifest.json, results.jsonl, results.csv (UTF-8).
  /// </summary>
  public static class HomeostasisHarnessRunner
  {
    /// <summary>
    /// Выполняет прогон по файлу входа и пишет артефакты в каталог (создаётся при необходимости).
    /// </summary>
    /// <param name="calculator">Калькулятор гомеостаза (обычно <see cref="GomeostasSystem.Calculator"/>).</param>
    /// <param name="inputJsonPath">Путь к UTF-8 JSON с полями schema_version, harness_id, cases.</param>
    /// <param name="outputDirectory">Каталог для manifest.json, results.jsonl, results.csv.</param>
    /// <returns>Результат с манифестом или текстом ошибки.</returns>
    public static HomeostasisHarnessRunResult Run(
        HomeostasisCalculator calculator,
        string inputJsonPath,
        string outputDirectory)
    {
      if (calculator == null)
        return Fail("calculator == null");
      if (string.IsNullOrWhiteSpace(inputJsonPath) || !File.Exists(inputJsonPath))
        return Fail("Входной JSON не найден.");
      if (string.IsNullOrWhiteSpace(outputDirectory))
        return Fail("Не задан каталог вывода.");

      try
      {
        Directory.CreateDirectory(outputDirectory);
      }
      catch (Exception ex)
      {
        return Fail("Не удалось создать каталог вывода: " + ex.Message);
      }

      var sw = Stopwatch.StartNew();
      HomeostasisHarnessInputFile input;
      try
      {
        var json = File.ReadAllText(inputJsonPath, Encoding.UTF8);
        input = JsonConvert.DeserializeObject<HomeostasisHarnessInputFile>(json);
      }
      catch (Exception ex)
      {
        return Fail("Разбор входного JSON: " + ex.Message);
      }

      if (input == null || string.IsNullOrWhiteSpace(input.HarnessId))
        return Fail("В файле нет harness_id.");
      if (input.Cases == null || input.Cases.Count == 0)
        return Fail("Список cases пуст.");

      if (string.Equals(input.HarnessId, HomeostasisHarnessIds.DominantAndFinalStyles, StringComparison.Ordinal))
        GlobalTimer.SetGlobalPulseCountForHarness(10_000);

      var rows = new List<HomeostasisHarnessResultRow>();
      foreach (var c in input.Cases)
      {
        var row = new HomeostasisHarnessResultRow
        {
          CaseId = string.IsNullOrWhiteSpace(c?.CaseId) ? "?" : c.CaseId.Trim(),
          HarnessId = input.HarnessId
        };
        try
        {
          if (string.Equals(input.HarnessId, HomeostasisHarnessIds.HasCriticalParameterChanges, StringComparison.Ordinal))
            RunHasCritical(calculator, c, row);
          else if (string.Equals(input.HarnessId, HomeostasisHarnessIds.AnyVitalHarmfulZone, StringComparison.Ordinal))
            RunAnyVitalHarmful(calculator, c, row);
          else if (string.Equals(input.HarnessId, HomeostasisHarnessIds.ExternalImpactCriticalFlags, StringComparison.Ordinal))
            RunExternalFlags(calculator, c, row);
          else if (string.Equals(input.HarnessId, HomeostasisHarnessIds.CalculateUrgencyFunction, StringComparison.Ordinal))
            RunUrgency(calculator, c, row);
          else if (string.Equals(input.HarnessId, HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment, StringComparison.Ordinal))
            RunOperatorAssessment(calculator, c, row);
          else if (string.Equals(input.HarnessId, HomeostasisHarnessIds.DominantAndFinalStyles, StringComparison.Ordinal))
            RunDominantAndFinal(calculator, c, row);
          else
            row.Error = "Неизвестный harness_id.";
        }
        catch (Exception ex)
        {
          row.Error = ex.Message;
        }
        rows.Add(row);
      }

      sw.Stop();
      var errors = rows.Count(r => !string.IsNullOrEmpty(r.Error));

      var baseName = "results";
      var jsonlPath = Path.Combine(outputDirectory, baseName + ".jsonl");
      var csvPath = Path.Combine(outputDirectory, baseName + ".csv");
      WriteJsonl(jsonlPath, rows);
      WriteCsv(csvPath, input.HarnessId, rows);

      var manifest = new HomeostasisHarnessManifest
      {
        SchemaVersion = string.IsNullOrWhiteSpace(input.SchemaVersion) ? "1.0" : input.SchemaVersion,
        HarnessId = input.HarnessId,
        RowCount = rows.Count,
        ErrorsCount = errors,
        ElapsedMs = sw.ElapsedMilliseconds,
        InputPath = Path.GetFullPath(inputJsonPath),
        OutputJsonl = Path.GetFullPath(jsonlPath),
        OutputCsv = Path.GetFullPath(csvPath),
        OutputReportHtml = Path.GetFullPath(Path.Combine(outputDirectory, "report.html"))
      };

      var manifestPath = Path.Combine(outputDirectory, "manifest.json");
      File.WriteAllText(manifestPath, JsonConvert.SerializeObject(manifest, Formatting.Indented), Encoding.UTF8);

      try
      {
        HomeostasisHarnessJsonReportHtmlBuilder.WriteReport(manifestPath, jsonlPath, manifest.OutputReportHtml);
      }
      catch
      {
        // отчёт не обязателен для успеха прогона
      }

      return new HomeostasisHarnessRunResult
      {
        Success = true,
        Manifest = manifest
      };
    }

    private static void RunHasCritical(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Current == null || c.Previous == null)
      {
        row.Error = "Для has_critical нужны массивы current и previous.";
        return;
      }
      var cur = MapList(c.Current);
      var prev = MapList(c.Previous);
      row.HasCritical = calculator.HasCriticalParameterChanges(cur, prev);
    }

    private static void RunAnyVitalHarmful(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Parameters == null || c.Parameters.Count == 0)
      {
        row.Error = "Для any_vital_harmful нужен массив parameters.";
        return;
      }
      var list = MapList(c.Parameters);
      row.AnyVitalHarmful = calculator.AnyVitalParameterInHarmfulZone(list);
    }

    private static void RunExternalFlags(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Parameters == null || c.Parameters.Count == 0)
      {
        row.Error = "Нужен массив parameters (один параметр).";
        return;
      }

      if (c.ExternalInfluences == null || c.ExternalInfluences.Count == 0)
      {
        row.Error = "Нужен объект external_influences (id → воздействие).";
        return;
      }

      var list = MapList(c.Parameters);
      row.HasExternalThreshold = calculator.HasExternalCriticalImpact(c.ExternalInfluences, list);
      row.IsExternalOrientationCritical = calculator.IsExternalImpactCritical(c.ExternalInfluences, list);
    }

    private static void RunUrgency(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Parameters == null || c.Parameters.Count != 1)
      {
        row.Error = "Нужен ровно один элемент в parameters.";
        return;
      }

      var list = MapList(c.Parameters);
      row.Urgency = calculator.CalculateUrgencyFunction(list[0]);
    }

    private static void RunOperatorAssessment(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Parameters == null || c.Parameters.Count == 0)
      {
        row.Error = "Нужен массив parameters.";
        return;
      }

      if (c.ValuesBefore == null || c.ValuesBefore.Count == 0)
      {
        row.Error = "Нужен объект values_before.";
        return;
      }

      if (!c.OverallBefore.HasValue || !c.OverallAfter.HasValue)
      {
        row.Error = "Нужны overall_before и overall_after (−1/0/1).";
        return;
      }

      var currents = MapList(c.Parameters);
      var dict = new Dictionary<int, float>(c.ValuesBefore);
      int focus = c.FocusParameterId ?? 0;
      var ob = MapOverallInt(c.OverallBefore.Value);
      var oa = MapOverallInt(c.OverallAfter.Value);
      row.OperatorAssessment = calculator.ComputeOperatorAutomatizmAssessment(dict, currents, focus, ob, oa);
    }

    private static AppGlobalState.HomeostasisState MapOverallInt(int v)
    {
      if (v == -1) return AppGlobalState.HomeostasisState.Bad;
      if (v == 1) return AppGlobalState.HomeostasisState.Well;
      return AppGlobalState.HomeostasisState.Normal;
    }

    private static void RunDominantAndFinal(HomeostasisCalculator calculator, HomeostasisHarnessCaseDto c, HomeostasisHarnessResultRow row)
    {
      if (c.Parameters == null || c.Parameters.Count != 1)
      {
        row.Error = "Нужен ровно один параметр в parameters.";
        return;
      }

      if (c.BaseStyleIds == null || c.BaseStyleIds.Count == 0)
      {
        row.Error = "Нужен массив base_style_ids.";
        return;
      }

      if (!c.DynamicTime.HasValue || !c.DifSensorPar.HasValue)
      {
        row.Error = "Нужны dynamic_time и dif_sensor_par.";
        return;
      }

      if (string.IsNullOrWhiteSpace(c.StyleActivations))
      {
        row.Error = "Нужна строка style_activations.";
        return;
      }

      var plist = MapList(c.Parameters);
      var p = plist[0];
      foreach (var kv in ResearchHarnessPipeRunner.ParseStyleActivationsHarness(c.StyleActivations))
        p.StyleActivations[kv.Key] = kv.Value.ToList();

      var baseStyles = ResearchHarnessPipeRunner.BuildBaseStylesFromIds(c.BaseStyleIds);
      int dyn = c.DynamicTime.Value;
      float dif = c.DifSensorPar.Value;
      var (dom, zone, score) = calculator.FindDominantParameter(plist, dyn, dif);
      calculator.GetFinalActiveStyles(baseStyles, plist, dyn, dif);
      row.DominantParamId = dom?.Id;
      row.DominantZone = zone;
      row.DominanceScore = score;
    }

    private static List<GomeostasSystem.ParameterData> MapList(IEnumerable<ParameterSnapshotDto> snapshots)
    {
      var list = new List<GomeostasSystem.ParameterData>();
      foreach (var s in snapshots)
      {
        if (s == null)
          continue;
        var name = string.IsNullOrWhiteSpace(s.Name) ? "p" : s.Name.Trim();
        list.Add(new GomeostasSystem.ParameterData(
            s.Id,
            name,
            "",
            s.Value,
            s.Weight,
            s.NormaWell,
            s.Speed,
            s.IsVital,
            s.CriticalMin,
            s.CriticalMax));
      }
      return list;
    }

    private static void WriteJsonl(string path, List<HomeostasisHarnessResultRow> rows)
    {
      var sb = new StringBuilder();
      foreach (var r in rows)
        sb.AppendLine(JsonConvert.SerializeObject(r));
      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteCsv(string path, string harnessId, List<HomeostasisHarnessResultRow> rows)
    {
      var sb = new StringBuilder();
      if (string.Equals(harnessId, HomeostasisHarnessIds.HasCriticalParameterChanges, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,has_critical,error");
      else if (string.Equals(harnessId, HomeostasisHarnessIds.AnyVitalHarmfulZone, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,any_vital_harmful,error");
      else if (string.Equals(harnessId, HomeostasisHarnessIds.ExternalImpactCriticalFlags, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,has_external_threshold,is_external_orientation_critical,error");
      else if (string.Equals(harnessId, HomeostasisHarnessIds.CalculateUrgencyFunction, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,urgency,error");
      else if (string.Equals(harnessId, HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,operator_assessment,error");
      else if (string.Equals(harnessId, HomeostasisHarnessIds.DominantAndFinalStyles, StringComparison.Ordinal))
        sb.AppendLine("case_id,harness_id,dominant_param_id,dominant_zone,dominance_score,error");
      else
        sb.AppendLine("case_id,harness_id,error");

      foreach (var r in rows)
      {
        if (string.Equals(harnessId, HomeostasisHarnessIds.HasCriticalParameterChanges, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.HasCritical.HasValue ? (r.HasCritical.Value ? "true" : "false") : "",
              Escape(r.Error ?? "")));
        else if (string.Equals(harnessId, HomeostasisHarnessIds.AnyVitalHarmfulZone, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.AnyVitalHarmful.HasValue ? (r.AnyVitalHarmful.Value ? "true" : "false") : "",
              Escape(r.Error ?? "")));
        else if (string.Equals(harnessId, HomeostasisHarnessIds.ExternalImpactCriticalFlags, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.HasExternalThreshold.HasValue ? (r.HasExternalThreshold.Value ? "true" : "false") : "",
              r.IsExternalOrientationCritical.HasValue ? (r.IsExternalOrientationCritical.Value ? "true" : "false") : "",
              Escape(r.Error ?? "")));
        else if (string.Equals(harnessId, HomeostasisHarnessIds.CalculateUrgencyFunction, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.Urgency.HasValue ? r.Urgency.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
              Escape(r.Error ?? "")));
        else if (string.Equals(harnessId, HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.OperatorAssessment.HasValue ? r.OperatorAssessment.Value.ToString() : "",
              Escape(r.Error ?? "")));
        else if (string.Equals(harnessId, HomeostasisHarnessIds.DominantAndFinalStyles, StringComparison.Ordinal))
          sb.AppendLine(string.Join(",",
              Escape(r.CaseId),
              Escape(r.HarnessId),
              r.DominantParamId.HasValue ? r.DominantParamId.Value.ToString() : "",
              r.DominantZone.HasValue ? r.DominantZone.Value.ToString() : "",
              r.DominanceScore.HasValue ? r.DominanceScore.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "",
              Escape(r.Error ?? "")));
        else
          sb.AppendLine(string.Join(",", Escape(r.CaseId), Escape(r.HarnessId), Escape(r.Error ?? "")));
      }

      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string Escape(string s)
    {
      if (string.IsNullOrEmpty(s))
        return "";
      if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0)
        return s;
      return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static HomeostasisHarnessRunResult Fail(string msg)
    {
      return new HomeostasisHarnessRunResult { Success = false, ErrorMessage = msg };
    }
  }
}
