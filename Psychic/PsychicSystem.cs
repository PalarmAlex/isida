using isida.Psychic.Automatism;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Media.Animation;

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
        InfluenceActionsImagesSystem influenceActionsImagesSystem,
        ActionsImagesSystem actionsImagesSystem,
        GomeostasSystem gomeostas)
    {
      if (_instance != null)
        throw new InvalidOperationException("PsychicSystem уже инициализирован.");

      _instance = new PsychicSystem(
          automatizmSystem,
          automatizmTreeSystem,
          influenceActionsImagesSystem,
          actionsImagesSystem,
          gomeostas);
    }

    private readonly AutomatizmSystem _automatizmSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly InfluenceActionsImagesSystem _influenceActionsImagesSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly GomeostasSystem _gomeostas;

    private PsychicSystem(
        AutomatizmSystem automatizmSystem,
        AutomatizmTreeSystem automatizmTreeSystem,
        InfluenceActionsImagesSystem influenceActionsImagesSystem,
        ActionsImagesSystem actionsImagesSystem,
        GomeostasSystem gomeostas)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _influenceActionsImagesSystem = influenceActionsImagesSystem ?? throw new ArgumentNullException(nameof(influenceActionsImagesSystem));
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

    // данные для инфо-картины
    private int _actionsImageId = 0;

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
    internal void ProcessPsychicPulse(int evolutionStage, int lifeTime, int pulseCount, int sleepingType)
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
          ProcessSleep();
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
    /// <param name="currentBaseId">ID состояния агента: -1: плохо, 0: норма, 1: хорошо</param>
    /// <param name="actionIdList">список ID действий с пульта</param>
    /// <param name="phraseIdList">список ID фраз с пульта</param>
    /// <param name="toneId">ID тона сообщения</param>
    /// <param name="moodId">ID настроения сообщения</param>
    /// <returns>True если нужно заблокировать рефлексы</returns>
    internal bool SensorActivation(
      int activationType,  
      int currentBaseId,
      List<int> actionIdList,
      List<int> phraseIdList,
      int toneId,
      int moodId)
    {
      if (PulseCount < 4)
        return false;

      if (EvolutionStage < 2)
      {
        LogWarning($"Стадия развития {EvolutionStage} НЕДОСТАТОЧНА ДЛЯ АВТОМАТИЗМОВ");
        return false;
      }

      ActivationTypeSensor = activationType;
      _actionsImageId = CreateActionsImage(actionIdList, phraseIdList, toneId, moodId); // для инфоркартины
      int currentActivityId = CreateInfluenceActionsImage(actionIdList, true);

      // Активация дерева автоматизмов
      int automatizmNodeId = AutomatizmTreeActivation(
          activationType,
          currentBaseId,
          _currentEmotionId, // новый класс по emotions.go. Тот же образ сочетаний контекстов, но с возможностью создавать его не только по стилям гомеостаза
          currentActivityId,
          _currentToneMoodId,
          _currentSimbolId,
          _currentPhraseId);
      // _currentToneMoodId, _currentSimbolId, _currentPhraseId - verbal_Broka_img.go

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
    internal int AutomatizmTreeActivation(
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
      SensorActivation(1, 0, null, null, 0, 0);

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

    #region Создание образов

    /// <summary>
    /// Создает образ действий оператора с учетом тона и настроения
    /// </summary>
    private int CreateActionsImage(List<int> actionIdList, List<int> phraseIdList, int toneId, int moodId)
    {
      try
      {
        if (_actionsImagesSystem == null || !ActionsImagesSystem.IsInitialized)
        {
          LogError("InfluenceActionsImagesSystem не инициализирована, образ действий не создан");
          return 0;
        }

        if (!ActionsImagesSystem.IsValidToneId(toneId))
        {
          LogError($"Некорректный toneId: {toneId}, используется значение по умолчанию (0)");
          toneId = 0; // Нормальный
        }

        if (!ActionsImagesSystem.IsValidMoodId(moodId))
        {
          LogError($"Некорректный moodId: {moodId}, используется значение по умолчанию (0)");
          moodId = 0; // Нормальное
        }

        // Создаем образ действий оператора
        // Kind = 0 (объективное действие) - реальное воздействие с пульта
        var (imageId, actionsImage) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0, // объективное действие
            actIdList: actionIdList,
            phraseIdList: phraseIdList,
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true // проверяем уникальность
        );

        if (imageId > 0)
          LogInfo($"Создан образ действий ID: {imageId}, Tone: {toneId}, Mood: {moodId}");
        else
          LogWarning("Не удалось создать образ действий");

        return imageId;
      }
      catch (Exception ex)
      {
        LogError($"Ошибка создания образа действий: {ex.Message}");
        return 0;
      }
    }

    /// <summary>
    /// Создает образ сочетаний действий с пульта (для дерева автоматизмов)
    /// </summary>
    /// <param name="actIdList">Список ID действий с пульта</param>
    /// <param name="checkUnicum">Проверять уникальность образа</param>
    /// <returns>ID созданного образа или 0 при ошибке</returns>
    private int CreateInfluenceActionsImage(List<int> actIdList, bool checkUnicum = true)
    {
      try
      {
        if (_influenceActionsImagesSystem == null || !InfluenceActionsImagesSystem.IsInitialized)
        {
          LogError("InfluenceActionsImagesSystem не инициализирована, образ сочетаний действий не создан");
          return 0;
        }

        if (actIdList == null || actIdList.Count == 0)
        {
          LogError("Список действий пуст, образ сочетаний действий не создан");
          return 0;
        }

        // Создаем образ сочетаний действий с пульта
        var (imageId, influenceActionsImage) = _influenceActionsImagesSystem.CreateNewInfluenceActionsImage(
            actIdList: actIdList,
            checkUnicum: checkUnicum
        );

        if (imageId > 0)
          LogInfo($"Создан образ сочетаний действий ID: {imageId}, " +
                         $"количество действий: {actIdList.Count}");
        else
          LogWarning("Не удалось создать образ сочетаний действий");

        return imageId;
      }
      catch (Exception ex)
      {
        LogError($"Ошибка создания образа сочетаний действий: {ex.Message}");
        return 0;
      }
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