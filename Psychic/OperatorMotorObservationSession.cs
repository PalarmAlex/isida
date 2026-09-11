using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Сессия наблюдения моторных действий оператора (механизм 2 стадии 2).
  /// </summary>
  /// <remarks>
  /// Сценарий:
  ///  1. Focus-витал в Bad + ОР1 не дал usable atmz -> открывается сессия.
  ///  2. Оператор действует (пульт, UI SW). В сессию попадают только G_AD.
  ///  3. После каждого G_AD -- post-motor wait (таймер B, пульсы).
  ///  4. Если focus ушёл из Bad -> запись одного atmz, сессия закрывается.
  ///  5. Таймаут wait -> мотор отбрасывается, сессия ждёт следующий G_AD.
  /// </remarks>
  public sealed class OperatorMotorObservationSession : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly InfluenceActionSystem _influenceActionSystem;
    private readonly InfluenceActionsImagesSystem _influenceActionsImagesSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;

    #region Инициализация

    private static OperatorMotorObservationSession _instance;

    /// <summary>
    /// Глобальный экземпляр сессии наблюдения. Должен быть инициализирован через InitializeInstance().
    /// </summary>
    public static OperatorMotorObservationSession Instance =>
        _instance ?? throw new InvalidOperationException("OperatorMotorObservationSession not initialized.");

    /// <summary>
    /// Флаг инициализации сессии наблюдения.
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сессии наблюдения.
    /// Вызывается из IsidaEngine при запуске движка ISIDA.
    /// </summary>
    /// <param name="adaptiveActionsSystem">Система адаптивных действий (G_AD).</param>
    /// <param name="actionsImagesSystem">Система образов действий.</param>
    /// <param name="automatizmSystem">Система автоматизмов.</param>
    /// <param name="influenceActionSystem">Система действий-влияний (probe-EA).</param>
    /// <param name="influenceActionsImagesSystem">Система образов действий с пульта (для ActivityID).</param>
    /// <param name="automatizmTreeSystem">Дерево автоматизмов (для привязки к узлу с ActivityID).</param>
    public static void InitializeInstance(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem,
        AutomatizmSystem automatizmSystem,
        InfluenceActionSystem influenceActionSystem,
        InfluenceActionsImagesSystem influenceActionsImagesSystem,
        AutomatizmTreeSystem automatizmTreeSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("OperatorMotorObservationSession already initialized.");

      _instance = new OperatorMotorObservationSession(
          adaptiveActionsSystem, actionsImagesSystem, automatizmSystem, influenceActionSystem,
          influenceActionsImagesSystem, automatizmTreeSystem);
    }

    private OperatorMotorObservationSession(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem,
        AutomatizmSystem automatizmSystem,
        InfluenceActionSystem influenceActionSystem,
        InfluenceActionsImagesSystem influenceActionsImagesSystem,
        AutomatizmTreeSystem automatizmTreeSystem)
    {
      _adaptiveActionsSystem = adaptiveActionsSystem ?? throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _influenceActionSystem = influenceActionSystem ?? throw new ArgumentNullException(nameof(influenceActionSystem));
      _influenceActionsImagesSystem = influenceActionsImagesSystem ?? throw new ArgumentNullException(nameof(influenceActionsImagesSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
    }

    #endregion

    #region Состояние сессии

    /// <summary>
    /// Активна ли сессия наблюдения (ожидает G_AD от оператора).
    /// </summary>
    public bool IsActive
    {
      get { _lock.EnterReadLock(); try { return _isActive; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// ID параметра гомеостаза, который является focus сессии (DominantParam).
    /// </summary>
    public int FocusParameterId
    {
      get { _lock.EnterReadLock(); try { return _focusParameterId; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// ID мотора (G_AD) от оператора на момент последнего зафиксированного действия.
    /// Используется при создании автоматизма для указания правильного actionId.
    /// </summary>
    public int LastMotorActionId
    {
      get { _lock.EnterReadLock(); try { return _lastMotorActionId; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// Был ли focus в зоне Bad на момент открытия сессии (rising-edge).
    /// </summary>
    public bool WasInBadZone
    {
      get { _lock.EnterReadLock(); try { return _wasInBadZone; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// Было ли состояние focus в Bad на предыдущем пульсе (для rising-edge определения).
    /// </summary>
    private bool _prevWasInBad = false;

    /// <summary>
    /// Пульс, когда был зафиксирован последний G_AD от оператора.
    /// Используется для расчёта post-motor wait (таймер B).
    /// </summary>
    public int LastMotorPulse
    {
      get { _lock.EnterReadLock(); try { return _lastMotorPulse; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// Список активных probe-EA (InfluenceAction IDs) на момент последнего мотора.
    /// Используется как якорь контекста для создаваемого автоматизма.
    /// </summary>
    public List<int> ActiveProbeActionIds
    {
      get { _lock.EnterReadLock(); try { return _activeProbeActionIds?.ToList() ?? new List<int>(); } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// Tone на момент открытия сессии (контекст состояния).
    /// </summary>
    public int ContextToneId
    {
      get { _lock.EnterReadLock(); try { return _contextToneId; } finally { _lock.ExitReadLock(); } }
    }

    /// <summary>
    /// Mood на момент открытия сессии (контекст настроения).
    /// </summary>
    public int ContextMoodId
    {
      get { _lock.EnterReadLock(); try { return _contextMoodId; } finally { _lock.ExitReadLock(); } }
    }

    private bool _isActive = false;
    private int _focusParameterId = 0;
    private bool _wasInBadZone = false;
    private int _lastMotorActionId = 0;
    private int _lastMotorPulse = 0;
    private List<int> _activeProbeActionIds = new List<int>();
    private int _contextToneId = 0;
    private int _contextMoodId = 0;

    #endregion

    #region Публичные методы

    /// <summary>
    /// Открыть сессию наблюдения: focus-витал в Bad, ОР1 не дал usable atmz.
    /// </summary>
    public bool OpenSession()
    {
      _lock.EnterWriteLock();
      try
      {
        if (_isActive)
        {
          Logger.Info("OperatorMotorObservationSession: session already active");
          return false;
        }

        if (AppGlobalState.EvolutionStage != 2)
        {
          Logger.Info("OperatorMotorObservationSession: not on stage 2");
          return false;
        }

        _focusParameterId = AppGlobalState.DominantParam;
        if (_focusParameterId <= 0)
        {
          Logger.Info("OperatorMotorObservationSession: no dominant param");
          return false;
        }

        _wasInBadZone = AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Bad;

        if (!_wasInBadZone)
        {
          Logger.Info("OperatorMotorObservationSession: focus not in Bad");
          return false;
        }

        // Сохраняем контекст на момент открытия сессии (Tone/Mood).
        // Это будет использовано при создании ActionsImage для автоматизма.
        _contextToneId = AppGlobalState.CurrentStimulusToneId;
        _contextMoodId = AppGlobalState.CurrentStimulusMoodId;

        _isActive = true;
        _lastMotorActionId = 0;
        _lastMotorPulse = 0;
        _activeProbeActionIds = new List<int>();
        _prevWasInBad = _wasInBadZone;

        Logger.Info(
            $"OperatorMotorObservationSession: opened focusParam={_focusParameterId} pulse={GlobalTimer.GlobalPulsCount} " +
            $"tone={_contextToneId} mood={_contextMoodId}");
        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Закрыть сессию (успешно: focus ушёл из Bad).
    /// </summary>
    public void CloseSessionSuccessfully(int automatizmId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_isActive) return;
        _isActive = false;
        Logger.Info(
            $"OperatorMotorObservationSession: closed successfully atmz={automatizmId} " +
            $"focusParam={_focusParameterId} pulse={GlobalTimer.GlobalPulsCount}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Закрыть сессию (таймаут: мотор не привёл к улучшению).
    /// </summary>
    public void CloseSessionTimeout()
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_isActive) return;
        _isActive = false;
        Logger.Info(
            $"OperatorMotorObservationSession: closed timeout focusParam={_focusParameterId} " +
            $"pulse={GlobalTimer.GlobalPulsCount}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Зафиксировать G_AD от оператора в сессии.
    /// </summary>
    public bool RecordOperatorMotor(int actionId, List<int> probeActionIds)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_isActive)
        {
          Logger.Info($"OperatorMotorObservationSession: session not active, ignoring motor actionId={actionId}");
          return false;
        }

        var action = _adaptiveActionsSystem.GetAdaptiveAction(actionId);
        if (action == null || action.TargetGomeoParamIdArr == null || action.TargetGomeoParamIdArr.Count == 0)
        {
          Logger.Info($"OperatorMotorObservationSession: actionId={actionId} is not a G_AD");
          return false;
        }

        _lastMotorPulse = GlobalTimer.GlobalPulsCount;
        _lastMotorActionId = actionId;
        _activeProbeActionIds = probeActionIds != null
            ? probeActionIds.Distinct().OrderBy(x => x).ToList()
            : new List<int>();

        Logger.Info(
            $"OperatorMotorObservationSession: recorded motor actionId={actionId} pulse={_lastMotorPulse} " +
            $"probes={string.Join(",", _activeProbeActionIds)}");
        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверить post-motor wait: истёк ли таймер.
    /// </summary>
    public bool IsPostMotorWaitExpired(int waitDurationPulses)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_isActive || _lastMotorPulse <= 0) return false;
        int elapsed = GlobalTimer.GlobalPulsCount - _lastMotorPulse;
        return elapsed >= waitDurationPulses;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить оставшееся время post-motor wait (пульсы).
    /// Возвращает 0, если сессия не активна или мотор не записан.
    /// </summary>
    public int GetRemainingPostMotorWaitPulses(int waitDurationPulses)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_isActive || _lastMotorPulse <= 0) return 0;
        int elapsed = GlobalTimer.GlobalPulsCount - _lastMotorPulse;
        int remaining = waitDurationPulses - elapsed;
        return Math.Max(0, remaining);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверить, ушёл ли focus из Bad (Bad -> Well / не-Bad).
    /// </summary>
    public bool FocusExitedBadZone()
    {
      _lock.EnterReadLock();
      try
      {
        if (!_isActive || !_wasInBadZone) return false;
        bool isCurrentBad = AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Bad;
        return !isCurrentBad;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать автоматизм по зафиксированному мотору оператора.
    /// Триггер привязывается к ActivityID (набор активных probe-EA), а не только к состоянию/стилям.
    /// </summary>
    public int CreateAutomatizmFromMotor(int actionId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_isActive || _lastMotorPulse <= 0) return 0;

        var action = _adaptiveActionsSystem.GetAdaptiveAction(actionId);
        if (action == null)
        {
          Logger.Error("OperatorMotorObservationSession: action not found for atmz creation");
          return 0;
        }

        var probeIds = _activeProbeActionIds ?? new List<int>();
        int actionsImageId;
        // Используем сохранённый контекст Tone/Mood на момент открытия сессии.
        // Это обеспечивает правильную привязку автоматизма к состоянию и стилям.
        (actionsImageId, _) = _actionsImagesSystem.CreateNewActionsImageWithIdNoLock(
            0, 0, new List<int> { actionId }, null, _contextToneId, _contextMoodId, true);

        if (actionsImageId <= 0)
        {
          Logger.Error("OperatorMotorObservationSession: failed to create ActionsImage");
          return 0;
        }

        // Создаём ActivityID из probe-EA для привязки триггера к метрике.
        // Это позволяет автоматизму срабатывать при активации той же метрики, а не только по состоянию/стилям.
        int activityId = 0;
        if (probeIds.Count > 0 && _influenceActionsImagesSystem != null)
        {
          var (actId, _) = _influenceActionsImagesSystem.CreateNewInfluenceActionsImage(probeIds, true);
          if (actId > 0)
            activityId = actId;
        }

        // Находим или используем текущий узел дерева с activityId.
        // Если activityId > 0 — ищем узел с привязкой к метрике.
        // Иначе — используем текущий AutomatizmNodeId (состояние + стили).
        int branchId;
        if (activityId > 0 && _automatizmTreeSystem != null)
        {
          // Определяем baseId совместимо с C# 7.3
          int baseId = 0;
          if (AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Bad)
            baseId = -1;
          else if (AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Well)
            baseId = 1;

          // Ищем существующий узел с activityId
          var foundNode = _automatizmTreeSystem.FindAutomatizmTreeNodeFromCondition(
              baseId,
              0, // emotionId
              activityId,
              0, // toneMoodId
              0, // simbolId
              0); // verbID

          if (foundNode.Node != null)
          {
            branchId = foundNode.Id;
          }
          else
          {
            // Узел не найден — используем текущий AutomatizmNodeId.
            // В будущем можно добавить создание узла с ActivityID.
            Logger.Info($"OperatorMotorObservationSession: node with activityId={activityId} not found, using AutomatizmNodeId={AppGlobalState.AutomatizmNodeId}");
            branchId = AppGlobalState.AutomatizmNodeId;
          }
        }
        else
        {
          branchId = AppGlobalState.AutomatizmNodeId;
        }

        int atmzId;
        Automatizm atmz = null;
        (atmzId, atmz) = _automatizmSystem.CreateNewAutomatizm(branchId, actionsImageId);

        if (atmz == null)
        {
          Logger.Error("OperatorMotorObservationSession: failed to create Automatizm");
          return 0;
        }

        atmz.Usefulness = 1; // Начальная полезность "+" по факту снятия проблемы

        Logger.Info(
            $"OperatorMotorObservationSession: created automatizm atmzId={atmzId} " +
            $"branchId={branchId} actionsImageId={actionsImageId} activityId={activityId} usefulness=1");

        return atmzId;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сбросить сессию (смена документа, падение стадии и т.п.).
    /// </summary>
    public void Reset()
    {
      _lock.EnterWriteLock();
      try
      {
        _isActive = false;
        _focusParameterId = 0;
        _wasInBadZone = false;
        _lastMotorActionId = 0;
        _lastMotorPulse = 0;
        _activeProbeActionIds = new List<int>();
        _contextToneId = 0;
        _contextMoodId = 0;
        _prevWasInBad = false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы сессии наблюдения и сбрасывает глобальный экземпляр.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
      catch (Exception ex)
      {
        Logger.Error($"OperatorMotorObservationSession.Dispose: {ex.Message}");
      }
    }

    #endregion
  }
}
