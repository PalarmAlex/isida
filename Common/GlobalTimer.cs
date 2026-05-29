using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using ISIDA.Psychic;
using ISIDA.Psychic.Understanding;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Глобальный таймер и диспетчер пульсации системы гомеостаза.
  /// Централизованно управляет обновлением состояния симбионта, включая:
  /// - обновление параметров гомеостаза
  /// - пересчёт активных стилей
  /// - реакцию на отклонения (через адаптивные действия)
  /// Клиент вызывает Start()/Stop() — всё остальное происходит внутри.
  /// </summary>
  public static class GlobalTimer
  {
    #region Поля и свойства

    private static Timer _timer;
    private static readonly object _timerLock = new object();
    private static bool _isRunning = false;
    private static ResearchLogger _researchLogger; // это нужно для корректной выгрузки в IsidaEngine!!!

    /// <summary>Ускорение пульса по календарю: 1 — базово ~1 с на цикл; &gt;1 укорачивает паузы (сценарии).</summary>
    /// <remarks>volatile: чтение с UI во время пульса не должно вызывать <c>lock(_timerLock)</c> — иначе взаимная блокировка с <c>Dispatcher.Invoke</c> в обработчике пульса при удержании того же lock в <c>ProcessAgentPulse</c>.</remarks>
    private static volatile int _pulseWallTimeMultiplier = 1;

    /// <summary>При прогоне сценария: не тратить время на фазу анимации пульса.</summary>
    private static volatile bool _suppressPulseAnimation = false;

    /// <summary>
    /// Установка ссылки на логер
    /// </summary>
    public static void SetResearchLogger(ResearchLogger logger)
    {
      _researchLogger = logger;
    }

    /// <summary>
    /// Событие завершения обработки пульса
    /// </summary>
    public static event Action<int> OnPulseCompleted;

    /// <summary>
    /// После <see cref="Gomeostas.GomeostasSystem.UpdateStateOnly"/> на пульсе, до <see cref="Psychic.PsychicSystem.ProcessPsychicPulse"/> —
    /// для сценария оператора: стимул на том же глобальном пульсе, но после дрейфа гомеостаза (как при клике в паузе между фазами),
    /// чтобы не накладываться на отложенную оценку ОР/зеркало до обновления параметров.
    /// </summary>
    public static event Action<int> OnPulseAfterGomeostasisBeforePsychic;

    /// <summary>
    /// Перед <see cref="Gomeostas.GomeostasSystem.UpdateStateOnly"/> на пульсе — для хоста: атомарная подстановка
    /// значений встроенных параметров среды из последнего полного снимка.
    /// </summary>
    public static event Action<int> OnPulseBeforeGomeostasis;

    /// <summary>
    /// Событие ошибки пульсации
    /// </summary>
    public static event Action<string> OnPulseError;

    // Настройки длительности фаз пульса
    private const int GreenDurationMs = 200;   // Яркая вспышка
    private const int FadeDurationMs = 300;    // Плавное затухание
    private const int GrayDurationMs = 500;    // Пауза после затухания (итого 1000мс = 1сек)

    /// <summary>
    /// Глобальный счетчик пульсов
    /// </summary>
    public static int GlobalPulsCount { get; private set; } = 0;

    /// <summary>
    /// Флаг активности пульсации
    /// </summary>
    public static bool IsPulsationRunning { get; private set; } = false;

    /// <summary>Текущий множитель скорости пульса по времени (1 = норма).</summary>
    public static int PulseWallTimeMultiplier => _pulseWallTimeMultiplier;

    /// <summary>Анимация пульса пропущена (ускоренный прогон).</summary>
    public static bool IsPulseAnimationSuppressed => _suppressPulseAnimation;

    /// <summary>
    /// Ускорение пульсации по календарю (например прогон сценария). Частота ~ в <paramref name="multiplier"/> раз выше при той же логике по счётчику пульсов.
    /// </summary>
    public static void SetPulseWallClockAcceleration(int multiplier, bool suppressAnimation)
    {
      int clamped = multiplier < 1 ? 1 : (multiplier > 20 ? 20 : multiplier);
      _pulseWallTimeMultiplier = clamped;
      _suppressPulseAnimation = suppressAnimation && clamped > 1;
      if (clamped <= 1)
        _suppressPulseAnimation = false;
    }

    /// <summary>Сброс ускорения пульса по времени (после сценария).</summary>
    public static void ClearPulseWallClockAcceleration()
    {
      _suppressPulseAnimation = false;
      _pulseWallTimeMultiplier = 1;
    }

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

    #endregion

    #region Публичные методы

    /// <summary>
    /// true, если после <see cref="InitializeSystems"/> статические ссылки на системы ещё не сброшены вызовом <see cref="ClearSystems"/>.
    /// Используется хостом, чтобы не считать контекст ISIDA живым после освобождения <see cref="T:ISIDA.Common.IsidaEngine+IsidaContext"/>.
    /// </summary>
    public static bool ArePulseSystemsReady =>
        _gomeostas != null && _actionsSystem != null;

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

        if (AppGlobalState.EvolutionStage < 1)
          throw new InvalidOperationException("Запуск пульсации разрешен только начиная со стадии 1");

        IsPulsationRunning = true;
        PulsationStateChanged?.Invoke();
        _isRunning = true;

        // Запуск таймера пульсации
        _timer = new Timer(TimerCallback, null, 0, Timeout.Infinite);
      }
    }

    /// <summary>
    /// Останавливает глобальную пульсацию
    /// </summary>
    public static void Stop()
    {
      bool wasRunning = false;
      lock (_timerLock)
      {
        wasRunning = _isRunning;
        if (_isRunning)
        {
          _isRunning = false;
          IsPulsationRunning = false; // Сначала устанавливаем флаг
        }
      }

      if (wasRunning)
      {
        OnPulsationStopped();
        // Уведомляем UI
        PulsationStateChanged?.Invoke();
        // Останавливаем таймеры (не уведомляем UI повторно)
        StopTimers(notifyUI: false);
        Logger.Info("Остановка по запросу пользователя завершена");
      }
    }

    /// <summary>
    /// Сбрасывает счетчик пульсов (без сброса времени жизни симбионта)
    /// </summary>
    public static void Reset()
    {
      GlobalPulsCount = 0;
    }

    /// <summary>
    /// Очищает все ссылки на системы (вызывать при завершении приложения)
    /// </summary>
    public static void ClearSystems()
    {
      lock (_timerLock)
      {
        StopTimers(notifyUI: false);

        // Очищаем все статические ссылки
        _gomeostas = null;
        _actionsSystem = null;
        _reflexesActivator = null;
        _conditionedReflexesSystem = null;
        _reflexFormationService = null;
        _psychicSystem = null;

        // Очищаем подписки на события
        OnPulseCompleted = null;
        OnPulseAfterGomeostasisBeforePsychic = null;
        OnPulseBeforeGomeostasis = null;
        OnPulseError = null;
        OnPulseStateChanged = null;
        OnPulseBrightnessChanged = null;
        PulsationStateChanged = null;

        Logger.Info("GlobalTimer: все системы очищены");
      }
    }

    #endregion

    #region Приватные методы

    private static void TimerCallback(object state)
    {
      lock (_timerLock)
      {
        if (!_isRunning || _timer == null)
        {
          Logger.Info("Таймер остановлен, пропускаем callback");
          Monitor.Pulse(_timerLock); // Сигнализируем о завершении
          return;
        }
      }

      try
      {
        int m = _pulseWallTimeMultiplier < 1 ? 1 : _pulseWallTimeMultiplier;
        if (m > 20) m = 20;
        bool suppressAnim = _suppressPulseAnimation;

        int greenMs = suppressAnim ? 0 : Math.Max(1, GreenDurationMs / m);
        int fadeStepMs = suppressAnim ? 0 : Math.Max(1, FadeDurationMs / 10 / m);
        int grayMs = Math.Max(1, GrayDurationMs / m);

        // Фаза 1–2: анимация пульса (пропуск при ускоренном прогоне сценария)
        OnPulseStateChanged?.Invoke(true);
        if (suppressAnim)
        {
          OnPulseBrightnessChanged?.Invoke(0);
        }
        else
        {
          OnPulseBrightnessChanged?.Invoke(1.0);
          Thread.Sleep(greenMs);
          for (int i = 9; i >= 0; i--)
          {
            lock (_timerLock)
            {
              if (!_isRunning)
                return;
            }
            OnPulseBrightnessChanged?.Invoke(i * 0.1);
            Thread.Sleep(fadeStepMs);
          }
        }

        // Фаза 3: обновление счётчика под lock; сам обработчик пульса — снаружи lock.
        // поток таймера удерживает _timerLock, UI ждёт lock в GlobalTimer — взаимная блокировка, «зависание» SW.
        lock (_timerLock)
        {
          if (!_isRunning)
          {
            Logger.Warning("Таймер остановлен перед ProcessAgentPulse");
            return;
          }
          GlobalPulsCount++;
          _gomeostas.PulseCount = GlobalPulsCount;
        }

        ProcessAgentPulse();

        // Фаза 4: Пауза и перезапуск таймера
        lock (_timerLock)
        {
          if (!_isRunning)
          {
            Logger.Warning("Таймер остановлен перед изменением интервала");
            return;
          }
          _timer?.Change(grayMs, Timeout.Infinite);
          Monitor.Pulse(_timerLock);
        }
      }
      catch (Exception ex)
      {
        Logger.Warning($"Общая ошибка в TimerCallback: {ex.Message}");
        OnPulseError?.Invoke($"Общая ошибка таймера: {ex.Message}");
        lock (_timerLock)
        {
          Monitor.Pulse(_timerLock); // Все равно сигнализируем
        }
        Stop();
      }
    }

    /// <summary>
    /// Безопасная остановка при смерти симбионта
    /// </summary>
    private static void SafeStopWithAgentDeath()
    {
      Logger.Info("Начало остановки из-за смерти симбионта");

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
        OnPulsationStopped();
        // Вызываем событие смерти симбионта
        OnPulseError?.Invoke("Симбионт умер");

        // Останавливаем таймеры
        StopTimers();
        Logger.Info("Остановка завершена");
      }
    }

    /// <summary>
    /// Безопасная остановка при ошибке
    /// </summary>
    private static void SafeStopWithError(string errorMessage)
    {
      Logger.Info($"Начало остановки из-за ошибки: {errorMessage}");

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
        OnPulsationStopped();
        // Вызываем событие ошибки
        OnPulseError?.Invoke(errorMessage);

        // Останавливаем таймеры
        StopTimers();

        Logger.Info("Остановка завершена");
      }
    }

    /// <summary>
    /// Выполняется при остановке пульсации: сброс периода ожидания, активных действий, стилей, цепочек, параметров.
    /// </summary>
    private static void OnPulsationStopped()
    {
      try
      {
        AppGlobalState.ForceStopWaitingForOperatorEvaluation();
        _actionsSystem?.ClearAllActiveState();
        AppGlobalState.ClearPulseIndicators();
        _gomeostas?.ClearPulseRuntimeIndicators();
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка при сбросе состояния при остановке пульсации: {ex.Message}");
      }
    }

    /// <summary>
    /// Остановка таймеров (без рекурсивных вызовов)
    /// </summary>
    private static void StopTimers(bool notifyUI = true)
    {
      Timer timerToDispose = null;

      lock (_timerLock)
      {
        // Устанавливаем флаг остановки ПЕРВЫМ делом
        _isRunning = false;
        IsPulsationRunning = false; // Уже установлено в Stop(), но для безопасности

        // Сохраняем ссылки на таймеры для dispose вне lock
        timerToDispose = _timer;

        // Обнуляем ссылки
        _timer = null;
      }

      try
      {
        // Dispose таймеров ВНЕ lock чтобы избежать deadlock
        timerToDispose?.Dispose();
        Logger.Info("Таймеры остановлены и disposed");

        try
        {
          if (_researchLogger == null || !_researchLogger.IsDisposed)
            _gomeostas?.SaveAgentProperties();
        }
        catch
        {
          // Игнорируем ошибки при завершении
        }

        // Уведомляем UI только если нужно
        if (notifyUI)
        {
          OnPulseStateChanged?.Invoke(false);
          OnPulseBrightnessChanged?.Invoke(0);
          PulsationStateChanged?.Invoke(); // Только если notifyUI = true
        }

        if (notifyUI)
        {
          // ОЧИСТКА ПОДПИСОК НА СОБЫТИЯ
          OnPulseCompleted = null;
          OnPulseAfterGomeostasisBeforePsychic = null;
          OnPulseBeforeGomeostasis = null;
          OnPulseError = null;
          OnPulseStateChanged = null;
          OnPulseBrightnessChanged = null;
          PulsationStateChanged = null;
        }

        Logger.Info("Остановка завершена успешно");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
            Logger.Warning("Таймер остановлен, пропускаем пульс");
            return;
          }
        }
        try
        {
          try
          {
            ThinkingThemePulseResolver.ResolveAtPulseStart(GlobalPulsCount);
          }
          catch (Exception themeEx)
          {
            Logger.Warning($"ThinkingThemePulseResolver: {themeEx.Message}");
          }

          try
          {
            OnPulseBeforeGomeostasis?.Invoke(GlobalPulsCount);
          }
          catch (Exception hostEx)
          {
            Logger.Warning($"OnPulseBeforeGomeostasis: {hostEx.Message}");
          }

          _gomeostas.UpdateStateOnly();
        }
        catch (Exception gomeostasEx)
        {
          Logger.Error($"{gomeostasEx}");
          SafeStopWithError($"Критическая ошибка гомеостаза: {gomeostasEx.Message}");
          return;
        }

        // Если симбионт мертв - прерываем обработку
        if (AppGlobalState.IsDead)
        {
          Logger.Warning($"Симбионт мертв на пульсе {GlobalPulsCount}");
          SafeStopWithAgentDeath();
          return;
        }

        try
        {
          OnPulseAfterGomeostasisBeforePsychic?.Invoke(GlobalPulsCount);
        }
        catch (Exception ex)
        {
          Logger.Warning($"OnPulseAfterGomeostasisBeforePsychic: {ex.Message}");
        }

        if (AppGlobalState.IsDead)
        {
          Logger.Warning($"Симбионт мертв на пульсе {GlobalPulsCount}");
          SafeStopWithAgentDeath();
          return;
        }

        // флаг сна получмть когда класс сна будет
        int sleepingType = 0;
        var currentStyles = AppGlobalState.ActiveStyles;
        var activetStyleIds = currentStyles.Select(s => s.Id).ToList();
        _psychicSystem.ProcessPsychicPulse(activetStyleIds, GlobalPulsCount, sleepingType);

        // Увеличение времени жизни в пульсах для условных рефлексов
        if (!AppGlobalState.IsDead && HasConditionedReflexesSystem)
        {
          try
          {
            _conditionedReflexesSystem.UpdateAgentLifetime();
          }
          catch (Exception conditionedEx)
          {
            Logger.Error($"{conditionedEx.Message}");
            // Важно: НЕ сбрасываем _conditionedReflexesSystem в null при ошибке,
            // так как это может быть временной проблемой
          }
        }

        if (!AppGlobalState.IsSleeping)
        {
          try
          {
            _reflexesActivator.ProcessReflexPulse(GlobalPulsCount, AppGlobalState.IsSleeping);
          }
          catch (Exception reflexEx)
          {
            Logger.Error($"{reflexEx.Message}");
            // Продолжаем выполнение, даже если рефлексы сломались
          }
        }

        // Строка симбионта за пульс буферизуется в ResearchLogger и обычно уходит в UI только на следующем пульсе.
        // Иначе HTML-отчёт сценария (построенный сразу после последнего шага) не видит ОР/автоматизм за этот пульс.
        try
        {
          _researchLogger?.FlushBufferedAgentRowToMemoryNow();
        }
        catch (Exception flushEx)
        {
          Logger.Warning($"FlushBufferedAgentRowToMemoryNow: {flushEx.Message}");
        }

        if (!AppGlobalState.IsDead)
        {
          try
          {
            _actionsSystem.CleanupExpiredReflexActions();
          }
          catch (Exception actionEx)
          {
            Logger.Error($"{actionEx.Message}");
            // Продолжаем выполнение, даже если действия сломались
          }
        }

        if (!AppGlobalState.IsDead)
        {
          try
          {
            ThinkingThemePulseResolver.RecordEndOfPulseAgentEvents();
          }
          catch (Exception endPulseEx)
          {
            Logger.Warning($"ThinkingThemePulseResolver.RecordEndOfPulse: {endPulseEx.Message}");
          }
        }

        // Сценарий оператора, UI и др. подписчики должны получать пульс независимо от наличия ResearchLogger.
        if (!AppGlobalState.IsDead)
          OnPulseCompleted?.Invoke(GlobalPulsCount);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        SafeStopWithError($"Критическая ошибка обработки пульса: {ex.Message}");
      }
      finally
      {
        try
        {
          AppGlobalState.IsNewConditions = false;
          _reflexesActivator.ResetStates(GlobalPulsCount);
        }
        catch (Exception finalEx)
        {
          Logger.Error($"{finalEx.Message}");
        }
      }
    }

    #endregion
  }
}