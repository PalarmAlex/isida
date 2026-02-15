using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Documents;
using System.Windows.Media.Animation;
using static ISIDA.Psychic.VerbalBrocaImagesSystem;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Центральная система психики - координатор автоматизмов и рефлексов
  /// </summary>
  public sealed class PsychicSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    private readonly AutomatizmSystem _automatizmSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly InfluenceActionsImagesSystem _influenceActionsImagesSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly EmotionsImageSystem _emotionsImageSystem;
    private readonly SensorySystem _sensorySystem;
    private readonly VerbalBrocaImagesSystem _verbalBrocaImages;
    private readonly AutomatismResultTracker _automatismResultTracker;
    private OrientationReflexSystem _orientationReflexSystem;
    private AutomatismExecutionService _automatismExecutionService;
    private PerceptionImagesSystem _perceptionImagesSystem;
    private readonly MirrorAutomatizmService _mirrorAutomatizmService;

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
        EmotionsImageSystem emotionsImageSystem,
        SensorySystem sensorySystem,
        VerbalBrocaImagesSystem verbalBrocaImages,
        AutomatismResultTracker automatismResultTracker)
    {
      if (_instance != null)
        throw new InvalidOperationException("PsychicSystem уже инициализирован.");

      _instance = new PsychicSystem(
        automatizmSystem,
        automatizmTreeSystem,
        influenceActionsImagesSystem,
        actionsImagesSystem,
        emotionsImageSystem,
        sensorySystem,
        verbalBrocaImages,
        automatismResultTracker);
    }

    private PsychicSystem(
      AutomatizmSystem automatizmSystem,
      AutomatizmTreeSystem automatizmTreeSystem,
      InfluenceActionsImagesSystem influenceActionsImagesSystem,
      ActionsImagesSystem actionsImagesSystem,
      EmotionsImageSystem emotionsImageSystem,
      SensorySystem sensorySystem,
      VerbalBrocaImagesSystem verbalBrocaImages,
      AutomatismResultTracker automatismResultTracker)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _influenceActionsImagesSystem = influenceActionsImagesSystem ?? throw new ArgumentNullException(nameof(influenceActionsImagesSystem));
      _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
      _emotionsImageSystem = emotionsImageSystem ?? throw new ArgumentNullException(nameof(emotionsImageSystem));
      _sensorySystem = sensorySystem ?? throw new ArgumentNullException(nameof(sensorySystem));
      _verbalBrocaImages = verbalBrocaImages ?? throw new ArgumentNullException(nameof(verbalBrocaImages));
      _automatismResultTracker = automatismResultTracker ?? throw new ArgumentNullException(nameof(automatismResultTracker));
      _mirrorAutomatizmService = new MirrorAutomatizmService(_automatizmSystem);

      InitializeBasicAutomatizmTree();
    }

    /// <summary>
    /// Установка сервиса выполнения автоматизмов
    /// </summary>
    public void SetPsychicSystemDop(
      AutomatismExecutionService executionService,
      OrientationReflexSystem orientationReflexSystem,
      PerceptionImagesSystem perceptionImagesSystem)
    {
      _automatismExecutionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
      _orientationReflexSystem = orientationReflexSystem ?? throw new ArgumentNullException(nameof(orientationReflexSystem));
      _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));
    }

    /// <summary>
    /// Инициализирует базовое дерево автоматизмов
    /// </summary>
    private void InitializeBasicAutomatizmTree()
    {
      try
      {
        // Создать первые три ветки базовых состояний, если их нет9
        _automatizmTreeSystem.CreateBasicAutomatizmTree();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Состояния и свойства

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

    // Текущие и предыдущие автоматизмы
    private int _currentAutomatizmId = 0;
    private int _previousAutomatizmId = 0;

    // оператор отреагировал
    private bool _isAnswer = false;

    #endregion

    #region Основные методы

    /// <summary>
    /// Обработка пульса психики
    /// </summary>
    internal void ProcessPsychicPulse(
      List<int> activetStyleIds,
      int pulseCount,
      int sleepingType)
    {
      int mirrorAutomatizmToExecute = 0;
      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage < 2) // Недостаточная стадия развития
          return;

        PulseCount = pulseCount;
        LifeTime = AppGlobalState.Lifetime;

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
          // Осознание при включении и бодрствовании
          if (AppGlobalState.EvolutionStage > 3 && PulseCount > 4 && WakeUppingActivation)
          {
            // Начало мышления
            WakeUpping(activetStyleIds);

            // Первый запуск дерева автоматизмов
            AutomatizmTreeActivation(1, 0, 0, 0, 0, 0, 0);
            WakeUppingActivation = false;
          }

          if (AppGlobalState.WaitingForOperatorEvaluation)
          {
            // Время ожидания оценки автоматизма истекло
            if (!AppGlobalState.IsEvaluationTime())
            {
              ResetAutomatizmWaitingState();
              Logger.Info($"Время ожидания оценки истекло для автоматизма ID={_currentAutomatizmId}");
            }
            else
            {
              int automatizmToEvaluate = _previousAutomatizmId > 0 ? _previousAutomatizmId : _currentAutomatizmId;
              if (automatizmToEvaluate > 0 && _isAnswer)
              {
                mirrorAutomatizmToExecute = EvaluatePreviousAutomatizm(automatizmToEvaluate);
                _isAnswer = false;
              }
            }
          }
          _automatismExecutionService.ProcessAutomatizmChainsPulse(pulseCount);
        }
        else
          ProcessSleep();
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (mirrorAutomatizmToExecute > 0)
      {
        var mirrorAutomatizm = _automatizmSystem.GetAutomatizmById(mirrorAutomatizmToExecute);
        if (mirrorAutomatizm != null)
          ExecuteAutomatizm(mirrorAutomatizm);
      }
    }

    /// <summary>
    /// Активация по событиям с Пульта - основной метод
    /// </summary>
    /// <param name="activationType">Тип активации: 1-изменение условий, 2-действие, 3-фраза</param>
    /// <param name="currentBaseId">ID состояния агента: -1: плохо, 0: норма, 1: хорошо</param>
    /// <param name="stileIdList">список ID активных стилей</param>
    /// <param name="actionIdList">список ID действий с пульта</param>
    /// <param name="phraseIdList">список ID фраз с пульта</param>
    /// <param name="toneId">ID тона сообщения</param>
    /// <param name="moodId">ID настроения сообщения</param>
    /// <returns>True если нужно заблокировать рефлексы</returns>
    internal bool SensorActivation(
      int activationType,  
      int currentBaseId,
      List<int> stileIdList, // хотя через пульсы передается StileIdList, от действия может поменяться stileIdList на текущем пульсе
      List<int> actionIdList,
      List<int> phraseIdList,
      int toneId,
      int moodId)
    {
      if (AppGlobalState.EvolutionStage < 2)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для автоматизмов");
        return false;
      }
      
      if ((actionIdList == null || actionIdList.Count == 0) && (phraseIdList == null || phraseIdList.Count == 0))
        return false;

      try
      {
        if (AppGlobalState.WaitingForOperatorEvaluation &&  activationType >= 2 && AppGlobalState.IsEvaluationTime())
          _isAnswer = true;

        int actionsImageId = CreateActionsImage(actionIdList, phraseIdList, toneId, moodId);
        int currentActivityId = CreateInfluenceActionsImage(actionIdList, true);
        (int currentEmotionId, _) = _emotionsImageSystem.CreateNewEmotionsImage(stileIdList, true);
        int toneMood = GetToneMoodID(toneId, moodId);

        int firstSimbol = 0;
        int verbId = 0;

        if (phraseIdList?.Any() == true)
        {
          firstSimbol = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(phraseIdList[0]);
          (verbId, _) = _verbalBrocaImages.CreateNewVerbalBrocaImage(firstSimbol, phraseIdList, toneId, moodId, true);
          AppGlobalState.CurActiveVerbalId = verbId;
          var perceptionImageId = _perceptionImagesSystem.AddPerceptionImage(actionIdList, phraseIdList);
          AppGlobalState.LastTriggerStimulusID = perceptionImageId;
        }
        else
          AppGlobalState.CurActiveVerbalId = 0;

        Automatizm atmz = null;
        int automatizmNodeId = AutomatizmTreeActivation(
            activationType,
            currentBaseId,
            currentEmotionId,
            currentActivityId,
            toneMood,
            firstSimbol,
            verbId);

        if (automatizmNodeId > 0)
        {
          bool hasVerbalPart = phraseIdList?.Any() == true;
          if (AppGlobalState.WaitingForOperatorEvaluation && activationType >= 2 && AppGlobalState.IsEvaluationTime())
            _mirrorAutomatizmService.RegisterOperatorResponseActionsImage(actionsImageId);

          AppGlobalState.AutomatizmNodeId = automatizmNodeId;
          var foundAutomatizm = GetAutomatizmFromNode(automatizmNodeId);

          if (foundAutomatizm == null &&
              AppGlobalState.EvolutionStage == 3 &&
              !AppGlobalState.WaitingForOperatorEvaluation &&
              activationType >= 2)
          {
            int parrotAutomatizmId = _mirrorAutomatizmService.TryCreateInitialParrotAutomatizm(
              automatizmNodeId,
              actionsImageId,
              hasVerbalPart);

            if (parrotAutomatizmId > 0)
            {
              var parrotAutomatizm = _automatizmSystem.GetAutomatizmById(parrotAutomatizmId);
              if (parrotAutomatizm != null)
                return ExecuteAutomatizm(parrotAutomatizm);
            }
          }

          atmz = _orientationReflexSystem.OrientationReflex(
            foundAutomatizm?.ID ?? 0, 
            currentEmotionId,
            actionsImageId);
        }

        if (atmz != null)
          return ExecuteAutomatizm(atmz); // блокируем рефлексы при удачном запуске автоматизма
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
        int verbId,
        bool isUnrecognizedPhrase = false)
    {
      if (PulseCount < 4)
        return 0;

      if (IsSleeping)
        return 0;

      // Активация дерева
      int detectedNodeId = _automatizmTreeSystem.AutomatizmTreeActivation(
          baseId,
          emotionId,
          activityId,
          toneMoodId,
          simbolId,
          verbId,
          isUnrecognizedPhrase);

      return detectedNodeId;
    }

    /// <summary>
    /// Получить автоматизм из узла дерева
    /// </summary>
    internal Automatizm GetAutomatizmFromNode(int nodeId)
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
    private bool ExecuteAutomatizm(Automatizm automatizm)
    {
      if (automatizm == null)
        return false;

      if (_automatismExecutionService == null)
      {
        Logger.Warning("Сервис выполнения автоматизмов не установлен");
        return false;
      }

      if (automatizm.Usefulness < 0)
      {
        Logger.Warning($"Автоматизм ID={automatizm.ID} заблокирован, отрицательная полезность");
        return false;
      }

      _lock.EnterWriteLock();
      try
      {
        if (_currentAutomatizmId > 0)
          _previousAutomatizmId = _currentAutomatizmId;

        // Начать отслеживание результата автоматизма
        var trackingResult = _automatismResultTracker.StartTracking(
            automatizm.ID,
            automatizm.BranchID,
            automatizm.ActionsImageID);

        var result = _automatismExecutionService.ExecuteAutomatizmWithChains(automatizm.ID, PulseCount);

        if (result.Success)
        {
          Logger.Info($"Запущен автоматизм ID: {automatizm.ID} для узла: {automatizm.BranchID}");

          // Включить ожидание оценки оператора
          AppGlobalState.WaitingForOperatorEvaluation = true;
          AppGlobalState.LastRunAutomatizmPulsCount = PulseCount;
          _currentAutomatizmId = automatizm.ID;
        }
        else
        {
          // Завершить отслеживание с ошибкой
          trackingResult.Result = AutomatismResultTracker.ExecutionResult.Error;
          trackingResult.ErrorMessage = result.ErrorMessage;
          _automatismResultTracker.FinishTracking(trackingResult);

          Logger.Warning($"Ошибка выполнения автоматизма {automatizm.ID}: {result.ErrorMessage}");
          AppGlobalState.LastRunAutomatizmPulsCount = 0;
          AppGlobalState.WaitingForOperatorEvaluation = false;
          AppGlobalState.ResetAutomatizmInfo();
          _currentAutomatizmId = 0;
          return false;
        }

        // Если это действие с пульта (BranchID > 1000000)
        if (automatizm.BranchID > 1000000 && automatizm.BranchID < 2000000)
        {
          int actionImageId = automatizm.BranchID - 1000000;
          Logger.Info($"Выполнение действия из образа: {actionImageId}");
        }
        // Если это фраза (BranchID > 2000000)
        else if (automatizm.BranchID > 2000000)
        {
          int phraseImageId = automatizm.BranchID - 2000000;
          Logger.Info($"Произнесение фразы из образа: {phraseImageId}");
        }

        return true;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        AppGlobalState.ResetAutomatizmInfo();
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сброс глобальных переменных по истечении времени ожидания
    /// </summary>
    private void ResetAutomatizmWaitingState()
    {
      AppGlobalState.ResetWaitingForOperatorEvaluation();
      AppGlobalState.ResetAutomatizmInfo();
      AppGlobalState.WaitingForOperatorEvaluation = false;
      _currentAutomatizmId = 0;
      _mirrorAutomatizmService.ResetDialogMirror();
    }

    /// <summary>
    /// Оценить предыдущий автоматизм на основе СТИМУЛА оператора
    /// </summary>
    private int EvaluatePreviousAutomatizm(int automatizmIdToEvaluate)
    {
      if (automatizmIdToEvaluate <= 0)
        return 0;

      // Получаем состояние до автоматизма
      var stateBefore = AppGlobalState.StateBeforeOperatorImpact;

      // Текущее состояние после стимула оператора
      var stateAfter = AppGlobalState.CurrentOverallState;

      // Вычисляем оценку
      int assessment = 0;
      if (stateAfter > stateBefore)
        assessment = 1; // Улучшение
      else if (stateAfter < stateBefore)
        assessment = -1; // Ухудшение

      // Время реакции оператора
      int responseTime = PulseCount - AppGlobalState.LastRunAutomatizmPulsCount;

      // Отметить распознавание оператором
      _automatismResultTracker.MarkOperatorRecognition(
          automatizmIdToEvaluate,
          true, // распознано оператором
          assessment,
          responseTime);

      int mirrorAutomatizmId = 0;
      if (AppGlobalState.EvolutionStage == 3)
        mirrorAutomatizmId = _mirrorAutomatizmService.TryCreateMirrorFromPendingOperatorResponse();

      Logger.Info($"Оценен автоматизм ID={automatizmIdToEvaluate}: оценка={assessment}, время реакции={responseTime}");
      return mirrorAutomatizmId;
    }

    /// <summary>
    /// Пробуждение - создание базового самоощущения
    /// </summary>
    private void WakeUpping(List<int> activetStyleIds)
    {
      // Активация самоощущения
      SensorActivation(1, 0, activetStyleIds, null, null, 0, 0);

      Logger.Info("Пробуждение - создание базового самоощущения");
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
        // добавить обработку сновидений
      }
      else
      {
        // Глубокий сон
        // Минимальная активность психики
      }
    }

    #endregion

    #region Методы работы с ToneMood ID

    /// <summary>
    /// Получить уникальный составной ID из тона и настроения
    /// </summary>
    /// <param name="tone">Тон: -1, 0, 1</param>
    /// <param name="mood">Настроение: 0-7</param>
    /// <returns>Уникальный числовой ID</returns>
    /// <remarks>
    /// Создает уникальный ID вида: первые 2 цифры - тон (смещенный в диапазон 1-3), 
    /// последние 2 цифры - настроение. Пример: нормальный(0) + хорошее(1) = 201
    /// </remarks>
    public static int GetToneMoodID(int tone, int mood)
    {
      // Проверка диапазонов используя статические методы валидации
      if (!ActionsImagesSystem.IsValidToneId(tone))
        throw new ArgumentOutOfRangeException(nameof(tone), $"Некорректный ID тона: {tone}");
      if (!ActionsImagesSystem.IsValidMoodId(mood))
        throw new ArgumentOutOfRangeException(nameof(mood), $"Некорректный ID настроения: {mood}");

      // Смещаем тон из -1..1 в 1..3 для избежания отрицательных значений
      int shiftedTone = tone + 2; // -1→1, 0→2, 1→3

      // Создаем составной ID: тон * 100 + настроение
      return shiftedTone * 100 + mood;
    }

    /// <summary>
    /// Получить тон и настроение из уникального составного ID
    /// </summary>
    /// <param name="toneMoodID">Уникальный составной ID</param>
    /// <returns>Кортеж (tone, mood)</returns>
    public static (int tone, int mood) GetToneMoodFromID(int toneMoodID)
    {
      // ID должен быть в диапазоне 100..307
      if (toneMoodID < 100 || toneMoodID > 307)
        throw new ArgumentOutOfRangeException(nameof(toneMoodID),
            $"Некорректный ToneMoodID: {toneMoodID}");

      // Настроение - последние 2 цифры (или 1 цифра)
      int mood = toneMoodID % 100;

      // Тон - первые цифры
      int shiftedTone = toneMoodID / 100;

      // Обратное смещение: из 1..3 в -1..1
      int tone = shiftedTone - 2;

      // Проверка корректности
      if (!ActionsImagesSystem.IsValidToneId(tone))
        throw new ArgumentException($"Некорректный тон в ID {toneMoodID}: {tone}");
      if (!ActionsImagesSystem.IsValidMoodId(mood))
        throw new ArgumentException($"Некорректное настроение в ID {toneMoodID}: {mood}");

      return (tone, mood);
    }

    /// <summary>
    /// Получить строковое представление ToneMood ID
    /// </summary>
    /// <param name="toneMoodID">Уникальный составной ID</param>
    /// <returns>Строковое описание тона и настроения</returns>
    public static string GetToneMoodString(int toneMoodID)
    {
      var (tone, mood) = GetToneMoodFromID(toneMoodID);
      return GetToneMoodStringDirect(tone, mood);
    }

    /// <summary>
    /// Получить строковое представление напрямую из тона и настроения
    /// </summary>
    /// <param name="tone">Тон: -1, 0, 1</param>
    /// <param name="mood">Настроение: 0-7</param>
    /// <returns>Строковое описание</returns>
    public static string GetToneMoodStringDirect(int tone, int mood)
    {
      string toneStr = ActionsImagesSystem.GetToneText(tone);
      string moodStr = ActionsImagesSystem.GetMoodText(mood);

      // Если не нашли в словарях, показываем значения как есть
      if (string.IsNullOrEmpty(toneStr))
        toneStr = $"Тон({tone})";
      if (string.IsNullOrEmpty(moodStr))
        moodStr = $"Настроение({mood})";

      return $"{toneStr} - {moodStr}";
    }

    /// <summary>
    /// Получить строку тона по ID
    /// </summary>
    /// <param name="toneId">ID тона: -1, 0, 1</param>
    /// <returns>Строковое описание тона</returns>
    public static string GetToneString(int toneId)
    {
      string toneStr = ActionsImagesSystem.GetToneText(toneId);
      return !string.IsNullOrEmpty(toneStr) ? toneStr : $"Тон({toneId})";
    }

    /// <summary>
    /// Получить строку настроения по ID
    /// </summary>
    /// <param name="moodId">ID настроения: 0-7</param>
    /// <returns>Строковое описание настроения</returns>
    public static string GetMoodString(int moodId)
    {
      string moodStr = ActionsImagesSystem.GetMoodText(moodId);
      return !string.IsNullOrEmpty(moodStr) ? moodStr : $"Настроение({moodId})";
    }

    /// <summary>
    /// Получить список всех доступных тонов
    /// </summary>
    /// <returns>Словарь тонов (ID -> Описание)</returns>
    public static Dictionary<int, string> GetToneList()
    {
      return ActionsImagesSystem.GetToneList();
    }

    /// <summary>
    /// Получить список всех доступных настроений
    /// </summary>
    /// <returns>Словарь настроений (ID -> Описание)</returns>
    public static Dictionary<int, string> GetMoodList()
    {
      return ActionsImagesSystem.GetMoodList();
    }

    /// <summary>
    /// Проверяет, существует ли тон с указанным ID
    /// </summary>
    public static bool IsValidToneId(int toneId)
    {
      return ActionsImagesSystem.IsValidToneId(toneId);
    }

    /// <summary>
    /// Проверяет, существует ли настроение с указанным ID
    /// </summary>
    public static bool IsValidMoodId(int moodId)
    {
      return ActionsImagesSystem.IsValidMoodId(moodId);
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
          Logger.Warning("InfluenceActionsImagesSystem не инициализирована, образ действий не создан");
          return 0;
        }

        if ((actionIdList == null || !actionIdList.Any()) &&
            (phraseIdList == null || !phraseIdList.Any()))
          return 0;

        if (!ActionsImagesSystem.IsValidToneId(toneId))
        {
          Logger.Warning($"Некорректный toneId: {toneId}, используется значение по умолчанию (0)");
          toneId = 0; // Нормальный
        }

        if (!ActionsImagesSystem.IsValidMoodId(moodId))
        {
          Logger.Warning($"Некорректный moodId: {moodId}, используется значение по умолчанию (0)");
          moodId = 0; // Нормальное
        }

        // Создаем образ действий оператора
        // Kind = 0 (объективное действие) - реальное воздействие с пульта
        var (imageId, actionsImage) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0, // объективное действие
            actIdList: actionIdList?.ToList() ?? new List<int>(),
            phraseIdList: phraseIdList,
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true // проверяем уникальность
        );

        if (imageId > 0)
          Logger.Info($"Создан образ действий ID: {imageId}, Tone: {toneId}, Mood: {moodId}");
        else
          Logger.Warning("Не удалось создать образ действий");

        return imageId;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
          Logger.Warning("InfluenceActionsImagesSystem не инициализирована, образ сочетаний действий не создан");
          return 0;
        }

        if (actIdList == null || actIdList.Count == 0)
        {
          Logger.Warning("Список действий пуст, образ сочетаний действий не создан");
          return 0;
        }

        // Создаем образ сочетаний действий с пульта
        var (imageId, influenceActionsImage) = _influenceActionsImagesSystem.CreateNewInfluenceActionsImage(
            actIdList: actIdList,
            checkUnicum: checkUnicum
        );

        if (imageId > 0)
          Logger.Info($"Создан образ сочетаний действий ID: {imageId}, " +
                         $"количество действий: {actIdList.Count}");
        else
          Logger.Warning("Не удалось создать образ сочетаний действий");

        return imageId;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return 0;
      }
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
        _mirrorAutomatizmService?.Dispose();
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