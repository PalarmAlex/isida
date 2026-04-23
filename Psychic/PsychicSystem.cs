using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Psychic.Memory.Episodic;
using ISIDA.Psychic.Thinking;
using ISIDA.Psychic.Thinking.Strategies;
using ISIDA.Psychic.Understanding;
using ISIDA.Psychic.Importance;
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
    /// <summary>
    /// Минимальный глобальный номер пульса, начиная с которого <see cref="AutomatizmTreeActivation"/> активирует дерево
    /// (при меньшем значении возвращается 0). Сценарии оператора сдвигают якорь, чтобы первый стимул не попадал на более ранний пульс.
    /// </summary>
    public const int MinGlobalPulseForAutomatizmTreeActivation = 4;

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    private readonly AutomatizmSystem _automatizmSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly InfluenceActionsImagesSystem _influenceActionsImagesSystem;
    private readonly InfluenceActionSystem _influenceActionSystem;
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
    private ThinkingCyclesSystem _thinkingCyclesSystem;

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
        InfluenceActionSystem influenceActionSystem,
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
        influenceActionSystem,
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
      InfluenceActionSystem influenceActionSystem,
      ActionsImagesSystem actionsImagesSystem,
      EmotionsImageSystem emotionsImageSystem,
      SensorySystem sensorySystem,
      VerbalBrocaImagesSystem verbalBrocaImages,
      AutomatismResultTracker automatismResultTracker)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _influenceActionsImagesSystem = influenceActionsImagesSystem ?? throw new ArgumentNullException(nameof(influenceActionsImagesSystem));
      _influenceActionSystem = influenceActionSystem ?? throw new ArgumentNullException(nameof(influenceActionSystem));
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

      // Циклы мышления (3-й уровень) — инициализируются при наличии IE.
      if (_informationEnvironmentSystem != null)
      {
        _thinkingCyclesSystem = new ThinkingCyclesSystem(
          _informationEnvironmentSystem,
          _episodicMemorySystem,
          _understandingTreeSystem,
          _problemTreeSystem,
          _automatizmSystem);

        // Инфо-функции 3-го уровня (один класс с switch по Id)
        _thinkingCyclesSystem.RegisterStrategy(new InfoFunctionsStrategy(_thinkingCyclesSystem.ExperienceMemory));
      }
    }

    /// <summary>
    /// После смены <see cref="AppGlobalState.CurStimulusImageId"/> пересчитывает объект экстремальной значимости в информационной среде (при стадии ≥ 4 и инициализированных зависимостях).
    /// </summary>
    /// <param name="actionsImageId">ID образа действий стимула, совпадающий с записанным в CurStimulusImageId.</param>
    private void RefreshExtremImportanceForCurrentStimulus(int actionsImageId)
    {
      ObjectImportanceService.UpdateExtremImportanceObject(
        _episodicMemorySystem,
        _informationEnvironmentSystem,
        actionsImageId,
        _understandingTreeSystem);
    }

    /// <summary>Параметры затухания и срока жизни главного цикла мышления.</summary>
    /// <param name="decayAgeDivisor">Устарело, передаётся для совместимости конфигов.</param>
    /// <param name="decayBase">Устарело, передаётся для совместимости конфигов.</param>
    /// <param name="mainMaxAgePulses">Максимальный возраст главного цикла в пульсах до принудительного снятия.</param>
    /// <param name="backgroundFadeTargetPulses">Целевой горизонт (пульсы) затухания веса фонового цикла.</param>
    public void ApplyThinkingCyclesConfig(int decayAgeDivisor, int decayBase, int mainMaxAgePulses, int backgroundFadeTargetPulses = 1000)
    {
      _thinkingCyclesSystem?.ApplyDecayParameters(decayAgeDivisor, decayBase, mainMaxAgePulses, backgroundFadeTargetPulses);
    }

    /// <summary>
    /// Сбрасывает диспетчер циклов мышления и снимок «Цикл М» в <see cref="AppGlobalState"/> при очистке данных стадии 4
    /// (переход на нижестоящую стадию в <see cref="ISIDA.Common.EvolutionStageService"/>). Подстраховка вне очередного пульса.
    /// </summary>
    public void ClearThinkingCyclesWhenStageFourDataCleared()
    {
      _thinkingCyclesSystem?.ClearAllCycles();
      PublishMainThinkingCycleToAppGlobalState();
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

    /// <summary>
    /// Очередь на оценку: стимул пришёл в SensorActivation; сама оценка выполняется только в начале следующего <see cref="ProcessPsychicPulse"/> (после <c>UpdateStateOnly</c>).
    /// Не переносить вызов <see cref="EvaluatePreviousAutomatizm"/> в SensorActivation — иначе снова не видно изменений гомеостаза после воздействия пульта.
    /// </summary>
    private int _deferredOperatorEvaluationAutomatizmId = 0;

    /// <summary>
    /// Снимок LastRunAutomatizmPulsCount на момент стимула-ответа (до StartWaiting нового автоматизма на том же пульсе).
    /// </summary>
    private int _deferredOperatorEvaluationLastRunPulseForResponse = 0;

    /// <summary>
    /// Стадия 3: на следующем пульсе не создавать пары зеркала по отложенной оценке — стимул уже запустил штатный сдвиг на своей ветке (сценарий шаги 6–7).
    /// </summary>
    private bool _skipStage3MirrorLearningOnNextEval;

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
      ThinkingDecision thinkingDecisionToExecute = null;
      _lock.EnterWriteLock();
      try
      {
        // Циклы мышления — только со стадии 4. Сбрасываем до раннего выхода (<2) и для сна: иначе на 0–1
        // очистка не выполнялась, а остаток после стадии 4 «жил» в памяти до первого пульса на стадии ≥2.
        if (AppGlobalState.EvolutionStage < 4)
        {
          _thinkingCyclesSystem?.ClearAllCycles();
          PublishMainThinkingCycleToAppGlobalState();
        }

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
            int wakeNodeId = AutomatizmTreeActivation(0, 0, 0, 0, 0, 0, 0);
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

          // Оценка автоматизма по ответу оператора: только здесь, на пульсе после стимула (не в SensorActivation).
          if (_deferredOperatorEvaluationAutomatizmId > 0)
          {
            int idToEval = _deferredOperatorEvaluationAutomatizmId;
            int lastRunSnap = _deferredOperatorEvaluationLastRunPulseForResponse;
            _deferredOperatorEvaluationAutomatizmId = 0;
            _deferredOperatorEvaluationLastRunPulseForResponse = 0;

            mirrorAutomatizmToExecute = EvaluatePreviousAutomatizm(idToEval, lastRunSnap);

            if (_currentAutomatizmId == idToEval && AppGlobalState.WaitingForOperatorEvaluation)
              EndOperatorEvaluationWait();
          }

          // Истечение окна ожидания без нового стимула с пульта (отложенная оценка не пересекается — она снимает ожидание только при совпадении id, см. выше).
          // IsEvaluationTime() ложен и на том же пульсе, что и LastRunAutomatizmPulsCount (timeSince==0) — это не истечение, а «ещё тот же пульс после стимула».
          // Не сбрасывать ожидание на этом пульсе: иначе сценарий/стимул в фазе до ProcessPsychicPulse уничтожает зеркало до ответа оператора на следующем пульсе.
          if (AppGlobalState.WaitingForOperatorEvaluation && !AppGlobalState.IsEvaluationTime())
          {
            int timeSinceAutomatizm = GlobalTimer.GlobalPulsCount - AppGlobalState.LastRunAutomatizmPulsCount;
            if (timeSinceAutomatizm > 0)
            {
              ResetAutomatizmWaitingState();
              Logger.Info($"Время ожидания оценки истекло для автоматизма ID={_currentAutomatizmId}");
            }
          }
          _automatismExecutionService.ProcessAutomatizmChainsPulse(pulseCount);

          // Диспетчеризация циклов — только стадия 4+ (сброс для <4 уже в начале метода).
          if (AppGlobalState.EvolutionStage >= 4 && _thinkingCyclesSystem != null)
          {
            thinkingDecisionToExecute = _thinkingCyclesSystem.DispatchCycles(
              pulseCount,
              isSleeping: IsSleeping);
            PublishMainThinkingCycleToAppGlobalState();
          }
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
        {
          // TryCreateMirror выставляет trigger = узел фразы оператора; фактически же на этом пульсе может исполняться
          // предпочтённый штатный автоматизм с другим ответом агента (напр. «все ОК» вместо повтора «как дела»).
          // Якорь следующего сдвига должен совпадать с узлом фразы ответа выполняемого автоматизма — иначе следующий
          // TryCreateMirror создаст Belief=2 на старом узле и перевяжет штат с ветки «как дела».
          if (AppGlobalState.EvolutionStage == 3 &&
              _automatizmTreeSystem != null &&
              !IsStage3MirrorEchoAutomatizm(mirrorAutomatizm))
          {
            var branchNode = _automatizmTreeSystem.GetNodeById(mirrorAutomatizm.BranchID);
            if (branchNode != null)
            {
              int responsePhraseNodeId = GetTreeNodeIdForResponseActionsImage(
                  mirrorAutomatizm.ActionsImageID,
                  branchNode.BaseID,
                  branchNode.EmotionID,
                  branchNode.ActivityID);
              if (responsePhraseNodeId > 0)
              {
                _mirrorAutomatizmService.SetDialogTriggerNodeIdForActiveMirror(responsePhraseNodeId);
              }
            }
          }

          ExecuteAutomatizm(mirrorAutomatizm);
        }
      }

      if (thinkingDecisionToExecute != null)
      {
        ExecuteThinkingDecision(thinkingDecisionToExecute);
      }
    }

    private void ExecuteThinkingDecision(ThinkingDecision decision)
    {
      if (decision == null) return;

      // 1) Готовый автоматизм
      if (decision.AutomatizmToExecute != null)
      {
        Logger.Info($"Решение цикла мышления: выполнить автоматизм id={decision.AutomatizmToExecute.ID}, образ действий={decision.AutomatizmToExecute.ActionsImageID}");
        var ok = ExecuteAutomatizm(decision.AutomatizmToExecute);
        if (ok && decision.CycleId > 0 && _thinkingCyclesSystem != null && decision.AutomatizmToExecute.ID > 0)
          _thinkingCyclesSystem.NotifySolutionExecutedAfterDispatch(decision.CycleId, decision.AutomatizmToExecute.ID, PulseCount);
        if (ok && _informationEnvironmentSystem != null)
        {
          // После запуска из thinking-cycles проблема на 2 уровне считается обработанной,
          // иначе после окончания waiting-for-operator система повторно выбирает то же действие.
          var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
          env.UnresolvedAtThinkingLevel2 = false;
          env.NeedThinkingAboutAutomatizm = false;
          env.UnresolvedNodeId = 0;
          env.UnresolvedActionsImageId = 0;
          env.UnresolvedPulseCount = 0;
        }
        return;
      }

      // 2) Сформировать автоматизм по ActionsImage и выполнить
      if (decision.ActionsImageIdToAutomatize > 0 && _informationEnvironmentSystem != null)
      {
        Logger.Info($"Решение цикла мышления: создать и выполнить по образу действий id={decision.ActionsImageIdToAutomatize}");
        var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
        var nodeId = env?.UnresolvedNodeId ?? 0;
        if (nodeId > 0)
        {
          var (newId, _) = _automatizmSystem.CreateNewAutomatizm(nodeId, decision.ActionsImageIdToAutomatize, true);
          var atmz = newId > 0 ? _automatizmSystem.GetAutomatizmById(newId) : null;
          if (atmz != null)
          {
            var ok = ExecuteAutomatizm(atmz);
            if (ok && decision.CycleId > 0 && newId > 0 && _thinkingCyclesSystem != null)
              _thinkingCyclesSystem.NotifySolutionExecutedAfterDispatch(decision.CycleId, newId, PulseCount);
            if (ok)
            {
              env.UnresolvedAtThinkingLevel2 = false;
              env.NeedThinkingAboutAutomatizm = false;
              env.UnresolvedNodeId = 0;
              env.UnresolvedActionsImageId = 0;
              env.UnresolvedPulseCount = 0;
            }
          }
        }
        return;
      }

      // 3) «Попугайство»/запрос у оператора — пока через MirrorAutomatizmService (если есть стимул)
      if (decision.RequestParrotFromOperator)
      {
        Logger.Info("Решение цикла мышления: запрос подсказки у оператора (попугайство)");
        // В isida паррот на стадии 3 уже реализован как TryCreateInitialParrotAutomatizm, а на 4+ будет стратегия.
        return;
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
    /// <param name="visualColorId">Код зрительного канала (см. <see cref="AgentVisualColor"/>)</param>
    /// <returns>True если нужно заблокировать рефлексы</returns>
    internal bool SensorActivation(
      int activationType,
      int currentBaseId,
      List<int> stileIdList, // хотя через пульсы передается StileIdList, от действия может поменяться stileIdList на текущем пульсе
      List<int> actionIdList,
      List<int> phraseIdList,
      int toneId,
      int moodId,
      int visualColorId = 0)
    {
      if (AppGlobalState.EvolutionStage < 2)
      {
        Logger.Warning($"Стадия развития {AppGlobalState.EvolutionStage} недостаточна для автоматизмов");
        return false;
      }

      if ((actionIdList?.Count ?? 0) == 0 && (phraseIdList?.Count ?? 0) == 0 &&
          visualColorId == AgentVisualColor.White)
        return false;

      if (!AgentVisualColor.IsValidCode(visualColorId))
        visualColorId = AgentVisualColor.White;

      try
      {
        if (actionIdList != null && actionIdList.Count > 0)
          AppGlobalState.RecordStimulusInfluenceActions(actionIdList);

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
              phraseIdList, toneId, moodId);
          AppGlobalState.CurActiveVerbalId = verbId;
          var perceptionImageId = _perceptionImagesSystem.AddPerceptionImage(
              actionIdList, phraseIdListForStimulus, visualColorId);
          AppGlobalState.LastTriggerStimulusID = perceptionImageId;
        }
        else
          AppGlobalState.CurActiveVerbalId = 0;

        actionsImageId = CreateActionsImage(actionIdList, phraseIdListForStimulus ?? phraseIdList, toneId, moodId, visualColorId);
        int stimulusActionsImageIdForContext = actionsImageId;

        // Зафиксировать стимул для пассивного режима (dreaming) в циклах — только со стадии 4 (как и сами циклы).
        if (AppGlobalState.EvolutionStage >= 4)
          _thinkingCyclesSystem?.NotifyStimulus(PulseCount);
        AppGlobalState.UpdateLastPultStimulusPulse(PulseCount);

        Automatizm atmz = null;
        int automatizmNodeId = AutomatizmTreeActivation(
            currentBaseId,
            currentEmotionId,
            currentActivityId,
            toneMood,
            firstSimbol,
            verbIdForTree,
            visualColorId);
        bool deferredOperatorEvalScheduledThisStimulus = false;

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

          AppGlobalState.AutomatizmNodeId = automatizmNodeId;

          // Обновить информационную среду (Danger, VeryActualSituation) для обоих веток.
          if (_informationEnvironmentSystem != null)
            _informationEnvironmentSystem.GetCurrentInformationEnvironment(currentEmotionId, actionsImageId);

          // Стадия 2+: RegisterOperatorResponse здесь; EvaluatePreviousAutomatizm — строго в следующем ProcessPsychicPulse (см. _deferredOperatorEvaluationAutomatizmId). Полезность по ответу оператора — со стадии 2.
          TryScheduleDeferredOperatorEvaluationOnStimulus(
              activationType, actionsImageId, automatizmNodeId, hasVerbalPart, hasNonVerbalPart);
          deferredOperatorEvalScheduledThisStimulus = _deferredOperatorEvaluationAutomatizmId > 0;

          // Стадия < 4 — только ОР (без уровней 1–2 и без циклов мышления). Стадия >= 4 — уровни мышления и циклы; без ОР.
          if (AppGlobalState.EvolutionStage < 4)
          {
            // Только ориентировочный рефлекс (ОР1/ОР2).
            AppGlobalState.CurrentStimulusActionsImageId = stimulusActionsImageIdForContext;
            AppGlobalState.CurrentStimulusActionIdList = actionIdList?.ToList() ?? new List<int>();
            AppGlobalState.CurrentStimulusToneId = toneId;
            AppGlobalState.CurrentStimulusMoodId = moodId;

            int orientationAutomatizmId = 0;
            var foundForOR = GetAutomatizmFromNode(automatizmNodeId, 0);
            if (foundForOR != null)
              orientationAutomatizmId = foundForOR.ID;
            else
            {
              var staffForOR = _automatizmSystem.GetBelief2AutomatizmFromTreeId(automatizmNodeId);
              if (staffForOR != null)
                orientationAutomatizmId = staffForOR.ID;
              else
              {
                var branchAutomatizms = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(automatizmNodeId);
                var anyInBranch = branchAutomatizms?.FirstOrDefault(a => a != null);
                if (anyInBranch != null)
                  orientationAutomatizmId = anyInBranch.ID;
              }
            }

            if (AppGlobalState.EvolutionStage == 3 && activationType >= 2 && hasVerbalPart)
            {
              string ph = BuildPultPhraseText(phraseIdListForStimulus ?? phraseIdList);
              string forDesc = foundForOR == null
                  ? "null"
                  : $"id={foundForOR.ID} br={foundForOR.BranchID} belief={foundForOR.Belief} use={foundForOR.Usefulness} echo={IsStage3MirrorEchoAutomatizm(foundForOR)}";
            }

            // Стадия 3: перед запуском уже выученного автоматизма включить цикл зеркалирования — иначе RegisterOperatorResponse
            // не примет следующий стимул оператора (требуется _dialogMirrorActive), цепочка «ответ агента → новый стимул» рвётся.
            // Якорь следующего сдвига — узел фразы ответа выполняемого автоматизма (как в зеркале до визуального канала), иначе после «хай→как дела»
            // следующий ответ оператора ошибочно строился бы как сдвиг от узла «хай», а не «как дела».
            // Не вызывать, если на этом же стимуле уже поставлена отложенная оценка зеркала: иначе StartDialogMirror перезапишет якорь
            // (например «здравствуй») узлом ответа только что сработавшего эхо («все ОК»), и TryCreateMirror сформирует не тот сдвиг.
            if (foundForOR != null && AppGlobalState.EvolutionStage == 3 && activationType >= 2 &&
                !deferredOperatorEvalScheduledThisStimulus)
            {
              int responseNodeId = GetTreeNodeIdForResponseActionsImage(
                  foundForOR.ActionsImageID, currentBaseId, currentEmotionId, currentActivityId);
              int anchor = responseNodeId > 0 ? responseNodeId : automatizmNodeId;
              _mirrorAutomatizmService.StartDialogMirrorForExistingAutomatizm(anchor);
            }

            // ОР1/ОР2 — на каждый стимул (в т.ч. ответ оператора в окне ожидания); моторный выбор откладываем ниже.
            atmz = _orientationReflexSystem.OrientationReflex(orientationAutomatizmId, currentEmotionId, actionsImageId);

            if (AppGlobalState.EvolutionStage == 3 && activationType >= 2 && hasVerbalPart)
            {
              string ph2 = BuildPultPhraseText(phraseIdListForStimulus ?? phraseIdList);
              string chosen = atmz == null
                  ? "null"
                  : $"id={atmz.ID} br={atmz.BranchID} belief={atmz.Belief} use={atmz.Usefulness} echo={IsStage3MirrorEchoAutomatizm(atmz)}";
            }

            // Стимул уже поставлен в очередь отложенной оценки: не исполнять автоматизм с этого вызова (зеркало на следующем пульсе).
            if (AppGlobalState.EvolutionStage == 3 &&
                activationType >= 2 &&
                hasVerbalPart &&
                deferredOperatorEvalScheduledThisStimulus)
            {
              AppGlobalState.CurStimulusImageId = actionsImageId;
              RefreshExtremImportanceForCurrentStimulus(actionsImageId);
              return true;
            }

            // Стадия 3: если ОР ничего не вернул — попробовать попугай (эхо оператору). Только при отсутствии ожидания оценки:
            // иначе на стимуле-ответе оператора (TrySchedule уже вызвал RegisterOperatorResponse) попугай создавал бы второе эхо и ломал зеркало.
            if (atmz == null &&
                AppGlobalState.EvolutionStage == 3 &&
                !AppGlobalState.WaitingForOperatorEvaluation &&
                activationType >= 2)
            {
              atmz = TryCreateStage3CommaGluedAutomatizm(
                  automatizmNodeId,
                  actionsImageId,
                  phraseIdListForStimulus,
                  currentBaseId,
                  currentEmotionId,
                  currentActivityId,
                  toneId,
                  moodId,
                  visualColorId,
                  activationType,
                  deferredOperatorEvalScheduledThisStimulus);
              if (atmz != null)
              {
                AppGlobalState.CurStimulusImageId = actionsImageId;
                RefreshExtremImportanceForCurrentStimulus(actionsImageId);
                ApplyStage3MirrorContextBeforeExecute(atmz, automatizmNodeId, deferredOperatorEvalScheduledThisStimulus);
                return ExecuteAutomatizm(atmz);
              }

              int parrotAutomatizmId = _mirrorAutomatizmService.TryCreateInitialParrotAutomatizm(
                automatizmNodeId,
                actionsImageId,
                hasVerbalPart,
                hasNonVerbalPart);
              if (parrotAutomatizmId > 0)
                atmz = _automatizmSystem.GetAutomatizmById(parrotAutomatizmId);
              if (atmz != null)
              {
                AppGlobalState.CurStimulusImageId = actionsImageId;
                RefreshExtremImportanceForCurrentStimulus(actionsImageId);
                ApplyStage3MirrorContextBeforeExecute(atmz, automatizmNodeId, deferredOperatorEvalScheduledThisStimulus);
                return ExecuteAutomatizm(atmz);
              }
            }
          }
          else
          {
            // Стадия >= 4: уровни 1–2, при провале — циклы мышления; ориентировочный рефлекс только на стадиях < 4.
            (bool problemSolved, Automatizm toExecute) = TryProcessThinkingLevels(automatizmNodeId, actionsImageId);

            if (problemSolved && toExecute != null)
            {
              if (activationType >= 2)
              {
                int responseNodeId = GetTreeNodeIdForResponseActionsImage(toExecute.ActionsImageID, currentBaseId, currentEmotionId, currentActivityId);
                _mirrorAutomatizmService.StartDialogMirrorForExistingAutomatizm(responseNodeId > 0 ? responseNodeId : automatizmNodeId);
              }
              AppGlobalState.CurStimulusImageId = actionsImageId;
              RefreshExtremImportanceForCurrentStimulus(actionsImageId);
              return ExecuteAutomatizm(toExecute);
            }

            // 3-й уровень: циклы мышления (только стадия 4+). Быстрый старт после провала уровня 2.
            if (!problemSolved &&
                AppGlobalState.EvolutionStage >= 4 &&
                _thinkingCyclesSystem != null &&
                _informationEnvironmentSystem != null &&
                _informationEnvironmentSystem.CurrentInformationEnvironment.UnresolvedAtThinkingLevel2)
            {
              var env = _informationEnvironmentSystem.CurrentInformationEnvironment;
              var problemTreeInfo = _understandingTreeSystem != null
                ? _understandingTreeSystem.ProblemTreeInfo
                : (AutTreeId: 0, SituationTreeId: 0, ThemeId: 0, PurposeId: 0);

              var ctx = new ThinkingCycleContext
              {
                PulseCount = PulseCount,
                BaseId = currentBaseId,
                EmotionId = currentEmotionId,
                AutomatizmNodeId = automatizmNodeId,
                StimulusActionsImageId = actionsImageId,
                ProblemNodeId = _problemTreeSystem?.DetectedActiveLastProblemNodeId ?? 0,
                ThemeId = problemTreeInfo.ThemeId,
                PurposeId = problemTreeInfo.PurposeId,
                Danger = env.Danger,
                VeryActualSituation = env.VeryActualSituation,
                IsWaitingPeriod = env.IsWaitingPeriod
              };

              _thinkingCyclesSystem.OnUnresolvedProblem(ctx);
              var decision = _thinkingCyclesSystem.DispatchCycles(PulseCount, isSleeping: IsSleeping);
              PublishMainThinkingCycleToAppGlobalState();
              if (decision != null && (decision.AutomatizmToExecute != null || decision.ActionsImageIdToAutomatize > 0))
              {
                AppGlobalState.CurStimulusImageId = actionsImageId;
                RefreshExtremImportanceForCurrentStimulus(actionsImageId);
                ExecuteThinkingDecision(decision);
                return true; // блокировать рефлексы при удачном запуске
              }
            }
            else if (!problemSolved &&
                     AppGlobalState.EvolutionStage >= 4 &&
                     _thinkingCyclesSystem != null &&
                     _informationEnvironmentSystem != null)
            {
              bool unresL2 = _informationEnvironmentSystem.CurrentInformationEnvironment.UnresolvedAtThinkingLevel2;
            }

            AppGlobalState.CurrentStimulusActionsImageId = stimulusActionsImageIdForContext;
            AppGlobalState.CurrentStimulusActionIdList = actionIdList?.ToList() ?? new List<int>();
            AppGlobalState.CurrentStimulusToneId = toneId;
            AppGlobalState.CurrentStimulusMoodId = moodId;
          }
        }

        if (atmz != null)
        {
          AppGlobalState.CurStimulusImageId = actionsImageId;
          RefreshExtremImportanceForCurrentStimulus(actionsImageId);
          if (automatizmNodeId > 0)
            ApplyStage3MirrorContextBeforeExecute(atmz, automatizmNodeId, deferredOperatorEvalScheduledThisStimulus);
          return ExecuteAutomatizm(atmz); // блокируем рефлексы при удачном запуске автоматизма
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }

      return false; // Не блокировать рефлексы
    }

    #region Стадия 3: склейка штатных автоматизмов по запятой

    /// <summary>
    /// Стадия 3: при стимуле с ровно одной «склейочной» запятой (слева и справа от неё части без запятых внутри),
    /// без штатного на целую фразу и с штатными на обе части — создать автоматизм на полный стимул.
    /// Две и более запятых в тексте не склеиваем (цепочки из трёх+ фрагментов — только эхо/зеркало). При неудаче — null.
    /// </summary>
    private Automatizm TryCreateStage3CommaGluedAutomatizm(
        int fullStimulusTreeNodeId,
        int stimulusActionsImageId,
        List<int> phraseIdListForStimulus,
        int currentBaseId,
        int currentEmotionId,
        int currentActivityId,
        int toneId,
        int moodId,
        int visualColorId,
        int activationType,
        bool deferredOperatorEvalScheduledThisStimulus)
    {
      if (activationType < 2 || fullStimulusTreeNodeId <= 0 || stimulusActionsImageId <= 0)
        return null;
      if (phraseIdListForStimulus == null || !phraseIdListForStimulus.Any())
        return null;

      // Уже есть штатный на весь фрагмент — склейка не нужна (и сюда обычно не попадаем).
      if (_automatizmSystem.GetBelief2AutomatizmFromTreeId(fullStimulusTreeNodeId) != null)
        return null;

      string fullText = BuildPultPhraseText(phraseIdListForStimulus);
      if (string.IsNullOrWhiteSpace(fullText))
        return null;

      int firstComma = fullText.IndexOf(',');
      if (firstComma < 0)
        return null;

      string leftPart = fullText.Substring(0, firstComma).Trim();
      string rightPart = fullText.Substring(firstComma + 1).Trim();
      if (string.IsNullOrEmpty(leftPart) || string.IsNullOrEmpty(rightPart))
        return null;
      // Справа от первой запятой не должно быть ещё запятых — только одна «склейка» пары.
      if (rightPart.IndexOf(',') >= 0)
        return null;

      var staff1 = TryResolveStaffAutomatizmForVerbalSubstring(
          leftPart, currentBaseId, currentEmotionId, currentActivityId, toneId, moodId, visualColorId);
      var staff2 = TryResolveStaffAutomatizmForVerbalSubstring(
          rightPart, currentBaseId, currentEmotionId, currentActivityId, toneId, moodId, visualColorId);
      if (staff1 == null || staff2 == null)
        return null;

      string mergedPhraseText = BuildCommaGluedResponseText(staff1.ActionsImageID, staff2.ActionsImageID);
      if (string.IsNullOrWhiteSpace(mergedPhraseText))
        return null;

      var recognizedMerged = _sensorySystem.VerbalChannel.RecognizeText(mergedPhraseText.Trim(), authoritativeWrite: true);
      if (recognizedMerged == null || !recognizedMerged.Any())
        return null;

      int responseImageId = GetOrCreateAgentResponseActionsImageFromStimulusTemplate(
          stimulusActionsImageId,
          new List<int> { recognizedMerged[0] });
      if (responseImageId <= 0)
        return null;

      var (newId, created) = _automatizmSystem.CreateNewAutomatizm(fullStimulusTreeNodeId, responseImageId, true);
      if (created == null)
        return null;

      created.Count = 0;
      if (!_automatizmSystem.ExistsAutomatizmForThisNodeId(fullStimulusTreeNodeId))
        _automatizmSystem.SetAutomatizmBelief(created, 2);

      if (!deferredOperatorEvalScheduledThisStimulus)
      {
        int responseNodeId = GetTreeNodeIdForResponseActionsImage(
            created.ActionsImageID,
            currentBaseId,
            currentEmotionId,
            currentActivityId);
        _mirrorAutomatizmService.StartDialogMirrorForExistingAutomatizm(
            responseNodeId > 0 ? responseNodeId : fullStimulusTreeNodeId);
      }

      return created;
    }

    private string BuildPultPhraseText(List<int> phraseIdList)
    {
      if (phraseIdList == null || !phraseIdList.Any())
        return "";
      if (phraseIdList.Count == 1)
        return _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(phraseIdList[0]) ?? "";
      return string.Join(" ", phraseIdList.Select(pid => _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(pid) ?? ""));
    }

    private Automatizm TryResolveStaffAutomatizmForVerbalSubstring(
        string subPhraseText,
        int currentBaseId,
        int currentEmotionId,
        int currentActivityId,
        int toneId,
        int moodId,
        int visualColorId)
    {
      if (string.IsNullOrWhiteSpace(subPhraseText))
        return null;

      var phraseIds = _sensorySystem.VerbalChannel.RecognizeText(subPhraseText.Trim(), authoritativeWrite: false);
      if (phraseIds == null || !phraseIds.Any())
        return null;

      var (_, verbIdForTree, firstSimbol, _) = PrepareVerbalStimulusForStage2(phraseIds, toneId, moodId);
      int toneMood = GetToneMoodID(toneId, moodId);
      int nodeId = AutomatizmTreeActivation(
          currentBaseId,
          currentEmotionId,
          currentActivityId,
          toneMood,
          firstSimbol,
          verbIdForTree,
          visualColorId);
      if (nodeId <= 0)
        return null;

      var staff = _automatizmSystem.GetBelief2AutomatizmFromTreeId(nodeId);
      if (staff != null && staff.Usefulness >= 0)
        return staff;
      return null;
    }

    private string BuildCommaGluedResponseText(int staffActionsImageId1, int staffActionsImageId2)
    {
      string t1 = TextFromActionsImagePhrases(staffActionsImageId1);
      string t2 = TextFromActionsImagePhrases(staffActionsImageId2);
      if (string.IsNullOrWhiteSpace(t1) || string.IsNullOrWhiteSpace(t2))
        return null;
      return $"{t1.Trim()}, {t2.Trim()}";
    }

    private string TextFromActionsImagePhrases(int actionsImageId)
    {
      if (actionsImageId <= 0 || _actionsImagesSystem == null)
        return null;
      var img = _actionsImagesSystem.GetActionsImage(actionsImageId);
      if (img?.PhraseIdList == null || !img.PhraseIdList.Any())
        return null;
      var parts = img.PhraseIdList
          .Select(pid => _sensorySystem.VerbalChannel.GetPhraseFromPhraseId(pid))
          .Where(s => !string.IsNullOrWhiteSpace(s))
          .ToList();
      if (!parts.Any())
        return null;
      return string.Join(" ", parts);
    }

    /// <summary>
    /// Образ ответа агента (kind=1, adaptive act ids), по образцу стимула с пульта — аналогично зеркалу.
    /// </summary>
    private int GetOrCreateAgentResponseActionsImageFromStimulusTemplate(int stimulusActionsImageId, List<int> responsePhraseIds)
    {
      if (stimulusActionsImageId <= 0 || responsePhraseIds == null || !responsePhraseIds.Any())
        return 0;
      if (!ActionsImagesSystem.IsInitialized)
        return 0;

      var img = _actionsImagesSystem.GetActionsImage(stimulusActionsImageId);
      if (img == null)
        return 0;

      List<int> adaptiveIds;
      if (img.ActIdList != null && img.ActIdList.Any() && AdaptiveActionsSystem.IsInitialized)
        adaptiveIds = AdaptiveActionsSystem.Instance.ConvertInfluenceActionIdsToAdaptiveActionIds(img.ActIdList);
      else
        adaptiveIds = new List<int>();

      var (newId, _) = _actionsImagesSystem.CreateNewActionsImage(
          kind: 1,
          actIdList: adaptiveIds ?? new List<int>(),
          phraseIdList: responsePhraseIds,
          toneId: img.ToneId,
          moodId: img.MoodId,
          checkUnicum: true,
          visualColorId: img.VisualColorId);
      return newId > 0 ? newId : 0;
    }

    #endregion

    #region Уровни мышления 1 и 2

    /// <summary>
    /// Оркестратор уровней осмысления: уровень 1 (штатный автоматизм) → уровень 2 (правила) → при неуспехе заглушка для циклов.
    /// </summary>
    /// <returns>(problemSolved, automatizm для выполнения или null)</returns>
    private (bool problemSolved, Automatizm toExecute) TryProcessThinkingLevels(
      int automatizmNodeId,
      int actionsImageId)
    {
      if (_informationEnvironmentSystem == null)
        return (false, null);

      var infoEnv = _informationEnvironmentSystem.CurrentInformationEnvironment;
      infoEnv.UnresolvedAtThinkingLevel2 = false;
      infoEnv.UnresolvedNodeId = 0;
      infoEnv.UnresolvedActionsImageId = 0;
      infoEnv.UnresolvedPulseCount = 0;

      (bool resolved, Automatizm toExecute) = ProcessLevel1(automatizmNodeId, actionsImageId);
      if (resolved && toExecute != null)
      {
        AppGlobalState.UpdateThinkingLevelInfo(1, true);
        return (true, toExecute);
      }

      (bool resolved2, Automatizm toExecute2) = ProcessLevel2(automatizmNodeId, actionsImageId);
      if (resolved2 && toExecute2 != null)
      {
        AppGlobalState.UpdateThinkingLevelInfo(2, true);
        return (true, toExecute2);
      }

      AppGlobalState.UpdateThinkingLevelInfo(2, false);
      return (false, null);
    }

    /// <summary>
    /// Первый уровень осмысления: решение только за счёт штатного/текущего автоматизма (без правил).
    /// </summary>
    /// <remarks>
    /// При <see cref="InformationEnvironmentSystem.InformationEnvironment.VeryActualSituation"/> и не-<see cref="InformationEnvironmentSystem.InformationEnvironment.Danger"/>:
    /// по прямым правилам эпизодической памяти оценивается пара «образ стимула <paramref name="stimulusActionsImageId"/> → планируемый ответ штатного»
    /// (<see cref="Automatizm.ActionsImageID"/>). При уверенном негативном прогнозе штатный автоматизм не утверждается — переход на уровень 2.
    /// </remarks>
    private (bool resolved, Automatizm toExecute) ProcessLevel1(int automatizmNodeId, int stimulusActionsImageId)
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
        if (_episodicMemorySystem != null && staff.ActionsImageID > 0 && stimulusActionsImageId > 0)
        {
          var (acc, eff) = EpisodicMemorySearch.GetAutomatizmActionPrognosis(
            _episodicMemorySystem,
            stimulusActionsImageId,
            staff.ActionsImageID);

          if (acc > 0 && eff < 0)
            return (false, null);
        }
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
      string ruleSrc = chain != null && chain.Count > 0 ? "GPT-цепочка" : "GetSingleBestRule(все типы, только валентность≥0)";
      if (rule == null || rule.ActionId <= 0)
      {
        Logger.Info(
            $"[ThinkingL2] узел={automatizmNodeId}, триггер-образ={actionsImageId}: правило не найдено ({ruleSrc}). Отрицательный опыт в эпизодике не подставляется как «лучшее» правило.");
        SetUnresolvedAtLevel2Stub(automatizmNodeId, actionsImageId);
        return (false, null);
      }

      Logger.Info(
          $"[ThinkingL2] источник={ruleSrc}, правило: Trigger={rule.TriggerId}, Action={rule.ActionId}, Effect={rule.Effect}, IsTeacher={rule.IsTeacher}, Importence/оценка={rule.Importence}, Count={rule.Count}.");

      var episodicAtmz = _automatizmSystem.GetMotorsAutomatizmListFromTreeId(automatizmNodeId)
        .FirstOrDefault(a => a.ActionsImageID == rule.ActionId);
      if (episodicAtmz == null)
      {
        var (newId, _) = _automatizmSystem.CreateNewAutomatizm(automatizmNodeId, rule.ActionId, true);
        episodicAtmz = newId > 0 ? _automatizmSystem.GetAutomatizmById(newId) : null;
      }
      if (episodicAtmz != null && episodicAtmz.Usefulness >= 0)
      {
        Logger.Info($"[ThinkingL2] выполняется автоматизм по правилу id={episodicAtmz.ID}, полезность={episodicAtmz.Usefulness}.");
        return (true, episodicAtmz);
      }

      Logger.Info(
          $"[ThinkingL2] автоматизм по образу действия {rule.ActionId} не найден или полезность < 0 (id={(episodicAtmz?.ID ?? 0)}).");
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
      env.IsWaitingPeriod = false;

      // Ожидание оценки снимается после отложенной оценки в ProcessPsychicPulse / при отсутствии ID (TryScheduleDeferredOperatorEvaluationOnStimulus), до сюда не дублировать ForceStop.

      Logger.Info($"Уровень 2 не решён — проблема для циклов мышления. NodeId={nodeId}, ActionsImageId={actionsImageId}");
    }

    #endregion

    /// <summary>
    /// Активация дерева автоматизмов
    /// </summary>
    /// <param name="baseId">Базовое состояние.</param>
    /// <param name="emotionId">ID образа эмоций.</param>
    /// <param name="activityId">ID образа действий.</param>
    /// <param name="toneMoodId">ID тона и настроения.</param>
    /// <param name="simbolId">ID первого символа фразы.</param>
    /// <param name="verbId">ID вербального образа.</param>
    /// <param name="visualId">Код зрительного канала (<see cref="AgentVisualColor"/>).</param>
    /// <param name="isUnrecognizedPhrase">Флаг нераспознанной фразы при обходе дерева.</param>
    internal int AutomatizmTreeActivation(
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbId,
        int visualId,
        bool isUnrecognizedPhrase = false)
    {
      if (PulseCount < MinGlobalPulseForAutomatizmTreeActivation)
        return 0;

      if (IsSleeping)
        return 0;

      if (!AgentVisualColor.IsValidCode(visualId))
        visualId = AgentVisualColor.White;

      // Активация дерева
      int detectedNodeId = _automatizmTreeSystem.AutomatizmTreeActivation(
          baseId,
          emotionId,
          activityId,
          toneMoodId,
          simbolId,
          verbId,
          visualId,
          isUnrecognizedPhrase);

      return detectedNodeId;
    }

    /// <summary>
    /// Готовит вербальный стимул: для стадии 2 при нескольких фразах («ма ма») склеивает в одну («мама») для образа стимула и узла дерева; CurActiveVerbalId остаётся по исходному списку для цепочки.
    /// </summary>
    /// <returns>(verbId для CurActiveVerbalId, verbId для дерева, firstSymbol, phraseIdList для образа стимула)</returns>
    private (int verbIdForCurActive, int verbIdForTree, int firstSimbol, List<int> phraseIdListForStimulus) PrepareVerbalStimulusForStage2(
        List<int> phraseIdList,
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
    /// <remarks>
    /// Сначала штатный (Belief=2): в системе не более одного на ветку — см. <see cref="AutomatizmSystem.SetAutomatizmBelief"/>.
    /// </remarks>
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
    /// Получить ID узла дерева автоматизмов по вербальной части образа действий (например ответа агента).
    /// Для сдвига после штатного автоматизма на ст. 3 см. <see cref="ApplyStage3MirrorContextBeforeExecute"/>.
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

      var (_, verbIdForTree, firstSimbol, _) = PrepareVerbalStimulusForStage2(phraseIdList, toneId, moodId);
      int toneMood = GetToneMoodID(toneId, moodId);

      int responseVisual = img.VisualColorId;
      if (!AgentVisualColor.IsValidCode(responseVisual))
        responseVisual = AgentVisualColor.White;

      return AutomatizmTreeActivation(currentBaseId, currentEmotionId, currentActivityId, toneMood, firstSimbol, verbIdForTree, responseVisual);
    }

    /// <summary>
    /// Стадия 3: эхо-автоматизм (S→S) — узел ветки совпадает с узлом по фразе ответа в контексте этой ветки.
    /// </summary>
    private bool IsStage3MirrorEchoAutomatizm(Automatizm automatizm)
    {
      if (automatizm == null || automatizm.BranchID <= 0)
        return false;
      var branchNode = _automatizmTreeSystem.GetNodeById(automatizm.BranchID);
      if (branchNode == null)
        return false;
      int phraseNodeId = GetTreeNodeIdForResponseActionsImage(
          automatizm.ActionsImageID,
          branchNode.BaseID,
          branchNode.EmotionID,
          branchNode.ActivityID);
      return phraseNodeId > 0 && phraseNodeId == automatizm.BranchID;
    }

    /// <summary>
    /// Стадия 3: стимул в окне ожидания пришёл вместе с постановкой отложенной оценки и сразу запускает штатный сдвиг на своей ветке —
    /// не создавать на следующем пульсе новые пары зеркала (сценарий: шаги 6–7); якорь следующего сдвига — узел фразы ответа этого сдвига (шаг 8).
    /// </summary>
    private void ApplyStage3MirrorContextBeforeExecute(
      Automatizm atmz,
      int stimulusTreeNodeId,
      bool deferredOperatorEvalScheduledThisStimulus)
    {
      if (AppGlobalState.EvolutionStage != 3 || atmz == null || !deferredOperatorEvalScheduledThisStimulus)
        return;

      if (atmz.BranchID != stimulusTreeNodeId)
        return;

      if (IsStage3MirrorEchoAutomatizm(atmz))
        return;

      // Снимок «кого оцениваем на следующем пульсе» — тот же, что в TrySchedule (до ExecuteAutomatizm / StartWaiting).
      int deferredEvalTargetSnap = AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation;
      if (deferredEvalTargetSnap <= 0)
        deferredEvalTargetSnap = _previousAutomatizmId > 0 ? _previousAutomatizmId : _currentAutomatizmId;
      if (deferredEvalTargetSnap > 0 && deferredEvalTargetSnap != atmz.ID)
        return;

      _skipStage3MirrorLearningOnNextEval = true;
      var branchNode = _automatizmTreeSystem.GetNodeById(atmz.BranchID);
      if (branchNode == null)
        return;

      int responsePhraseNodeId = GetTreeNodeIdForResponseActionsImage(
          atmz.ActionsImageID,
          branchNode.BaseID,
          branchNode.EmotionID,
          branchNode.ActivityID);

      _mirrorAutomatizmService.SetDialogTriggerNodeIdForActiveMirror(responsePhraseNodeId);
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

        var result = _automatismExecutionService.ExecuteAutomatizmWithChains(automatizm.ID);

        if (result.Success)
        {
          Logger.Info($"Запущен автоматизм ID: {automatizm.ID} для узла: {automatizm.BranchID}");

          // Период ожидания оценки оператора: только после фактической активации автоматизма (сброс таймера при каждом успешном запуске)
          if (AppGlobalState.EvolutionStage >= 2)
            AppGlobalState.StartWaitingForOperatorEvaluation(automatizm.ID);
          _currentAutomatizmId = automatizm.ID;
        }
        else
        {
          // Завершить отслеживание с ошибкой
          if (trackingResult != null)
          {
            trackingResult.Result = AutomatismResultTracker.ExecutionResult.Error;
            trackingResult.ErrorMessage = result.ErrorMessage;
            _automatismResultTracker.FinishTracking(trackingResult);
          }

          // Триггер «Игнор агента»: негативный эффект моторного автоматизма — обновить тему и дерево проблем
          if (_understandingTreeSystem != null && _problemTreeSystem != null)
            _understandingTreeSystem.UpdateThemeByTriggerAndRefreshProblemTree(AgentEventsCatalog.Codes.AgentIgnore, _problemTreeSystem);

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
      _deferredOperatorEvaluationAutomatizmId = 0;
      _deferredOperatorEvaluationLastRunPulseForResponse = 0;
      _skipStage3MirrorLearningOnNextEval = false;
    }

    /// <summary>
    /// Сброс ожидания оценки после разбора отложенной оценки, если новый автоматизм на стимул не перезаписал ожидание
    /// (иначе гонка с <see cref="ThinkingCyclesSystem.DispatchCycles"/>).
    /// </summary>
    private static void EndOperatorEvaluationWait()
    {
      AppGlobalState.ResetWaitingForOperatorEvaluation();
    }

    /// <summary>
    /// Стимул с пульта в окне ожидания: зарегистрировать ответ оператора и поставить оценку в очередь на следующий <see cref="ProcessPsychicPulse"/>.
    /// Не вызывать отсюда <see cref="EvaluatePreviousAutomatizm"/> — только после <c>UpdateStateOnly</c> на следующем пульсе, иначе оценка не увидит изменений гомеостаза.
    /// </summary>
    private void TryScheduleDeferredOperatorEvaluationOnStimulus(
      int activationType,
      int actionsImageId,
      int automatizmNodeId,
      bool hasVerbalPart,
      bool hasNonVerbalPart)
    {
      if (!AppGlobalState.WaitingForOperatorEvaluation || activationType < 2)
        return;

      // IsEvaluationTime() требует timeSince>0; при стимуле сценария на том же пульсе, что и LastRun (второй вызов
      // SensorActivation после эхо) timeSince==0 — ответ оператора всё равно валиден, иначе сбрасывается зеркало и не создаётся сдвиг.
      if (!AppGlobalState.IsOperatorResponseWithinWaitingWindow())
      {
        if (AppGlobalState.EvolutionStage == 2 || AppGlobalState.EvolutionStage == 3)
          ResetAutomatizmWaitingState();
        return;
      }

      _mirrorAutomatizmService.RegisterOperatorResponse(actionsImageId, automatizmNodeId, hasVerbalPart, hasNonVerbalPart);

      // Кого оцениваем: в первую очередь тот, на кого открыто ожидание (снимок до выполнения следующего автоматизма
      // на этом пульсе, иначе StartWaiting перезапишет waitTarget). Цепочка prev/cur — запасной вариант.
      int automatizmToEvaluate = AppGlobalState.AutomatizmIdWaitingForOperatorEvaluation;
      if (automatizmToEvaluate <= 0)
        automatizmToEvaluate = _previousAutomatizmId > 0 ? _previousAutomatizmId : _currentAutomatizmId;
      if (automatizmToEvaluate <= 0)
      {
        EndOperatorEvaluationWait();
        return;
      }

      _deferredOperatorEvaluationAutomatizmId = automatizmToEvaluate;
      _deferredOperatorEvaluationLastRunPulseForResponse = AppGlobalState.LastRunAutomatizmPulsCount;
    }

    /// <summary>
    /// Оценить предыдущий автоматизм по стимулу оператора. Вызывать только из <see cref="ProcessPsychicPulse"/> на пульсе после стимула (не из SensorActivation).
    /// </summary>
    /// <param name="automatizmIdToEvaluate">ID автоматизма агента, на который отвечает оператор.</param>
    /// <param name="lastRunPulseForResponseTime">
    /// Пульс, с которого считать время реакции оператора (снимок до StartWaiting нового автоматизма на пульсе стимула); 0 — взять из AppGlobalState.
    /// </param>
    private int EvaluatePreviousAutomatizm(int automatizmIdToEvaluate, int lastRunPulseForResponseTime = 0)
    {
      if (automatizmIdToEvaluate <= 0)
        return 0;

      if (!AppGlobalState.WaitingForOperatorEvaluation)
        return 0;

      if (lastRunPulseForResponseTime <= 0)
        lastRunPulseForResponseTime = AppGlobalState.LastRunAutomatizmPulsCount;

      if (AppGlobalState.EvolutionStage < 2)
      {
        Logger.Info(
            $"Стадия {AppGlobalState.EvolutionStage}: ответ оператора без оценки полезности для автоматизма ID={automatizmIdToEvaluate}");
        return 0;
      }

      // Состояние до ответа оператора (интегральное — для запасной ветки и смешивания)
      var stateBefore = AppGlobalState.StateBeforeOperatorImpact;

      // Текущее состояние после стимула оператора (пересчитано в UpdateStateOnly в начале этого пульса)
      var stateAfter = AppGlobalState.CurrentOverallState;

      int assessment;
      string assessmentSource;
      if (GomeostasSystem.IsInitialized &&
          AppGlobalState.TryGetOperatorEvaluationParameterSnapshot(out var snapshotBefore, out int focusParamId) &&
          snapshotBefore != null)
      {
        var gh = GomeostasSystem.Instance;
        var currentParams = gh.GetAllParameters();
        assessmentSource = "gomeo_calculator";
        assessment = gh.Calculator.ComputeOperatorAutomatizmAssessment(
            snapshotBefore,
            currentParams,
            focusParamId,
            stateBefore,
            stateAfter);
        Logger.Info(
            $"[USEFULNESS_EVAL] EvaluatePreviousAutomatizm rawAssessment source={assessmentSource} atmzId={automatizmIdToEvaluate} " +
            $"pulse={GlobalTimer.GlobalPulsCount} focusParamId={focusParamId} snapshotKeys={snapshotBefore.Count} " +
            $"stateBefore={stateBefore} stateAfter={stateAfter} assessment={assessment}");
      }
      else
      {
        assessment = 0;
        if (stateAfter > stateBefore)
          assessment = 1;
        else if (stateAfter < stateBefore)
          assessment = -1;
        assessmentSource = "integral_state_only";
        Logger.Info(
            $"[USEFULNESS_EVAL] EvaluatePreviousAutomatizm rawAssessment source={assessmentSource} atmzId={automatizmIdToEvaluate} " +
            $"pulse={GlobalTimer.GlobalPulsCount} gomeoInit={GomeostasSystem.IsInitialized} " +
            $"stateBefore={stateBefore} stateAfter={stateAfter} assessment={assessment}");
      }

      int responseTime = PulseCount - lastRunPulseForResponseTime;

      int operatorResponseImageId = _mirrorAutomatizmService?.GetPendingOperatorResponseActionsImageId() ?? 0;
      int assessmentBeforeMerge = assessment;
      assessment = MergeOperatorAssessmentWithPultInfluence(assessment, operatorResponseImageId);
      if (assessmentBeforeMerge != assessment)
        Logger.Info(
            $"[USEFULNESS_EVAL] EvaluatePreviousAutomatizm mergeChanged assessment {assessmentBeforeMerge}->{assessment} " +
            $"operatorResponseActionsImageId={operatorResponseImageId}");
      _automatismResultTracker.MarkOperatorRecognition(
          automatizmIdToEvaluate,
          true, // распознано оператором
          assessment,
          responseTime,
          operatorResponseImageId);

      // Триггер «Игнор агента»: негативный эффект при отрицательной оценке оператора
      if (assessment < 0 && _understandingTreeSystem != null && _problemTreeSystem != null)
        _understandingTreeSystem.UpdateThemeByTriggerAndRefreshProblemTree(AgentEventsCatalog.Codes.AgentIgnore, _problemTreeSystem);

      Logger.Info($"Оценен автоматизм ID={automatizmIdToEvaluate}: оценка={assessment}, время реакции={responseTime}");

      // Стадия 3: после оценки полезности — зеркальная пара по ответу оператора (сдвиг + эхо), как раньше до объединения со стадией 4.
      if (AppGlobalState.EvolutionStage == 3)
      {
        int mirrorAutomatizmIdEarly = 0;
        if (_skipStage3MirrorLearningOnNextEval)
        {
          _skipStage3MirrorLearningOnNextEval = false;
          _mirrorAutomatizmService.DiscardPendingOperatorResponseWithoutMirror();
        }
        else
        {
          int pendingOperatorPhraseNodeId = _mirrorAutomatizmService.GetPendingResponseTreeNodeId();
          mirrorAutomatizmIdEarly = _mirrorAutomatizmService.TryCreateMirrorFromPendingOperatorResponse();
          if (mirrorAutomatizmIdEarly > 0 &&
              pendingOperatorPhraseNodeId > 0 &&
              _automatizmSystem != null)
          {
            var staffOnOperatorPhrase =
                _automatizmSystem.GetBelief2AutomatizmFromTreeId(pendingOperatorPhraseNodeId);
            if (staffOnOperatorPhrase != null &&
                staffOnOperatorPhrase.Usefulness >= 0 &&
                staffOnOperatorPhrase.ID != mirrorAutomatizmIdEarly)
            {
              mirrorAutomatizmIdEarly = staffOnOperatorPhrase.ID;
            }
          }
        }

        return mirrorAutomatizmIdEarly;
      }

      return 0;
    }

    /// <summary>
    /// При конфликте дельты параметров и явного знака воздействий с пульта приоритет у намерения оператора
    /// (<see cref="InfluenceActionSystem.GetSignedOperatorValenceSumForActions"/> — знак воздействия с учётом типа параметра по Speed).
    /// </summary>
    private int MergeOperatorAssessmentWithPultInfluence(int assessment, int operatorResponseActionsImageId)
    {
      if (operatorResponseActionsImageId <= 0 || _influenceActionSystem == null || _influenceActionsImagesSystem == null)
      {
        Logger.Info(
            $"[USEFULNESS_EVAL] MergeOperatorAssessmentWithPultInfluence skip imageId={operatorResponseActionsImageId} " +
            $"influenceActionSystem={_influenceActionSystem != null} influenceImagesSystem={_influenceActionsImagesSystem != null} " +
            $"assessment={assessment}");
        return assessment;
      }

      // Pending — это ID образа из ActionsImagesSystem (SensorActivation → CreateActionsImage), а не из InfluenceActionsImagesSystem.
      IReadOnlyList<int> ids;
      var actionsImg = _actionsImagesSystem.GetActionsImage(operatorResponseActionsImageId);
      if (actionsImg != null)
        ids = actionsImg.ActIdList ?? new List<int>();
      else
        ids = _influenceActionsImagesSystem.GetInfluenceActionIds(operatorResponseActionsImageId);

      if (ids == null || ids.Count == 0)
      {
        Logger.Info(
            $"[USEFULNESS_EVAL] MergeOperatorAssessmentWithPultInfluence no_action_ids imageId={operatorResponseActionsImageId} assessment={assessment}");
        return assessment;
      }

      int sum = _influenceActionSystem.GetSignedOperatorValenceSumForActions(ids);
      if (sum == 0)
      {
        Logger.Info(
            $"[USEFULNESS_EVAL] MergeOperatorAssessmentWithPultInfluence valence_sum_zero imageId={operatorResponseActionsImageId} " +
            $"actionIdsCount={ids.Count} assessment={assessment}");
        return assessment;
      }

      int pSign = sum > 0 ? 1 : -1;
      int merged;
      if (assessment == 0)
        merged = pSign;
      else if ((assessment > 0 && pSign < 0) || (assessment < 0 && pSign > 0))
        merged = pSign;
      else
        merged = assessment;

      if (merged != assessment || assessment == 0)
        Logger.Info(
            $"[USEFULNESS_EVAL] MergeOperatorAssessmentWithPultInfluence imageId={operatorResponseActionsImageId} " +
            $"actionIdsCount={ids.Count} operatorValenceSum={sum} pSign={pSign} assessmentIn={assessment} mergedOut={merged}");

      return merged;
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
    /// Создает образ действий оператора с учетом тона, настроения и зрительного канала сцены
    /// </summary>
    /// <param name="actionIdList">Список ID действий с пульта.</param>
    /// <param name="phraseIdList">Список ID фраз.</param>
    /// <param name="toneId">ID тона.</param>
    /// <param name="moodId">ID настроения.</param>
    /// <param name="visualColorId">Код зрительного канала (<see cref="AgentVisualColor"/>)</param>
    private int CreateActionsImage(List<int> actionIdList, List<int> phraseIdList, int toneId, int moodId, int visualColorId)
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

        if (!AgentVisualColor.IsValidCode(visualColorId))
          visualColorId = AgentVisualColor.White;

        // Создаем образ действий оператора
        // Kind = 0 (объективное действие) - реальное воздействие с пульта
        var (imageId, actionsImage) = _actionsImagesSystem.CreateNewActionsImage(
            kind: 0, // объективное действие
            actIdList: actionIdList?.ToList() ?? new List<int>(),
            phraseIdList: phraseIdList,
            toneId: toneId,
            moodId: moodId,
            checkUnicum: true, // проверяем уникальность
            visualColorId: visualColorId);

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

    /// <summary>Публикует снимок главного цикла в <see cref="AppGlobalState"/> для ResearchLogger и UI.</summary>
    private void PublishMainThinkingCycleToAppGlobalState()
    {
      if (_thinkingCyclesSystem == null)
      {
        AppGlobalState.ClearMainThinkingCycleSnapshot();
        return;
      }
      var snap = _thinkingCyclesSystem.GetMainCycleSnapshot(maxLogLinesPerCycle: 0);
      if (snap == null)
        AppGlobalState.ClearMainThinkingCycleSnapshot();
      else
        AppGlobalState.UpdateMainThinkingCycleSnapshot(
          snap.Id, snap.Weight, snap.ProblemNodeId, snap.ThemeId, snap.PurposeId, snap.LastStrategyId,
          snap.AwaitingEvaluation, snap.PendingSolutionAutomatizmId);
    }

    #region Диагностика циклов осмысления (3-й уровень)

    /// <summary>Возвращает копию снимка главного цикла мышления (или null).</summary>
    /// <param name="maxLogLinesPerCycle">Максимум строк лога.</param>
    /// <returns>Снимок или null.</returns>
    public ThinkingCycleInfo GetThinkingCyclesMainSnapshot(int maxLogLinesPerCycle = 50)
    {
      return _thinkingCyclesSystem?.GetMainCycleSnapshot(maxLogLinesPerCycle);
    }

    /// <summary>Краткий список всех циклов мышления без логов (для матрицы UI).</summary>
    /// <returns>Список элементов.</returns>
    public IReadOnlyList<ThinkingCycleListItem> GetThinkingCyclesListSnapshot()
    {
      return _thinkingCyclesSystem?.GetAllCyclesLightweightSnapshot() ?? new List<ThinkingCycleListItem>();
    }

    /// <summary>Полный снимок одного цикла по идентификатору (с логом).</summary>
    /// <param name="cycleId">Идентификатор цикла.</param>
    /// <param name="maxLogLinesPerCycle">Максимум последних строк лога.</param>
    /// <returns>Снимок или null.</returns>
    public ThinkingCycleInfo GetThinkingCycleSnapshotById(int cycleId, int maxLogLinesPerCycle = 50)
    {
      return _thinkingCyclesSystem?.GetCycleSnapshotById(cycleId, maxLogLinesPerCycle);
    }

    /// <summary>Текущая инфо-картина (информационная среда) для диагностического UI.</summary>
    /// <returns>Снимок или null.</returns>
    public InformationEnvironmentViewSnapshot GetInformationEnvironmentViewSnapshot()
    {
      var ieSys = _informationEnvironmentSystem;
      if (ieSys?.CurrentInformationEnvironment == null)
        return null;
      var e = ieSys.CurrentInformationEnvironment;
      var targets = e.CurTargetArrID;
      var targetsText = (targets != null && targets.Count > 0) ? string.Join(",", targets) : string.Empty;
      return new InformationEnvironmentViewSnapshot
      {
        LifeTime = e.LifeTime,
        Danger = e.Danger,
        VeryActualSituation = e.VeryActualSituation,
        Mood = e.Mood,
        PsyMood = e.PsyMood,
        PsyEmotionId = e.PsyEmotionId,
        ActionsImageId = e.ActionsImageID,
        ActualEpisodicMemoryId = e.ActualEpisodicMemoryID,
        DominantaId = e.DominantaID,
        NeedThinkingAboutAutomatizm = e.NeedThinkingAboutAutomatizm,
        IsWaitingPeriod = e.IsWaitingPeriod,
        UnresolvedAtThinkingLevel2 = e.UnresolvedAtThinkingLevel2,
        UnresolvedNodeId = e.UnresolvedNodeId,
        UnresolvedActionsImageId = e.UnresolvedActionsImageId,
        UnresolvedPulseCount = e.UnresolvedPulseCount,
        IsSleep = e.IsSleep,
        IsStimulToForce = e.IsStimulToForce,
        CurTargetArrIdText = targetsText
      };
    }

    /// <summary>
    /// Возвращает текстовый отладочный снимок всех циклов мышления (или сообщение при отсутствии данных).
    /// </summary>
    /// <param name="maxLogLinesPerCycle">Максимум строк лога на цикл.</param>
    /// <returns>Текст снимка.</returns>
    public string GetThinkingCyclesDebugSnapshot(int maxLogLinesPerCycle = 5)
    {
      return _thinkingCyclesSystem?.GetDebugSnapshot(maxLogLinesPerCycle) ?? "ThinkingCycles: none";
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