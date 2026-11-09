using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система отслеживания и анализа результатов выполнения автоматизмов
  /// </summary>
  public sealed class AutomatismResultTracker : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Инициализация

    private static AutomatismResultTracker _instance;

    /// <summary>
    /// Глобальный экземпляр системы отслеживания результатов
    /// </summary>
    public static AutomatismResultTracker Instance => _instance ??
        throw new InvalidOperationException("AutomatismResultTracker не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    private readonly AutomatizmSystem _automatizmSystem;
    private EpisodicMemoryRulesService _episodicRulesService;

    /// <summary>
    /// Установить сервис записи правил эпизодической памяти (вызывается из IsidaEngine)
    /// </summary>
    public void SetEpisodicMemoryRulesService(EpisodicMemoryRulesService service)
    {
      _episodicRulesService = service;
    }

    /// <summary>
    /// Инициализирует глобальный экземпляр системы отслеживания результатов
    /// </summary>
    public static void InitializeInstance(AutomatizmSystem automatizmSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatismResultTracker уже инициализирован.");

      _instance = new AutomatismResultTracker(automatizmSystem);
    }

    private AutomatismResultTracker(AutomatizmSystem automatizmSystem)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));

      InitializeTracking();
    }

    private void InitializeTracking()
    {
      _lastAutomatizmResults = new Dictionary<int, AutomatizmResult>();
      _automatizmHistory = new List<AutomatizmExecutionRecord>();
      _failedAutomatizms = new List<int>();
      _blockedAutomatizms = new Dictionary<int, BlockedAutomatizmInfo>();
      _executionStatistics = new ExecutionStatistics();
    }

    #endregion

    #region Структуры данных

    /// <summary>
    /// Результат выполнения автоматизма
    /// </summary>
    public enum ExecutionResult
    {
      /// <summary>
      /// Успешно выполнено
      /// </summary>
      Success,

      /// <summary>
      /// Выполнено с ошибкой
      /// </summary>
      Error,

      /// <summary>
      /// Пропущено (не запускалось)
      /// </summary>
      Skipped,

      /// <summary>
      /// Заблокировано системой
      /// </summary>
      Blocked,

      /// <summary>
      /// Ожидание ответа оператора
      /// </summary>
      WaitingForResponse
    }

    /// <summary>
    /// Информация о результате выполнения автоматизма
    /// </summary>
    public class AutomatizmResult
    {
      /// <summary>
      /// ID автоматизма
      /// </summary>
      public int AutomatizmId { get; set; }

      /// <summary>
      /// Время запуска (глобальный пульс)
      /// </summary>
      public int StartPulse { get; set; }

      /// <summary>
      /// Время завершения (глобальный пульс)
      /// </summary>
      public int EndPulse { get; set; }

      /// <summary>
      /// Результат выполнения
      /// </summary>
      public ExecutionResult Result { get; set; }

      /// <summary>
      /// Сообщение об ошибке (если есть)
      /// </summary>
      public string ErrorMessage { get; set; }

      /// <summary>
      /// ID узла дерева автоматизмов
      /// </summary>
      public int BranchId { get; set; }

      /// <summary>
      /// ID образа действий (ответ Beast)
      /// </summary>
      public int ActionsImageId { get; set; }

      /// <summary>
      /// ID образа стимула оператора (перед ответом Beast)
      /// </summary>
      public int StimulusImageId { get; set; }

      /// <summary>
      /// Предыдущее глобальное состояние симбионта
      /// </summary>
      public AppGlobalState.HomeostasisState PreviousState { get; set; }

      /// <summary>
      /// Текущее глобальное состояние симбионта после выполнения
      /// </summary>
      public AppGlobalState.HomeostasisState CurrentState { get; set; }

      /// <summary>
      /// Изменение полезности автоматизма
      /// </summary>
      public int UsefulnessDelta { get; set; }

      /// <summary>
      /// Флаг распознавания оператором
      /// </summary>
      public bool RecognizedByOperator { get; set; }

      /// <summary>
      /// Оценка результата оператором (если распознано)
      /// </summary>
      public int OperatorAssessment { get; set; }

      /// <summary>
      /// Время реакции оператора (в пульсах)
      /// </summary>
      public int OperatorResponseTime { get; set; }

      /// <summary>
      /// Пульс фактического завершения мотора хостом (0 — мотор ещё не завершался).
      /// До этого момента оценка по реакции среды не выполняется: среда не могла отреагировать на
      /// действие, которого ещё не было (диалог открыт, оператор ничего не нажал).
      /// </summary>
      public int MotorCompletedPulse { get; set; }

      /// <summary>
      /// Хост сообщил, что действие в моторе фактически не выполнено (диалог закрыт без подтверждения).
      /// Такой результат оценивается как <see cref="ExecutionResult.Skipped"/> без ожидания отклика среды.
      /// </summary>
      public bool MotorSkippedByOperator { get; set; }
    }

    /// <summary>
    /// Запись истории выполнения автоматизма
    /// </summary>
    public class AutomatizmExecutionRecord
    {
      /// <summary>
      /// ID записи
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// ID автоматизма
      /// </summary>
      public int AutomatizmId { get; set; }

      /// <summary>
      /// Время выполнения (глобальный пульс)
      /// </summary>
      public int ExecutionPulse { get; set; }

      /// <summary>
      /// Результат выполнения
      /// </summary>
      public ExecutionResult Result { get; set; }

      /// <summary>
      /// Краткое описание
      /// </summary>
      public string Description { get; set; }
    }

    /// <summary>
    /// Информация о заблокированном автоматизме
    /// </summary>
    public class BlockedAutomatizmInfo
    {
      /// <summary>
      /// ID автоматизма
      /// </summary>
      public int AutomatizmId { get; set; }

      /// <summary>
      /// Причина блокировки
      /// </summary>
      public string BlockReason { get; set; }

      /// <summary>
      /// Время блокировки (глобальный пульс)
      /// </summary>
      public int BlockPulse { get; set; }

      /// <summary>
      /// Время разблокировки (глобальный пульс)
      /// </summary>
      public int UnblockPulse { get; set; }

      /// <summary>
      /// Счетчик попыток использования после блокировки
      /// </summary>
      public int AttemptsAfterBlock { get; set; }
    }

    /// <summary>
    /// Статистика выполнения автоматизмов
    /// </summary>
    public class ExecutionStatistics
    {
      /// <summary>
      /// Всего выполнено автоматизмов
      /// </summary>
      public int TotalExecutions { get; set; }

      /// <summary>
      /// Успешных выполнений
      /// </summary>
      public int SuccessCount { get; set; }

      /// <summary>
      /// Выполнений с ошибкой
      /// </summary>
      public int ErrorCount { get; set; }

      /// <summary>
      /// Пропущенных выполнений
      /// </summary>
      public int SkippedCount { get; set; }

      /// <summary>
      /// Заблокированных выполнений
      /// </summary>
      public int BlockedCount { get; set; }

      /// <summary>
      /// Среднее время выполнения (в пульсах)
      /// </summary>
      public double AverageExecutionTime { get; set; }

      /// <summary>
      /// Время последнего выполнения
      /// </summary>
      public int LastExecutionPulse { get; set; }

      /// <summary>
      /// ID последнего успешного автоматизма
      /// </summary>
      public int LastSuccessfulAutomatizmId { get; set; }
    }

    #endregion

    #region Поля и свойства

    private Dictionary<int, AutomatizmResult> _lastAutomatizmResults;
    private List<AutomatizmExecutionRecord> _automatizmHistory;
    private List<int> _failedAutomatizms;
    private Dictionary<int, BlockedAutomatizmInfo> _blockedAutomatizms;
    private ExecutionStatistics _executionStatistics;
    private int _lastHistoryId = 0;

    /// <summary>
    /// Последние результаты выполнения автоматизмов (ID автоматизма -> результат)
    /// </summary>
    public IReadOnlyDictionary<int, AutomatizmResult> LastAutomatizmResults
    {
      get
      {
        _lock.EnterReadLock();
        try
        {
          return new Dictionary<int, AutomatizmResult>(_lastAutomatizmResults);
        }
        finally
        {
          _lock.ExitReadLock();
        }
      }
    }

    /// <summary>
    /// История выполнения автоматизмов
    /// </summary>
    public IReadOnlyList<AutomatizmExecutionRecord> AutomatizmHistory
    {
      get
      {
        _lock.EnterReadLock();
        try
        {
          return _automatizmHistory.AsReadOnly();
        }
        finally
        {
          _lock.ExitReadLock();
        }
      }
    }

    /// <summary>
    /// Список неудачных автоматизмов
    /// </summary>
    public IReadOnlyList<int> FailedAutomatizms
    {
      get
      {
        _lock.EnterReadLock();
        try
        {
          return _failedAutomatizms.AsReadOnly();
        }
        finally
        {
          _lock.ExitReadLock();
        }
      }
    }

    /// <summary>
    /// Статистика выполнения
    /// </summary>
    public ExecutionStatistics Statistics
    {
      get
      {
        _lock.EnterReadLock();
        try
        {
          return new ExecutionStatistics
          {
            TotalExecutions = _executionStatistics.TotalExecutions,
            SuccessCount = _executionStatistics.SuccessCount,
            ErrorCount = _executionStatistics.ErrorCount,
            SkippedCount = _executionStatistics.SkippedCount,
            BlockedCount = _executionStatistics.BlockedCount,
            AverageExecutionTime = _executionStatistics.AverageExecutionTime,
            LastExecutionPulse = _executionStatistics.LastExecutionPulse,
            LastSuccessfulAutomatizmId = _executionStatistics.LastSuccessfulAutomatizmId
          };
        }
        finally
        {
          _lock.ExitReadLock();
        }
      }
    }

    #endregion

    #region Основные методы

    /// <summary>
    /// Снимок значений параметров и фокус (доминирующий параметр) для оценки ответа оператора по дельте, а не только по интегральному состоянию.
    /// </summary>
    private static void CaptureOperatorEvaluationParameterSnapshot()
    {
      try
      {
        if (!GomeostasSystem.IsInitialized)
        {
          AppGlobalState.SetOperatorEvaluationParameterSnapshot(null, 0);
          return;
        }

        var go = GomeostasSystem.Instance;
        var parameters = go.GetAllParameters();
        if (parameters == null || parameters.Count == 0)
        {
          AppGlobalState.SetOperatorEvaluationParameterSnapshot(null, 0);
          return;
        }

        var dict = new Dictionary<int, float>(parameters.Count);
        foreach (var p in parameters)
          dict[p.Id] = p.Value;

        var dominant = go.Calculator.FindDominantParameter(parameters, go.DynamicTime, go.DifSensorPar);
        int focusId = dominant.dominantParam?.Id ?? 0;

        AppGlobalState.SetOperatorEvaluationParameterSnapshot(dict, focusId);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        AppGlobalState.SetOperatorEvaluationParameterSnapshot(null, 0);
      }
    }

    /// <summary>
    /// Начать отслеживание выполнения автоматизма
    /// </summary>
    public AutomatizmResult StartTracking(int automatizmId, int branchId, int actionsImageId)
    {
      if (automatizmId <= 0)
        return null;

      _lock.EnterWriteLock();
      try
      {
        // Проверяем, не заблокирован ли автоматизм
        if (_blockedAutomatizms.ContainsKey(automatizmId))
        {
          var blockInfo = _blockedAutomatizms[automatizmId];
          blockInfo.AttemptsAfterBlock++;

          var result = new AutomatizmResult
          {
            AutomatizmId = automatizmId,
            StartPulse = GlobalTimer.GlobalPulsCount,
            Result = ExecutionResult.Blocked,
            ErrorMessage = $"Автоматизм заблокирован: {blockInfo.BlockReason}",
            BranchId = branchId,
            ActionsImageId = actionsImageId,
            PreviousState = AppGlobalState.CurrentOverallState
          };

          _lastAutomatizmResults[automatizmId] = result;
          AddHistoryRecord(automatizmId, ExecutionResult.Blocked, $"Блокирован: {blockInfo.BlockReason}");
          _executionStatistics.BlockedCount++;
          _executionStatistics.TotalExecutions++;

          return result;
        }

        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
        if (automatizm == null)
        {
          Logger.Warning($"Автоматизм ID={automatizmId} не найден");
          return null;
        }

        var trackingResult = new AutomatizmResult
        {
          AutomatizmId = automatizmId,
          StartPulse = GlobalTimer.GlobalPulsCount,
          BranchId = branchId,
          ActionsImageId = actionsImageId,
          StimulusImageId = AppGlobalState.CurStimulusImageId,
          PreviousState = AppGlobalState.CurrentOverallState,
          CurrentState = AppGlobalState.CurrentOverallState,
          Result = ExecutionResult.Success // по умолчанию
        };

        _lastAutomatizmResults[automatizmId] = trackingResult;
        AppGlobalState.StateBeforeOperatorImpact = AppGlobalState.CurrentOverallState;
        if (AppGlobalState.EvolutionStage >= 2)
          CaptureOperatorEvaluationParameterSnapshot();
        else
          AppGlobalState.SetOperatorEvaluationParameterSnapshot(null, 0);
        AppGlobalState.UpdateAutomatizmInfo(automatizmId, GlobalTimer.GlobalPulsCount);

        // Окно ожидания открывается здесь, а не у вызывающего кода: StartTracking — единственная точка,
        // где фиксируется пульс запуска, и хост обязан уметь привязать завершившийся мотор к окну
        // (ResolveWaitingOperatorEvaluationAutomatizmId). Отсчёт идёт от пульса запуска автоматизма.
        if (AppGlobalState.EvolutionStage >= 2)
          AppGlobalState.StartWaitingForOperatorEvaluation(automatizmId);
        else
          AppGlobalState.ResetWaitingForOperatorEvaluation();

        Logger.Info($"Начато отслеживание автоматизма ID={automatizmId}, ветка={branchId}");

        return trackingResult;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сообщить от хоста о завершении моторного выполнения автоматизма.
    /// Окно ожидания открыто с момента запуска автоматизма и обслуживает ДВЕ задачи: ответ оператора
    /// (стимул с пульта в пределах окна) и отклик среды. Завершение мотора отсчёт НЕ перезапускает —
    /// иначе «оператор думал 29 пульсов, потом нажал экспорт» превращалось бы в новые полные 30 пульсов.
    /// Здесь только фиксируется факт завершения мотора (без него оценка по среде запрещена) и выполняются
    /// ранние исходы: действие не выполнено оператором или среда уже отреагировала.
    /// </summary>
    /// <param name="automatizmId">ID автоматизма, мотор которого завершён.</param>
    /// <param name="motorSkipped">
    /// Хост сообщает, что действие фактически не выполнено (диалог закрыт без подтверждения).
    /// Такой результат оценивается как <see cref="ExecutionResult.Skipped"/> сразу, без ожидания среды.
    /// </param>
    /// <returns>
    /// <c>true</c>, если окно ожидания открыто на этом автоматизме и отсчёт теперь обслуживает
    /// и ответ среды (мотор отмечен завершённым).
    /// </returns>
    public bool NotifyMotorCompleted(int automatizmId, bool motorSkipped = false)
    {
      if (automatizmId <= 0)
        return false;

      try
      {
        var lastResult = GetLastResult(automatizmId);

        // Незавершённый результат = «не на что ждать реакции среды»: StartTracking не создавал
        // окно (автоматизм не запускался этим путём) либо оно уже закрыто оценкой.
        // Без этого окна завершение мотора открывало ФАНТОМНОЕ окно на чужом автоматизме
        // (хост резолвит automatizmId по последнему открытому окну — см. AdaptiveActionsSystem.NotifyMotorCompleted),
        // что и ломало отсчёт на пульте.
        if (lastResult == null || lastResult.EndPulse != 0)
        {
          Logger.Info(
              $"NotifyMotorCompleted: automatizmId={automatizmId} pulse={GlobalTimer.GlobalPulsCount} " +
              $"пропущено (нет незавершённого результата, EndPulse={(lastResult == null ? -1 : lastResult.EndPulse)})");
          return false;
        }

        lastResult.MotorCompletedPulse = GlobalTimer.GlobalPulsCount;
        lastResult.MotorSkippedByOperator = motorSkipped;

        var stateBefore = lastResult.PreviousState;
        var stateAfter = AppGlobalState.CurrentOverallState;

        // Оператор закрыл диалог, ничего не выполнив: действия не было — оценивать нечего.
        // Окно закрывается сразу (без 30 пульсов ожидания) с нейтральным исходом.
        if (motorSkipped)
        {
          CloseResultAsSkipped(lastResult, automatizmId, stateBefore, stateAfter,
              "действие не выполнено (диалог закрыт без подтверждения)");
          return false;
        }

        // Если состояние уже изменилось с момента запуска автоматизма — оценить немедленно и закрыть окно.
        // Переиспользуем FinishTracking: он сам выставит CurrentState, вызовет AnalyzeResult,
        // запишет в эпизодическую память и обновит статистику.
        if (stateBefore != stateAfter)
        {
          lastResult.CurrentState = stateAfter;
          lastResult.EndPulse = GlobalTimer.GlobalPulsCount;
          FinishTracking(lastResult);
          UpdateAutomatizmUsefulness(automatizmId, lastResult.UsefulnessDelta);

          AppGlobalState.ResetWaitingForOperatorEvaluation();
          ShortLogChannel.WriteBlock("ОЦЕНКА АВТОМАТИЗМА:",
              $"ID {automatizmId}, по метрике на завершении мотора, дельта={lastResult.UsefulnessDelta}, состояние {stateBefore}→{stateAfter}");
          Logger.Info(
              $"NotifyMotorCompleted: automatizmId={automatizmId} pulse={GlobalTimer.GlobalPulsCount} " +
              $"state={stateBefore}→{stateAfter} delta={lastResult.UsefulnessDelta} (оценка по метрике)");
          return false;
        }

        // Дедлайн окна уже истёк, пока шёл мотор: ждать нечего, закрываем нейтрально.
        // Если окно потеряно сбросом индикаторов пульсации (случай 39), проверка по глобальному
        // состоянию ожидания бессмысленна — дедлайн сверяем с пульсом запуска самого автоматизма.
        bool windowLost = !AppGlobalState.WaitingForOperatorEvaluation;
        if (!windowLost && !AppGlobalState.IsOperatorResponseWithinWaitingWindow())
        {
          CloseResultAsSkipped(lastResult, automatizmId, stateBefore, stateAfter,
              "окно ожидания истекло, пока выполнялся мотор");
          return false;
        }

        // Окно могли закрыть сбросом индикаторов пульсации (случай 39): StartTracking окно открыл,
        // результат ещё жив, но WaitingForOperatorEvaluation уже снят. Переоткрываем окно на этом
        // автоматизме, чтобы polling-оценка по среде работала, — но от исходного дедлайна (пульса
        // запуска автоматизма), а не от полного периода заново.
        if (!AppGlobalState.WaitingForOperatorEvaluation)
        {
          AppGlobalState.StartWaitingForOperatorEvaluation(automatizmId, lastResult.StartPulse);
          Logger.Info(
              $"NotifyMotorCompleted: automatizmId={automatizmId} pulse={GlobalTimer.GlobalPulsCount} " +
              $"окно было сброшено, переоткрыто от пульса запуска {lastResult.StartPulse}");
        }

        // Отсчёт НЕ перезапускаем: окно продолжает тикать с пульса запуска автоматизма,
        // теперь уже с разрешённой оценкой по реакции среды (см. TryEvaluateWaitingByEnvironmentResponse).
        Logger.Info(
            $"NotifyMotorCompleted: automatizmId={automatizmId} pulse={GlobalTimer.GlobalPulsCount} " +
            $"state={stateAfter} мотор завершён, ожидание реакции метрики (осталось {AppGlobalState.WaitingPeriodCountdown})");
        return true;
      }
      catch (Exception ex)
      {
        Logger.Error($"NotifyMotorCompleted: automatizmId={automatizmId} error={ex.Message}");
        return false;
      }
    }

    /// <summary>
    /// Закрыть незавершённый результат нейтральным исходом (<see cref="ExecutionResult.Skipped"/>)
    /// без оценки по среде: действия фактически не было либо окно ожидания истекло.
    /// </summary>
    private void CloseResultAsSkipped(
        AutomatizmResult result,
        int automatizmId,
        AppGlobalState.HomeostasisState stateBefore,
        AppGlobalState.HomeostasisState stateAfter,
        string reason)
    {
      result.CurrentState = stateAfter;
      result.EndPulse = GlobalTimer.GlobalPulsCount;
      result.UsefulnessDelta = 0;
      result.Result = ExecutionResult.Skipped;
      FinishTracking(result);

      // FinishTracking → AnalyzeResult мог заменить нейтральный исход на WaitingForResponse
      // по длительности окна. Для уже закрытого окна это неверно: ответа уже не будет.
      result.UsefulnessDelta = 0;
      result.Result = ExecutionResult.Skipped;

      AppGlobalState.ResetWaitingForOperatorEvaluation();

      ShortLogChannel.WriteBlock("ОЦЕНКА АВТОМАТИЗМА:",
          $"ID {automatizmId}, без оценки — {reason}, состояние {stateBefore}→{stateAfter}");
      Logger.Info(
          $"NotifyMotorCompleted: automatizmId={automatizmId} pulse={GlobalTimer.GlobalPulsCount} " +
          $"state={stateBefore}→{stateAfter} (без оценки: {reason})");
    }

    /// <summary>
    /// Оценка запущенного автоматизма по реакции среды (дельта параметров/состояния), пока открыто
    /// окно ожидания. Вызывается движком на каждом пульсе после пересчёта гомеостаза.
    /// Без этого звена отпускание метрики не замечалось вообще: <see cref="AnalyzeResult"/> штатно
    /// выполняется только при истечении окна или ответе оператора, а плотная моторика перезапускала окно.
    /// </summary>
    /// <returns><c>true</c>, если окно закрыто оценкой по среде.</returns>
    public bool TryEvaluateWaitingByEnvironmentResponse()
    {
      if (!AppGlobalState.WaitingForOperatorEvaluation)
        return false;

      int automatizmId = AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation;
      if (automatizmId <= 0)
        return false;

      _lock.EnterWriteLock();
      try
      {
        // Словарь читается напрямую: GetLastResult берёт read-lock, а ReaderWriterLockSlim
        // в этом классе не рекурсивен — вложенный вызов под write-lock дал бы взаимоблокировку.
        AutomatizmResult result;
        if (!_lastAutomatizmResults.TryGetValue(automatizmId, out result))
          return false;
        if (result == null || result.EndPulse != 0 || result.RecognizedByOperator)
          return false;

        // Оценка по среде допустима только после фактического завершения мотора: пока диалог открыт,
        // среда не могла отреагировать на действие, которого ещё не было. Без этого гейта окно
        // закрывалось бы «улучшением» по посторонней причине ещё до выполнения рецепта.
        if (result.MotorCompletedPulse <= 0)
          return false;

        var stateBefore = AppGlobalState.StateBeforeOperatorImpact;
        var stateAfter = AppGlobalState.CurrentOverallState;

        int assessment;
        if (AppGlobalState.EvolutionStage >= 2
            && AppGlobalState.TryGetOperatorEvaluationParameterSnapshot(out var snapshotBefore, out int focusParamId)
            && snapshotBefore != null
            && GomeostasSystem.IsInitialized)
        {
          assessment = GomeostasSystem.Instance.Calculator.ComputeOperatorAutomatizmAssessment(
              snapshotBefore,
              GomeostasSystem.Instance.GetAllParameters(),
              focusParamId,
              stateBefore,
              stateAfter);
        }
        else
        {
          assessment = 0;
          if (stateAfter > stateBefore)
            assessment = 1;
          else if (AppGlobalState.IsStateWorsening(stateBefore, stateAfter))
            assessment = -1;
        }

        // Среда ещё не отреагировала — окно остаётся открытым (оценка наступит по истечении или по ответу оператора).
        if (assessment == 0)
          return false;

        result.CurrentState = stateAfter;
        result.EndPulse = GlobalTimer.GlobalPulsCount;
        result.UsefulnessDelta = assessment;
        result.Result = assessment > 0
            ? ExecutionResult.Success
            : ExecutionResult.Error;

        if (_episodicRulesService != null && result.ActionsImageId > 0)
          _episodicRulesService.FixDirectRule(
              result.StimulusImageId, result.ActionsImageId, assessment, AppGlobalState.PrevStimulsEffect);

        UpdateStatistics(result);
        AddHistoryRecord(result.AutomatizmId, result.Result, GetResultDescription(result));
        UpdateAutomatizmUsefulness(automatizmId, assessment);

        AppGlobalState.LastAutomatizmEvaluationTime = GlobalTimer.GlobalPulsCount;
        AppGlobalState.ResetWaitingForOperatorEvaluation();

        ShortLogChannel.WriteBlock("ОЦЕНКА АВТОМАТИЗМА:",
            $"ID {automatizmId}, по реакции среды, дельта={assessment}, состояние {stateBefore}→{stateAfter}");
        Logger.Info(
            $"Оценка автоматизма ID={automatizmId} по реакции среды на пульсе {GlobalTimer.GlobalPulsCount}: " +
            $"state={stateBefore}→{stateAfter}, дельта={assessment}, результат={result.Result}");
        return true;
      }
      catch (Exception ex)
      {
        Logger.Error($"TryEvaluateWaitingByEnvironmentResponse: {ex.Message}");
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Завершить отслеживание выполнения автоматизма
    /// </summary>
    public void FinishTracking(AutomatizmResult result)
    {
      if (result == null || result.AutomatizmId <= 0)
        return;

      try
      {
        result.EndPulse = GlobalTimer.GlobalPulsCount;
        result.CurrentState = AppGlobalState.CurrentOverallState;

        AnalyzeResult(result);

        // Запись в эпизодическую память
        if (_episodicRulesService != null && result.ActionsImageId > 0)
        {
          int triggerId = result.StimulusImageId;
          int effect = result.UsefulnessDelta;
          int stimulsEffect = AppGlobalState.PrevStimulsEffect;
          _episodicRulesService.FixDirectRule(triggerId, result.ActionsImageId, effect, stimulsEffect);
        }

        UpdateStatistics(result);
        AddHistoryRecord(
          result.AutomatizmId,
          result.Result,
          GetResultDescription(result));

        Logger.Info($"Завершено отслеживание автоматизма ID={result.AutomatizmId}, результат={result.Result}");
      }
      catch(Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Отметить результат распознавания оператором
    /// </summary>
    /// <param name="automatizmId">ID автоматизма</param>
    /// <param name="recognized">Признак распознавания оператором</param>
    /// <param name="assessment">Оценка (-1..1)</param>
    /// <param name="responseTime">Время реакции оператора</param>
    /// <param name="operatorResponseActionsImageId">ID образа действий оператора (для FixTeacherRule, stage 4)</param>
    /// <param name="updateUsefulness">Обновлять полезность automatizm (false для вторичного канала Operator на фазе B).</param>
    public void MarkOperatorRecognition(
        int automatizmId,
        bool recognized,
        int assessment = 0,
        int responseTime = 0,
        int operatorResponseActionsImageId = 0,
        bool updateUsefulness = true)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_lastAutomatizmResults.TryGetValue(automatizmId, out var result))
        {
          result.RecognizedByOperator = recognized;
          result.OperatorAssessment = assessment;
          result.OperatorResponseTime = responseTime;

          if (_episodicRulesService != null && operatorResponseActionsImageId > 0 && result.ActionsImageId > 0)
            _episodicRulesService.FixTeacherRule(operatorResponseActionsImageId, result.ActionsImageId, AppGlobalState.CurrentStimulsEffect);

          if (updateUsefulness)
            UpdateAutomatizmUsefulness(automatizmId, assessment);
          FinishTracking(result);
        }
        else
        {
          var atmz = _automatizmSystem.GetAutomatizmById(automatizmId);
          var newResult = new AutomatizmResult
          {
            AutomatizmId = automatizmId,
            ActionsImageId = atmz?.ActionsImageID ?? 0,
            RecognizedByOperator = recognized,
            OperatorAssessment = assessment,
            OperatorResponseTime = responseTime,
            Result = assessment > 0 ? ExecutionResult.Success :
                      assessment < 0 ? ExecutionResult.Error : ExecutionResult.Skipped
          };

          if (_episodicRulesService != null && operatorResponseActionsImageId > 0 && newResult.ActionsImageId > 0)
            _episodicRulesService.FixTeacherRule(operatorResponseActionsImageId, newResult.ActionsImageId, AppGlobalState.CurrentStimulsEffect);

          _lastAutomatizmResults[automatizmId] = newResult;
          if (updateUsefulness)
            UpdateAutomatizmUsefulness(automatizmId, assessment);
          FinishTracking(newResult);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получить результат последнего выполнения автоматизма
    /// </summary>
    /// <param name="automatizmId">ID automatizm.</param>
    /// <returns>Последний результат или null.</returns>
    public AutomatizmResult GetLastResult(int automatizmId)
    {
      _lock.EnterReadLock();
      try
      {
        return _lastAutomatizmResults.TryGetValue(automatizmId, out var result) ? result : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Очистить историю выполнения
    /// </summary>
    public void ClearHistory()
    {
      _lock.EnterWriteLock();
      try
      {
        _automatizmHistory.Clear();
        _lastHistoryId = 0;
        Logger.Info("История выполнения автоматизмов очищена");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получить сводку по выполнению автоматизмов
    /// </summary>
    public string GetExecutionSummary()
    {
      _lock.EnterReadLock();
      try
      {
        var stats = _executionStatistics;
        var successRate = stats.TotalExecutions > 0 ?
            (double)stats.SuccessCount / stats.TotalExecutions * 100 : 0;

        return $"Всего выполнений: {stats.TotalExecutions}, " +
               $"Успешно: {stats.SuccessCount} ({successRate:F1}%), " +
               $"Ошибок: {stats.ErrorCount}, " +
               $"Заблокировано: {stats.BlockedCount}, " +
               $"Среднее время: {stats.AverageExecutionTime:F1} пульсов";
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Вспомогательные методы

    private void AnalyzeResult(AutomatizmResult result)
    {
      // Оценка оператора уже задана в MarkOperatorRecognition (EvaluatePreviousAutomatizm в психике).
      // Не подменять её сравнением PreviousState/CurrentState за окно выполнения — иначе в логе всегда Skipped при совпадении уровней, даже при положительной оценке.
      if (result.RecognizedByOperator)
      {
        if (result.OperatorAssessment > 0)
        {
          result.UsefulnessDelta = 1;
          result.Result = ExecutionResult.Success;
        }
        else if (result.OperatorAssessment < 0)
        {
          result.UsefulnessDelta = -1;
          result.Result = ExecutionResult.Error;
        }
        else
        {
          result.UsefulnessDelta = 0;
          var executionTimeOp = result.EndPulse - result.StartPulse;
          if (executionTimeOp > AppGlobalState.WaitingPeriodForActionsVal)
            result.Result = ExecutionResult.WaitingForResponse;
          else
            result.Result = ExecutionResult.Skipped;
        }
        return;
      }

      // Анализ изменения состояния гомеостаза.
      // Возврат из «Хорошо» в «Норму» — это нормализация, а не ухудшение:
      // автоматизм не должен получать штраф, если его действие убрало «Хорошо» и система
      // вернулась в устойчивое состояние «Норма». Ухудшением считаем только переход
      // «Норма → Плохо» (и «Хорошо → Плохо» — но это уже покрывается текущим кодом).
      if (result.PreviousState != result.CurrentState)
      {
        // Состояние улучшилось
        if (result.CurrentState > result.PreviousState)
        {
          result.UsefulnessDelta = 1;
          result.Result = ExecutionResult.Success;
        }
        // Ухудшение: только переход в «Плохо». «Хорошо» → «Норма» — нормализация, не штраф.
        else if (AppGlobalState.IsStateWorsening(result.PreviousState, result.CurrentState))
        {
          result.UsefulnessDelta = -1;
          result.Result = ExecutionResult.Error;
        }
        else
        {
          result.UsefulnessDelta = 0;
          result.Result = ExecutionResult.Skipped;
        }
      }
      else
      {
        // Состояние не изменилось
        result.UsefulnessDelta = 0;

        // Проверяем, было ли ожидание ответа оператора
        var executionTime = result.EndPulse - result.StartPulse;
        if (executionTime > AppGlobalState.WaitingPeriodForActionsVal)
          result.Result = ExecutionResult.WaitingForResponse;
        else
          result.Result = ExecutionResult.Skipped;
      }
    }

    private void UpdateStatistics(AutomatizmResult result)
    {
      var executionTime = result.EndPulse - result.StartPulse;

      _executionStatistics.TotalExecutions++;
      _executionStatistics.LastExecutionPulse = result.EndPulse;

      switch (result.Result)
      {
        case ExecutionResult.Success:
          _executionStatistics.SuccessCount++;
          _executionStatistics.LastSuccessfulAutomatizmId = result.AutomatizmId;
          break;
        case ExecutionResult.Error:
          _executionStatistics.ErrorCount++;
          break;
        case ExecutionResult.Skipped:
          _executionStatistics.SkippedCount++;
          break;
        case ExecutionResult.Blocked:
          _executionStatistics.BlockedCount++;
          break;
      }

      // Обновление среднего времени выполнения
      if (_executionStatistics.TotalExecutions > 1)
      {
        _executionStatistics.AverageExecutionTime =
            (_executionStatistics.AverageExecutionTime * (_executionStatistics.TotalExecutions - 1) + executionTime) /
            _executionStatistics.TotalExecutions;
      }
      else
      {
        _executionStatistics.AverageExecutionTime = executionTime;
      }
    }

    private void AddHistoryRecord(int automatizmId, ExecutionResult result, string description)
    {
      var record = new AutomatizmExecutionRecord
      {
        Id = ++_lastHistoryId,
        AutomatizmId = automatizmId,
        ExecutionPulse = GlobalTimer.GlobalPulsCount,
        Result = result,
        Description = description
      };

      _automatizmHistory.Add(record);

      // Ограничиваем размер истории
      if (_automatizmHistory.Count > 1000)
      {
        _automatizmHistory.RemoveAt(0);
      }
    }

    private string GetResultDescription(AutomatizmResult result)
    {
      var executionTime = result.EndPulse - result.StartPulse;

      switch (result.Result)
      {
        case ExecutionResult.Success:
          return $"Успешно за {executionTime} пульсов, состояние: {result.PreviousState} → {result.CurrentState}";

        case ExecutionResult.Error:
          return $"Ошибка за {executionTime} пульсов, состояние: {result.PreviousState} → {result.CurrentState}" +
                 (string.IsNullOrEmpty(result.ErrorMessage) ? "" : $", ошибка: {result.ErrorMessage}");

        case ExecutionResult.Skipped:
          return $"Пропущено за {executionTime} пульсов, состояние не изменилось";

        case ExecutionResult.Blocked:
          return $"Заблокирован: {result.ErrorMessage}";

        case ExecutionResult.WaitingForResponse:
          return $"Ожидание ответа оператора ({executionTime} пульсов)";

        default:
          return $"Неизвестный результат: {result.Result}";
      }
    }

    /// <summary>
    /// Подсчитывает количество ошибок выполнения для указанного автоматизма за последние N выполнений
    /// </summary>
    private int CountRecentErrors(int automatizmId, int checkCount)
    {
      int errorCount = 0;
      int checkedCount = 0;

      // Идем с конца списка (последние выполнения)
      for (int i = _automatizmHistory.Count - 1; i >= 0 && checkedCount < checkCount; i--)
      {
        var record = _automatizmHistory[i];
        if (record.AutomatizmId == automatizmId)
        {
          checkedCount++;
          if (record.Result == ExecutionResult.Error)
          {
            errorCount++;
          }
        }
      }

      return errorCount;
    }

    /// <summary>
    /// Получает последние N записей истории для указанного автоматизма
    /// </summary>
   public List<AutomatizmExecutionRecord> GetRecentRecords(int automatizmId, int count)
    {
      var result = new List<AutomatizmExecutionRecord>();

      // Идем с конца списка (последние выполнения)
      for (int i = _automatizmHistory.Count - 1; i >= 0 && result.Count < count; i--)
      {
        var record = _automatizmHistory[i];
        if (record.AutomatizmId == automatizmId)
        {
          result.Add(record);
        }
      }

      // Возвращаем в правильном порядке (от старых к новым)
      result.Reverse();
      return result;
    }

    private void UpdateAutomatizmUsefulness(int automatizmId, int assessment)
    {
      try
      {
        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
        if (automatizm != null)
        {
          int before = automatizm.Usefulness;
          automatizm.Usefulness += assessment;
          automatizm.Usefulness = AddUtils.Clamp(automatizm.Usefulness, -10, 10);
          // Факт оценки пишем всегда: при дельте 0 или у границы шкалы строки «полезность X → X» нет,
          // и по короткому логу нельзя понять, оценивался автоматизм или нет (эвристика E43).
          ShortLogChannel.Write("Автоматизм {0}: полезность {1} → {2} (дельта {3}{4})",
              automatizmId, before, automatizm.Usefulness, assessment,
              before == automatizm.Usefulness ? ", без изменения" : string.Empty);
          _automatizmSystem.AfterAutomatizmUsefulnessUpdated(automatizmId);
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AutomatismResultTracker
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        _lock?.Dispose();
      }
      finally
      {
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}