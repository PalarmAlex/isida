using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ISIDA.Psychic;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Глобальный таймер и диспетчер пульсации системы гомеостаза.
  /// Централизованно управляет обновлением состояния агента, включая:
  /// - обновление параметров гомеостаза
  /// - пересчёт активных стилей
  /// - реакцию на отклонения (через адаптивные действия)
  /// Клиент вызывает Start()/Stop() — всё остальное происходит внутри.
  /// </summary>
  public static class GlobalTimer
  {
    #region Поля и свойства

    private static Timer _timer;
    private static Timer _autosaveTimer;
    private static readonly object _timerLock = new object();
    private static bool _isRunning = false;
    private static int _secondsSinceLastSave = 0;

    /// <summary>
    /// Событие завершения обработки пульса
    /// </summary>
    public static event Action<int> OnPulseCompleted;

    /// <summary>
    /// Событие ошибки пульсации
    /// </summary>
    public static event Action<string> OnPulseError;

    // Настройки длительности фаз пульса
    private const int GreenDurationMs = 200;   // Яркая вспышка
    private const int FadeDurationMs = 300;    // Плавное затухание
    private const int GrayDurationMs = 500;    // Пауза после затухания (итого 1000мс = 1сек)
    private const int AutosaveIntervalSeconds = 10; // Интервал автосохранения

    /// <summary>
    /// Глобальный счетчик пульсов
    /// </summary>
    public static int GlobalPulsCount { get; private set; } = 0;

    /// <summary>
    /// Флаг активности пульсации
    /// </summary>
    public static bool IsPulsationRunning { get; private set; } = false;

    // Системы, участвующие в пульсе
    private static GomeostasSystem _gomeostas;
    private static AdaptiveActionsSystem _actionsSystem;
    private static ReflexesActivator _reflexesActivator;
    private static ConditionedReflexesSystem _conditionedReflexesSystem;
    private static ConditionedReflexFormationService _reflexFormationService;
    private static PsychicSystem _psychicSystem;

    private static bool HasConditionedReflexesSystem => _conditionedReflexesSystem != null;
    private static bool HasReflexFormationService => _reflexFormationService != null;

    /// <summary>
    /// Установка зависимости для _conditionedReflexesSystem
    /// </summary>
    public static void SetConditionedReflexesSystem(ConditionedReflexesSystem system)
    {
      _conditionedReflexesSystem = system ?? throw new ArgumentNullException(nameof(system));
    }

    /// <summary>
    /// Установка зависимости для _reflexFormationService
    /// </summary>
    public static void SetReflexFormationService(ConditionedReflexFormationService service)
    {
      _reflexFormationService = service ?? throw new ArgumentNullException(nameof(service));
    }

    #endregion

    #region События

    /// <summary>
    /// Событие изменения состояния пульсации (запуск/остановка)
    /// </summary>
    public static event Action PulsationStateChanged;

    /// <summary>
    /// Событие изменения состояния пульса (вкл/выкл)
    /// </summary>
    public static event Action<bool> OnPulseStateChanged;

    /// <summary>
    /// Событие изменения яркости пульса (для плавной анимации)
    /// </summary>
    public static event Action<double> OnPulseBrightnessChanged;

    /// <summary>
    /// Событие автосохранения
    /// </summary>
    public static event Action OnAutosave;

    #endregion

    #region Публичные методы

    /// <summary>
    /// Инициализирует системы, участвующие в пульсации.
    /// Должен быть вызван один раз при старте приложения, до Start().
    /// </summary>
    /// <param name="gomeostas">Система гомеостаза</param>
    /// <param name="actionsSystem">Система адаптивных действий</param>
    /// <param name="reflexesActivator">Система запуска условных и безусловных рефлексов</param>
    /// <param name="psychicSystem">Система запуска психики</param>
    public static void InitializeSystems(
        GomeostasSystem gomeostas,
        AdaptiveActionsSystem actionsSystem,
        ReflexesActivator reflexesActivator,
        PsychicSystem psychicSystem)
    {
      if (gomeostas == null) throw new ArgumentNullException(nameof(gomeostas));
      if (actionsSystem == null) throw new ArgumentNullException(nameof(actionsSystem));
      if (reflexesActivator == null) throw new ArgumentNullException(nameof(reflexesActivator));
      if (psychicSystem == null) throw new ArgumentNullException(nameof(psychicSystem));

      _gomeostas = gomeostas;
      _actionsSystem = actionsSystem;
      _reflexesActivator = reflexesActivator;
      _psychicSystem = psychicSystem;
    }

    /// <summary>
    /// Запускает глобальную пульсацию системы гомеостаза
    /// </summary>
    public static void Start()
    {
      lock (_timerLock)
      {
        if (_isRunning) return;
        if (_gomeostas == null || _actionsSystem == null)
          throw new InvalidOperationException("GlobalTimer: системы не инициализированы. Вызовите InitializeSystems().");

        var agentState = _gomeostas.GetAgentState();
        if (agentState.EvolutionStage < 1)
          throw new InvalidOperationException("Запуск пульсации разрешен только начиная со стадии 1");

        IsPulsationRunning = true;
        PulsationStateChanged?.Invoke();
        _isRunning = true;
        _secondsSinceLastSave = 0;

        // Запуск таймера пульсации
        _timer = new Timer(TimerCallback, null, 0, Timeout.Infinite);

        // Запуск таймера автосохранения
        _autosaveTimer = new Timer(AutosaveCallback, null, 1000, 1000);
      }
    }

    /// <summary>
    /// Останавливает глобальную пульсацию
    /// </summary>
    public static void Stop()
    {
      bool shouldStop = false;
      lock (_timerLock)
      {
        if (_isRunning)
        {
          shouldStop = true;
          _isRunning = false;
        }
      }

      if (shouldStop)
      {
        StopTimers();
        Debug.WriteLine("GlobalTimer.Stop: Остановка по запросу пользователя завершена");
      }
    }

    /// <summary>
    /// Сбрасывает счетчик пульсов (без сброса времени жизни агента)
    /// </summary>
    public static void Reset()
    {
      GlobalPulsCount = 0;
      _secondsSinceLastSave = 0;
    }

    #endregion

    #region Приватные методы

    /// <summary>
    /// Callback таймера автосохранения
    /// </summary>
    private static void AutosaveCallback(object state)
    {
      lock (_timerLock)
      {
        if (!_isRunning) return;

        _secondsSinceLastSave++;

        if (_secondsSinceLastSave >= AutosaveIntervalSeconds)
        {
          TriggerAutosave();
          _secondsSinceLastSave = 0;
        }
      }
    }

    /// <summary>
    /// Вызывает автосохранение
    /// </summary>
    private static void TriggerAutosave()
    {
      try
      {
        _gomeostas.SaveAgentProperties();
        OnAutosave?.Invoke();
      }
      catch
      {

      }
    }

    private static void TimerCallback(object state)
    {
      lock (_timerLock)
      {
        if (!_isRunning || _timer == null)
        {
          Debug.WriteLine("GlobalTimer.TimerCallback: Таймер остановлен, пропускаем callback");
          return;
        }
      }

      try
      {
        // Фаза 1: Сигнализация начала пульса
        OnPulseStateChanged?.Invoke(true);
        OnPulseBrightnessChanged?.Invoke(1.0);
        Thread.Sleep(GreenDurationMs);

        // Фаза 2: Затухание
        for (int i = 9; i >= 0; i--)
        {
          lock (_timerLock)
          {
            if (!_isRunning)
            {
              Debug.WriteLine("GlobalTimer.TimerCallback: Таймер остановлен во время fade");
              return;
            }
          }
          OnPulseBrightnessChanged?.Invoke(i * 0.1);
          Thread.Sleep(FadeDurationMs / 10);
        }

        // Фаза 3: Обновление состояния агента
        lock (_timerLock)
        {
          if (!_isRunning)
          {
            Debug.WriteLine("GlobalTimer.TimerCallback: Таймер остановлен перед ProcessAgentPulse");
            return;
          }
          GlobalPulsCount++;
          _gomeostas.PulseCount = GlobalPulsCount;
          ProcessAgentPulse();
        }

        // Фаза 4: Пауза и перезапуск таймера
        lock (_timerLock)
        {
          if (!_isRunning)
          {
            Debug.WriteLine("GlobalTimer.TimerCallback: Таймер остановлен перед изменением интервала");
            return;
          }
          _timer?.Change(GrayDurationMs, Timeout.Infinite);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"GlobalTimer.TimerCallback: Общая ошибка в TimerCallback: {ex.Message}");
        OnPulseError?.Invoke($"Общая ошибка таймера: {ex.Message}");
        Stop();
      }
    }

    /// <summary>
    /// Безопасная остановка при смерти агента
    /// </summary>
    private static void SafeStopWithAgentDeath()
    {
      Debug.WriteLine("SafeStopWithAgentDeath: Начало остановки из-за смерти агента");

      bool shouldStop = false;
      lock (_timerLock)
      {
        if (_isRunning)
        {
          shouldStop = true;
          _isRunning = false;
        }
      }

      if (shouldStop)
      {
        // Вызываем событие смерти агента
        OnPulseError?.Invoke("Агент умер");

        // Останавливаем таймеры
        StopTimers();
        Debug.WriteLine("SafeStopWithAgentDeath: Остановка завершена");
      }
    }

    /// <summary>
    /// Безопасная остановка при ошибке
    /// </summary>
    private static void SafeStopWithError(string errorMessage)
    {
      Debug.WriteLine($"SafeStopWithError: Начало остановки из-за ошибки: {errorMessage}");

      bool shouldStop = false;
      lock (_timerLock)
      {
        if (_isRunning)
        {
          shouldStop = true;
          _isRunning = false;
        }
      }

      if (shouldStop)
      {
        // Вызываем событие ошибки
        OnPulseError?.Invoke(errorMessage);

        // Останавливаем таймеры
        StopTimers();

        Debug.WriteLine("SafeStopWithError: Остановка завершена");
      }
    }

    /// <summary>
    /// Остановка таймеров (без рекурсивных вызовов)
    /// </summary>
    private static void StopTimers(bool notifyUI = true)
    {
      Timer timerToDispose = null;
      Timer autosaveTimerToDispose = null;

      lock (_timerLock)
      {
        IsPulsationRunning = false;

        // Сохраняем ссылки на таймеры для dispose вне lock
        timerToDispose = _timer;
        autosaveTimerToDispose = _autosaveTimer;

        // Обнуляем ссылки
        _timer = null;
        _autosaveTimer = null;
      }

      try
      {
        // Dispose таймеров ВНЕ lock чтобы избежать deadlock
        timerToDispose?.Dispose();
        autosaveTimerToDispose?.Dispose();
        Debug.WriteLine("StopTimers: Таймеры disposed");

        TriggerAutosave();

        // Уведомляем UI только если нужно
        if (notifyUI)
        {
          OnPulseStateChanged?.Invoke(false);
          OnPulseBrightnessChanged?.Invoke(0);
          PulsationStateChanged?.Invoke();
        }

        Debug.WriteLine("StopTimers: Остановка завершена успешно");
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"StopTimers: Ошибка при остановке: {ex.Message}");
      }
    }

    /// <summary>
    /// Обрабатывает один пульс: обновляет параметры, стили и активирует реакции.
    /// </summary>
    private static void ProcessAgentPulse()
    {
      try
      {
        lock (_timerLock)
        {
          if (!_isRunning)
          {
            Debug.WriteLine("GlobalTimer.ProcessAgentPulse: Таймер остановлен, пропускаем пульс");
            return;
          }
        }
        try
        {
          _gomeostas.UpdateStateOnly();
        }
        catch (Exception gomeostasEx)
        {
          Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: КРИТИЧЕСКАЯ ОШИБКА в UpdateStateOnly: {gomeostasEx}");
          SafeStopWithError($"Критическая ошибка гомеостаза: {gomeostasEx.Message}");
          return;
        }

        GomeostasSystem.AgentStateInfo agentState = null;
        try
        {
          agentState = _gomeostas.GetAgentState();
        }
        catch (Exception stateEx)
        {
          Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка получения состояния агента: {stateEx.Message}");
          SafeStopWithError($"Ошибка получения состояния: {stateEx.Message}");
          return;
        }

        // Если агент мертв - прерываем обработку
        if (agentState?.IsDead == true)
        {
          Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Агент мертв на пульсе {GlobalPulsCount}");
          SafeStopWithAgentDeath();
          return;
        }
        else
        {
          // флаг сна получмть кодга класс сна будет
          int sleepingType = 0;
          var currentStyles = agentState.ActiveStyles;
          var activetStyleIds = currentStyles.Select(s => s.Id).ToList();
          _psychicSystem.ProcessPsychicPulse(agentState.EvolutionStage, agentState.Lifetime, activetStyleIds, GlobalPulsCount, sleepingType);
        }

        // Увеличение времени жизни в пульсах для условных рефлексов
        if (!agentState.IsDead && HasConditionedReflexesSystem)
        {
          try
          {
            _conditionedReflexesSystem.UpdateAgentLifetime();
          }
          catch (Exception conditionedEx)
          {
            Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка в IncrementPulse: {conditionedEx.Message}");
            // Важно: НЕ сбрасываем _conditionedReflexesSystem в null при ошибке,
            // так как это может быть временной проблемой
          }
        }

        if (!agentState.IsSleeping)
        {
          try
          {
            _reflexesActivator.ProcessReflexPulse(GlobalPulsCount, agentState.IsSleeping);
          }
          catch (Exception reflexEx)
          {
            Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка в ProcessReflexPulse: {reflexEx.Message}");
            // Продолжаем выполнение, даже если рефлексы сломались
          }
        }

        // периодическая очистка условных рефлексов
        if (!agentState.IsDead && !agentState.IsSleeping &&
            HasReflexFormationService &&
            GlobalPulsCount % 100 == 0)
        {
          try
          {
            _reflexFormationService.CleanupOldReflexes(GlobalPulsCount);
          }
          catch (Exception cleanupEx)
          {
            Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка очистки рефлексов: {cleanupEx.Message}");
          }
        }

        if (!agentState.IsDead)
        {
          try
          {
            _actionsSystem.CleanupExpiredReflexActions();
          }
          catch (Exception actionEx)
          {
            Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка в CleanupExpiredReflexActions: {actionEx.Message}");
            // Продолжаем выполнение, даже если действия сломались
          }
        }

        if (!agentState.IsDead)
          OnPulseCompleted?.Invoke(GlobalPulsCount);
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: НЕОБРАБОТАННАЯ КРИТИЧЕСКАЯ ОШИБКА: {ex}");
        SafeStopWithError($"Критическая ошибка обработки пульса: {ex.Message}");
      }
      finally
      {
        try
        {
          _gomeostas.IsNewConditions = false;
          _reflexesActivator.ResetStates();
        }
        catch (Exception finalEx)
        {
          Debug.WriteLine($"GlobalTimer.ProcessAgentPulse: Ошибка в finally блоке: {finalEx.Message}");
        }
      }
    }

    #endregion
  }
}