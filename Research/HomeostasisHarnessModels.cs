using System.Collections.Generic;
using Newtonsoft.Json;

namespace ISIDA.Research
{
  /// <summary>Входной файл прогона (JSON). Удобен для внешних инструментов (Python и т.д.).</summary>
  public sealed class HomeostasisHarnessInputFile
  {
    /// <summary>Версия схемы входного файла.</summary>
    [JsonProperty("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>Идентификатор прогона (см. <see cref="HomeostasisHarnessIds"/>).</summary>
    [JsonProperty("harness_id")]
    public string HarnessId { get; set; }

    /// <summary>Список кейсов для последовательного выполнения.</summary>
    [JsonProperty("cases")]
    public List<HomeostasisHarnessCaseDto> Cases { get; set; } = new List<HomeostasisHarnessCaseDto>();
  }

  /// <summary>Один кейс входного файла.</summary>
  public sealed class HomeostasisHarnessCaseDto
  {
    /// <summary>Произвольный идентификатор кейса для отчёта.</summary>
    [JsonProperty("case_id")]
    public string CaseId { get; set; }

    /// <summary>Для <c>homeostasis.has_critical_parameter_changes</c> — снимок «текущий пульс».</summary>
    [JsonProperty("current")]
    public List<ParameterSnapshotDto> Current { get; set; }

    /// <summary>Для <c>homeostasis.has_critical_parameter_changes</c> — снимок «конец предыдущего пульса».</summary>
    [JsonProperty("previous")]
    public List<ParameterSnapshotDto> Previous { get; set; }

    /// <summary>Для <c>homeostasis.any_vital_harmful_zone</c> — один набор параметров.</summary>
    [JsonProperty("parameters")]
    public List<ParameterSnapshotDto> Parameters { get; set; }

    /// <summary>Для <c>homeostasis.external_impact_critical_flags</c> — id → величина внешнего воздействия.</summary>
    [JsonProperty("external_influences")]
    public Dictionary<int, int> ExternalInfluences { get; set; }

    /// <summary>Для <c>homeostasis.compute_operator_automatizm_assessment</c> — значения «до» по id параметра.</summary>
    [JsonProperty("values_before")]
    public Dictionary<int, float> ValuesBefore { get; set; }

    /// <summary>Фокусный параметр (0 — не задан).</summary>
    [JsonProperty("focus_parameter_id")]
    public int? FocusParameterId { get; set; }

    /// <summary>Интегральное состояние до (−1/0/1).</summary>
    [JsonProperty("overall_before")]
    public int? OverallBefore { get; set; }

    /// <summary>Интегральное состояние после (−1/0/1).</summary>
    [JsonProperty("overall_after")]
    public int? OverallAfter { get; set; }

    /// <summary>Для доминирующего стиля: dynamicTime.</summary>
    [JsonProperty("dynamic_time")]
    public int? DynamicTime { get; set; }

    /// <summary>Для доминирующего стиля: difSensorPar.</summary>
    [JsonProperty("dif_sensor_par")]
    public float? DifSensorPar { get; set; }

    /// <summary>Id базовых стилей для GetFinalActiveStyles.</summary>
    [JsonProperty("base_style_ids")]
    public List<int> BaseStyleIds { get; set; }

    /// <summary>Строка активаций «зона:стили;…».</summary>
    [JsonProperty("style_activations")]
    public string StyleActivations { get; set; }
  }

  /// <summary>Плоское описание параметра для JSON (без INotify).</summary>
  public sealed class ParameterSnapshotDto
  {
    /// <summary>Идентификатор параметра.</summary>
    [JsonProperty("id")]
    public int Id { get; set; }

    /// <summary>Краткое имя (для <see cref="ISIDA.Gomeostas.GomeostasSystem.ParameterData"/>).</summary>
    [JsonProperty("name")]
    public string Name { get; set; }

    /// <summary>Текущее значение 0…100.</summary>
    [JsonProperty("value")]
    public float Value { get; set; }

    /// <summary>Вес параметра.</summary>
    [JsonProperty("weight")]
    public int Weight { get; set; } = 50;

    /// <summary>Целевая норма (целое в модели параметра).</summary>
    [JsonProperty("normaWell")]
    public int NormaWell { get; set; } = 50;

    /// <summary>Скорость/направление (&lt; 0 — дефицит-ориентированный).</summary>
    [JsonProperty("speed")]
    public int Speed { get; set; } = -1;

    /// <summary>Признак жизненно важного параметра.</summary>
    [JsonProperty("isVital")]
    public bool IsVital { get; set; }

    /// <summary>Нижняя граница критической зоны.</summary>
    [JsonProperty("criticalMin")]
    public float CriticalMin { get; set; }

    /// <summary>Верхняя граница критической зоны.</summary>
    [JsonProperty("criticalMax")]
    public float CriticalMax { get; set; } = 100f;
  }

  /// <summary>Одна строка результата (JSON Lines + CSV).</summary>
  public sealed class HomeostasisHarnessResultRow
  {
    /// <summary>Идентификатор кейса из входа.</summary>
    [JsonProperty("case_id")]
    public string CaseId { get; set; }

    /// <summary>Идентификатор прогона.</summary>
    [JsonProperty("harness_id")]
    public string HarnessId { get; set; }

    /// <summary>Результат для прогона has_critical; иначе null.</summary>
    [JsonProperty("has_critical")]
    public bool? HasCritical { get; set; }

    /// <summary>Результат для прогона any_vital_harmful; иначе null.</summary>
    [JsonProperty("any_vital_harmful")]
    public bool? AnyVitalHarmful { get; set; }

    /// <summary>Результат <see cref="ISIDA.Gomeostas.HomeostasisCalculator.HasExternalCriticalImpact"/>; иначе null.</summary>
    [JsonProperty("has_external_threshold")]
    public bool? HasExternalThreshold { get; set; }

    /// <summary>Результат <see cref="ISIDA.Gomeostas.HomeostasisCalculator.IsExternalImpactCritical"/>; иначе null.</summary>
    [JsonProperty("is_external_orientation_critical")]
    public bool? IsExternalOrientationCritical { get; set; }

    /// <summary>Результат <see cref="ISIDA.Gomeostas.HomeostasisCalculator.CalculateUrgencyFunction"/>; иначе null.</summary>
    [JsonProperty("urgency")]
    public float? Urgency { get; set; }

    /// <summary>Результат <see cref="ISIDA.Gomeostas.HomeostasisCalculator.ComputeOperatorAutomatizmAssessment"/> (−1/0/+1); иначе null.</summary>
    [JsonProperty("operator_assessment")]
    public int? OperatorAssessment { get; set; }

    /// <summary>Id доминирующего параметра (<see cref="ISIDA.Gomeostas.HomeostasisCalculator.FindDominantParameter"/>); иначе null.</summary>
    [JsonProperty("dominant_param_id")]
    public int? DominantParamId { get; set; }

    /// <summary>Зона доминирования (как в прогоне доминанты); иначе null.</summary>
    [JsonProperty("dominant_zone")]
    public int? DominantZone { get; set; }

    /// <summary>Скор доминирования; иначе null.</summary>
    [JsonProperty("dominance_score")]
    public float? DominanceScore { get; set; }

    /// <summary>Текст ошибки по кейсу; пусто при успехе.</summary>
    [JsonProperty("error")]
    public string Error { get; set; }
  }

  /// <summary>Манифест прогона (рядом с jsonl/csv).</summary>
  public sealed class HomeostasisHarnessManifest
  {
    /// <summary>Версия схемы манифеста.</summary>
    [JsonProperty("schema_version")]
    public string SchemaVersion { get; set; } = "1.0";

    /// <summary>Идентификатор выполненного прогона.</summary>
    [JsonProperty("harness_id")]
    public string HarnessId { get; set; }

    /// <summary>Число обработанных кейсов.</summary>
    [JsonProperty("row_count")]
    public int RowCount { get; set; }

    /// <summary>Число кейсов с непустым полем <see cref="HomeostasisHarnessResultRow.Error"/>.</summary>
    [JsonProperty("errors_count")]
    public int ErrorsCount { get; set; }

    /// <summary>Длительность прогона, мс.</summary>
    [JsonProperty("elapsed_ms")]
    public long ElapsedMs { get; set; }

    /// <summary>Полный путь к входному JSON.</summary>
    [JsonProperty("input_path")]
    public string InputPath { get; set; }

    /// <summary>Полный путь к results.jsonl.</summary>
    [JsonProperty("output_jsonl")]
    public string OutputJsonl { get; set; }

    /// <summary>Полный путь к results.csv.</summary>
    [JsonProperty("output_csv")]
    public string OutputCsv { get; set; }

    /// <summary>Полный путь к report.html (создаётся вне движка при необходимости).</summary>
    [JsonProperty("output_report_html")]
    public string OutputReportHtml { get; set; }
  }

  /// <summary>Пути и счётчики после вызова прогона.</summary>
  public sealed class HomeostasisHarnessRunResult
  {
    /// <summary>true, если файлы результатов записаны без фатальной ошибки.</summary>
    public bool Success { get; set; }

    /// <summary>Сообщение об ошибке при <see cref="Success"/> == false.</summary>
    public string ErrorMessage { get; set; }

    /// <summary>Манифест при успешном завершении.</summary>
    public HomeostasisHarnessManifest Manifest { get; set; }
  }
}
