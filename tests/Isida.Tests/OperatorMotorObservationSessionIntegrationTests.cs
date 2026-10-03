using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Интеграционные тесты механизмов 2 стадии эволюции:
  /// <list type="bullet">
  /// <item>механизм 2 — сессия наблюдения моторных действий оператора
  /// (<see cref="OperatorMotorObservationSession"/>): открытие по rising-edge Bad без usable atmz,
  /// запись G_AD, post-motor wait, создание автоматизма по факту снятия проблемы (Usefulness=1);</item>
  /// <item>механизм 3 — случайная проба (<see cref="PurposeGeneticImageSystem"/>): create-one atmz
  /// при стилях Поиск/Игра и отсутствии VeryActual;</item>
  /// <item>запрет селективного клона (путь A) на стадии 2 — он доступен только со стадии 3;</item>
  /// <item>оценка полезности через <see cref="AutomatismResultTracker"/> (улучшение +1, ухудшение −1,
  /// без изменений 0) и удаление автоматизма при отрицательной полезности на стадии 2.</item>
  /// </list>
  /// <para>
  /// Automatism-подсистемы и сессия наблюдения не входят в базовую <see cref="EngineFixture"/>,
  /// поэтому поднимаются здесь идемпотентно (<c>IsInitialized</c>-гарды) поверх того же временного
  /// каталога данных. Синглтоны статические, коллекция прогоняется последовательно
  /// (<see cref="EngineIntegrationCollection"/>), изоляция — сбросом состояния в
  /// <see cref="ResetStage2State"/>.
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class OperatorMotorObservationSessionIntegrationTests : EngineIntegrationTestBase
  {
    private readonly int _paramId;
    private readonly int _actionId;

    public OperatorMotorObservationSessionIntegrationTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      EnsurePurposeSystem();
      EnsureObservationSession();

      // Параметр и G_AD создаются только на стадии 0, поэтому поднимаем её локально.
      var (paramId, actionId) = CreateParameterAndGad();
      _paramId = paramId;
      _actionId = actionId;

      ResetStage2State();
    }

    private static AutomatizmSystem Autom => AutomatizmSystem.Instance;
    private static AutomatizmTreeSystem AutomatizmTree => AutomatizmTreeSystem.Instance;
    private static AutomatismResultTracker Tracker => AutomatismResultTracker.Instance;
    private static OperatorMotorObservationSession Session => OperatorMotorObservationSession.Instance;

    #region Поднятие подсистем

    /// <summary>
    /// Идемпотентно поднимает Automatism-подсистемы поверх временного каталога фикстуры
    /// и гарантирует наличие базового дерева (3 корневых узла базовых состояний).
    /// </summary>
    private void EnsureAutomatismSystems()
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

      AutomatizmTree.CreateBasicAutomatizmTree();
    }

    /// <summary>Идемпотентно поднимает <see cref="PurposeGeneticImageSystem"/> (механизм 3 / путь A).</summary>
    private void EnsurePurposeSystem()
    {
      if (!PurposeGeneticImageSystem.IsInitialized)
      {
        PurposeGeneticImageSystem.InitializeInstance(
            InformationEnvironmentSystem.Instance,
            ActionsImagesSystem.Instance,
            AutomatizmSystem.Instance,
            AdaptiveActionsSystem.Instance);
      }

      PurposeGeneticImageSystem.Instance.SetInfluenceActionSystem(InfluenceActionSystem.Instance);
    }

    /// <summary>Идемпотентно поднимает сессию наблюдения (механизм 2).</summary>
    private void EnsureObservationSession()
    {
      if (!OperatorMotorObservationSession.IsInitialized)
      {
        OperatorMotorObservationSession.InitializeInstance(
            AdaptiveActionsSystem.Instance,
            ActionsImagesSystem.Instance,
            AutomatizmSystem.Instance,
            InfluenceActionSystem.Instance,
            InfluenceActionsImagesSystem.Instance,
            AutomatizmTreeSystem.Instance);
      }
    }

    /// <summary>Создаёт на стадии 0 пару «параметр гомеостаза + G_AD, нацеленный на него».</summary>
    private (int ParamId, int ActionId) CreateParameterAndGad()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Тест-G_AD-" + Engine.NextSeed(), "", targetGomeoParamIdArr: new List<int> { paramId }, Vigor: 5);
        return (paramId, actionId);
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    /// <summary>Создаёт на стадии 0 моторное действие без целевого параметра гомеостаза.</summary>
    private int CreateActionWithoutTarget()
    {
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        return AdaptiveActionsSystem.Instance.AddAction("Действие-без-цели-" + Engine.NextSeed(), "").ActionId;
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }
    }

    /// <summary>Готовит «чистый лист» 2 стадии: стадия 2, пустой реестр автоматизмов, сброс сессии и глобального пульса.</summary>
    private void ResetStage2State()
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();

      AppGlobalState.DominantParam = 0;
      AppGlobalState.AutomatizmNodeId = 0;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = 0;
      AppGlobalState.FlgConditionReflexes = false;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);

      if (OperatorMotorObservationSession.IsInitialized)
        OperatorMotorObservationSession.Instance.Reset();
      GlobalTimer.Reset();
    }

    /// <summary>
    /// Присваивает <see cref="GlobalTimer.GlobalPulsCount"/> (сеттер приватный) — сессия наблюдения
    /// отсчитывает post-motor wait именно по этому счётчику, а не по <see cref="EngineFixture.CurrentPulse"/>.
    /// </summary>
    private static void SetGlobalPulse(int pulse)
    {
      var prop = typeof(GlobalTimer).GetProperty(
          "GlobalPulsCount", BindingFlags.Public | BindingFlags.Static);
      var setter = prop.GetSetMethod(nonPublic: true);
      setter.Invoke(null, new object[] { pulse });
    }

    /// <summary>Приводит агента к состоянию «focus в Bad» и открывает сессию наблюдения.</summary>
    private void OpenSessionInBad()
    {
      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      Session.Reset();
      Assert.True(Session.OpenSession(), "сессия наблюдения не открылась при Bad + DominantParam");
    }

    /// <summary>Создаёт автоматизм с уникальной веткой (для трекера полезности).</summary>
    private (int Id, Automatizm Automatizm) NewAutomatizm()
    {
      return Autom.CreateNewAutomatizm(
          Engine.NextSeed(), Engine.NextSeed(), checkUnicum: false);
    }

    #endregion

    #region Механизм 2: открытие сессии

    [Fact]
    public void OpenSession_NotStage2_ReturnsFalse()
    {
      AppGlobalState.EvolutionStage = 1;
      try
      {
        AppGlobalState.DominantParam = _paramId;
        AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;

        Assert.False(Session.OpenSession());
        Assert.False(Session.IsActive);
      }
      finally
      {
        AppGlobalState.EvolutionStage = 2;
      }
    }

    [Fact]
    public void OpenSession_NoDominantParam_ReturnsFalse()
    {
      AppGlobalState.DominantParam = 0;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;

      Assert.False(Session.OpenSession());
      Assert.False(Session.IsActive);
    }

    [Fact]
    public void OpenSession_FocusNotBad_ReturnsFalse()
    {
      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Assert.False(Session.OpenSession());
      Assert.False(Session.IsActive);
    }

    [Fact]
    public void OpenSession_BadWithDominantParam_ActivatesAndCapturesFocus()
    {
      OpenSessionInBad();

      Assert.True(Session.IsActive);
      Assert.Equal(_paramId, Session.FocusParameterId);
      Assert.True(Session.WasInBadZone);
    }

    [Fact]
    public void OpenSession_SecondTime_ReturnsFalse()
    {
      OpenSessionInBad();

      Assert.False(Session.OpenSession());
    }

    #endregion

    #region Механизм 2: запись мотора оператора

    [Fact]
    public void RecordOperatorMotor_InactiveSession_ReturnsFalse()
    {
      Session.Reset();

      Assert.False(Session.RecordOperatorMotor(_actionId, null));
    }

    [Fact]
    public void RecordOperatorMotor_UnknownAction_ReturnsFalse()
    {
      OpenSessionInBad();

      Assert.False(Session.RecordOperatorMotor(999_999, null));
    }

    [Fact]
    public void RecordOperatorMotor_ActionWithoutTarget_ReturnsFalse()
    {
      OpenSessionInBad();
      int noTarget = CreateActionWithoutTarget();

      Assert.False(Session.RecordOperatorMotor(noTarget, null));
    }

    [Fact]
    public void RecordOperatorMotor_ValidGad_RecordsActionAndPulse()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);

      Assert.True(Session.RecordOperatorMotor(_actionId, null));
      Assert.Equal(_actionId, Session.LastMotorActionId);
      Assert.Equal(100, Session.LastMotorPulse);
    }

    [Fact]
    public void RecordOperatorMotor_ProbeActions_AreSortedAndDeduplicated()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);

      Assert.True(Session.RecordOperatorMotor(_actionId, new List<int> { 7, 3, 7, 5 }));
      Assert.Equal(new List<int> { 3, 5, 7 }, Session.ActiveProbeActionIds);
    }

    #endregion

    #region Механизм 2: post-motor wait

    [Fact]
    public void IsPostMotorWaitExpired_BeforeWait_ReturnsFalse()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);
      Session.RecordOperatorMotor(_actionId, null);

      SetGlobalPulse(105);
      Assert.False(Session.IsPostMotorWaitExpired(waitDurationPulses: 10));
      Assert.Equal(5, Session.GetRemainingPostMotorWaitPulses(waitDurationPulses: 10));
    }

    [Fact]
    public void IsPostMotorWaitExpired_AfterWait_ReturnsTrue()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);
      Session.RecordOperatorMotor(_actionId, null);

      SetGlobalPulse(110);
      Assert.True(Session.IsPostMotorWaitExpired(waitDurationPulses: 10));
      Assert.Equal(0, Session.GetRemainingPostMotorWaitPulses(waitDurationPulses: 10));
    }

    [Fact]
    public void IsPostMotorWaitExpired_NoMotorRecorded_ReturnsFalse()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);

      Assert.False(Session.IsPostMotorWaitExpired(waitDurationPulses: 1));
    }

    #endregion

    #region Механизм 2: выход focus из Bad и создание автоматизма

    [Fact]
    public void FocusExitedBadZone_AfterStateLeavesBad_ReturnsTrue()
    {
      OpenSessionInBad();
      Assert.False(Session.FocusExitedBadZone());

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;
      Assert.True(Session.FocusExitedBadZone());
    }

    [Fact]
    public void CreateAutomatizmFromMotor_ValidMotor_CreatesUsefulAutomatizm()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);
      Session.RecordOperatorMotor(_actionId, null);

      int atmzId = Session.CreateAutomatizmFromMotor(_actionId);

      Assert.True(atmzId > 0, "автоматизм по мотору оператора не создан");
      var atmz = Autom.GetAutomatizmById(atmzId);
      Assert.NotNull(atmz);
      Assert.Equal(1, atmz.Usefulness);
      Assert.True(atmz.ActionsImageID > 0);
    }

    [Fact]
    public void CreateAutomatizmFromMotor_WithProbes_BindsBranchToActivityNode()
    {
      AppGlobalState.AutomatizmNodeId = 0;
      OpenSessionInBad();
      SetGlobalPulse(100);

      int probeId = Engine.NextSeed();
      var probes = new List<int> { probeId };

      // ActivityID формируется из набора probe-EA; создаём узел дерева под этот ActivityID заранее.
      var (activityId, _) = InfluenceActionsImagesSystem.Instance.CreateNewInfluenceActionsImage(probes, checkUnicum: true);
      Assert.True(activityId > 0);

      var (nodeId, _) = AutomatizmTree.CreateNewAutomatizmNode(
          AutomatizmTree.Tree, id: 0, baseId: -1, emotionId: 0, activityId: activityId,
          toneMoodId: 0, simbolId: 0, verbID: 0, commandID: 0, visualID: 0, checkUnicum: true);
      Assert.True(nodeId > 0);

      Session.RecordOperatorMotor(_actionId, probes);
      int atmzId = Session.CreateAutomatizmFromMotor(_actionId);

      var atmz = Autom.GetAutomatizmById(atmzId);
      Assert.NotNull(atmz);
      Assert.Equal(nodeId, atmz.BranchID);
    }

    [Fact]
    public void CreateAutomatizmFromMotor_NoMotorRecorded_ReturnsZero()
    {
      OpenSessionInBad();

      Assert.Equal(0, Session.CreateAutomatizmFromMotor(_actionId));
    }

    [Fact]
    public void CloseSessionSuccessfully_DeactivatesSession()
    {
      OpenSessionInBad();

      Session.CloseSessionSuccessfully(automatizmId: 42);

      Assert.False(Session.IsActive);
    }

    [Fact]
    public void CloseSessionTimeout_DeactivatesSession()
    {
      OpenSessionInBad();

      Session.CloseSessionTimeout();

      Assert.False(Session.IsActive);
    }

    [Fact]
    public void Reset_ClearsSessionState()
    {
      OpenSessionInBad();
      SetGlobalPulse(100);
      Session.RecordOperatorMotor(_actionId, null);

      Session.Reset();

      Assert.False(Session.IsActive);
      Assert.Equal(0, Session.FocusParameterId);
      Assert.Equal(0, Session.LastMotorActionId);
      Assert.Equal(0, Session.LastMotorPulse);
    }

    #endregion

    #region Механизм 3 и запрет пути A на стадии 2

    [Fact]
    public void RandomProbe_Stage2_SearchPlayNoVeryActual_CreatesNeutralAutomatizm()
    {
      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      PurposeGeneticImageSystem.Instance.SetStage2SearchPlayStyleIds(new int[0]); // без ограничения по стилям
      Session.Reset();
      SetGlobalPulse(1000);

      int before = Autom.GetAllAutomatizms().Count;
      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.NotNull(atmz);
      Assert.Equal(0, atmz.Usefulness);
      Assert.Equal(before + 1, Autom.GetAllAutomatizms().Count);
    }

    [Fact]
    public void RandomProbe_Stage2_VeryActual_DoesNotCreateAutomatizm()
    {
      AppGlobalState.EvolutionStage = 2;
      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(true);
      Session.Reset();
      SetGlobalPulse(1000);

      int before = Autom.GetAllAutomatizms().Count;
      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      Assert.Null(atmz);
      Assert.Equal(before, Autom.GetAllAutomatizms().Count);
    }

    [Fact]
    public void RandomProbe_ActiveSession_Skipped()
    {
      OpenSessionInBad();
      SetGlobalPulse(1000);

      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      PurposeGeneticImageSystem.Instance.SetStage2SearchPlayStyleIds(new int[0]);

      int before = Autom.GetAllAutomatizms().Count;
      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();

      // Приоритет сессии наблюдения: случайная проба не выполняется, пока оператор может показать действие.
      Assert.Null(atmz);
      Assert.Equal(before, Autom.GetAllAutomatizms().Count);
    }

    [Fact]
    public void SelectiveClone_PathA_ForbiddenOnStage2_AllowedFromStage3()
    {
      AdaptiveActionsSystem.Instance.DefaultAdaptiveActionId = _actionId;
      AppGlobalState.DominantParam = _paramId;
      AppGlobalState.CurActiveVerbalId = 0;
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(true);
      Session.Reset();

      // Стадия 2: селективный клон (путь A) запрещён — ни один механизм не срабатывает.
      AppGlobalState.EvolutionStage = 2;
      int beforeStage2 = Autom.GetAllAutomatizms().Count;
      Assert.Null(PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose());
      Assert.Equal(beforeStage2, Autom.GetAllAutomatizms().Count);

      // Стадия 3: путь A разрешён и создаёт автоматизм со стартовой полезностью 2.
      AppGlobalState.EvolutionStage = 3;
      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();
      Assert.NotNull(atmz);
      Assert.Equal(2, atmz.Usefulness);
    }

    #endregion

    #region Оценка полезности (AutomatismResultTracker)

    [Fact]
    public void Tracker_RecognizedPositiveAssessment_AddsUsefulnessAndSucceeds()
    {
      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Tracker.MarkOperatorRecognition(id, recognized: true, assessment: 1);

      Assert.Equal(1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Success, result.Result);
      Assert.Equal(4, Autom.GetAutomatizmById(id).Usefulness);
    }

    [Fact]
    public void Tracker_RecognizedNegativeAssessment_SubtractsUsefulnessAndErrors()
    {
      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Tracker.MarkOperatorRecognition(id, recognized: true, assessment: -1);

      Assert.Equal(-1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Error, result.Result);
      Assert.Equal(2, Autom.GetAutomatizmById(id).Usefulness);
    }

    [Fact]
    public void Tracker_RecognizedZeroAssessment_NoDeltaAndSkipped()
    {
      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Tracker.MarkOperatorRecognition(id, recognized: true, assessment: 0);

      Assert.Equal(0, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Skipped, result.Result);
      Assert.Equal(3, Autom.GetAutomatizmById(id).Usefulness);
    }

    [Fact]
    public void Tracker_StateImproved_SuccessWithPositiveDelta()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;
      Tracker.FinishTracking(result);

      Assert.Equal(1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Success, result.Result);
    }

    [Fact]
    public void Tracker_StateWorsened_ErrorWithNegativeDelta()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      Tracker.FinishTracking(result);

      Assert.Equal(-1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Error, result.Result);
    }

    [Fact]
    public void Tracker_NegativeUsefulness_DeletesAutomatizmOnStage2()
    {
      var (id, atmz) = NewAutomatizm();
      atmz.Usefulness = 0;
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Tracker.MarkOperatorRecognition(id, recognized: true, assessment: -1);

      Assert.Null(Autom.GetAutomatizmById(id));
    }

    #endregion
  }
}
