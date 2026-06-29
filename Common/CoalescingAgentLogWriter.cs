using System;

namespace ISIDA.Common
{
  /// <summary>
  /// Сливает несколько симбионтных строк <see cref="ResearchLogger"/> с одним глобальным пульсом в один снимок для UI
  /// (аналогично <c>ScenarioLogComparer.AggregateByPulse</c>). После каждого входящего снимка вызывает <see cref="ILogWriter.WriteLog"/>
  /// с уже объединённым состоянием — приёмник должен заменять последнюю строку того же пульса, а не только добавлять.
  /// Полный лог для отчётов сценария остаётся на отдельном <see cref="ILogWriter"/> без этого обёртки.
  /// </summary>
  public sealed class CoalescingAgentLogWriter : ILogWriter
  {
    private const string AgentClassName = "ResearchLogger";
    private const string AgentMethod = "LogSystemState";

    private readonly ILogWriter _inner;
    private readonly object _lock = new object();

    private PendingRow _pending;

    /// <summary>
    /// Создаёт обёртку, передающую объединённые симбионтные строки во внутренний писатель.
    /// </summary>
    /// <param name="inner">Приёмник после слияния по пульсу (например UI с заменой строки при том же <c>pulse</c>).</param>
    public CoalescingAgentLogWriter(ILogWriter inner)
    {
      _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    /// <summary>Сбрасывает накопленный снимок (например, при очистке логов UI).</summary>
    public void ResetPending()
    {
      lock (_lock)
      {
        _pending = null;
      }
    }

    /// <summary>
    /// Записывает системный лог во внутренний писатель. Для <c>ResearchLogger</c> / <c>LogSystemState</c> с заданным пульсом
    /// объединяет поля с предыдущими вызовами того же глобального пульса; иначе сбрасывает накопленное и пробрасывает вызов.
    /// Семантика параметров — как у <see cref="ILogWriter.WriteLog"/>.
    /// </summary>
    public void WriteLog(string className, string method, int? pulse, int? baseId,
        int? baseStyleId, int? triggerStimulusId, int? orientationReflexType,
        int? geneticReflexId, int? conditionedReflexId, int? automatizmId = null,
        string reflexChainInfo = null, string automatizmChainInfo = null,
        int? thinkingLevel = null, bool? thinkingLevelSuccess = null,
        int? thinkingThemeTypeId = null, string thinkingThemeTooltip = null,
        int? mainThinkingCycleId = null,         string mainThinkingCycleTooltip = null,
        string mainThinkingCycleTaskStatus = null,
        bool informationEnvironmentDanger = false,
        bool informationEnvironmentVeryActual = false,
        int? automatizmUsefulnessAtSnapshot = null,
        string backgroundThinkingCyclesJson = null,
        string environmentPressureCell = null,
        string environmentPressureTooltip = null)
    {
      lock (_lock)
      {
        if (!string.Equals(className, AgentClassName, StringComparison.Ordinal)
            || !string.Equals(method, AgentMethod, StringComparison.Ordinal)
            || !pulse.HasValue)
        {
          _pending = null;
          _inner.WriteLog(className, method, pulse, baseId, baseStyleId, triggerStimulusId,
              orientationReflexType, geneticReflexId, conditionedReflexId, automatizmId,
              reflexChainInfo, automatizmChainInfo, thinkingLevel, thinkingLevelSuccess,
              thinkingThemeTypeId, thinkingThemeTooltip, mainThinkingCycleId,
              mainThinkingCycleTooltip, mainThinkingCycleTaskStatus, informationEnvironmentDanger,
              informationEnvironmentVeryActual, automatizmUsefulnessAtSnapshot,
              backgroundThinkingCyclesJson, environmentPressureCell, environmentPressureTooltip);
          return;
        }

        int p = pulse.Value;
        if (_pending == null || _pending.Pulse != p)
          _pending = PendingRow.FromSnapshot(className, method, p, baseId, baseStyleId,
              triggerStimulusId, orientationReflexType, geneticReflexId, conditionedReflexId,
              automatizmId, reflexChainInfo, automatizmChainInfo, thinkingLevel,
              thinkingLevelSuccess, thinkingThemeTypeId, thinkingThemeTooltip,
              mainThinkingCycleId, mainThinkingCycleTooltip, mainThinkingCycleTaskStatus,
              informationEnvironmentDanger, informationEnvironmentVeryActual, automatizmUsefulnessAtSnapshot,
              backgroundThinkingCyclesJson, environmentPressureCell, environmentPressureTooltip);
        else
          _pending.Merge(baseId, baseStyleId, triggerStimulusId, orientationReflexType,
              geneticReflexId, conditionedReflexId, automatizmId, reflexChainInfo,
              automatizmChainInfo, thinkingLevel, thinkingLevelSuccess, thinkingThemeTypeId,
              thinkingThemeTooltip, mainThinkingCycleId, mainThinkingCycleTooltip,
              mainThinkingCycleTaskStatus, informationEnvironmentDanger, informationEnvironmentVeryActual,
              automatizmUsefulnessAtSnapshot, backgroundThinkingCyclesJson,
              environmentPressureCell, environmentPressureTooltip);

        _pending.WriteTo(_inner);
      }
    }

    /// <remarks>Не используется для канала отображения симбионта; полные логи параметров идут в основной <see cref="ILogWriter"/>.</remarks>
    public void WriteParameterLog(int pulse, int paramId, string paramName, int weight,
        int normaWell, int speed, float value, float urgencyFunction,
        string parameterState, string activationZone)
    {
    }

    /// <inheritdoc cref="WriteParameterLog"/>
    public void WriteStyleLog(int pulse, string stage, int styleId, string styleName)
    {
    }

    /// <inheritdoc cref="WriteParameterLog"/>
    public void WriteStyleParameterActivation(int pulse, string stage, int parameterId, string parameterName,
        int zoneId, string zoneDescription, int styleId, string styleName,
        string activationDetails)
    {
    }

    private sealed class PendingRow
    {
      public string ClassName;
      public string Method;
      public int Pulse;
      public int? BaseId;
      public int? BaseStyleId;
      public int? TriggerStimulusId;
      public int? OrientationReflexType;
      public int? GeneticReflexId;
      public int? ConditionedReflexId;
      public int? AutomatizmId;
      public string ReflexChainInfo;
      public string AutomatizmChainInfo;
      public int? ThinkingLevel;
      public bool? ThinkingLevelSuccess;
      public int? ThinkingThemeTypeId;
      public string ThinkingThemeTooltip;
      public int? MainThinkingCycleId;
      public string MainThinkingCycleTooltip;
      public string MainThinkingCycleTaskStatus;
      public bool InformationEnvironmentDanger;
      public bool InformationEnvironmentVeryActual;
      public int? AutomatizmUsefulnessAtSnapshot;
      public string BackgroundThinkingCyclesJson;
      public string EnvironmentPressureCell;
      public string EnvironmentPressureTooltip;

      public static PendingRow FromSnapshot(string className, string method, int pulse,
          int? baseId, int? baseStyleId, int? triggerStimulusId, int? orientationReflexType,
          int? geneticReflexId, int? conditionedReflexId, int? automatizmId,
          string reflexChainInfo, string automatizmChainInfo, int? thinkingLevel,
          bool? thinkingLevelSuccess, int? thinkingThemeTypeId, string thinkingThemeTooltip,
          int? mainThinkingCycleId, string mainThinkingCycleTooltip,
          string mainThinkingCycleTaskStatus,           bool informationEnvironmentDanger,
          bool informationEnvironmentVeryActual, int? automatizmUsefulnessAtSnapshot,
          string backgroundThinkingCyclesJson, string environmentPressureCell, string environmentPressureTooltip)
      {
        return new PendingRow
        {
          ClassName = className ?? string.Empty,
          Method = method ?? string.Empty,
          Pulse = pulse,
          BaseId = baseId,
          BaseStyleId = ZeroToNull(baseStyleId),
          TriggerStimulusId = ZeroToNull(triggerStimulusId),
          OrientationReflexType = ZeroToNull(orientationReflexType),
          GeneticReflexId = ZeroToNull(geneticReflexId),
          ConditionedReflexId = ZeroToNull(conditionedReflexId),
          AutomatizmId = ZeroToNull(automatizmId),
          ReflexChainInfo = reflexChainInfo ?? string.Empty,
          AutomatizmChainInfo = automatizmChainInfo ?? string.Empty,
          ThinkingLevel = thinkingLevel,
          ThinkingLevelSuccess = thinkingLevelSuccess,
          ThinkingThemeTypeId = PositiveOrNull(thinkingThemeTypeId),
          ThinkingThemeTooltip = string.IsNullOrEmpty(thinkingThemeTooltip) ? null : thinkingThemeTooltip,
          MainThinkingCycleId = PositiveOrNull(mainThinkingCycleId),
          MainThinkingCycleTooltip = string.IsNullOrEmpty(mainThinkingCycleTooltip) ? null : mainThinkingCycleTooltip,
          MainThinkingCycleTaskStatus = string.IsNullOrEmpty(mainThinkingCycleTaskStatus) ? null : mainThinkingCycleTaskStatus,
          InformationEnvironmentDanger = informationEnvironmentDanger,
          InformationEnvironmentVeryActual = informationEnvironmentVeryActual,
          AutomatizmUsefulnessAtSnapshot = automatizmUsefulnessAtSnapshot,
          BackgroundThinkingCyclesJson = string.IsNullOrWhiteSpace(backgroundThinkingCyclesJson)
              ? null
              : backgroundThinkingCyclesJson,
          EnvironmentPressureCell = string.IsNullOrWhiteSpace(environmentPressureCell) ? null : environmentPressureCell,
          EnvironmentPressureTooltip = string.IsNullOrWhiteSpace(environmentPressureTooltip) ? null : environmentPressureTooltip
        };
      }

      public void Merge(int? baseId, int? baseStyleId, int? triggerStimulusId,
          int? orientationReflexType, int? geneticReflexId, int? conditionedReflexId,
          int? automatizmId, string reflexChainInfo, string automatizmChainInfo,
          int? thinkingLevel, bool? thinkingLevelSuccess, int? thinkingThemeTypeId,
          string thinkingThemeTooltip, int? mainThinkingCycleId, string mainThinkingCycleTooltip,
          string mainThinkingCycleTaskStatus, bool informationEnvironmentDanger,
          bool informationEnvironmentVeryActual, int? automatizmUsefulnessAtSnapshot,
          string backgroundThinkingCyclesJson, string environmentPressureCell, string environmentPressureTooltip)
      {
        if (baseId.HasValue)
          BaseId = baseId;
        BaseStyleId = MergeId(BaseStyleId, baseStyleId);
        TriggerStimulusId = MergeId(TriggerStimulusId, triggerStimulusId);
        OrientationReflexType = MergeId(OrientationReflexType, orientationReflexType);
        GeneticReflexId = MergeId(GeneticReflexId, geneticReflexId);
        ConditionedReflexId = MergeId(ConditionedReflexId, conditionedReflexId);
        AutomatizmId = MergeId(AutomatizmId, automatizmId);
        ReflexChainInfo = MergeText(ReflexChainInfo, reflexChainInfo);
        AutomatizmChainInfo = MergeText(AutomatizmChainInfo, automatizmChainInfo);
        if (thinkingLevel.HasValue)
          ThinkingLevel = thinkingLevel;
        if (thinkingLevelSuccess.HasValue)
          ThinkingLevelSuccess = thinkingLevelSuccess;
        ThinkingThemeTypeId = MergePositive(ThinkingThemeTypeId, thinkingThemeTypeId);
        ThinkingThemeTooltip = MergeTooltip(ThinkingThemeTooltip, thinkingThemeTooltip);
        MainThinkingCycleId = MergePositive(MainThinkingCycleId, mainThinkingCycleId);
        MainThinkingCycleTooltip = MergeTooltip(MainThinkingCycleTooltip, mainThinkingCycleTooltip);
        MainThinkingCycleTaskStatus = MergeTooltip(MainThinkingCycleTaskStatus, mainThinkingCycleTaskStatus);
        InformationEnvironmentDanger = informationEnvironmentDanger;
        InformationEnvironmentVeryActual = informationEnvironmentVeryActual;
        if (automatizmUsefulnessAtSnapshot.HasValue)
          AutomatizmUsefulnessAtSnapshot = automatizmUsefulnessAtSnapshot;
        BackgroundThinkingCyclesJson = MergeTooltip(BackgroundThinkingCyclesJson, backgroundThinkingCyclesJson);
        EnvironmentPressureCell = MergeTooltip(EnvironmentPressureCell, environmentPressureCell);
        EnvironmentPressureTooltip = MergeTooltip(EnvironmentPressureTooltip, environmentPressureTooltip);
      }

      public void WriteTo(ILogWriter inner)
      {
        inner.WriteLog(ClassName, Method, Pulse, BaseId, BaseStyleId, TriggerStimulusId,
            OrientationReflexType, GeneticReflexId, ConditionedReflexId, AutomatizmId,
            ReflexChainInfo, AutomatizmChainInfo, ThinkingLevel, ThinkingLevelSuccess,
            ThinkingThemeTypeId, ThinkingThemeTooltip, MainThinkingCycleId,
            MainThinkingCycleTooltip, MainThinkingCycleTaskStatus, InformationEnvironmentDanger,
            InformationEnvironmentVeryActual, AutomatizmUsefulnessAtSnapshot, BackgroundThinkingCyclesJson,
            EnvironmentPressureCell, EnvironmentPressureTooltip);
      }

      private static int? ZeroToNull(int? v) => v == 0 ? null : v;

      private static int? PositiveOrNull(int? v) => v.HasValue && v.Value > 0 ? v : null;

      private static int? MergeId(int? cur, int? inc)
      {
        var n = ZeroToNull(inc);
        return n ?? cur;
      }

      private static int? MergePositive(int? cur, int? inc)
      {
        var n = PositiveOrNull(inc);
        return n ?? cur;
      }

      private static string MergeText(string cur, string inc)
      {
        if (!string.IsNullOrEmpty(inc))
          return inc;
        return cur ?? string.Empty;
      }

      private static string MergeTooltip(string cur, string inc)
      {
        if (!string.IsNullOrEmpty(inc))
          return inc;
        return cur;
      }
    }
  }
}
