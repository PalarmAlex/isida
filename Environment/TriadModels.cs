using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Происхождение изменения гомеостаза Creature (только для логов и API исследователя, не для восприятия).
  /// </summary>
  public enum StimulusOrigin
  {
    /// <summary>Неизвестный источник.</summary>
    Unknown = 0,

    /// <summary>Воздействие оператора (пульт, сценарий).</summary>
    Operator = 1,

    /// <summary>Отклик Niche через coupling.</summary>
    Niche = 2,

    /// <summary>Спontaneous drift / метаболизм Creature.</summary>
    CreatureSelf = 3,

    /// <summary>Автономная динамика Niche без действия Creature.</summary>
    Spontaneous = 4
  }

  /// <summary>
  /// Тип сигнала оператора (фазовые ограничения §6.2).
  /// </summary>
  public enum AssessmentType
  {
    /// <summary>Начальная настройка, прямое переопределение (фаза A).</summary>
    Bootstrap = 0,

    /// <summary>Демонстрация, зеркало, ритуал (фаза B).</summary>
    RitualScaffold = 1,

    /// <summary>Meta: нарушение ритуала (фаза C).</summary>
    RitualViolation = 2,

    /// <summary>Аварийное переопределение.</summary>
    EmergencyOverride = 3
  }

  /// <summary>
  /// Фаза педагогической песочницы (§0.7).
  /// </summary>
  public enum TriadPhase
  {
    /// <summary>Щадящие условия, Operator модулирует среду.</summary>
    A = 0,

    /// <summary>Ритуал + coupling Niche.</summary>
    B = 1,

    /// <summary>Operator только через Niche.</summary>
    C = 2
  }

  /// <summary>
  /// Тип сброса диады Creature↔Niche (§6.12).
  /// </summary>
  public enum DyadResetType
  {
    /// <summary>Параметры Niche → NicheInitSnapshot.</summary>
    NicheSoft = 0,

    /// <summary>Гомеостаз Creature → норма; прервать окна AOE.</summary>
    CreatureSoft = 1,

    /// <summary>Niche + Creature → начальные снимки; новый experiment_run_id.</summary>
    DyadHard = 2,

    /// <summary>Только калибровка параметров Niche в диапазоне эксперимента.</summary>
    Calibration = 3
  }

  /// <summary>
  /// Coupling Operator→Niche (фаза C, §6.4).
  /// </summary>
  public sealed class OperatorNicheCouplingTarget
  {
    /// <summary>ID воздействия с пульта (InfluenceAction).</summary>
    public int InfluenceActionId { get; set; }

    /// <summary>ID параметра Niche.</summary>
    public int NicheParamId { get; set; }

    /// <summary>Базовая дельта.</summary>
    public float Delta { get; set; }

    /// <summary>Множитель.</summary>
    public float Scale { get; set; }
  }

  /// <summary>
  /// Результат сброса диады.
  /// </summary>
  public sealed class DyadResetResult
  {
    /// <summary>Тип выполненного сброса.</summary>
    public DyadResetType ResetType { get; set; }

    /// <summary>True, если сброс выполнен.</summary>
    public bool Success { get; set; }

    /// <summary>experiment_run_id после сброса (при DyadHard — новый).</summary>
    public string ExperimentRunId { get; set; }

    /// <summary>coupling_mapping_version на момент сброса.</summary>
    public int CouplingMappingVersion { get; set; }

    /// <summary>Описание для лога.</summary>
    public string Message { get; set; }
  }

  /// <summary>
  /// Исход окна AOE (§5.3.2).
  /// </summary>
  public enum AoeOutcome
  {
    /// <summary>Не классифицировано.</summary>
    None = 0,

    /// <summary>Обусловленный отклик + положительный Δ.</summary>
    Success = 1,

    /// <summary>Обусловленный отклик + отрицательный Δ.</summary>
    Fail = 2,

    /// <summary>Niche не откликнулась.</summary>
    NoResponse = 3,

    /// <summary>Δ без выделенного niche_response.</summary>
    Ambiguous = 4
  }

  /// <summary>
  /// Цель coupling: параметр Niche, изменяемый действием Creature (§6.7).
  /// </summary>
  public sealed class CouplingTarget
  {
    /// <summary>ID адаптивного действия Creature.</summary>
    public int ActionId { get; set; }

    /// <summary>ID параметра Niche.</summary>
    public int NicheParamId { get; set; }

    /// <summary>Базовая дельта при срабатывании coupling.</summary>
    public float Delta { get; set; }

    /// <summary>Множитель (например интенсivity действия).</summary>
    public float Scale { get; set; }
  }

  /// <summary>
  /// Статическое отображение параметра Niche на параметр Creature.
  /// </summary>
  public sealed class NicheCreatureMapping
  {
    /// <summary>ID параметра Niche.</summary>
    public int NicheParamId { get; set; }

    /// <summary>ID параметра Creature.</summary>
    public int CreatureParamId { get; set; }

    /// <summary>Масштаб переноса значения.</summary>
    public float Scale { get; set; }

    /// <summary>Задержка в пульсах (MVP: 0).</summary>
    public int LagPulses { get; set; }
  }

  /// <summary>
  /// Описание параметра host-Niche.
  /// </summary>
  public sealed class NicheParameterDef
  {
    /// <summary>ID параметра Niche.</summary>
    public int ParamId { get; set; }

    /// <summary>Начальное значение (0…100).</summary>
    public float InitialValue { get; set; }

    /// <summary>Дрейф за такт (signed Speed/100); 0 — статичная Niche.</summary>
    public float SpeedPerPulse { get; set; }
  }

  /// <summary>
  /// Конфигурация эксперимента триады (загрузка из каталога Environment).
  /// </summary>
  public sealed class TriadExperimentConfig
  {
    /// <summary>Фаза A/B/C.</summary>
    public TriadPhase Phase { get; set; } = TriadPhase.A;

    /// <summary>Идентификатор контура (§6.8).</summary>
    public string ContourId { get; set; } = "static_mvp";

    /// <summary>Включён ли спонтанный дрейф параметров Niche.</summary>
    public bool SpontaneousDriftEnabled { get; set; }

    /// <summary>Версия таблицы coupling для лога прогона.</summary>
    public int CouplingMappingVersion { get; set; } = 1;

    /// <summary>Coupling action→Niche.</summary>
    public List<CouplingTarget> ActionCoupling { get; set; } = new List<CouplingTarget>();

    /// <summary>Coupling Operator→Niche (фаза C).</summary>
    public List<OperatorNicheCouplingTarget> OperatorNicheCoupling { get; set; } = new List<OperatorNicheCouplingTarget>();

    /// <summary>Отображение Niche→Creature.</summary>
    public List<NicheCreatureMapping> NicheToCreature { get; set; } = new List<NicheCreatureMapping>();

    /// <summary>Параметры host-Niche.</summary>
    public List<NicheParameterDef> NicheParameters { get; set; } = new List<NicheParameterDef>();

    /// <summary>Параметры первичного AOE по Niche (§5.3).</summary>
    public TriadAoeSettings AoeSettings { get; set; } = new TriadAoeSettings();

    /// <summary>Использовать полный NicheEngine вместо голого host-state (§4.5).</summary>
    public bool UseFullNicheEngine { get; set; }

    /// <summary>Идентификатор RoleProfile Niche (niche_minimal, niche_reactive).</summary>
    public string NicheRoleProfileId { get; set; } = "niche_minimal";

    /// <summary>Контур через EnvironmentMetricProbeKey (contour_probes.dat).</summary>
    public bool UseProbeContour { get; set; }

    /// <summary>Mapping probeKey→Niche (contour_probes.dat).</summary>
    public List<ContourProbeCoupling> ContourProbes { get; set; } = new List<ContourProbeCoupling>();

    /// <summary>True, если загружен хотя бы один mapping или параметр Niche.</summary>
    public bool HasCouplingData =>
        ActionCoupling.Count > 0 || NicheToCreature.Count > 0 || NicheParameters.Count > 0;
  }

  /// <summary>
  /// Coupling пробы контура на параметр Niche (contour_probes.dat).
  /// </summary>
  public sealed class ContourProbeCoupling
  {
    /// <summary>Ключ пробы (EnvironmentMetricProbeKey).</summary>
    public string ProbeKey { get; set; } = string.Empty;

    /// <summary>ID параметра Niche.</summary>
    public int NicheParamId { get; set; }

    /// <summary>Дельта при активации пробы.</summary>
    public float Delta { get; set; }
  }

  /// <summary>
  /// Запись лога диады за один такт (§5.3.1).
  /// </summary>
  public sealed class DyadPulseLogEntry
  {
    /// <summary>Глобальный номер пульса.</summary>
    public int Pulse { get; set; }

    /// <summary>ID действия Creature на такте (0 если не было).</summary>
    public int CreatureActionId { get; set; }

    /// <summary>Состояние Niche до такта (paramId→value).</summary>
    public Dictionary<int, float> NicheStateBefore { get; set; }

    /// <summary>Состояние Niche после такта.</summary>
    public Dictionary<int, float> NicheStateAfter { get; set; }

    /// <summary>Гомеостаз Creature до mapping Niche.</summary>
    public Dictionary<int, float> CreatureGomeoBefore { get; set; }

    /// <summary>Гомеостаз Creature после mapping (до UpdateStateOnly drift).</summary>
    public Dictionary<int, float> CreatureGomeoAfterMapping { get; set; }

    /// <summary>Происхождение последнего batch-update Creature.</summary>
    public StimulusOrigin LastCreatureUpdateOrigin { get; set; }

    /// <summary>Спontaneous delta Niche за такт.</summary>
    public Dictionary<int, float> NicheSpontaneousDelta { get; set; }

    /// <summary>Response delta Niche (после действия Creature).</summary>
    public Dictionary<int, float> NicheResponseDelta { get; set; }

    /// <summary>ContourId активного контура.</summary>
    public string ContourId { get; set; }

    /// <summary>Число реактивных рефлексов Niche на такте (§1.4).</summary>
    public int NicheReflexesApplied { get; set; }

    /// <summary>RoleProfile Niche на такте.</summary>
    public string NicheRoleProfileId { get; set; }

    /// <summary>EnvironmentMetricProbeKey, применённый на такте (§6.8).</summary>
    public string ContourProbeKey { get; set; }

    /// <summary>Dim InputSnapshot контура на такте.</summary>
    public int ContourInputDim { get; set; }

    /// <summary>Дельты Niche от contour InputSnapshot (отдельно от spontaneous).</summary>
    public Dictionary<int, float> ContourInputDelta { get; set; }

    /// <summary>experiment_run_id на такте.</summary>
    public string ExperimentRunId { get; set; }

    /// <summary>coupling_mapping_version на такте.</summary>
    public int CouplingMappingVersion { get; set; }
  }

  /// <summary>
  /// Снимок инициализации Niche (§6.11).
  /// </summary>
  public sealed class NicheInitSnapshot
  {
    /// <summary>Момент фиксации.</summary>
    public DateTime CapturedAtUtc { get; set; }

    /// <summary>Идентификатор прогона эксперимента.</summary>
    public string ExperimentRunId { get; set; }

    /// <summary>Каталог Environment.</summary>
    public string EnvironmentFolder { get; set; }

    /// <summary>Конфигурация триады на момент старта.</summary>
    public TriadExperimentConfig Config { get; set; }

    /// <summary>Начальные значения параметров Niche.</summary>
    public Dictionary<int, float> InitialNicheParams { get; set; }

    /// <summary>Начальные значения параметров Creature (NormaWell).</summary>
    public Dictionary<int, float> InitialCreatureParams { get; set; }
  }
}
