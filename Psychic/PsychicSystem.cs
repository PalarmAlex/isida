using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Understanding;
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
    private EpisodicMemorySystem _episodicMemorySystem;
    private UnderstandingTreeSystem _understandingTreeSystem;
    private ProblemTreeSystem _problemTreeSystem;
    private InformationEnvironmentSystem _informationEnvironmentSystem;
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
    /// Установка сервиса выполнения автоматизмов и дополнительных зависимостей (в т.ч. эпизодическая память, дерево понимания, информационная среда)
    /// </summary>
    public void SetPsychicSystemDop(
      AutomatismExecutionService executionService,
      OrientationReflexSystem orientationReflexSystem,
      PerceptionImagesSystem perceptionImagesSystem,
      EpisodicMemorySystem episodicMemorySystem = null,
      UnderstandingTreeSystem understandingTreeSystem = null,
      ProblemTreeSystem problemTreeSystem = null,
      InformationEnvironmentSystem informationEnvironmentSystem = null)
    {
      _automatismExecutionService = executionService ?? throw new ArgumentNullException(nameof(executionService));
      _orientationReflexSystem = orientationReflexSystem ?? throw new ArgumentNullException(nameof(orientationReflexSystem));
      _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));
      _episodicMemorySystem = episodicMemorySystem;
      _understandingTreeSystem = understandingTreeSystem;
      _problemTreeSystem = problemTreeSystem;
      _informationEnvironmentSystem = informationEnvironmentSystem;
    }

    /// <summary>
    /// Инициализирует базовое дерево автоматизмов
    /// </summary>
    private void InitializeBasicAutomatizmTree()
    {
      try
      {
        // Создать первые три ветки базовых состояний, если их нет
        if (_automatizmTreeSystem.Tree.Children.Count == 0)
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
    /// Сервис зеркалирования автоматизмов (доступен для внешних потребителей через контекст).
    /// </summary>
    public MirrorAutomatizmService MirrorAutomatizmService => _mirrorAutomatizmService;

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

        // При первом запуске (на 2-м пульсе) вставить пустой кадр — разрыв цепочки с предыдущей сессией
        if (pulseCount == 2 && AppGlobalState.EvolutionStage >= 4 && _episodicMemorySystem != null)
          _episodicMemorySystem.SetInterruption();

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

            // Первый запуск дерева автоматизмов и активация Understanding
            int wakeNodeId = AutomatizmTreeActivation(1, 0, 0, 0, 0, 0, 0);
            if (_understandingTreeSystem != null && _problemTreeSystem != null && wakeNodeId > 0)
            {
              int baseId = AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Bad ? -1
                  : AppGlobalState.CurrentOverallState == AppGlobalState.HomeostasisState.Well ? 1 : 0;
              int emotionId = _emotionsImageSystem.CreateNewEmotionsImage(activetStyleIds ?? new List<int>(), true).Item1;
              var wakeCtx = new SituationImageContext
              {
                HasAutomatismInBranch = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(wakeNodeId).Count > 0
              };
              _understandingTreeSystem.ActivateSituation(
                1, wakeNodeId, baseId, emotionId, _problemTreeSystem, wakeCtx);
            }
            WakeUppingActivation = false;
          }

          if (AppGlobalState.WaitingForOperatorEvaluation)
          {
            // Время ожидания оценки автоматизма истекло
            if (!AppGlobalState.IsEvaluationTime())
            {
              // Если оператор успел прислать ответ в окне, но следующий пульс пришёл уже после его закрытия — всё равно создаём зеркальные автоматизмы
              if (_isAnswer)
              {
                int automatizmToEvaluate = _previousAutomatizmId > 0 ? _previousAutomatizmId : _currentAutomatizmId;
                if (automatizmToEvaluate > 0)
                {
                  mirrorAutomatizmToExecute = EvaluatePreviousAutomatizm(automatizmToEvaluate);
                  _isAnswer = false;
                }
              }
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
        if (AppGlobalState.WaitingForOperatorEvaluation && activationType >= 2 && AppGlobalState.IsEvaluationTime())
          _isAnswer = true;

        int currentActivityId = CreateInfluenceActionsImage(actionIdList, true);
        (int currentEmotionId, _) = _emotionsImageSystem.CreateNewEmotionsImage(stileIdList, true);
        int toneMood = GetToneMoodID(toneId, moodId);

        int firstSimbol = 0;
        int verbId = 0;
        int verbIdForTree = 0;
        int actionsImageId = 0;
        List<int> phraseIdListForStimulus = phraseIdList;

        if (phraseIdList?.Any() == true)
        {
          (verbId, verbIdForTree, firstSimbol, phraseIdListForStimulus) = PrepareVerbalStimulusForStage2(
              phraseIdList, actionIdList, toneId, moodId);
          AppGlobalState.CurActiveVerbalId = verbId;
          var perceptionImageId = _perceptionImagesSystem.AddPerceptionImage(actionIdList, phraseIdListForStimulus);
          AppGlobalState.LastTriggerStimulusID = perceptionImageId;
        }
        else
          AppGlobalState.CurActiveVerbalId = 0;

        actionsImageId = CreateActionsImage(actionIdList, phraseIdListForStimulus ?? phraseIdList, toneId, moodId);
        int stimulusActionsImageIdForContext = actionsImageId;

        Automatizm atmz = null;
        int automatizmNodeId = AutomatizmTreeActivation(
            activationType,
            currentBaseId,
            currentEmotionId,
            currentActivityId,
            toneMood,
            firstSimbol,
            verbIdForTree);

        if (_understandingTreeSystem != null && _problemTreeSystem != null && automatizmNodeId > 0)
        {
          var situationCtx = new SituationImageContext
          {
            HasAutomatismInBranch = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(automatizmNodeId).Count > 0,
            MoodId = toneMood,
            ActionIds = actionIdList?.ToArray()
          };
          _understandingTreeSystem.ActivateSituation(
            activationType,
            automatizmNodeId,
            currentBaseId,
            currentEmotionId,
            _problemTreeSystem,
            situationCtx);
        }

        if (automatizmNodeId > 0)
        {
          bool hasVerbalPart = phraseIdList?.Any() == true;
          bool hasNonVerbalPart = actionIdList?.Any() == true;
          if (AppGlobalState.WaitingForOperatorEvaluation && activationType >= 2)
          {
            if (AppGlobalState.IsEvaluationTime())
              _mirrorAutomatizmService.RegisterOperatorResponse(actionsImageId, automatizmNodeId, hasVerbalPart, hasNonVerbalPart);
            else
              ResetAutomatizmWaitingState();
          }

          AppGlobalState.AutomatizmNodeId = automatizmNodeId;

          // Обновить информационную среду для уровней осмысления (Danger, VeryActualSituation)
          if (_informationEnvironmentSystem != null)
            _informationEnvironmentSystem.GetCurrentInformationEnvironment(currentEmotionId, actionsImageId);

          var (problemSolved, levelAutomatizm) = TryProcessThinkingLevels(automatizmNodeId, actionsImageId, currentEmotionId);

          if (problemSolved && levelAutomatizm != null)
          {
            if (AppGlobalState.EvolutionStage == 3 && activationType >= 2)
            {
              int responseNodeId = GetTreeNodeIdForResponseActionsImage(levelAutomatizm.ActionsImageID, currentBaseId, currentEmotionId, currentActivityId);
              _mirrorAutomatizmService.StartDialogMirrorForExistingAutomatizm(responseNodeId > 0 ? responseNodeId : automatizmNodeId);
            }
            AppGlobalState.CurStimulusImageId = actionsImageId;
            return ExecuteAutomatizm(levelAutomatizm);
          }

          if (!problemSolved &&
              AppGlobalState.EvolutionStage == 3 &&
              !AppGlobalState.WaitingForOperatorEvaluation &&
              activationType >= 2)
          {
            int parrotAutomatizmId = _mirrorAutomatizmService.TryCreateInitialParrotAutomatizm(
              automatizmNodeId,
              actionsImageId,
              hasVerbalPart,
              hasNonVerbalPart);

            if (parrotAutomatizmId > 0)
            {
              var parrotAutomatizm = _automatizmSystem.GetAutomatizmById(parrotAutomatizmId);
              if (parrotAutomatizm != null)
              {
                AppGlobalState.CurStimulusImageId = actionsImageId;
                return ExecuteAutomatizm(parrotAutomatizm);
              }
            }
          }

          AppGlobalState.CurrentStimulusActionsImageId = stimulusActionsImageIdForContext;
          AppGlobalState.CurrentStimulusActionIdList = actionIdList?.ToList() ?? new List<int>();
          AppGlobalState.CurrentStimulusToneId = toneId;
          AppGlobalState.CurrentStimulusMoodId = moodId;

          atmz = _orientationReflexSystem.OrientationReflex(0, currentEmotionId, actionsImageId);
        }

        if (atmz != null)
        {
          AppGlobalState.CurStimulusImageId = actionsImageId;
          return ExecuteAutomatizm(atmz); // блокируем рефлексы при удачном запуске автоматизма
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }

      return false; // Не блокировать рефлексы
    }

    #region Уровни мышления 1 и 2

    /// <summary>
    /// Оркестратор уровней осмысления: уровень 1 (штатный автоматизм) → уровень 2 (правила) → при неуспехе заглушка для циклов.
    /// </summary>
    /// <returns>(problemSolved, automatizm для выполнения или null)</returns>
    private (bool problemSolved, Automatizm toExecute) TryProcessThinkingLevels(
      int automatizmNodeId,
      int actionsImageId,
      int currentEmotionId)
    {
      if (_informationEnvironmentSystem == null)
        return (false, null);

      var infoEnv = _informationEnvironmentSystem.CurrentInformationEnvironment;
      infoEnv.UnresolvedAtThinkingLevel2 = false;
      infoEnv.UnresolvedNodeId = 0;
      infoEnv.UnresolvedActionsImageId = 0;
      infoEnv.UnresolvedPulseCount = 0;

      var (resolved1, atmz1) = ProcessLevel1(automatizmNodeId, currentEmotionId);
      if (resolved1 && atmz1 != null)
        return (true, atmz1);

      var (resolved2, atmz2) = ProcessLevel2(automatizmNodeId, actionsImageId);
      if (resolved2 && atmz2 != null)
        return (true, atmz2);

      return (false, null);
    }

    /// <summary>
    /// Первый уровень осмысления: решение только за счёт штатного/текущего автоматизма (без правил).
    /// </summary>
    private (bool resolved, Automatizm toExecute) ProcessLevel1(int automatizmNodeId, int currentEmotionId)
    {
      Automatizm staff = GetAutomatizmFromNode(automatizmNodeId, 0);
      if (staff == null)
        return (false, null);

      if (staff.Usefulness < 0)
      {
        if (_informationEnvironmentSystem != null)
          _informationEnvironmentSystem.CurrentInformationEnvironment.NeedThinkingAboutAutomatizm = true;
        return (false, null);
      }

      if (_informationEnvironmentSystem == null)
        return (true, staff);

      var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
      if (env.Danger)
        return (true, staff);

      if (env.VeryActualSituation && !env.Danger)
      {
        // Опционально: в будущем — проверка по правилам/прогнозу (аналог checkAutomatizm). Пока запускаем штатный.
      }

      return (true, staff);
    }

    /// <summary>
    /// Второй уровень осмысления: попытка решить за счёт правил эпизодической памяти (найти/создать автоматизм по правилу).
    /// </summary>
    private (bool resolved, Automatizm toExecute) ProcessLevel2(int automatizmNodeId, int actionsImageId)
    {
      if (AppGlobalState.EvolutionStage < 4 || _episodicMemorySystem == null)
        return (false, null);

      var chain = _episodicMemorySystem.GetTargetChain(actionsImageId);
      var rule = (chain != null && chain.Count > 0) ? chain[0] : _episodicMemorySystem.GetSingleBestRule(3, actionsImageId);
      if (rule == null || rule.ActionId <= 0)
      {
        SetUnresolvedAtLevel2Stub(automatizmNodeId, actionsImageId);
        return (false, null);
      }

      var episodicAtmz = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(automatizmNodeId)
        .FirstOrDefault(a => a.ActionsImageID == rule.ActionId);
      if (episodicAtmz == null)
      {
        var (newId, _) = _automatizmSystem.CreateNewAutomatizm(automatizmNodeId, rule.ActionId, true);
        episodicAtmz = newId > 0 ? _automatizmSystem.GetAutomatizmById(newId) : null;
      }
      if (episodicAtmz != null && episodicAtmz.Usefulness >= 0)
        return (true, episodicAtmz);

      SetUnresolvedAtLevel2Stub(automatizmNodeId, actionsImageId);
      return (false, null);
    }

    /// <summary>
    /// Заглушка: проблема не решена на 2 уровне — подготовка к модулям циклов мышления.
    /// </summary>
    private void SetUnresolvedAtLevel2Stub(int nodeId, int actionsImageId)
    {
      if (_informationEnvironmentSystem == null)
        return;

      var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
      env.NeedThinkingAboutAutomatizm = true;
      env.UnresolvedAtThinkingLevel2 = true;
      env.UnresolvedNodeId = nodeId;
      env.UnresolvedActionsImageId = actionsImageId;
      env.UnresolvedPulseCount = PulseCount;
      Logger.Info($"Отработка уровня 2. Проблема не решена — для циклов мышления. NodeId={nodeId}, ActionsImageId={actionsImageId}");
    }

    #endregion

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
    /// Готовит вербальный стимул: для стадии 2 при нескольких фразах («ма ма») склеивает в одну («мама») для образа стимула и узла дерева; CurActiveVerbalId остаётся по исходному списку для цепочки.
    /// </summary>
    /// <returns>(verbId для CurActiveVerbalId, verbId для дерева, firstSymbol, phraseIdList для образа стимула)</returns>
    private (int verbIdForCurActive, int verbIdForTree, int firstSimbol, List<int> phraseIdListForStimulus) PrepareVerbalStimulusForStage2(
        List<int> phraseIdList,
        List<int> actionIdList,
        int toneId,
        int moodId)
    {
      int firstSimbol = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(phraseIdList[0]);
      int verbId = _verbalBrocaImages.CreateNewVerbalBrocaImage(firstSimbol, phraseIdList, toneId, moodId, true).Item1;

      // RecognizeText с пульта для "ма ма" возвращает один phraseId (фраза "ма ма" целиком), а не два. Части получаем разбивкой по пробелу/дефису.
      List<int> partsForMerge = phraseIdList.Count == 1
          ? _sensorySystem.VerbalChannel.GetPartPhraseIdsFromPhraseId(phraseIdList[0])
          : phraseIdList;

      string originalPhraseText = phraseIdList.Count == 1
          ? _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(phraseIdList[0])
          : string.Join(" ", phraseIdList.Select(pid => _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(pid) ?? ""));
      // Склеиваем только если в исходном вводе был пробел («ма ма» → «мама»). При дефисе («тик-так») триггер не склеиваем.
      bool shouldMerge = AppGlobalState.EvolutionStage == 2 && partsForMerge != null && partsForMerge.Count > 1
          && !string.IsNullOrEmpty(originalPhraseText) && originalPhraseText.Contains(' ');

      if (shouldMerge)
      {
        string mergedText = string.Concat(partsForMerge
            .Select(pid => _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(pid) ?? ""));
        if (!string.IsNullOrEmpty(mergedText))
        {
          var wordIdOpt = _sensorySystem.VerbalChannel.ProcessWord(mergedText);
          if (wordIdOpt.HasValue)
            _sensorySystem.VerbalChannel.ProcessPhrase(new List<int> { wordIdOpt.Value });
          int mergedPhraseId = _sensorySystem.VerbalChannel.FindPhraseId(mergedText);
          if (mergedPhraseId != 0)
          {
            int firstSymbolMerged = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(mergedPhraseId);
            int verbIdMerged = _verbalBrocaImages.CreateNewVerbalBrocaImage(firstSymbolMerged, new List<int> { mergedPhraseId }, toneId, moodId, true).Item1;
            return (verbId, verbIdMerged, firstSymbolMerged, new List<int> { mergedPhraseId });
          }
        }
      }

      return (verbId, verbId, firstSimbol, phraseIdList);
    }

    /// <summary>
    /// Получить автоматизм из узла дерева. При preferredActionId > 0 предпочитается автоматизм с данным ActionsImageID (из эпизодической памяти).
    /// </summary>
    /// <param name="nodeId">ID узла дерева автоматизмов</param>
    /// <param name="preferredActionId">ID образа действий из эпизодического правила (0 — не учитывать)</param>
    internal Automatizm GetAutomatizmFromNode(int nodeId, int preferredActionId = 0)
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

      var suitable = automatizms.Where(a => a.Usefulness >= 0).ToList();
      if (suitable.Count == 0)
        return null;

      // При наличии предпочтительного действия из эпизодической памяти — ставим его выше в приоритете
      if (preferredActionId > 0)
      {
        var preferred = suitable.FirstOrDefault(a => a.ActionsImageID == preferredActionId);
        if (preferred != null)
          return preferred;
      }

      // Выбираем самый успешный автоматизм
      return suitable
          .OrderByDescending(a => a.Usefulness)
          .ThenByDescending(a => a.Count)
          .FirstOrDefault();
    }

    /// <summary>
    /// Получить ID узла дерева автоматизмов для образа ответа (например, ответа агента «как дела»).
    /// Используется для зеркалирования: триггером учительской пары должен быть узел ответа агента, а не стимула.
    /// </summary>
    /// <param name="responseActionsImageId">ID образа действий (ответ автоматизма).</param>
    /// <param name="currentBaseId">Текущее базовое состояние.</param>
    /// <param name="currentEmotionId">Текущий образ эмоций.</param>
    /// <param name="currentActivityId">Текущая активность.</param>
    /// <returns>ID узла дерева или 0, если узел по вербальной части не найден.</returns>
    private int GetTreeNodeIdForResponseActionsImage(
      int responseActionsImageId,
      int currentBaseId,
      int currentEmotionId,
      int currentActivityId)
    {
      if (responseActionsImageId <= 0 || _actionsImagesSystem == null)
        return 0;

      var img = _actionsImagesSystem.GetActionsImage(responseActionsImageId);
      if (img?.PhraseIdList == null || !img.PhraseIdList.Any())
        return 0;

      var phraseIdList = img.PhraseIdList;
      var actionIdList = img.ActIdList ?? new List<int>();
      int toneId = img.ToneId;
      int moodId = img.MoodId;

      var (_, verbIdForTree, firstSimbol, _) = PrepareVerbalStimulusForStage2(phraseIdList, actionIdList, toneId, moodId);
      int toneMood = GetToneMoodID(toneId, moodId);

      return AutomatizmTreeActivation(2, currentBaseId, currentEmotionId, currentActivityId, toneMood, firstSimbol, verbIdForTree);
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

          // Включить ожидание оценки оператора только начиная с 4 стадии
          if (AppGlobalState.EvolutionStage >= 4)
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

          // Триггер 7: негативный эффект моторного автоматизма — обновить тему и дерево проблем
          if (_understandingTreeSystem != null && _problemTreeSystem != null)
            _understandingTreeSystem.UpdateThemeByTriggerAndRefreshProblemTree(7, _problemTreeSystem);

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
      if (AppGlobalState.EvolutionStage >= 4 && _episodicMemorySystem != null)
        _episodicMemorySystem.SetInterruption();
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

      int operatorResponseImageId = _mirrorAutomatizmService?.GetPendingOperatorResponseActionsImageId() ?? 0;
      _automatismResultTracker.MarkOperatorRecognition(
          automatizmIdToEvaluate,
          true, // распознано оператором
          assessment,
          responseTime,
          operatorResponseImageId);

      // Триггер 7: негативный эффект моторного автоматизма при отрицательной оценке оператора
      if (assessment < 0 && _understandingTreeSystem != null && _problemTreeSystem != null)
        _understandingTreeSystem.UpdateThemeByTriggerAndRefreshProblemTree(7, _problemTreeSystem);

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