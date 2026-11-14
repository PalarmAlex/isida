using System.Collections.Generic;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессия (Случай 40): повторный прогон одного и того же сценария без перезапуска
  /// приложения создавал «лишний» случайный автоматизм вместо ожидаемого отсутствия ответа.
  /// <para>
  /// <see cref="GlobalTimer.GlobalPulsCount"/> — статический счётчик, который не обнулялся
  /// <see cref="EvolutionStageService.ClearStageDataOnlyForScenarioPreRun"/>. Между повторными
  /// прогонами он накапливался и, достигнув порога ожидания (по умолчанию 30 пульсов),
  /// позволял <see cref="PurposeGeneticImageSystem"/> (метод TryCreateRandomProbeAutomatizm)
  /// проскочить там, где в чистом состоянии должна быть пауза.
  /// </para>
  /// <para>
  /// Дополнительно: окна ожидания оценки оператора (IsEvaluationTime,
  /// AutomatizmInfoStillFresh) считали <c>timeSince = GlobalPulsCount − LastRunAutomatizmPulsCount</c>;
  /// если пульс не обнулялся, а <c>LastRunAutomatizmPulsCount</c> оставался от прошлого прогона,
  /// <c>timeSince</c> становился отрицательным и окна зависали открытыми.
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class GlobalTimerResetOnPreRunTests : EngineIntegrationTestBase, System.IDisposable
  {
    /// <summary>
    /// Поднимает Automatism- и Purpose-подсистемы поверх временного каталога фикстуры.
    /// </summary>
    public GlobalTimerResetOnPreRunTests(EngineFixture engine) : base(engine)
    {
      if (!ActionsImagesSystem.IsInitialized)
        ActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!InfluenceActionsImagesSystem.IsInitialized)
        InfluenceActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmTreeSystem.IsInitialized)
        AutomatizmTreeSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmSystem.IsInitialized)
        AutomatizmSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.InitializeInstance(AutomatizmSystem.Instance);
      if (AutomatizmTreeSystem.Instance.Tree.Children.Count == 0)
        AutomatizmTreeSystem.Instance.CreateBasicAutomatizmTree();

      if (!EvolutionStageService.IsInitialized)
        EvolutionStageService.InitializeInstance(
            AutomatizmSystem.Instance,
            Crs,
            AutomatizmTreeSystem.Instance,
            reflexTreeSystem: ReflexTreeSystem.Instance);

      if (!PurposeGeneticImageSystem.IsInitialized)
      {
        PurposeGeneticImageSystem.InitializeInstance(
            InformationEnvironmentSystem.Instance,
            ActionsImagesSystem.Instance,
            AutomatizmSystem.Instance,
            AdaptiveActionsSystem.Instance);
      }

      (_paramId, _actionId) = CreateActionTargetingParameter();

      ResetStage2State();
    }

    /// <summary>Освобождает статические синглтоны после теста.</summary>
    public void Dispose()
    {
      if (PurposeGeneticImageSystem.IsInitialized)
        PurposeGeneticImageSystem.Instance.Dispose();
      if (EvolutionStageService.IsInitialized)
        EvolutionStageService.Instance.Dispose();
    }

    private static AutomatizmSystem Autom => AutomatizmSystem.Instance;

    private readonly int _paramId;
    private readonly int _actionId;

    #region Подготовка

    /// <summary>
    /// Устанавливает GlobalPulsCount через рефлексию (свойство имеет private set).
    /// </summary>
    private static void SetGlobalPulsCount(int value)
    {
      typeof(GlobalTimer)
          .GetProperty("GlobalPulsCount", BindingFlags.Public | BindingFlags.Static)
          ?.SetValue(null, value);
    }

    /// <summary>На стадии 0 заводит параметр и G_AD, нацеленный на него.</summary>
    private (int ParamId, int ActionId) CreateActionTargetingParameter()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-пульс-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-пульс-" + Engine.NextSeed(), "",
            targetGomeoParamIdArr: new List<int> { paramId }, Vigor: 5);
        return (paramId, actionId);
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    /// <summary>Готовит «чистый лист» 2 стадии.</summary>
    private void ResetStage2State()
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();

      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.AutomatizmNodeId = 0;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.DefaultAdaptiveActionId = _actionId;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      GlobalTimer.Reset();
    }

    #endregion

    #region Тесты

    /// <summary>
    /// Предзапуск сценария обнуляет GlobalPulsCount: после очистки он равен 0, а не
    /// «наработке» предыдущего прогона.
    /// </summary>
    [Fact]
    public void PreRun_ClearStageData_ResetsGlobalPulsCount()
    {
      // Симулируем «наработку» предыдущих прогонов.
      SetGlobalPulsCount(50);

      Assert.Equal(50, GlobalTimer.GlobalPulsCount);

      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(
          AppGlobalState.EvolutionStage);

      // После очистки пульс должен быть сброшен.
      Assert.Equal(0, GlobalTimer.GlobalPulsCount);
    }

    /// <summary>
    /// Предзапуск сценария сбрасывает окна ожидания: ResetWaitingForOperatorEvaluation
    /// и ClearPulseIndicators вызываются вместе с пульсом.
    /// </summary>
    [Fact]
    public void PreRun_ClearStageData_ResetsWaitingWindows()
    {
      // Увеличим период ожидания, чтобы IsEvaluationTime() вернула true при пульсе 50.
      int savedWaitPeriod = AppGlobalState.WaitingPeriodForActionsVal;
      AppGlobalState.WaitingPeriodForActionsVal = 100;
      try
      {
        // Имитируем «зависшее» окно ожидания от прошлого прогона.
        SetGlobalPulsCount(50);
        AppGlobalState.StartWaitingForOperatorEvaluation(automatizmId: 42, startPulse: 10);

        Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
        Assert.True(AppGlobalState.IsEvaluationTime());

        EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(
            AppGlobalState.EvolutionStage);

        // Окно ожидания должно быть закрыто.
        Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      }
      finally
      {
        AppGlobalState.WaitingPeriodForActionsVal = savedWaitPeriod;
      }
    }

    /// <summary>
    /// После предзапуска сценария (при GlobalPulsCount == 0) случайный автоматизм НЕ создаётся,
    /// даже когда механика «Search/Play» потенциально активна: пауза (waitPeriod > 0) блокирует пробу.
    /// Это проверяет, что накопленный пульс предыдущего прогона не просачивается.
    /// </summary>
    [Fact]
    public void PreRun_ClearStageData_PreventsRandomProbe()
    {
      // 1. Симулируем «наработку» — пульс выше порога паузы (30).
      SetGlobalPulsCount(50);

      // 2. Вызываем предзапуск — как это делает движок перед прогоном сценария.
      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(
          AppGlobalState.EvolutionStage);

      // 3. Пульс должен быть обнулён.
      Assert.Equal(0, GlobalTimer.GlobalPulsCount);

      // 4. Состояние: 2-я стадия, нет VeryActual, нет FlgConditionReflexes,
      //    нет вербального стимула, нет моторной части стимула (путь M/B не сработает).
      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.CurrentStimulusActionIdList = null;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);

      // WaitingPeriodForActionsVal должен быть > 0, иначе гейт паузы не работает.
      int savedWaitPeriod = AppGlobalState.WaitingPeriodForActionsVal;
      AppGlobalState.WaitingPeriodForActionsVal = 30;
      try
      {
        // 5. GetAutomatizmByGeneticPurpose: hasCloneTrigger = false, путь M/B не сработает,
        //    TryCreateRandomProbeAutomatizm — пауза активна (GlobalPulsCount=0 < waitPeriod=30),
        //    возвращает null.
        var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

        Assert.Null(atmz);
      }
      finally
      {
        AppGlobalState.WaitingPeriodForActionsVal = savedWaitPeriod;
      }
    }

    #endregion
  }
}
