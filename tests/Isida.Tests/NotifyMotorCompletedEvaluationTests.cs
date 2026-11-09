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
  /// Регрессионные тесты для Случая 31: оценка автоматизма по метрике в <see cref="AutomatismResultTracker.NotifyMotorCompleted"/>.
  /// <list type="bullet">
  /// <item>Если к моменту завершения мотора состояние уже изменилось — немедленная оценка по дельте и закрытие окна ожидания.</item>
  /// <item>Если состояние не изменилось — перезапуск таймера ожидания (прежнее поведение).</item>
  /// </list>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class NotifyMotorCompletedEvaluationTests : EngineIntegrationTestBase
  {
    private readonly int _paramId;
    private readonly int _actionId;

    public NotifyMotorCompletedEvaluationTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      EnsurePurposeSystem();

      // Параметр и G_AD создаются только на стадии 0.
      var (paramId, actionId) = CreateParameterAndGad();
      _paramId = paramId;
      _actionId = actionId;

      ResetStage2State();
    }

    private static AutomatizmSystem Autom => AutomatizmSystem.Instance;
    private static AutomatismResultTracker Tracker => AutomatismResultTracker.Instance;

    #region Поднятие подсистем

    /// <summary>
    /// Идемпотентно поднимает Automatism-подсистемы поверх временного каталога фикстуры.
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

      // CreateBasicAutomatizmTree создаёт узлы с checkUnicum:false — повторный вызов дублирует
      // базовые ветки, поэтому гарантируем создание только для пустого дерева.
      if (AutomatizmTreeSystem.Instance.Tree.Children.Count == 0)
        AutomatizmTreeSystem.Instance.CreateBasicAutomatizmTree();
    }

    /// <summary>Идемпотентно поднимает <see cref="PurposeGeneticImageSystem"/>.</summary>
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
      GlobalTimer.Reset();
    }

    /// <summary>
    /// Присваивает <see cref="GlobalTimer.GlobalPulsCount"/> (сеттер приватный).
    /// </summary>
    private static void SetGlobalPulse(int pulse)
    {
      var prop = typeof(GlobalTimer).GetProperty(
          "GlobalPulsCount", BindingFlags.Public | BindingFlags.Static);
      var setter = prop.GetSetMethod(nonPublic: true);
      setter.Invoke(null, new object[] { pulse });
    }

    /// <summary>Создаёт автоматизм с уникальной веткой.</summary>
    private (int Id, Automatizm Automatizm) NewAutomatizm()
    {
      return Autom.CreateNewAutomatizm(
          Engine.NextSeed(), Engine.NextSeed(), checkUnicum: false);
    }

    #endregion

    #region Состояние улучшилось (Bad → Well)

    [Fact]
    public void NotifyMotorCompleted_StateImproved_EvaluatesImmediatelyAndClosesWait()
    {
      // Старт: состояние Bad.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(100);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Assert.Equal(AppGlobalState.HomeostasisState.Bad, result.PreviousState);
      // Окно ожидания открыто с пульса запуска автоматизма (обслуживает ответ оператора и отклик среды).
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);

      // Моторика выполняется, метрика отпускает → состояние Well.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;

      // Завершение мотора.
      Tracker.NotifyMotorCompleted(id);

      // Оценка по дельте: Bad → Well = Success.
      Assert.Equal(1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Success, result.Result);
      Assert.Equal(AppGlobalState.HomeostasisState.Well, result.CurrentState);

      // Окно ожидания закрыто (не перезапускается).
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(0, AppGlobalState.WaitingPeriodCountdown);
    }

    [Fact]
    public void NotifyMotorCompleted_StateImproved_DoesNotRestartWaitTimer()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(200);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Tracker.NotifyMotorCompleted(id);

      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(0, AppGlobalState.WaitingPeriodCountdown);
      Assert.Equal(0, AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation);
    }

    #endregion

    #region Состояние ухудшилось (Normal → Bad)

    [Fact]
    public void NotifyMotorCompleted_StateWorsened_EvaluatesErrorAndClosesWait()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(300);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Assert.Equal(AppGlobalState.HomeostasisState.Normal, result.PreviousState);

      // Моторика ухудшает состояние.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;

      Tracker.NotifyMotorCompleted(id);

      // Оценка: Normal → Bad = Error.
      Assert.Equal(-1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Error, result.Result);
      Assert.Equal(AppGlobalState.HomeostasisState.Bad, result.CurrentState);

      // Окно закрыто.
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);

      // Случай 38: реакция среды двигает полезность. Стартовая полезность стадии 2 (Default-роль) = 2,
      // дельта Normal→Bad = −1 → 1.
      Assert.Equal(1, Autom.GetAutomatizmById(id).Usefulness);
    }

    #endregion

    #region Состояние не изменилось — окно остаётся открытым, отсчёт не перезапускается

    [Fact]
    public void NotifyMotorCompleted_StateUnchanged_KeepsWaitWindowOpen()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(400);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Окно открыто с момента запуска автоматизма (обслуживает и ответ оператора, и отклик среды).
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(id, AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation);

      // Состояние НЕ изменилось.
      SetGlobalPulse(405);
      Tracker.NotifyMotorCompleted(id);

      // Окно живо, мотор отмечен, отсчёт НЕ перезапущен (дедлайн остался с пульса запуска).
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(id, AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation);
      Assert.Equal(400, AppGlobalState.LastRunAutomatizmPulsCount);
      Assert.Equal(405, result.MotorCompletedPulse);
      Assert.True(AppGlobalState.WaitingPeriodCountdown > 0);
    }

    [Fact]
    public void NotifyMotorCompleted_StateUnchanged_DoesNotResetDeadlineToFullDuration()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(500);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);
      int startPulse = GlobalTimer.GlobalPulsCount;

      // Оператор долго думал и выполнил действие на 20-м пульсе окна.
      SetGlobalPulse(startPulse + 20);
      Tracker.NotifyMotorCompleted(id);

      // Дедлайн остаётся от пульса запуска, а не «ещё 30 пульсов от мотора».
      Assert.Equal(startPulse, AppGlobalState.LastRunAutomatizmPulsCount);
    }

    [Fact]
    public void NotifyMotorCompleted_WindowExpiredWhileMotorRan_ClosesAsSkipped()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(550);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Оператор провозился дольше окна — мотор завершился после дедлайна.
      SetGlobalPulse(550 + 31);
      Tracker.NotifyMotorCompleted(id);

      // Окно истекло, действия не было: нейтральный исход независимо от длительности
      // (WaitingForResponse здесь означал бы «оператор ещё отвечает», чего уже нет).
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Skipped, result.Result);
      Assert.Equal(0, result.UsefulnessDelta);
      Assert.True(result.EndPulse > 0);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
    }

    [Fact]
    public void NotifyMotorCompleted_OperatorCancelledDialog_ClosesAsSkippedWithoutWaiting()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(560);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Диалог закрыт без выполнения действия: действия не было — оценивать нечего.
      Tracker.NotifyMotorCompleted(id, motorSkipped: true);

      Assert.Equal(AutomatismResultTracker.ExecutionResult.Skipped, result.Result);
      Assert.Equal(0, result.UsefulnessDelta);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(0, AppGlobalState.WaitingPeriodCountdown);
    }

    #endregion

    #region Порядок состояний: Normal → Normal (без изменений)

    [Fact]
    public void NotifyMotorCompleted_NormalToNormal_KeepsWaitWindowOpen()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(600);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Текущее состояние всё ещё Normal.
      Tracker.NotifyMotorCompleted(id);

      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(id, AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation);
    }

    #endregion

    #region Well → Well (без изменений)

    [Fact]
    public void NotifyMotorCompleted_WellToWell_KeepsWaitWindowOpen()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;
      SetGlobalPulse(700);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      Tracker.NotifyMotorCompleted(id);

      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(id, AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation);
    }

    #endregion

    #region Well → Bad (ухудшение)

    [Fact]
    public void NotifyMotorCompleted_WellToBad_EvaluatesErrorAndClosesWait()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;
      SetGlobalPulse(800);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;

      Tracker.NotifyMotorCompleted(id);

      Assert.Equal(-1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Error, result.Result);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Bad → Normal (улучшение)

    [Fact]
    public void NotifyMotorCompleted_BadToNormal_EvaluatesSuccessAndClosesWait()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(900);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Tracker.NotifyMotorCompleted(id);

      Assert.Equal(1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Success, result.Result);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Well → Normal (нормализация, не ухудшение)

    /// <summary>
    /// Регрессия: возврат из «Хорошо» в «Норму» не должен снижать полезность автоматизма.
    /// Раньше `Well(1) → Normal(0)` трактовалось как «ухудшение» → `-1`, что ломало
    /// автоматизмы, исправившие ситуацию и вернувшие систему в устойчивое состояние.
    /// </summary>
    [Fact]
    public void NotifyMotorCompleted_WellToNormal_NoPenalty()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Well;
      SetGlobalPulse(2000);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Среда успокоилась: Well → Normal.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Tracker.NotifyMotorCompleted(id);

      // Нормализация: без штрафа, окно закрыто.
      Assert.Equal(0, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Skipped, result.Result);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Invalid ID

    [Fact]
    public void NotifyMotorCompleted_InvalidId_DoesNothing()
    {
      bool threw = false;
      try
      {
        Tracker.NotifyMotorCompleted(0);
        Tracker.NotifyMotorCompleted(-1);
      }
      catch
      {
        threw = true;
      }

      Assert.False(threw, "NotifyMotorCompleted должен молча игнорировать некорректные ID");
    }

    #endregion

    #region Случай 38: не открывать фантомное окно на чужом автоматизме

    [Fact]
    public void NotifyMotorCompleted_NoOpenTracking_DoesNotOpenWindow()
    {
      // Автоматизм создан, но StartTracking не вызывался (нет незавершённого результата).
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1000);

      var (id, _) = NewAutomatizm();

      Tracker.NotifyMotorCompleted(id);

      // Фантомное окно не открыто: реакции среды ждать не на что.
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(0, AppGlobalState.WaitingPeriodCountdown);
    }

    [Fact]
    public void NotifyMotorCompleted_AlreadyFinishedResult_DoesNotReopenWindow()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1100);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Метрика отпустила ещё до завершения мотора — первая оценка закрывает окно.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      Tracker.NotifyMotorCompleted(id);
      Assert.True(result.EndPulse > 0, "Первый NotifyMotorCompleted должен завершить результат");
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);

      // Повторный мотор того же автоматизма не должен открывать окно заново.
      Tracker.NotifyMotorCompleted(id);
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Случай 38: повторный мотор того же автоматизма не перезапускает отсчёт

    [Fact]
    public void NotifyMotorCompleted_SameAutomatizmWindowAlreadyOpen_DoesNotRestartCountdown()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1200);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Первый мотор: состояние не изменилось → окно открыто, отсчёт стартовал.
      SetGlobalPulse(1200);
      Tracker.NotifyMotorCompleted(id);
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
      int firstRunPulse = AppGlobalState.LastRunAutomatizmPulsCount;

      // Второй мотор того же автоматизма на том же пульсе → отсчёт НЕ перезапускается.
      SetGlobalPulse(1200);
      Tracker.NotifyMotorCompleted(id);
      Assert.Equal(firstRunPulse, AppGlobalState.LastRunAutomatizmPulsCount);
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Случай 38: polling-оценка по реакции среды закрывает окно и двигает полезность

    [Fact]
    public void TryEvaluateWaitingByEnvironmentResponse_StateImproved_EvaluatesAndClosesWait()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1300);

      var (id, atmz) = NewAutomatizm();
      var result = Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Мотор завершился, состояние ещё не отреагировало → окно открыто.
      SetGlobalPulse(1300);
      Tracker.NotifyMotorCompleted(id);
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);

      int usefulnessBefore = Autom.GetAutomatizmById(id).Usefulness;

      // На следующем пульсе метрика отпустила: Bad → Normal.
      SetGlobalPulse(1301);
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      bool evaluated = Tracker.TryEvaluateWaitingByEnvironmentResponse();

      Assert.True(evaluated, "Polling-оценка должна закрыть окно по дельте состояния");
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(1, result.UsefulnessDelta);
      Assert.Equal(AutomatismResultTracker.ExecutionResult.Success, result.Result);
      Assert.Equal(usefulnessBefore + 1, Autom.GetAutomatizmById(id).Usefulness);
    }

    [Fact]
    public void TryEvaluateWaitingByEnvironmentResponse_StateUnchanged_KeepsWindowOpen()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1400);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);
      SetGlobalPulse(1400);
      Tracker.NotifyMotorCompleted(id);

      // Состояние не изменилось → polling ничего не оценивает, окно живо.
      SetGlobalPulse(1401);
      bool evaluated = Tracker.TryEvaluateWaitingByEnvironmentResponse();

      Assert.False(evaluated);
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
    }

    [Fact]
    public void TryEvaluateWaitingByEnvironmentResponse_NoWindow_ReturnsFalse()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(1500);

      Assert.False(Tracker.TryEvaluateWaitingByEnvironmentResponse());
    }

    [Fact]
    public void TryEvaluateWaitingByEnvironmentResponse_MotorNotCompleted_ReturnsFalse()
    {
      // Диалог ещё открыт: среда не могла отреагировать на действие, которого не было.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1550);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Постороннее «улучшение» состояния при незавершённом моторе не должно давать оценку.
      SetGlobalPulse(1551);
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Assert.False(Tracker.TryEvaluateWaitingByEnvironmentResponse());
      Assert.True(AppGlobalState.WaitingForOperatorEvaluation);
    }

    #endregion

    #region Случай 39: окно ожидания восстановлено после сброса индикаторов пульсации

    [Fact]
    public void NotifyMotorCompleted_WindowLost_ReopensWindowForCompletedMotor()
    {
      // Сценарий из логов 2026-11-09: StartTracking сработал, но окно ожидания закрыто сбросом
      // (ResetAutomatizmInfo / ResetWaitingForOperatorEvaluation), хост резолвит мотор по активному
      // автоматизму — и раньше получал 0 («cannot resolve automatizmId»), оценки не было вовсе.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1700);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);

      // Окно потеряно, снимок активного автоматизма ещё жив (пульс тот же).
      AppGlobalState.ResetWaitingForOperatorEvaluation();
      Assert.Equal(id, AppGlobalState.ResolveActiveAutomatizmIdForMotor());

      // Мотор завершился, состояние пока не отреагировало → окно должно быть переоткрыто на этом автоматизме.
      SetGlobalPulse(1701);
      int resolved = AppGlobalState.ResolveActiveAutomatizmIdForMotor();
      Assert.Equal(id, resolved);
      bool windowReopened = Tracker.NotifyMotorCompleted(resolved);
      Assert.True(windowReopened,
          $"window={AppGlobalState.WaitingForOperatorEvaluation}, id={AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation} (expected {id}), lastRun={AppGlobalState.LastRunAutomatizmPulsCount}, pulse={GlobalTimer.GlobalPulsCount}, endPulse={Tracker.GetLastResult(id)?.EndPulse}, motorPulse={Tracker.GetLastResult(id)?.MotorCompletedPulse}, result={Tracker.GetLastResult(id)?.Result}, start={Tracker.GetLastResult(id)?.StartPulse}, stage={AppGlobalState.EvolutionStage}");
      // Снимок активного автоматизма обновляем на текущий пульс, чтобы резолвинг привязал
      // последующие моторы к тому же автоматизму.
      AppGlobalState.UpdateAutomatizmInfo(id, GlobalTimer.GlobalPulsCount);

      Assert.True(AppGlobalState.WaitingForOperatorEvaluation,
          $"window={AppGlobalState.WaitingForOperatorEvaluation}, id={AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation} (expected {id}), lastRun={AppGlobalState.LastRunAutomatizmPulsCount}, pulse={GlobalTimer.GlobalPulsCount}, endPulse={Tracker.GetLastResult(id)?.EndPulse}, motorPulse={Tracker.GetLastResult(id)?.MotorCompletedPulse}, result={Tracker.GetLastResult(id)?.Result}, start={Tracker.GetLastResult(id)?.StartPulse}, stage={AppGlobalState.EvolutionStage}");
      int usefulnessBefore = Autom.GetAutomatizmById(id).Usefulness;
      SetGlobalPulse(1702);
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;

      Assert.True(Tracker.TryEvaluateWaitingByEnvironmentResponse());
      Assert.False(AppGlobalState.WaitingForOperatorEvaluation);
      Assert.Equal(usefulnessBefore + 1, Autom.GetAutomatizmById(id).Usefulness);
    }

    [Fact]
    public void ResolveActiveAutomatizmIdForMotor_StaleSnapshot_ReturnsZero()
    {
      // Просроченный снимок активного автоматизма не должен привязывать мотор к чужому автоматизму.
      // Период отсчитывается от пульса StartTracking: резолвинг берёт эффективное значение (30 пульсов,
      // когда настройка не применена), поэтому границу проверяем именно по нему.
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      SetGlobalPulse(1800);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);
      int startPulse = GlobalTimer.GlobalPulsCount;

      SetGlobalPulse(startPulse + 30);
      Assert.Equal(id, AppGlobalState.ResolveActiveAutomatizmIdForMotor());

      SetGlobalPulse(startPulse + 31);
      Assert.Equal(0, AppGlobalState.ResolveActiveAutomatizmIdForMotor());
    }

    #endregion

    #region Случай 38: обратный отсчёт считается от глобального пульса, а не от числа UI-тиков

    [Fact]
    public void UpdateWaitingPeriodCountdown_CountsDownByGlobalPulse()
    {
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Bad;
      SetGlobalPulse(1600);

      var (id, atmz) = NewAutomatizm();
      Tracker.StartTracking(id, atmz.BranchID, atmz.ActionsImageID);
      SetGlobalPulse(1600);
      Tracker.NotifyMotorCompleted(id);

      int period = AppGlobalState.WaitingPeriodCountdown;
      Assert.True(period > 0);

      // Прошло 5 пульсов — остаток уменьшился ровно на 5, сколько бы UI-тиков ни случилось между ними.
      SetGlobalPulse(1605);
      AppGlobalState.UpdateWaitingPeriodCountdown();
      Assert.Equal(period - 5, AppGlobalState.WaitingPeriodCountdown);

      // UI-тик без движения пульса остаток не меняет.
      AppGlobalState.UpdateWaitingPeriodCountdown();
      Assert.Equal(period - 5, AppGlobalState.WaitingPeriodCountdown);
    }

    #endregion
  }
}
