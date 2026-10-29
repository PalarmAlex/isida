using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс Случая 28: после того как движок сам начал снимать состояние на каждом пульсе
  /// (<see cref="GlobalTimer.ProcessAgentPulse"/> → <see cref="ResearchLogger.LogSystemState"/>),
  /// повторный снимок того же пульса затирал буфер-дельту, зафиксированную активатором.
  ///
  /// Порядок рантаймового пульса: <c>ReflexesActivator.ActiveFrom…</c> вызывает
  /// <c>LogSystemState</c>, пока <c>_activeGeneticReflexID</c> ещё выставлен, и сразу после этого
  /// обнуляет его (это механизм дельта-колонок — «Б/у рефлекс» пишется один раз при запуске).
  /// Концовочный снимок видит переход 18→null, считает его штатной дельтой и перезаписывал буфер
  /// пустой строкой тем же пульсом — в CSV/UI/HTML-отчёт сценария уходила строка без
  /// «Б/у рефлекс»/«Усл. рефлекс», хотя рефлекс на пульсе реально выполнялся.
  /// </summary>
  /// <remarks>
  /// Тест не поднимает <see cref="GlobalTimer"/>: достаточно двух вызовов <c>LogSystemState</c>
  /// на одном пульсе с тем же переходом состояния, что делает и движок. Колонки наблюдаются через
  /// <see cref="ILogWriter.WriteLog"/> — тот же путь, что CSV-строка и память UI.
  /// </remarks>
  [Collection("EngineIntegration")]
  public sealed class ReflexDeltaClobberTests : EngineIntegrationTestBase
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public ReflexDeltaClobberTests(EngineFixture engine) : base(engine)
    {
      EnsureActivator();
    }

    /// <summary>
    /// Поднимает сервис выполнения и активатор поверх фикстуры (порядок как в IsidaEngine),
    /// чтобы <see cref="ResearchLogger"/> мог читать активные ID рефлексов.
    /// </summary>
    private static void EnsureActivator()
    {
      if (!ReflexExecutionService.IsInitialized)
      {
        ReflexExecutionService.InitializeInstance(
            AdaptiveActionsSystem.Instance,
            InfluenceActionSystem.Instance,
            GeneticReflexesSystem.Instance,
            ConditionedReflexesSystem.Instance);
      }

      if (!ReflexesActivator.IsInitialized)
      {
        ReflexesActivator.InitializeInstance(
            GomeostasSystem.Instance,
            GeneticReflexesSystem.Instance,
            ConditionedReflexesSystem.Instance,
            InfluenceActionSystem.Instance,
            ReflexTreeSystem.Instance,
            ReflexChainsSystem.Instance,
            ReflexExecutionService.Instance,
            AdaptiveActionsSystem.Instance,
            ConditionedReflexFormationService.Instance,
            PerceptionImagesSystem.Instance);
      }
    }

    private static void SetField(object target, string name, object value)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      Assert.NotNull(field);
      field.SetValue(target, value);
    }

    /// <summary>
    /// Приводит дельта-поля активатора к нулю. <see cref="EngineFixture.ResetToCleanState"/>
    /// их НЕ сбрасывает (это рантаймовое состояние активации), а в полном прогоне коллекции
    /// предыдущий тест может оставить <c>_activeConditionReflexID &gt; 0</c>. Поскольку в
    /// <c>CollectSystemState</c> условный рефлекс приоритетнее б/у (при cond&gt;0 б/у обнуляется),
    /// такой остаток глушит тестируемую колонку — тест зелёный изолированно и падает в общем прогоне.
    /// </summary>
    private static void ClearActivatorReflexIds(ReflexesActivator activator)
    {
      SetField(activator, "_activeGeneticReflexID", 0);
      SetField(activator, "_activeConditionReflexID", 0);
    }

    private static ResearchLogger CreateLogger(ReflexesActivator activator, string tempDir)
    {
      return new ResearchLogger(
          GomeostasSystem.Instance,
          PerceptionImagesSystem.Instance,
          activator,
          AdaptiveActionsSystem.Instance,
          logsDirectory: tempDir,
          logFileName: "ReflexDeltaClobberTests",
          format: ResearchLogger.LogFormat.None,
          clearOnStart: true,
          enabled: true);
    }

    /// <summary>
    /// Снимок активатора (б/у рефлекс ещё активен) фиксирует дельту в буфере, а сброс
    /// <c>_activeGeneticReflexID</c> (как это делает <c>ResetStates</c> в конце пульса) не даёт
    /// повторному снимку того же пульса затереть её.
    /// </summary>
    [Fact]
    public void SecondSnapshotOnSamePulse_KeepsGeneticReflexDelta()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_reflexdelta_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);

      var activator = ReflexesActivator.Instance;
      var logger = CreateLogger(activator, tempDir);
      var writer = new RecordingLogWriter();
      logger.SetMemoryLogWriter(writer);
      try
      {
        int pulse = Engine.CurrentPulse;
        ClearActivatorReflexIds(activator);
        SetField(activator, "_activeGeneticReflexID", 18);

        // Снимок активатора: рефлекс ещё активен → в буфере «Б/у рефлекс» = 18.
        logger.LogSystemState(pulse);

        // Конец активации: активатор гасит ID, чтобы он не повторялся на следующих пульсах.
        SetField(activator, "_activeGeneticReflexID", 0);

        // Снимок движка в конце того же пульса (GlobalTimer) — до фикса он Перезаписывал буфер
        // пустой строкой, потому что переход 18→null выглядел как обычная дельта.
        logger.LogSystemState(pulse);
        logger.FlushBufferedAgentRowToMemoryNow();

        Assert.NotEmpty(writer.Rows);
        var row = writer.Rows[writer.Rows.Count - 1];
        Assert.Equal(pulse, row.Pulse);
        Assert.Equal(18, row.GeneticReflexId);
      }
      finally
      {
        ClearActivatorReflexIds(activator);
        logger.SetMemoryLogWriter(null);
        logger.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    /// <summary>
    /// Тот же переход для условного рефлекса: он приоритетнее б/у в <c>CollectSystemState</c>,
    /// и затирается повторным снимком точно так же.
    /// </summary>
    [Fact]
    public void SecondSnapshotOnSamePulse_KeepsConditionedReflexDelta()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_reflexdelta_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);

      var activator = ReflexesActivator.Instance;
      var logger = CreateLogger(activator, tempDir);
      var writer = new RecordingLogWriter();
      logger.SetMemoryLogWriter(writer);
      try
      {
        int pulse = Engine.CurrentPulse;
        ClearActivatorReflexIds(activator);
        SetField(activator, "_activeConditionReflexID", 3);

        logger.LogSystemState(pulse);

        SetField(activator, "_activeConditionReflexID", 0);

        logger.LogSystemState(pulse);
        logger.FlushBufferedAgentRowToMemoryNow();

        Assert.NotEmpty(writer.Rows);
        var row = writer.Rows[writer.Rows.Count - 1];
        Assert.Equal(3, row.ConditionedReflexId);
      }
      finally
      {
        ClearActivatorReflexIds(activator);
        logger.SetMemoryLogWriter(null);
        logger.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    /// <summary>
    /// Страховка от перегиба в другую сторону: на следующем пульсе повторный снимок обязан
    /// работать как раньше — иначе движок перестал бы писать строки вообще.
    /// </summary>
    [Fact]
    public void NextPulse_StillWritesNewState()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_reflexdelta_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);

      var activator = ReflexesActivator.Instance;
      var logger = CreateLogger(activator, tempDir);
      var writer = new RecordingLogWriter();
      logger.SetMemoryLogWriter(writer);
      try
      {
        int pulse = Engine.CurrentPulse;
        ClearActivatorReflexIds(activator);
        SetField(activator, "_activeGeneticReflexID", 5);
        logger.LogSystemState(pulse);
        SetField(activator, "_activeGeneticReflexID", 0);
        logger.LogSystemState(pulse);
        logger.FlushBufferedAgentRowToMemoryNow();

        int firstPulseRows = writer.Rows.Count;
        Assert.Equal(1, firstPulseRows);
        Assert.Equal(5, writer.Rows[0].GeneticReflexId);

        // Следующий пульс: буфер пуст, снимок движка обязан снова породить строку.
        int nextPulse = pulse + 1;
        SetField(activator, "_activeGeneticReflexID", 19);
        logger.LogSystemState(nextPulse);
        logger.FlushBufferedAgentRowToMemoryNow();

        Assert.Equal(2, writer.Rows.Count);
        Assert.Equal(19, writer.Rows[1].GeneticReflexId);
      }
      finally
      {
        ClearActivatorReflexIds(activator);
        logger.SetMemoryLogWriter(null);
        logger.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    private static void TryDeleteDir(string dir)
    {
      try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>
    /// Наблюдатель симбионтной строки: фиксирует пульс и дельта-колонки рефлексов из
    /// <see cref="ILogWriter.WriteLog"/> (параметры 8 и 9), остальные — не значимы для теста.
    /// </summary>
    private sealed class RecordingLogWriter : ILogWriter
    {
      public sealed class Row
      {
        public int Pulse { get; set; }
        public int? GeneticReflexId { get; set; }
        public int? ConditionedReflexId { get; set; }
      }

      public List<Row> Rows { get; } = new List<Row>();

      public void WriteLog(string className, string method, int? pulse, int? baseId,
          int? baseStyleId, int? triggerStimulusId, int? orientationReflexType,
          int? geneticReflexId, int? conditionedReflexId, int? automatizmId = null,
          string reflexChainInfo = null, string automatizmChainInfo = null,
          int? thinkingLevel = null, bool? thinkingLevelSuccess = null,
          int? thinkingThemeTypeId = null, string thinkingThemeTooltip = null,
          int? mainThinkingCycleId = null, string mainThinkingCycleTooltip = null,
          string mainThinkingCycleTaskStatus = null, bool informationEnvironmentDanger = false,
          bool informationEnvironmentVeryActual = false, int? automatizmUsefulnessAtSnapshot = null,
          string backgroundThinkingCyclesJson = null, string environmentPressureCell = null,
          string environmentPressureTooltip = null)
      {
        Rows.Add(new Row
        {
          Pulse = pulse ?? -1,
          GeneticReflexId = geneticReflexId,
          ConditionedReflexId = conditionedReflexId
        });
      }

      public void WriteParameterLog(int pulse, int paramId, string paramName, int weight,
          int normaWell, int speed, float value, float urgencyFunction,
          string parameterState, string activationZone) { }

      public void WriteStyleLog(int pulse, string stage, int styleId, string styleName) { }

      public void WriteStyleParameterActivation(int pulse, string stage, int parameterId,
          string parameterName, int zoneId, string zoneDescription, int styleId,
          string styleName, string activationDetails) { }
    }
  }
}
