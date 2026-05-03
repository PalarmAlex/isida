using ISIDA.Psychic;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static ISIDA.Reflexes.GeneticReflexesSystem;
using System.Diagnostics;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Активатор рефлексов - управляет запуском безусловных и условных рефлексов
  /// </summary>
  public sealed class ReflexesActivator : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private ResearchLogger _researchLogger;

    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private readonly InfluenceActionSystem _influenceActions;
    private readonly ReflexTreeSystem _reflexTree;
    private readonly ReflexExecutionService _reflexExecutionService;
    private readonly AdaptiveActionsSystem _adaptiveActions;
    private readonly ReflexChainsSystem _reflexChainsSystem;
    private readonly ConditionedReflexFormationService _reflexFormationService;
    private readonly PerceptionImagesSystem _perceptionImageSystem;
    private PsychicSystem _psychicSystem;

    #region Инициализация

    private static ReflexesActivator _instance;

    /// <summary>
    /// Глобальный экземпляр активатора рефлексов
    /// </summary>
    public static ReflexesActivator Instance => _instance ??
        throw new InvalidOperationException("ReflexesActivator не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр активатора рефлексов
    /// </summary>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        InfluenceActionSystem influenceActions,
        ReflexTreeSystem reflexTree,
        ReflexChainsSystem reflexChainsSystem,
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions,
        ConditionedReflexFormationService reflexFormationService,
        PerceptionImagesSystem perceptionImagesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexesActivator уже инициализирован.");

      _instance = new ReflexesActivator(
        gomeostas, 
        geneticReflexes, 
        conditionedReflexes, 
        influenceActions, 
        reflexTree, 
        reflexChainsSystem, 
        reflexExecution, 
        adaptiveActions, 
        reflexFormationService,
        perceptionImagesSystem);
    }

    private ReflexesActivator(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        InfluenceActionSystem influenceActions,
        ReflexTreeSystem reflexTree,
        ReflexChainsSystem reflexChainsSystem,
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions,
        ConditionedReflexFormationService reflexFormationService,
        PerceptionImagesSystem perceptionImagesSystem)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
      _influenceActions = influenceActions ?? throw new ArgumentNullException(nameof(influenceActions));
      _reflexTree = reflexTree ?? throw new ArgumentNullException(nameof(reflexTree));
      _reflexChainsSystem = reflexChainsSystem ?? throw new ArgumentNullException(nameof(reflexChainsSystem));
      _reflexExecutionService = reflexExecution ?? throw new ArgumentNullException(nameof(reflexExecution));
      _adaptiveActions = adaptiveActions ?? throw new ArgumentNullException(nameof(adaptiveActions));
      _reflexFormationService = reflexFormationService ?? throw new ArgumentNullException(nameof(reflexFormationService));
      _perceptionImageSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));

      _reflexActionDuration = _adaptiveActions.ReflexActionDisplayDuration;

      _influenceActions.TriggerStimulusActivated += OnTriggerStimulusActivated;
      _influenceActions.PhraseStimulusActivated += OnPhraseStimulusActivated;

      ResetStates(GlobalTimer.GlobalPulsCount);
    }

    private void OnTriggerStimulusActivated(
      int pulseCount,
      List<int> actionIdList,
      bool authoritativeMode)
    {
      ActiveFromAction(pulseCount, actionIdList, authoritativeMode);
    }

    private void OnPhraseStimulusActivated(
      int pulseCount,
      List<int> actionIdList,
      List<int> phraseIdList,
      int toneId,
      int moodId,
      bool authoritativeMode)
    {
      ActiveFromPhrase(pulseCount, actionIdList, phraseIdList, toneId, moodId, authoritativeMode);
    }

    /// <summary>
    /// Установка логгера
    /// </summary>
    public void SetResearchLogger(ResearchLogger logger)
    {
      _researchLogger = logger;
    }

    /// <summary>
    /// Установка психики
    /// </summary>
    public void SetPsychicSystemm(PsychicSystem psychicSystem)
    {
      _psychicSystem = psychicSystem ?? throw new ArgumentNullException(nameof(psychicSystem));
    }

    #endregion

    #region Константы и состояния

    private bool _lastStepSuccessResult = true;         // Результат выполнения последнего шага цепочки(успех/неудача)
    private int _chainCooldownUntilPulse = 0;           // Пульс, до которого заблокирована активация новых цепочек (период задержки)

    /// <summary>
    /// Текущий результат выполнения действия (для цепочек рефлексов)
    /// </summary>
    public bool LastStepSuccessResult
    {
      get => _lastStepSuccessResult;
      set
      {
        _lock.EnterWriteLock();
        try
        {
          _lastStepSuccessResult = value;
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
    }

    private int _activeCurBaseID = 0;                   // ID текущего базового состояния гомеостаза
    private int _activeCurBaseStyleID = 0;              // ID текущего образа сочетания стилей поведения
    private int _activeCurTriggerStimulusID = 0;        // ID текущего полного активного образа сочетаний пусковых стимулов
    private int _activeCurReflexTriggerStimulusID = 0;  // ID текущего частичного активного образа сочетаний пусковых стимулов
    private int _activeGlobalCurTriggerStimulusID = 0;  // ID триггера для логов

    private int _activeConditionReflexID = 0;           // ID текущего условного рефлекса
    private int _activeGeneticReflexID = 0;             // ID текущего безусловного рефлекса

    private int _activatedPulsCount = 0;                // номер текущего пульса (блокирует ActiveFromConditionChange и ProcessReflexPulse)
    private int _activatedFromPhrasePulse = 0;          // пульс последней активации по CS-пути (фраза/цвет) — не блокирует US-путь
    private int _activatedFromActionPulse = 0;           // пульс последней активации по US-пути (действие) — не блокирует CS-путь
    private int _reflexActionDuration = 0;              // время удержания действия рефлекса (в пульсах) для визуализации
    private int _weitPulceCount = 0;
    private bool _chainAlreadyActivatedInThisContext = false;   // Флаг предотвращения повторной активации цепочки в тех же условиях
    private int _lastReflexActivationPulse = 0;

    private int _chainBaseID = 0;                       // Базовое состояние при активации цепочки                       
    private int _chainStyleID = 0;                      // Стиль поведения при активации цепочки

    private bool _isChainActive => AppGlobalState.IsReflexChainActive
      || AppGlobalState.IsAutomatizmChainActive;        // Флаг наличия активной цепочки рефлексов или автоматизмов
    private int _activeChainId = 0;                     // ID текущей активной цепочки рефлексов
    private int _currentPulse = 0;                      // Текущий номер пульса (для логирования)

    // Список выполненных ID рефлексов в текущей цепочке
    private readonly List<int> _completedReflexesInChain = new List<int>();

    // Флаг режима сна агента
    private bool _isSleeping = false;

    private readonly List<int> _geneticReflexesToRun = new List<int>();       // Список безусловных рефлексов для выполнения
    private readonly List<int> _conditionedReflexesToRun = new List<int>();   // Список условных рефлексов для выполнения
    private List<int> _activetStyleIds = new List<int>();                     // Список текущих активных стилей

    #endregion

    #region Публичные свойства

    /// <summary>
    /// Текущий ID базового состояния
    /// </summary>
    public int ActiveCurBaseID => _activeCurBaseID;

    /// <summary>
    /// Текущий ID образа стилей поведения
    /// </summary>
    public int ActiveCurBaseStyleID => _activeCurBaseStyleID;

    /// <summary>
    /// Текущий ID полного образа пусковых стимулов
    /// </summary>
    public int ActiveCurTriggerStimulusID => _activeCurTriggerStimulusID;

    /// <summary>
    /// Текущий ID часичного  образа пусковых стимулов
    /// </summary>
    public int ActiveCurReflexTriggerStimulusID => _activeCurReflexTriggerStimulusID;

    /// <summary>
    /// Текущий ID условного рефлекса
    /// </summary>
    public int ActiveConditionReflexID => _activeConditionReflexID;

    /// <summary>
    /// Текущий ID безусловного рефлекса
    /// </summary>
    public int ActiveGeneticReflexID => _activeGeneticReflexID;

    /// <summary>
    /// Текущий ID активаного тригера
    /// </summary>
    public int ActiveGlobalCurTriggerStimulusID => _activeGlobalCurTriggerStimulusID;

    #endregion

    #region Основные методы активации

    /// <summary>
    /// Метод обработки пульса - вызывается из GlobalTimer.ProcessAgentPulse()
    /// </summary>
    public void ProcessReflexPulse(int pulseCount, bool isSleeping)
    {
      _isSleeping = isSleeping;
      _currentPulse = pulseCount;

      if (AppGlobalState.IsNewConditions || (pulseChainCompleted != 0 && pulseCount > pulseChainCompleted + _reflexActionDuration))
      {
        _lock.EnterWriteLock();
        try
        {
          DeactivateChainCore(pulseCount);
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }

      ProcessActiveChain(pulseCount);

      if (_pendingChainId > 0 && pulseCount >= _pendingChainActivationPulse)
      {
        Logger.Info($"Активация отложенной цепочки рефлексов {_pendingChainId} на пульсе {pulseCount}");
        ActivatePendingChain(pulseCount);
        _pendingChainId = 0;
        _pendingChainActivationPulse = 0;
      }

      if (pulseCount > _chainCooldownUntilPulse)
        _chainCooldownUntilPulse = 0;

      // только если нет активной цепочки, проверяем новые условия
      if (!_isChainActive)
      {
        if (!CanActivate(pulseCount, isSleeping)) return;

        if (_weitPulceCount == 0)
        {
          if (AppGlobalState.IsNewConditions)
            ActiveFromConditionChange(pulseCount);
        }
        else
        {
          if (pulseCount > _weitPulceCount + _reflexActionDuration)
            _weitPulceCount = 0;
          if (AppGlobalState.IsNewConditions)
            ActiveFromConditionChange(pulseCount);
        }
      }
    }

    /// <summary>
    /// Активация при изменении сочетания стилей реагирования
    /// </summary>
    private void ActiveFromConditionChange(int pulseCount)
    {
      if (_isSleeping) return;
      if (!CanActivate(pulseCount, _isSleeping)) return;

      // Не активируем новые рефлексы, если уже выполняется цепочка
      if (_isChainActive)
        return;

      _activatedPulsCount = pulseCount;
      _weitPulceCount = 0;
      UpdateCurrentStates();
      var conditions = GetCurrentConditionsWithoutTrigger();
      _reflexTree.ConditionsDetection(conditions);

      CollectReflexesForExecution();
      bool psychicBlocked = _psychicSystem.SensorActivation(1, _activeCurBaseID, _activetStyleIds, null, null, 0, 0); // Тип 1 - изменение условий
      if (psychicBlocked)
      {
        Logger.Info("Рефлекс заблокирован психикой");
        return;
      }

      ExecuteReflexes(pulseCount);

      // Как в ActiveFromAction: снимок до конца пульса и ResetStates. Иначе при ускоренном прогоне
      // LogSystemState уходит в Dispatcher.BeginInvoke и видит уже обнулённый _activeGeneticReflexID.
      if (_activeGeneticReflexID != 0)
      {
        if (_activeCurReflexTriggerStimulusID > 0)
        {
          _reflexFormationService.RecordStimulus(
              pulseCount,
              _activeCurReflexTriggerStimulusID,
              _activeCurBaseID,
              _activeCurBaseStyleID,
              _activeGeneticReflexID);

          _reflexFormationService.CheckTemporalCorrelations(pulseCount, false);
        }

        _researchLogger.LogSystemState(pulseCount);
        _activeGlobalCurTriggerStimulusID = 0;
        _activeGeneticReflexID = 0;
      }
    }

    /// <summary>
    /// Активация при действиях с Пульта
    /// </summary>  
    private void ActiveFromAction(
      int pulseCount,
      List<int> actionIdList,
      bool authoritativeMode = false)
    {
      if (_activatedFromActionPulse == pulseCount || _isSleeping) return;

      try
      {
        _activatedFromActionPulse = pulseCount;
        _activatedPulsCount = pulseCount;
        _weitPulceCount = pulseCount;
        UpdateCurrentStates();
        GetActiveTriggerStimulusImage();

        var conditions = GetCurrentGeneticConditionsArray();
        _reflexTree.ConditionsDetection(conditions);
        bool fullMatchFound = _reflexTree.DetectedLevel == 2;

        _chainCooldownUntilPulse = 0;
        _adaptiveActions.ClearActiveAction();
        StopAllReflexChains(pulseCount);

        if (!fullMatchFound && AppGlobalState.DetectedReflexNodeId > 0)
        {
          _geneticReflexesToRun.Clear();
          _geneticReflexesToRun.Add(-1);
          GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        }
        CollectReflexesForExecution();
        bool psychicBlocked = _psychicSystem.SensorActivation(2, _activeCurBaseID, _activetStyleIds, actionIdList, null, 0, 0); // Тип 2 - действие с пульта
        if (psychicBlocked)
        {
          Logger.Info("Рефлекс заблокирован психикой");
          StopAllReflexChains(pulseCount);
          return;
        }

        ExecuteReflexes(pulseCount);

        if (_activeGeneticReflexID != 0)
        {
          if (_activeCurReflexTriggerStimulusID > 0)
          {
            var reflex = _geneticReflexes.GetAllGeneticReflexesList()
                .FirstOrDefault(r => r.Id == _activeGeneticReflexID);

            _reflexFormationService.RecordStimulus(
                pulseCount,
                _activeCurReflexTriggerStimulusID,
                _activeCurBaseID,
                _activeCurBaseStyleID,
                _activeGeneticReflexID);

            _reflexFormationService.CheckTemporalCorrelations(pulseCount, authoritativeMode);
          }

          _researchLogger.LogSystemState(pulseCount);
          // чтобы не попало в логи на следующем пульсе
          _activeGlobalCurTriggerStimulusID = 0;
          _activeGeneticReflexID = 0;
        }
      }
      finally
      {

      }
    }

    /// <summary>
    /// Активация при фразе с Пульта
    /// </summary>
    private void ActiveFromPhrase(
      int pulseCount,
      List<int> actionIdList,
      List<int> phraseIdList,
      int toneId,
      int moodId,
      bool authoritativeMode = false)
    {
      if (_activatedFromPhrasePulse == pulseCount || _isSleeping) return;

      try
      {
        _activatedFromPhrasePulse = pulseCount;
        _activatedPulsCount = pulseCount;
        _weitPulceCount = pulseCount;
        UpdateCurrentStates();
        GetActiveTriggerStimulusImage();
        var conditions = GetCurrentConditionsArray();
        _reflexTree.ConditionsDetection(conditions);

        // Триггер с пульта (вербальный или нет) прерывает все цепочки — иначе условный рефлекс не запустится из-за проверки _isChainActive в ExecuteReflexes.
        // Стадия 0: условные по фразе не используем. Стадии 1+ : при совпадении фразы с у-рефлексом прерываем. Стадия 2: клонирование в автоматизмы. Стадии 3+: всегда прерываем.
        var condFerList = FindConditionedReflexesByPhrase(phraseIdList);
        bool shouldInterruptChain = (condFerList.Any() && AppGlobalState.EvolutionStage > 0) || AppGlobalState.EvolutionStage > 2;

        if (shouldInterruptChain)
        {
          StopAllReflexChains(pulseCount);
          _adaptiveActions.ClearActiveAction();
          _adaptiveActions.ClearActivePhrases();
        }

        CollectReflexesForExecution();
        // Безусловные рефлексы — только на триггеры действий с пульта или изменение состояния/стилей.
        // На одну лишь фразу с пульта (без смены гомеостаза/стилей) безусловные не запускаем.
        _geneticReflexesToRun.Clear();
        GetActionsForGeneticReflexToRun(_geneticReflexesToRun);

        bool psychicBlocked = _psychicSystem.SensorActivation(3, _activeCurBaseID, _activetStyleIds, actionIdList, phraseIdList, toneId, moodId,
            _influenceActions.LastAppliedVisualColorId);
        if (psychicBlocked)
        {
          Logger.Info("Рефлекс заблокирован психикой");
          StopAllReflexChains(pulseCount);
          return;
        }
        
        ExecuteReflexes(pulseCount);

        if (_activeCurTriggerStimulusID != 0)
        {
          // Вторичное обусловливание: если эта фраза активировала у-рефлекс,
          // проверяем, не было ли перед ней другой фразы (CS) во временном окне.
          // Проверка до RecordStimulus, чтобы _lastConditionedStimulus ещё хранил предыдущий CS.
          if (_activeConditionReflexID != 0)
          {
            _reflexFormationService.CheckSecondaryConditioning(
                pulseCount,
                _activeCurTriggerStimulusID,
                _activeCurBaseID,
                _activeCurBaseStyleID,
                _activeConditionReflexID,
                authoritativeMode,
                toneId,
                moodId);
          }

          // Сохраняем как условный стимул (с тоном и настроением фразы с пульта)
          _reflexFormationService.RecordStimulus(
            pulseCount,
            _activeCurTriggerStimulusID,
            _activeCurBaseID,
            _activeCurBaseStyleID,
            0,
            toneId,
            moodId);
        }

        if (_activeConditionReflexID != 0)
        {
          _researchLogger.LogSystemState(pulseCount);
          // чтобы не попало в логи на следующем пульсе
          _activeGlobalCurTriggerStimulusID = 0;
          _activeConditionReflexID = 0;
          _activeGeneticReflexID = 0;
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {

      }
    }

    // Выполнение рефлексов
    // обнуление ID рефлексов и триггеров через ResetStates() в ProcessAgentPulse()
    private void ExecuteReflexes(int pulseCount)
    {
      if (_lastReflexActivationPulse > 0 && pulseCount < _lastReflexActivationPulse + _reflexActionDuration)
        return;

      // Не собираем рефлексы, если уже выполняется цепочка
      if (_isChainActive)
        return;

      if (_geneticReflexesToRun.Contains(-1) && _reflexTree.DetectedLevel < 2)
      {
        var result = _reflexExecutionService.ExecuteGeneticReflex(-1);
        if (result.Success)
        {
          _activeGeneticReflexID = -1;
          _lastReflexActivationPulse = pulseCount;
        }
        return;
      }

      if (_geneticReflexesToRun.Any() && _geneticReflexesToRun[0] == -1)
      {
        var result = _reflexExecutionService.ExecuteGeneticReflex(-1);
        if (result.Success)
        {
          _activeGeneticReflexID = -1;
          _lastReflexActivationPulse = pulseCount;
        }
        return;
      }

      try
      {
        int lastActivatedReflexId = 0;
        bool lastWasConditioned = false;

        if (_conditionedReflexesToRun.Any())
        {
          foreach (var conditionedReflexId in _conditionedReflexesToRun)
          {
            // Находим исходный безусловный рефлекс
            var conditionedReflex = _conditionedReflexes.GetAllConditionedReflexes()
                .FirstOrDefault(r => r.Id == conditionedReflexId);

            if (conditionedReflex != null && conditionedReflex.SourceGeneticReflexId > 0)
            {
              var result = _reflexExecutionService.ExecuteConditionedReflex(conditionedReflex.Id);
              if (result.Success)
              {
                // Логируем как активацию условного рефлекса
                _activeConditionReflexID = conditionedReflexId;
                _activeGeneticReflexID = conditionedReflex.SourceGeneticReflexId;
                _activeGlobalCurTriggerStimulusID = _activeCurTriggerStimulusID;
                _lastReflexActivationPulse = pulseCount;
                lastActivatedReflexId = conditionedReflexId;
                lastWasConditioned = true;

                Logger.Info($"Условный рефлекс {conditionedReflexId} активировал действие безусловного {conditionedReflex.SourceGeneticReflexId}");
              }
            }
          }
        }

        // безусловные рефлексы (если нет условных)
        if (!_conditionedReflexesToRun.Any() && _geneticReflexesToRun.Any())
        {
          foreach (var reflexId in _geneticReflexesToRun)
          {
            var result = _reflexExecutionService.ExecuteGeneticReflex(reflexId);
            if (result.Success)
            {
              _activeGeneticReflexID = reflexId;
              _lastReflexActivationPulse = pulseCount;
              lastActivatedReflexId = reflexId;
              lastWasConditioned = false;
            }
          }
        }

        // Проверяем цепочки после выполнения рефлексов (отложенная активация через _reflexActionDuration пульсов,
        // как у безусловных — условные не должны запускать цепочку сразу в том же пульсе)
        if (lastActivatedReflexId > 0)
          CheckForChainActivation(pulseCount, lastActivatedReflexId, lastWasConditioned);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Получает все узлы дерева рефлексов
    /// </summary>
    private List<ReflexTreeSystem.ReflexNode> GetAllReflexTreeNodes()
    {
      try
      {
        var allNodes = _reflexTree.GetAllNodes();
        if (allNodes != null && allNodes.Count > 0)
          return allNodes;
        
        // Fallback: система не предоставила список, возврат пустого списка
        Logger.Warning("ReflexTree.GetAllNodes() вернул null или пустой список. Цепочки не будут активированы.");
        return new List<ReflexTreeSystem.ReflexNode>();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return new List<ReflexTreeSystem.ReflexNode>();
      }
    }

    /// <summary>
    /// Запускает цепочку рефлексов, начиная с текущего рефлекса
    /// </summary>
    private void ExecuteChainFromReflex(ReflexTreeSystem.ReflexNode node, int pulseCount)
    {
      try
      {
        if (!CanActivateChain(pulseCount))
        {
          Logger.Info($"Активация цепочки рефлексов заблокирована (задержка или уже активна)");
          return;
        }

        int chainId = node.ReflexChainID;
        if (chainId <= 0) return;

        if (_chainAlreadyActivatedInThisContext)
          return;

        var chain = _reflexChainsSystem.GetChain(chainId);
        if (chain == null || !chain.Links.Any())
          return;

        var firstChainLink = chain.Links.OrderBy(l => l.ID).First();

        _lock.EnterWriteLock();
        try
        {
          _chainBaseID = _activeCurBaseID;
          _chainStyleID = _activeCurBaseStyleID;
          _chainAlreadyActivatedInThisContext = true;

          AppGlobalState.IsReflexChainActive = true;

          _activeReflexChain = new ActiveReflexChain
          {
            ChainId = chainId,
            StartPulse = pulseCount,
            LastStepPulse = pulseCount,
            LastEvaluation = null,
            CurrentLinkId = firstChainLink.ID
          };

          _activeChainId = chainId;
          Logger.Info($"Цепочка рефлексов {chainId} активирована после рефлекса, " +
                 $"первое звено цепочки: {firstChainLink.ID}, действие: {firstChainLink.ActionId}");

          _researchLogger?.RegisterActiveChain(chainId, $"ReflexChain_{chainId}", "Reflex");
        }
        finally
        {
          _lock.ExitWriteLock();
        }

        ExecuteChainLink(pulseCount);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        DeactivateChain(pulseCount);
      }
    }

    /// <summary>
    /// Активирует отложенную цепочку рефлексов
    /// </summary>
    private void ActivatePendingChain(int pulseCount)
    {
      if (_pendingChainId <= 0)
        return;

      try
      {
        // Находим узел дерева с этой цепочкой
        var allNodes = GetAllReflexTreeNodes();
        var chainNode = allNodes?.FirstOrDefault(n => n.ReflexChainID == _pendingChainId);

        if (chainNode == null)
        {
          Logger.Warning($"Не найден узел дерева для цепочки {_pendingChainId}");
          return;
        }

        // Проверяем возможность активации цепочки
        if (!CanActivateChain(pulseCount))
        {
          Logger.Info($"Активация цепочки рефлексов {_pendingChainId} заблокирована (задержка или уже активна)");
          return;
        }

        if (_pendingChainBaseID != _activeCurBaseID || _pendingChainStyleID != _activeCurBaseStyleID)
        {
          Logger.Info($"Активация цепочки рефлексов {_pendingChainId} отменена: изменились условия " +
                     $"(base: {_pendingChainBaseID}->{_activeCurBaseID}, style: {_pendingChainStyleID}->{_activeCurBaseStyleID})");
          return;
        }

        var chain = _reflexChainsSystem.GetChain(_pendingChainId);
        if (chain == null || !chain.Links.Any())
        {
          Logger.Warning($"Цепочка {_pendingChainId} не найдена или не имеет звеньев");
          return;
        }

        var firstChainLink = chain.Links.OrderBy(l => l.ID).First();

        _lock.EnterWriteLock();
        try
        {
          _chainBaseID = _pendingChainBaseID;
          _chainStyleID = _pendingChainStyleID;
          _chainAlreadyActivatedInThisContext = true;
          AppGlobalState.IsReflexChainActive = true;

          _activeReflexChain = new ActiveReflexChain
          {
            ChainId = _pendingChainId,
            StartPulse = pulseCount,
            LastStepPulse = pulseCount,
            LastEvaluation = null,
            CurrentLinkId = firstChainLink.ID
          };

          _activeChainId = _pendingChainId;
          Logger.Info($"Отложенная цепочка рефлексов {_pendingChainId} активирована на пульсе {pulseCount}, " +
                     $"первое звено цепочки: {firstChainLink.ID}, действие: {firstChainLink.ActionId}");

          _researchLogger?.RegisterActiveChain(_pendingChainId, $"ReflexChain_{_pendingChainId}", "Reflex");
        }
        finally
        {
          _lock.ExitWriteLock();
        }

        ExecuteChainLink(pulseCount);
      }
      catch (Exception ex)
      {
        Logger.Error($"Ошибка активации отложенной цепочки {_pendingChainId}: {ex.Message}");
      }
    }

    private int _pendingChainId = 0;
    private int _pendingChainActivationPulse = 0;
    private int _pendingChainBaseID = 0;
    private int _pendingChainStyleID = 0;

    /// <summary>
    /// Проверяет и активирует цепочки после выполнения рефлексов
    /// </summary>
    private void CheckForChainActivation(int pulseCount, int activeReflexId, bool isConditionedReflex)
    {
      var detectedNodeId = AppGlobalState.DetectedReflexNodeId;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null) return;

      bool reflexMatchesNode = (isConditionedReflex && detectedNode.ConditionedReflex == activeReflexId) ||
                              (!isConditionedReflex && detectedNode.GeneticReflexID == activeReflexId);

      if (!reflexMatchesNode) return;

      // ID цепочки: из узла или для условного рефлекса — из исходного безусловного (узел мог быть создан без ReflexChainID)
      int chainId = detectedNode.ReflexChainID;
      if (chainId <= 0 && isConditionedReflex && activeReflexId > 0)
      {
        var conditionedReflex = _conditionedReflexes.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == activeReflexId);
        if (conditionedReflex != null && conditionedReflex.SourceGeneticReflexId > 0)
        {
          var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
              .FirstOrDefault(r => r.Id == conditionedReflex.SourceGeneticReflexId);
          if (geneticReflex != null && geneticReflex.ReflexChainID > 0)
            chainId = geneticReflex.ReflexChainID;
        }
      }

      if (chainId > 0)
      {
        _pendingChainId = chainId;
        _pendingChainActivationPulse = pulseCount + _reflexActionDuration;
        _pendingChainBaseID = _activeCurBaseID;
        _pendingChainStyleID = _activeCurBaseStyleID;
        Logger.Info($"Цепочка рефлексов {chainId} запланирована к активации через {_reflexActionDuration} пульсов (на пульсе {pulseCount + _reflexActionDuration})");
      }
    }

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Получает ID активной цепочки
    /// </summary>
    public int GetActiveChainId()
    {
      _lock.EnterReadLock();
      try
      {
        return _activeChainId;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Флаг активности цепочки рефлекса или автоматизма
    /// </summary>
    public bool IsChainActive => _isChainActive;

    /// <summary>
    /// Проверка возможности активации цепочки в текущем пульсе
    /// </summary>
    private bool CanActivateChain(int pulseCount)
    {   
      // Проверяем задержку после деактивации цепочки
      if (pulseCount <= _chainCooldownUntilPulse)
        return false;

      return !_isChainActive && !_chainAlreadyActivatedInThisContext;
    }

    /// <summary>
    /// Проверка возможности активации в текущем пульсе
    /// </summary>
    private bool CanActivate(int pulseCount, bool isSleeping)
    {
      return _activatedPulsCount != pulseCount && !isSleeping;
    }

    /// <summary>
    /// Обновление текущих состояний восприятия
    /// </summary>
    private void UpdateCurrentStates()
    {
      int oldBaseID = _activeCurBaseID;
      int oldStyleID = _activeCurBaseStyleID;

      // Базовое состояние гомеостаза
      var homeostasisState = _gomeostas.GetHomeostasisState();
      _activeCurBaseID = (int)homeostasisState.OverallState;

      var currentStyles = AppGlobalState.ActiveStyles;
      _activetStyleIds = currentStyles.Select(s => s.Id).ToList();

      // Образ сочетания базовых контекстов (стилей поведения)
      _activeCurBaseStyleID = _gomeostas.ActiveBehaviorStyleImageId;

      // Сбрасываем флаг, если изменились условия (не для цепочки)
      if (!_isChainActive && (oldBaseID != _activeCurBaseID || oldStyleID != _activeCurBaseStyleID))
        _chainAlreadyActivatedInThisContext = false;
    }

    /// <summary>
    /// Создание нового образа пусковых стимулов
    /// </summary>
    private void GetActiveTriggerStimulusImage()
    {
      _activeCurTriggerStimulusID = _influenceActions.ActiveCurTriggerStimulusID;
      _activeCurReflexTriggerStimulusID = _influenceActions.ActiveCurReflexTriggerStimulusID;
      _activeGlobalCurTriggerStimulusID = _activeCurReflexTriggerStimulusID;
    }

    #endregion

    #region Публичные методы для работы с цепочками

    /// <summary>
    /// Устанавливает результат выполнения текущего действия в цепочке
    /// </summary>
    /// <param name="success">true - действие успешно, false - неудачно</param>
    public void SetChainStepResult(bool success)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_activeReflexChain != null && _activeReflexChain.IsWaitingForResult)
        {
          // Установка оценки в активную цепочку
          if (_activeReflexChain.LastEvaluation != success)
          {
            Logger.Info($"Оценка звена {_activeReflexChain.CurrentLinkId} цепочки {_activeReflexChain.ChainId} изменена: " +
                       $"{_activeReflexChain.LastEvaluation?.ToString() ?? "null"} -> {success}");
          }
          _activeReflexChain.LastEvaluation = success;
        }
        else
          _lastStepSuccessResult = success;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получает текущий результат выполнения действия
    /// </summary>
    public bool GetChainStepResult()
    {
      _lock.EnterReadLock();
      try
      {
        if (_activeReflexChain != null && _activeReflexChain.LastEvaluation.HasValue)
          return _activeReflexChain.LastEvaluation.Value;
        return _lastStepSuccessResult;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет, есть ли активная цепочка, ожидающая результат выполнения
    /// </summary>
    public bool IsChainWaitingForResult()
    {
      _lock.EnterReadLock();
      try
      {
        return _activeReflexChain != null && _activeReflexChain.IsWaitingForResult;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Активация и выполнение рефлексов

    /// <summary>
    /// Получение текущего массива условий без учета триггера [baseID, styleID, 0]
    /// </summary>
    private int[] GetCurrentConditionsWithoutTrigger()
    {
      return new int[] { _activeCurBaseID, _activeCurBaseStyleID, 0 };
    }

    /// <summary>
    /// Получение текущего массива условий полного образа пускового триггера [baseID, styleID, actionID, PhraseID]
    /// </summary>
    private int[] GetCurrentConditionsArray()
    {
      return new int[] { _activeCurBaseID, _activeCurBaseStyleID, _activeCurTriggerStimulusID };
    }

    /// <summary>
    /// Получение текущего массива условий полного образа пускового триггера [baseID, styleID, actionID]
    /// </summary>
    private int[] GetCurrentGeneticConditionsArray()
    {
      return new int[] { _activeCurBaseID, _activeCurBaseStyleID, _activeCurReflexTriggerStimulusID };
    }

    /// <summary>
    /// Сбор рефлексов для выполнения (с учетом цепочек)
    /// </summary>
    private void CollectReflexesForExecution()
    {
      _geneticReflexesToRun.Clear();
      _conditionedReflexesToRun.Clear();
      AppGlobalState.FlgConditionReflexes = false;
      GetActionsForConditionReflexToRun(_conditionedReflexesToRun);
      GetActionsForGeneticReflexToRun(_geneticReflexesToRun);

      if (_isChainActive)
        return;

      var detectedNodeId = AppGlobalState.DetectedReflexNodeId;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null) return;

      // Цепочка б/у рефлекса — раннее завершение ТОЛЬКО когда нет условного стимула.
      // Если есть trigStimulusID > 0, сначала проверяем условные рефлексы
      // (exact → compound → hierarchical), и лишь при отсутствии — fallback на chain.
      if (detectedNode.IsChainNode && detectedNode.ConditionedReflex == 0
          && detectedNode.GeneticReflexID > 0 && _activeCurTriggerStimulusID <= 0)
      {
        _geneticReflexesToRun.Add(detectedNode.GeneticReflexID);
        GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        return;
      }

      if (_activeCurTriggerStimulusID > 0)
        CollectConditionedReflexes(detectedNode);

      // Компаунд-стимул (2+ модальности): суммация / смешанный ответ / конкурентное подавление.
      if (!_conditionedReflexesToRun.Any() && _activeCurTriggerStimulusID > 0)
        CollectCompoundConditionedReflexes();

      // Иерархический поиск (частичный стимул → более богатый триггер): сенсорная прекондиция и пр.
      if (!_conditionedReflexesToRun.Any() && _activeCurTriggerStimulusID > 0)
        CollectConditionedReflexesHierarchical();

      // Fallback: chain-node генетический рефлекс, если условные не найдены
      if (!_conditionedReflexesToRun.Any() && detectedNode.IsChainNode
          && detectedNode.ConditionedReflex == 0 && detectedNode.GeneticReflexID > 0)
      {
        _geneticReflexesToRun.Add(detectedNode.GeneticReflexID);
        GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        return;
      }

      if (_activeCurReflexTriggerStimulusID > 0)
      {
        CollectGeneticReflexesWithTriggers(detectedNode);
        if (!_geneticReflexesToRun.Any())
        {
          _geneticReflexesToRun.Add(-1); // рефлекс по умолчанию         
          GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        }
      }

      if (!_conditionedReflexesToRun.Any() && !_geneticReflexesToRun.Any())
        CollectReflexesWithoutTrigger(detectedNode);
    }

    /// <summary>
    /// Сбор условных рефлексов из найденного узла
    /// </summary>
    private void CollectConditionedReflexes(ReflexTreeSystem.ReflexNode node)
    {
      if (node.ConditionedReflex > 0)
      {
        var reflex = _conditionedReflexes.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == node.ConditionedReflex);
        if (reflex != null && reflex.CanBeActivated() &&
            IsReflexConditionsMet(reflex))
        {
          _conditionedReflexesToRun.Add(node.ConditionedReflex);
          AppGlobalState.FlgConditionReflexes = true;
        }
        else
          AppGlobalState.FlgConditionReflexes = false;

        GetActionsForConditionReflexToRun(_conditionedReflexesToRun);
      }
    }

    /// <summary>
    /// Сбор условных рефлексов при компаундном стимуле (суммация / конкурентное подавление).
    /// Вызывается, если точного совпадения не найдено, но компоненты составного
    /// стимула по отдельности совпадают с пусковыми стимулами существующих у-рефлексов.
    /// </summary>
    private void CollectCompoundConditionedReflexes()
    {
      var candidates = _conditionedReflexes.FindReflexesForCompoundStimulus(
          _activeCurBaseID, _activetStyleIds, _activeCurTriggerStimulusID);

      if (candidates.Count < 2)
        return;

      var resolution = _conditionedReflexes.ResolveCompoundActivation(candidates);

      if (resolution.ReflexesToActivate.Any())
      {
        foreach (var reflex in resolution.ReflexesToActivate)
          _conditionedReflexesToRun.Add(reflex.Id);

        AppGlobalState.FlgConditionReflexes = true;
        GetActionsForConditionReflexToRun(_conditionedReflexesToRun);

        Logger.Info($"Компаунд-активация: режим={resolution.Mode}, " +
                    $"рефлексов={resolution.ReflexesToActivate.Count}");
      }
    }

    /// <summary>
    /// Иерархия пусковых образов (полный / частичный стимул) и суммация по группам одного UR на уровне.
    /// </summary>
    private void CollectConditionedReflexesHierarchical()
    {
      var resolution = _conditionedReflexes.ResolveHierarchicalConditionedActivation(
          _activeCurBaseID, _activetStyleIds, _activeCurTriggerStimulusID);

      if (!resolution.ReflexesToActivate.Any())
        return;

      foreach (var reflex in resolution.ReflexesToActivate)
        _conditionedReflexesToRun.Add(reflex.Id);

      AppGlobalState.FlgConditionReflexes = true;
      GetActionsForConditionReflexToRun(_conditionedReflexesToRun);

      Logger.Info($"Иерархическая активация у-рефлексов: режим={resolution.Mode}, " +
                  $"n={resolution.ReflexesToActivate.Count}");
    }

    /// <summary>
    /// Сбор безусловных рефлексов (с пусковыми стимулами)
    /// </summary>
    private void CollectGeneticReflexesWithTriggers(ReflexTreeSystem.ReflexNode node)
    {
      if (node.GeneticReflexID > 0)
      {
        var reflex = _geneticReflexes.GetAllGeneticReflexes()
            .FirstOrDefault(r => r.Id == node.GeneticReflexID);

        if (reflex != null && IsReflexConditionsMet(reflex))
        {
          _geneticReflexesToRun.Add(node.GeneticReflexID);
          GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        }
      }
    }

    /// <summary>
    /// Сбор рефлексов, которые могут быть активированы только по состоянию и стилю (без триггера)
    /// </summary>
    private void CollectReflexesWithoutTrigger(ReflexTreeSystem.ReflexNode node)
    {
      if (node.GeneticReflexID > 0)
      {
        var reflex = _geneticReflexes.GetAllGeneticReflexes()
            .FirstOrDefault(r => r.Id == node.GeneticReflexID);

        if (reflex != null && IsReflexConditionsMet(reflex))
        {
          _geneticReflexesToRun.Add(node.GeneticReflexID);
          GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
        }
      }
    }

    /// <summary>
    /// Проверка условий для безусловного рефлекса (с поддержкой активации без триггера)
    /// </summary>
    private bool IsReflexConditionsMet(GeneticReflex reflex)
    {
      // Проверка Level1 - базовое состояние
      if (reflex.Level1 != _activeCurBaseID)
        return false;

      // Проверка Level2 - стили поведения
      if (reflex.Level2 != null && reflex.Level2.Any())
      {
        if (_activetStyleIds == null || !_activetStyleIds.Any())
          return false;

        // Точное совпадение всех элементов Level2 с текущими активными стилями
        if (!reflex.Level2.All(styleId => _activetStyleIds.Contains(styleId)) ||
            !_activetStyleIds.All(styleId => reflex.Level2.Contains(styleId)))
          return false;
      }

      // Проверка Level3 - пусковые стимулы
      if (reflex.Level3 != null && reflex.Level3.Any())
      {
        // Получаем текущие активные воздействия
        var currentTriggers = GetCurrentTriggerActionIDs();
        if (currentTriggers == null || !currentTriggers.Any())
          return false;

        // Точное совпадение всех элементов Level3 с текущими триггерами
        if (!reflex.Level3.All(trigger => currentTriggers.Contains(trigger)) ||
            !currentTriggers.All(trigger => reflex.Level3.Contains(trigger)))
          return false;
      }

      return true;
    }

    /// <summary>
    /// Проверка условий для условного рефлекса
    /// </summary>
    private bool IsReflexConditionsMet(ConditionedReflexesSystem.ConditionedReflex reflex)
    {
      // Проверка Level1 - базовое состояние
      if (reflex.Level1 != _activeCurBaseID)
        return false;

      if (_activetStyleIds == null || !_activetStyleIds.Any())
        return false;

      // Точное совпадение Level2 с текущими активными стилями
      if (!reflex.Level2.All(styleId => _activetStyleIds.Contains(styleId)) ||
          !_activetStyleIds.All(styleId => reflex.Level2.Contains(styleId)))
        return false;

      // Проверка Level3 - пусковой стимул
      if (reflex.Level3 != _activeCurTriggerStimulusID)
        return false;

      return true;
    }

    /// <summary>
    ///  Найти условный рефлекс по фразе
    /// </summary>
    public List<int> FindConditionedReflexesByPhrase(List<int> phraseIdList)
    {
      var result = new List<int>();
      var allReflexes = _conditionedReflexes.GetAllConditionedReflexes();
      var allImages = _perceptionImageSystem.GetAllPerceptionImagesList();

      foreach (var phraseId in phraseIdList)
      {
        var imageIdsWithPhrase = allImages
            .Where(img => img.PhraseIdList != null && img.PhraseIdList.Contains(phraseId))
            .Select(img => img.Id)
            .ToList();
        foreach (var reflex in allReflexes)
        {
          if (imageIdsWithPhrase.Contains(reflex.Level3))
            result.Add(reflex.Id);
        }
      }

      return result;
    }

    /// <summary>
    /// Получает текущие ID активных воздействий с пульта
    /// </summary>
    private int[] GetCurrentTriggerActionIDs()
    {
      // Всегда возвращаем активные воздействия, если они есть
      var activeActions = _influenceActions.GetActiveInfluenceActions();
      return activeActions?.Select(a => a.Id).ToArray() ?? new int[0];
    }

    #endregion

    #region Получение списков действий активируемых рефлексов

    /// <summary>
    /// Сохраняет список действий для безусловного рефлекса в глобальную переменную
    /// </summary>
    public void GetActionsForGeneticReflexToRun(List<int> reflexIdarr)
    {
      List<int> actionIdList = new List<int>();
      List<int> actIdList = new List<int>();

      foreach (int id in reflexIdarr)
      {
        actIdList = _reflexExecutionService.GetActionsForGeneticReflex(id);
        actionIdList.AddRange(actIdList);
      }

      int geneticReflexId = reflexIdarr != null && reflexIdarr.Count > 0 ? reflexIdarr[0] : 0;
      AppGlobalState.UpdateGlobalGeneticReflexesActions(actionIdList, geneticReflexId);
    }

    /// <summary>
    /// Сохраняет список действий для условного рефлекса в глобальную переменную
    /// </summary>
    public void GetActionsForConditionReflexToRun(List<int> reflexIdarr)
    {
      List<int> actionIdList = new List<int>();
      List<int> actIdList = new List<int>();

      foreach (int id in reflexIdarr)
      {
        actIdList = _reflexExecutionService.GetActionsForConditionedReflexFromSource(id);
        actionIdList.AddRange(actIdList);
      }

      AppGlobalState.UpdateGlobalConditionedReflexesActions(actionIdList);
    }

    #endregion

    #region Классы для управления цепочками рефлексов

    /// <summary>
    /// Активная цепочка рефлексов
    /// </summary>
    private class ActiveReflexChain
    {
      public int ChainId { get; set; }
      public int CurrentLinkId { get; set; }
      public int StartPulse { get; set; }
      public int LastStepPulse { get; set; }
      public List<int> CompletedActions { get; set; } = new List<int>();
      public bool IsWaitingForResult { get; set; }
      public bool OperatorEvaluated => LastEvaluation.HasValue;
      public bool? LastEvaluation { get; set; }
    }

    #endregion

    #region Активация и выполнение цепочек рефлексов

    // Единая активная цепочка
    private ActiveReflexChain _activeReflexChain = null;
    // Пульс завершения последней цепочки (для контроля задержки)
    private int pulseChainCompleted = 0;

    /// <summary>
    /// Выполняет звено цепочки рефлексов
    /// </summary>
    private void ExecuteChainLink(int pulseCount)
    {
      int chainId = 0;
      int linkId = 0;
      int actionId = 0;
      bool isFromConditionedReflex = false;
      bool hasStepToRun = false;

      _lock.EnterWriteLock();
      try
      {
        if (_activeReflexChain == null)
          return;

        var reflexChain = _reflexChainsSystem.GetChain(_activeReflexChain.ChainId);
        var currentLink = reflexChain?.Links.FirstOrDefault(l => l.ID == _activeReflexChain.CurrentLinkId);

        if (currentLink == null)
        {
          StopCurrentReflexChainCore(pulseCount);
          return;
        }

        chainId = _activeReflexChain.ChainId;
        linkId = currentLink.ID;
        actionId = currentLink.ActionId;

        var node = _reflexTree.FindNodeByID(AppGlobalState.DetectedReflexNodeId);
        isFromConditionedReflex = node?.ConditionedReflex > 0;
        hasStepToRun = true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (!hasStepToRun)
        return;

      var actionResult = _reflexExecutionService.ExecuteChainAction(
          actionId,
          isFromConditionedReflex);

      _lock.EnterWriteLock();
      try
      {
        if (_activeReflexChain == null
            || _activeReflexChain.ChainId != chainId
            || _activeReflexChain.CurrentLinkId != linkId)
        {
          return;
        }

        if (!actionResult.Success)
        {
          Logger.Warning($"Ошибка выполнения действия {actionId} из звена {linkId} цепочки {chainId}");
          StopCurrentReflexChainCore(pulseCount);
          return;
        }

        string chainInfo = $"{chainId}:{actionId}";
        SetChainInfoForCurrentPulse(pulseCount, "Reflex", chainInfo);

        _activeReflexChain.IsWaitingForResult = true;
        _activeReflexChain.LastStepPulse = pulseCount;
        _activeReflexChain.LastEvaluation = null;

        Logger.Info($"Выполнено действие {actionId} из звена {linkId} цепочки {chainId}, " +
                    $"ожидание оценки в течение {_reflexActionDuration} пульсов");

        _researchLogger?.LogChainLinkExecution(chainId, linkId, actionId, pulseCount);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private void SetChainInfoForCurrentPulse(int pulse, string chainType, string chainInfo)
    {
      try
      {
        // Используем рефлексию для вызова приватного метода ResearchLogger
        var method = typeof(ResearchLogger).GetMethod("SetChainInfoForCurrentPulse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        if (method != null && _researchLogger != null)
        {
          method.Invoke(_researchLogger, new object[] { pulse, chainType, chainInfo });
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"Error setting chain info: {ex.Message}");
      }
    }

    /// <summary>
    /// Устанавливает результат выполнения шага цепочки
    /// </summary>
    public bool SetChainStepResult(int chainId, bool success)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_activeReflexChain == null || _activeReflexChain.ChainId != chainId)
          return false;

        if (!_activeReflexChain.IsWaitingForResult)
          return false;

        _researchLogger?.LogChainEvaluation(chainId, _activeReflexChain.CurrentLinkId, success, _currentPulse);

        if (_activeReflexChain.LastEvaluation != success)
        {
          Logger.Info($"Оценка звена {_activeReflexChain.CurrentLinkId} цепочки {chainId} изменена: " +
                     $"{_activeReflexChain.LastEvaluation?.ToString() ?? "null"} -> {success}");
        }

        _activeReflexChain.LastEvaluation = success;
        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверка возможности продолжения цепочки
    /// </summary>
    private bool CanContinueChain()
    {
      if (_activeReflexChain == null)
        return false;

      if (_chainBaseID != _activeCurBaseID || _chainStyleID != _activeCurBaseStyleID)
        return false;

      if (_activeCurTriggerStimulusID != 0 || _activeCurReflexTriggerStimulusID != 0)
        return false;

      if (_gomeostas.HasCriticalChanges)
        return false;

      return true;
    }

    /// <summary>
    /// Проверка и выполнение активной цепочки в текущем пульсе
    /// </summary>
    private void ProcessActiveChain(int pulseCount)
    {
      bool shouldExecuteLink = false;

      _lock.EnterWriteLock();
      try
      {
        if (_activeReflexChain == null)
          return;

        if (AppGlobalState.IsNewConditions || !CanContinueChain())
        {
          StopCurrentReflexChainCore(pulseCount);
          Logger.Info($"Цепочка рефлексов завершена из-за смены условий");
          _chainAlreadyActivatedInThisContext = false;
          return;
        }

        var chain = _activeReflexChain;

        if (chain.IsWaitingForResult)
        {
          if (pulseCount >= chain.LastStepPulse + _reflexActionDuration)
          {
            bool finalEvaluation;

            if (chain.LastEvaluation.HasValue)
            {
              finalEvaluation = chain.LastEvaluation.Value;
              Logger.Info($"Время ожидания оценки истекло, используется последняя оценка: {finalEvaluation}");
            }
            else
            {
              finalEvaluation = true;
              Logger.Info($"Время ожидания оценки истекло, оценка не получена, используется успех=true по умолчанию");
            }

            var reflexChain = _reflexChainsSystem.GetChain(chain.ChainId);
            var currentLink = reflexChain?.Links.FirstOrDefault(l => l.ID == chain.CurrentLinkId);

            if (currentLink == null)
            {
              StopCurrentReflexChainCore(pulseCount);
              return;
            }

            int nextLinkId = finalEvaluation ? currentLink.SuccessNextLink : currentLink.FailureNextLink;

            if (nextLinkId == 0)
            {
              StopCurrentReflexChainCore(pulseCount);
              Logger.Info($"Цепочка рефлексов {chain.ChainId} успешно завершена. " +
                         $"Выполнено действий в цепочке: {chain.CompletedActions.Count}");

              _researchLogger?.LogChainCompletion(chain.ChainId, pulseCount,
                                                  chain.CompletedActions.Count, finalEvaluation);
            }
            else
            {
              string branchType = finalEvaluation ? "Success" : "Failure";
              _researchLogger?.LogChainBranchDecision(chain.ChainId, chain.CurrentLinkId,
                                                      finalEvaluation, nextLinkId, branchType, pulseCount);

              chain.CurrentLinkId = nextLinkId;
              chain.LastStepPulse = pulseCount;
              chain.IsWaitingForResult = false;
              chain.LastEvaluation = null;

              shouldExecuteLink = true;
            }
          }
        }
        else if (pulseCount >= chain.LastStepPulse + _reflexActionDuration)
        {
          shouldExecuteLink = true;
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      if (shouldExecuteLink)
        ExecuteChainLink(pulseCount);
    }

    /// <summary>
    /// Деактивация активной цепочки без захвата блокировки (вызывать под <see cref="_lock"/> или из Core-методов).
    /// </summary>
    private void DeactivateChainCore(int pulseCount)
    {
      if (_activeReflexChain != null)
      {
        _reflexTree.DeactivateChain(_activeReflexChain.ChainId);
        Logger.Info($"Цепочка рефлексов {_activeReflexChain.ChainId} остановлена");
        _activeReflexChain = null;
      }

      _activeChainId = 0;
      _chainBaseID = 0;
      _chainStyleID = 0;
      _chainAlreadyActivatedInThisContext = false;
      AppGlobalState.IsReflexChainActive = false;
      _completedReflexesInChain.Clear();
      _chainCooldownUntilPulse = GlobalTimer.GlobalPulsCount;
      pulseChainCompleted = 0;
    }

    /// <summary>
    /// Деактивация цепочки (останавливает только АКТИВНУЮ цепочку, НЕ трогает отложенную)
    /// </summary>
    private void DeactivateChain(int pulseCount)
    {
      _lock.EnterWriteLock();
      try
      {
        DeactivateChainCore(pulseCount);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Полная остановка всех цепочек рефлексов (активных и отложенных)
    /// </summary>
    public void StopAllReflexChains(int pulseCount)
    {
      _lock.EnterWriteLock();
      try
      {
        DeactivateChainCore(pulseCount);

        _pendingChainId = 0;
        _pendingChainActivationPulse = 0;
        _pendingChainBaseID = 0;
        _pendingChainStyleID = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Останавливает активную цепочку и сбрасывает отложенную активацию без захвата <see cref="_lock"/>.
    /// </summary>
    private void StopCurrentReflexChainCore(int pulseCount)
    {
      if (_activeReflexChain != null)
      {
        _reflexTree.DeactivateChain(_activeReflexChain.ChainId);
        Logger.Info($"Цепочка рефлексов {_activeReflexChain.ChainId} остановлена");
        _activeReflexChain = null;
      }

      _activeChainId = 0;
      _chainBaseID = 0;
      _chainStyleID = 0;
      _chainAlreadyActivatedInThisContext = false;
      AppGlobalState.IsReflexChainActive = false;
      _completedReflexesInChain.Clear();
      _chainCooldownUntilPulse = GlobalTimer.GlobalPulsCount;
      pulseChainCompleted = 0;

      _pendingChainId = 0;
      _pendingChainActivationPulse = 0;
      _pendingChainBaseID = 0;
      _pendingChainStyleID = 0;
    }

    #endregion

    #region Сброс и инициализация

    /// <summary>
    /// Сброс состояний
    /// </summary>
    public void ResetStates(int pulseCount)
    {
      _lock.EnterWriteLock();
      try
      {
        // НЕ вызываем DeactivateChain или StopCurrentReflexChain здесь!
        // ProcessReflexPulse уже управляет активной цепочкой.
        // ResetStates только сбрасывает текущие триггеры и рефлексы для текущего пульса.
        _activeCurTriggerStimulusID = 0;
        _activeCurReflexTriggerStimulusID = 0;
        _activeGlobalCurTriggerStimulusID = 0;
        _activeGeneticReflexID = 0;
        _activeConditionReflexID = 0;
        _activatedPulsCount = 0;
        _activatedFromPhrasePulse = 0;
        _activatedFromActionPulse = 0;
        _influenceActions.ActiveCurTriggerStimulusID = 0;
        _influenceActions.ActiveCurReflexTriggerStimulusID = 0;
        _lastReflexActivationPulse = 0;
        _geneticReflexesToRun.Clear();
        _conditionedReflexesToRun.Clear();
        AppGlobalState.FlgConditionReflexes = false;
        GetActionsForConditionReflexToRun(_conditionedReflexesToRun);
        GetActionsForGeneticReflexToRun(_geneticReflexesToRun);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом ReflexesActivator
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        // Отписываемся от событий
        if (_influenceActions != null)
        {
          _influenceActions.TriggerStimulusActivated -= OnTriggerStimulusActivated;
          _influenceActions.PhraseStimulusActivated -= OnPhraseStimulusActivated;
        }

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