using isida.Psychic.Automatism;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace isida.Psychic
{
  /// <summary>
  /// Центральная система психики - координатор автоматизмов и рефлексов
  /// </summary>
  public sealed class PsychicSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Инициализация

    private static PsychicSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы психики
    /// </summary>
    public static PsychicSystem Instance => _instance ??
        throw new InvalidOperationException("PsychicSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы психики
    /// </summary>
    public static void InitializeInstance(
        AutomatizmSystem automatizmSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        InfluenceActionsImagesSystem actionsImagesSystem,
        GomeostasSystem gomeostas)
    {
      if (_instance != null)
        throw new InvalidOperationException("PsychicSystem уже инициализирован.");

      _instance = new PsychicSystem(
          automatizmSystem,
          automatizmTreeSystem,
          actionsImagesSystem,
          gomeostas);
    }

    private readonly AutomatizmSystem _automatizmSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly InfluenceActionsImagesSystem _actionsImagesSystem;
    private readonly GomeostasSystem _gomeostas;

    private PsychicSystem(
        AutomatizmSystem automatizmSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        InfluenceActionsImagesSystem actionsImagesSystem,
        GomeostasSystem gomeostas)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));

      // Инициализация базового дерева автоматизмов
      InitializeBasicAutomatizmTree();
    }

    /// <summary>
    /// Инициализирует базовое дерево автоматизмов
    /// </summary>
    private void InitializeBasicAutomatizmTree()
    {
      try
      {
        // Создать первые три ветки базовых состояний, если их нет
        _automatizmTreeSystem.CreateBasicAutomatizmTree();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации дерева автоматизмов: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Состояния и свойства

    /// <summary>
    /// Стадия развития психики
    /// 0-1: Нет психики
    /// 2-3: Базовая психика
    /// 4+: Осознанная психика
    /// </summary>
    public int EvolutionStage { get; private set; } = 0;

    /// <summary>
    /// Текущий пульс психики
    /// </summary>
    public int PulseCount { get; private set; } = 0;

    /// <summary>
    /// Время жизни агента (в пульсах)
    /// </summary>
    public int LifeTime { get; private set; } = 0;

    /// <summary>
    /// Флаг сна без сновидений
    /// </summary>
    public bool IsSleeping { get; private set; } = false;

    /// <summary>
    /// Флаг фазы сновидений
    /// </summary>
    public bool IsSleepingDream { get; private set; } = false;

    /// <summary>
    /// Флаг активации при пробуждении
    /// </summary>
    public bool WakeUppingActivation { get; private set; } = true;

    /// <summary>
    /// Флаг первой активации после пробуждения
    /// </summary>
    public bool IsFirstActivation { get; private set; } = true;

    /// <summary>
    /// Готовность к общению
    /// 0 - не готов
    /// 1 - психика активирована без осознания
    /// 2 - готов к общению
    /// </summary>
    public int ReadyStatus { get; private set; } = 0;

    /// <summary>
    /// Блокировка любых действий
    /// </summary>
    public bool NotAllowAnyActions { get; private set; } = false;

    /// <summary>
    /// Флаг активации изменением условий (не оператором)
    /// </summary>
    public bool WasConditionsActivated { get; private set; } = false;

    /// <summary>
    /// Флаг активации оператором
    /// </summary>
    public bool WasOperatorActivated { get; private set; } = false;

    /// <summary>
    /// Тип активации сенсора
    /// 1 - изменение условий
    /// 2 - действие с пульта
    /// 3 - фраза с пульта
    /// </summary>
    public int ActivationTypeSensor { get; private set; } = 0;

    // Текущие состояния восприятия для дерева автоматизмов
    private int _currentBaseId = 0;
    private int _currentEmotionId = 0;
    private int _currentActivityId = 0;
    private int _currentToneMoodId = 0;
    private int _currentSimbolId = 0;
    private int _currentPhraseId = 0;

    // Предыдущие состояния (для сравнения)
    private int _oldBaseId = 0;
    private int _oldEmotionId = 0;

    // Текущий активный автоматизм
    private int _currentAutomatizmId = 0;
    private int _lastRunAutomatizmPulsCount = 0;
    private Automatizm _lastRunAutomatizm = null;

    // Детектор отсутствия автоматизма
    private int _noAutomatizmAfterStimul = 0;

    #endregion

    #region Основные методы

    /// <summary>
    /// Обработка пульса психики
    /// </summary>
    public void ProcessPsychicPulse(int evolutionStage, int lifeTime, int pulseCount, int sleepingType)
    {
      _lock.EnterWriteLock();
      try
      {
        if (evolutionStage < 2) // Недостаточная стадия развития
          return;

        LifeTime = lifeTime;
        EvolutionStage = evolutionStage;
        PulseCount = pulseCount;

        // Обработка состояния сна
        if (sleepingType > 0)
        {
          IsSleeping = true;
          IsSleepingDream = (sleepingType == 2);
        }
        else
        {
          IsSleeping = false;
          IsSleepingDream = false;
        }

        // Обработка тиков при бодрствовании
        if (!IsSleeping)
        {
          if (IsFirstActivation)
          {
            ReadyStatus = 1; // Психика активирована без осознания
            IsFirstActivation = false;
          }

          // Осознание при включении и бодрствовании
          if (EvolutionStage > 3 && PulseCount > 4 && WakeUppingActivation)
          {
            // Начало мышления
            WakeUpping();
            ReadyStatus = 2; // Готов к общению

            // Первый запуск дерева автоматизмов
            AutomatizmTreeActivation(1, 0, 0, 0, 0, 0, 0);
            WakeUppingActivation = false;
          }

          // Детектор отсутствия автоматизма на стимул
          if (_noAutomatizmAfterStimul > 2 && (_noAutomatizmAfterStimul < PulseCount - 2) && PulseCount > 5)
          {
            _noAutomatizmAfterStimul = 2; // Сигнал детектора отсутствия автоматизма
            LogInfo("ПРАВИЛА. Уже 2 пульса как нет автоматизма в ответ на Стимул");
          }
        }
        else
        {
          // Обработка сна
          ProcessSleep();
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Активация по событиям с Пульта - основной метод
    /// </summary>
    /// <param name="activationType">Тип активации: 1-изменение условий, 2-действие, 3-фраза</param>
    /// <returns>True если нужно заблокировать рефлексы</returns>
    public bool SensorActivation(int activationType)
    {
      if (PulseCount < 4)
        return false;

      if (EvolutionStage < 2)
      {
        LogWarning($"Стадия развития {EvolutionStage} НЕДОСТАТОЧНА ДЛЯ АВТОМАТИЗМОВ");
        return false;
      }

      ActivationTypeSensor = activationType;

      // Активация дерева автоматизмов
      int automatizmNodeId = AutomatizmTreeActivation(
          activationType,
          _currentBaseId,
          _currentEmotionId,
          _currentActivityId,
          _currentToneMoodId,
          _currentSimbolId,
          _currentPhraseId);

      if (automatizmNodeId > 0)
      {
        // Получить автоматизм из узла
        var automatizm = GetAutomatizmFromNode(automatizmNodeId);
        if (automatizm != null)
        {
          // Выполнить автоматизм
          ExecuteAutomatizm(automatizm);
          return true; // Блокировать рефлексы
        }
      }

      return false; // Не блокировать рефлексы
    }

    /// <summary>
    /// Активация дерева автоматизмов
    /// </summary>
    public int AutomatizmTreeActivation(
        int activationType,
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int phraseId,
        bool isUnrecognizedPhrase = false)
    {
      if (PulseCount < 4)
        return 0;

      if (IsSleeping)
        return 0;

      // Сохранить предыдущие состояния
      _oldBaseId = _currentBaseId;
      _oldEmotionId = _currentEmotionId;

      // Обновить текущие состояния
      _currentBaseId = baseId;
      _currentEmotionId = emotionId;
      _currentActivityId = activityId;
      _currentToneMoodId = toneMoodId;
      _currentSimbolId = simbolId;
      _currentPhraseId = phraseId;

      // Сброс детектора для действий оператора
      if (activationType > 1)
        _noAutomatizmAfterStimul = PulseCount;

      // Активация дерева
      int detectedNodeId = _automatizmTreeSystem.AutomatizmTreeActivation(
          baseId,
          emotionId,
          activityId,
          toneMoodId,
          simbolId,
          phraseId,
          isUnrecognizedPhrase);

      return detectedNodeId;
    }

    /// <summary>
    /// Получить автоматизм из узла дерева
    /// </summary>
    private Automatizm GetAutomatizmFromNode(int nodeId)
    {
      if (nodeId <= 0)
        return null;

      // Сначала проверяем штатный автоматизм (Belief == 2)
      var beliefAutomatizm = _automatizmSystem.GetBelief2AutomatizmFromTreeId(nodeId);
      if (beliefAutomatizm != null && beliefAutomatizm.Usefulness >= 0)
        return beliefAutomatizm;

      // Ищем автоматизмы для этого узла
      var automatizms = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(nodeId);
      if (automatizms.Count == 0)
        return null;

      // Выбираем самый успешный автоматизм
      return automatizms
          .Where(a => a.Usefulness >= 0)
          .OrderByDescending(a => a.Usefulness)
          .ThenByDescending(a => a.Count)
          .FirstOrDefault();
    }

    /// <summary>
    /// Выполнение автоматизма
    /// </summary>
    private void ExecuteAutomatizm(Automatizm automatizm)
    {
      if (automatizm == null)
        return;

      _lock.EnterWriteLock();
      try
      {
        _currentAutomatizmId = automatizm.ID;
        _lastRunAutomatizmPulsCount = PulseCount;
        _lastRunAutomatizm = automatizm;

        LogInfo($"Запущен автоматизм ID: {automatizm.ID} для узла: {automatizm.BranchID}");

        // Здесь будет логика выполнения действий автоматизма
        // Пока просто логируем

        // Если это действие с пульта (BranchID > 1000000)
        if (automatizm.BranchID > 1000000 && automatizm.BranchID < 2000000)
        {
          int actionImageId = automatizm.BranchID - 1000000;
          LogInfo($"Выполнение действия из образа: {actionImageId}");
        }
        // Если это фраза (BranchID > 2000000)
        else if (automatizm.BranchID > 2000000)
        {
          int phraseImageId = automatizm.BranchID - 2000000;
          LogInfo($"Произнесение фразы из образа: {phraseImageId}");
        }

        // Сброс детектора отсутствия автоматизма
        if (_noAutomatizmAfterStimul > 0)
          _noAutomatizmAfterStimul = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Пробуждение - создание базового самоощущения
    /// </summary>
    private void WakeUpping()
    {
      // Активация самоощущения
      SensorActivation(1);

      LogInfo("Пробуждение - создание базового самоощущения");
    }

    /// <summary>
    /// Обработка сна
    /// </summary>
    private void ProcessSleep()
    {
      // Логика обработки сна
      if (IsSleepingDream)
      {
        // Фаза сновидений
        // Здесь можно добавить обработку сновидений
      }
      else
      {
        // Глубокий сон
        // Минимальная активность психики
      }
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Блокировать любые действия
    /// </summary>
    public void SetNotAllowAnyActions(bool notAllow)
    {
      _lock.EnterWriteLock();
      try
      {
        NotAllowAnyActions = notAllow;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Установить флаг активации оператором
    /// </summary>
    public void SetWasOperatorActivated(bool activated)
    {
      _lock.EnterWriteLock();
      try
      {
        WasOperatorActivated = activated;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получить информацию о готовности для пульта
    /// </summary>
    public string GetPsychicReady()
    {
      return ReadyStatus.ToString();
    }

    /// <summary>
    /// Получить расширенную информацию для пульта
    /// </summary>
    public string GetExtendInfoForPult()
    {
      int detectedNodeId = _automatizmTreeSystem.DetectedActiveLastNodeId;
      if (detectedNodeId == 0)
        return "";

      return $"Инфо: BaseID=<b>{_currentBaseId}</b>, " +
             $"EmotionID=<b>{_currentEmotionId}</b>, " +
             $"atmzmID=<b>{detectedNodeId}</b>, " +
             $"стадия=<b>{EvolutionStage}</b>, " +
             $"готовность=<b>{ReadyStatus}</b>";
    }

    #endregion

    #region Логирование

    // тут надо сохранять логи в файле - подумать над структурой
    private static void LogInfo(string message)
    {
      Debug.WriteLine($"[PsychicSystem] INFO: {message}");
    }

    private static void LogWarning(string message)
    {
      Debug.WriteLine($"[PsychicSystem] WARNING: {message}");
    }

    private static void LogError(string message)
    {
      FileValidator.LogError($"[PsychicSystem] ERROR: {message}");
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом PsychicSystem
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
      }
    }

    #endregion
  }
}