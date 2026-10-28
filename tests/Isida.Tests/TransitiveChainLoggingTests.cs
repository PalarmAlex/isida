using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс Случая 27: транзитивная цепочка УР в адаптере Velum не попадала в колонку
  /// «Цепочка РФ», потому что (а) виртуальная цепочка не регистрировала сессию в
  /// <see cref="ResearchLogger"/> и все <c>LogChainLink…</c> молча выходили по
  /// <c>_activeChains.TryGetValue</c>; (б) <c>motorActionId</c> звена оставался 0;
  /// (в) колонку формирует только <see cref="ResearchLogger.LogSystemState"/>, которую Velum
  /// не вызывал на пульсах звеньев.
  ///
  /// Поток повторяет рантаймовый (как в <see cref="TransitiveReplayIntegrationTests"/>):
  /// фраза → УР1 → построена цепочка → хост подтвердил стартовый мотор → ProcessActiveChain
  /// разворачивает звено (УР2). На каждом шаге проверяется то, что раньше терялось.
  /// </summary>
  /// <remarks>
  /// <see cref="ResearchLogger"/> создаётся напрямую на временном каталоге с
  /// <see cref="ResearchLogger.LogFormat.None"/> — файловые writers не открываются, но
  /// регистрация сессий (<c>_activeChains</c>), копилка пульсов (<c>_chainInfoByPulse</c>) и
  /// писатель в память (<c>SetMemoryLogWriter</c>) работают штатно. Это даёт наблюдаемый
  /// эквивалент CSV-колонки без UI и без живого <see cref="GlobalTimer"/>.
  /// </remarks>
  [Collection("EngineIntegration")]
  public sealed class TransitiveChainLoggingTests : EngineIntegrationTestBase
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public TransitiveChainLoggingTests(EngineFixture engine) : base(engine)
    {
      EnsureActivator();
    }

    /// <summary>
    /// Поднимает сервис выполнения и активатор поверх фикстуры (порядок как в IsidaEngine:
    /// ReflexExecutionService раньше ReflexesActivator), если предыдущий тест обнулил синглтоны.
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

      if (!MotorStimulusAssociationSystem.IsInitialized)
      {
        MotorStimulusAssociationSystem.InitializeInstance(
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

      ReflexesActivator.Instance.SetMotorStimulusAssociations(
          MotorStimulusAssociationSystem.Instance);
    }

    private static void SetField(object target, string name, object value)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      if (field != null)
        field.SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      return field?.GetValue(target);
    }

    private const BindingFlags ChainMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static T GetChainField<T>(object chain, string name)
    {
      Assert.NotNull(chain);
      var property = chain.GetType().GetProperty(name, ChainMembers);
      Assert.NotNull(property);
      return (T)property.GetValue(chain);
    }

    private static void InvokePrivate(string name, int pulseCount) =>
        typeof(ReflexesActivator)
            .GetMethod(name, PrivateInstance)
            .Invoke(ReflexesActivator.Instance, new object[] { pulseCount });

    /// <summary>Стиль-образ активных стилей (тот же, что строит дерево).</summary>
    private int SeedStyleImage() =>
        Images.AddBehaviorStyleImage(new List<int> { Engine.SeedStyleId });

    /// <summary>
    /// Эмулирует <c>ActiveFromPhrase</c> до момента <c>ExecuteReflexes</c>: задаёт контекст
    /// активатора и вызывает приватный <c>CollectReflexesForExecution</c>.
    /// </summary>
    private void EmulatePhraseTrigger(int triggerImageId, int styleImageId)
    {
      var activator = ReflexesActivator.Instance;

      SetField(activator, "_activeCurBaseID", 0);
      SetField(activator, "_activetStyleIds", new List<int> { Engine.SeedStyleId });
      SetField(activator, "_activeCurBaseStyleID", styleImageId);
      SetField(activator, "_activeCurTriggerStimulusID", triggerImageId);
      SetField(activator, "_activeCurReflexTriggerStimulusID", 0);
      SetField(activator, "_activatedPulsCount", Engine.CurrentPulse);
      SetField(activator, "_weitPulceCount", Engine.CurrentPulse);

      AppGlobalState.IsReflexChainActive = false;
      AppGlobalState.IsNewConditions = false;
      SetField(activator, "_activeReflexChain", null);
      SetField(activator, "_chainAlreadyActivatedInThisContext", false);
      SetField(activator, "_isChainActive", false);
      SetField(activator, "_chainBaseID", 0);
      SetField(activator, "_chainStyleID", 0);

      ReflexTreeSystem.Instance.ConditionsDetection(new[] { 0, styleImageId, triggerImageId });

      typeof(ReflexesActivator)
          .GetMethod("CollectReflexesForExecution", PrivateInstance)
          .Invoke(activator, null);
    }

    /// <summary>Воспроизводит конец пульса движка (сброс триггеров, цепочка живёт дальше).</summary>
    private void EndPulse(int pulseCount)
    {
      AppGlobalState.IsNewConditions = false;
      ReflexesActivator.Instance.ResetStates(pulseCount);
    }

    /// <summary>
    /// Строит реальную транзитивную цепочку из двух УР (общий мотор эталонного б/у):
    /// возвращает ID стартового УР, ID УР-звена и мотор звена (мотор исходного б/у звена).
    /// </summary>
    private (int StartReflexId, int LinkReflexId, int LinkMotorId, int StyleImageId)
        BuildTwoLinkChain()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();

      float gamma = Settings.MotorAssociationActivationThreshold;
      do
      {
        MotorStimulusAssociationSystem.Instance.StrengthenLink(motor1, triggerB);
      }
      while (!MotorStimulusAssociationSystem.Instance.TryGetStrength(motor1, triggerB, out float c) || c < gamma);

      EmulatePhraseTrigger(triggerA, styleImageId);
      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "транзитивная цепочка не построена после активации УР1");

      int linkMotor = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex2Id).SourceGeneticReflexId).First();

      return (reflex1Id, reflex2Id, linkMotor, styleImageId);
    }

    /// <summary>
    /// Разворачивает первое звено: хост подтверждает стартовый мотор, затем один пульс
    /// штатного контура вызывает <c>ExecuteTransitiveChainLink</c>. Возвращает пульс звена.
    /// </summary>
    private int AdvanceFirstLink()
    {
      EndPulse(Engine.CurrentPulse);
      ReflexesActivator.Instance.SetChainStepResult(true);
      Engine.AdvancePulses(1);
      int pulse = Engine.CurrentPulse;
      InvokePrivate("ProcessActiveChain", pulse);
      return pulse;
    }

    private static ResearchLogger CreateLogger(ReflexesActivator activator, string tempDir)
    {
      return new ResearchLogger(
          GomeostasSystem.Instance,
          PerceptionImagesSystem.Instance,
          activator,
          AdaptiveActionsSystem.Instance,
          logsDirectory: tempDir,
          logFileName: "TransitiveChainLoggingTests",
          format: ResearchLogger.LogFormat.None,
          clearOnStart: true,
          enabled: true);
    }

    /// <summary>
    /// Правка 2 (BuildTransitiveChain → RegisterActiveChain): виртуальная цепочка обязана
    /// завести сессию в <c>_activeChains</c> под ключом <c>-стартовыйУР</c>, иначе все
    /// <c>LogChainLink…</c> молча теряются.
    /// </summary>
    [Fact]
    public void TransitiveChain_IsRegisteredInLoggerSessions()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_chainlog_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      var logger = CreateLogger(ReflexesActivator.Instance, tempDir);
      ReflexesActivator.Instance.SetResearchLogger(logger);
      try
      {
        var (startReflexId, _, _, _) = BuildTwoLinkChain();

        var activeChains = (IDictionary)GetField(logger, "_activeChains");
        Assert.NotNull(activeChains);
        Assert.True(activeChains.Contains(-startReflexId),
            $"Виртуальная цепочка -{startReflexId} не зарегистрирована в логгере: " +
            "LogChainLinkExecution/LogChainCompletion не найдут сессию (регресс Случая 27).");
      }
      finally
      {
        ReflexesActivator.Instance.SetResearchLogger(null);
        logger.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    /// <summary>
    /// Правка 3 (ExecuteTransitiveChainLink → motorActionId): после разворота звена копилка
    /// <c>_chainInfoByPulse</c> обязана содержать реальный мотор звена, а не <c>:0</c>.
    /// </summary>
    [Fact]
    public void TransitiveChain_LinkExecution_LogsRealMotorNotZero()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_chainlog_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      var logger = CreateLogger(ReflexesActivator.Instance, tempDir);
      ReflexesActivator.Instance.SetResearchLogger(logger);
      try
      {
        var (startReflexId, linkReflexId, linkMotorId, _) = BuildTwoLinkChain();
        int pulse = AdvanceFirstLink();

        var chainInfoByPulse = (IDictionary)GetField(logger, "_chainInfoByPulse");
        Assert.NotNull(chainInfoByPulse);
        Assert.True(chainInfoByPulse.Contains(pulse),
            $"Пульс звена {pulse} не попал в _chainInfoByPulse (сессия не зарегистрирована).");

        object boxed = chainInfoByPulse[pulse]; // (string ReflexChain, string AutomatizmChain)
        string reflexChain = boxed.GetType().GetField("Item1").GetValue(boxed) as string;

        Assert.Equal($"{-startReflexId}:{linkMotorId}", reflexChain);
        Assert.DoesNotContain(":0", reflexChain);
        // Различимость: мотор звена ≠ ID у-рефлекса звена (иначе «:motor» неотличим от «:reflexId»).
        Assert.NotEqual(linkReflexId, linkMotorId);
      }
      finally
      {
        ReflexesActivator.Instance.SetResearchLogger(null);
        logger.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    /// <summary>
    /// Правка 1 (корень бага): колонку «Цепочка РФ» формирует только <c>LogSystemState</c>.
    /// Эмулируем два хоста на одном пульсе звена:
    /// «Velum» (зовёт лишь <c>FlushBufferedAgentRowToMemoryNow</c>) → строки нет;
    /// «движок» (зовёт <c>LogSystemState</c> + flush) → строка есть с мотором звена.
    /// </summary>
    [Fact]
    public void TransitiveChain_ColumnAppearsOnlyWhenLogSystemStateCalled()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_chainlog_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);

      var (startReflexId, _, linkMotorId, _) = BuildTwoLinkChain();
      int pulse = AdvanceFirstLink();

      // Хост «Velum»: без LogSystemState колонка не появляется вообще.
      var noStateWriter = new RecordingLogWriter();
      var loggerA = CreateLogger(ReflexesActivator.Instance, tempDir);
      ReflexesActivator.Instance.SetResearchLogger(loggerA);
      loggerA.SetMemoryLogWriter(noStateWriter);
      try
      {
        // Цепочка построена без логгера, поэтому регистрируем сессию вручную — как это
        // делает BuildTransitiveChain в рантайме (RegisterActiveChain), иначе LogChainLink… молча выйдет.
        loggerA.RegisterActiveChain(-startReflexId, $"TransitiveChain_{startReflexId}", "Reflex");
        loggerA.LogChainLinkExecution(-startReflexId, 1, linkMotorId, pulse);
        loggerA.FlushBufferedAgentRowToMemoryNow();
        Assert.Empty(noStateWriter.Rows);
      }
      finally
      {
        loggerA.SetMemoryLogWriter(null);
        ReflexesActivator.Instance.SetResearchLogger(null);
        loggerA.Dispose();
      }

      // Хост «новый движок»: LogSystemState на пульсе звена переносит копилку в колонку.
      var stateWriter = new RecordingLogWriter();
      var loggerB = CreateLogger(ReflexesActivator.Instance, tempDir);
      ReflexesActivator.Instance.SetResearchLogger(loggerB);
      loggerB.SetMemoryLogWriter(stateWriter);
      try
      {
        loggerB.RegisterActiveChain(-startReflexId, $"TransitiveChain_{startReflexId}", "Reflex");
        loggerB.LogChainLinkExecution(-startReflexId, 1, linkMotorId, pulse);
        loggerB.LogSystemState(pulse);
        loggerB.FlushBufferedAgentRowToMemoryNow();

        Assert.NotEmpty(stateWriter.Rows);
        var row = stateWriter.Rows.Last();
        Assert.Equal($"{-startReflexId}:{linkMotorId}", row.ReflexChainInfo);
      }
      finally
      {
        loggerB.SetMemoryLogWriter(null);
        ReflexesActivator.Instance.SetResearchLogger(null);
        loggerB.Dispose();
        TryDeleteDir(tempDir);
      }
    }

    private static void TryDeleteDir(string dir)
    {
      try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }

    /// <summary>
    /// Наблюдатель симбионтной строки: фиксирует пульс и колонку «Цепочка РФ» из
    /// <see cref="ILogWriter.WriteLog"/> (параметр 11), остальные — пустые.
    /// </summary>
    private sealed class RecordingLogWriter : ILogWriter
    {
      public sealed class Row
      {
        public int Pulse { get; set; }
        public string ReflexChainInfo { get; set; }
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
          ReflexChainInfo = reflexChainInfo
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
