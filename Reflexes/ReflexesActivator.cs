using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Sensors;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Gomeostas.GomeostasSystem;
using static ISIDA.Reflexes.GeneticReflexesSystem;

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
        ConditionedReflexFormationService reflexFormationService)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexesActivator уже инициализирован.");

      _instance = new ReflexesActivator(gomeostas, geneticReflexes, conditionedReflexes, influenceActions, reflexTree, reflexChainsSystem, reflexExecution, adaptiveActions, reflexFormationService);
    }

    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private readonly InfluenceActionSystem _influenceActions;
    private readonly ReflexTreeSystem _reflexTree;
    private readonly ReflexExecutionService _reflexExecutionService;
    private readonly AdaptiveActionsSystem _adaptiveActions;
    private readonly ReflexChainsSystem _reflexChainsSystem;
    private readonly ConditionedReflexFormationService _reflexFormationService;

    private ReflexesActivator(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        InfluenceActionSystem influenceActions,
        ReflexTreeSystem reflexTree,
        ReflexChainsSystem reflexChainsSystem,
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions,
        ConditionedReflexFormationService reflexFormationService)
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

      _reflexActionDuration = _adaptiveActions.ReflexActionDisplayDuration;

      _influenceActions.TriggerStimulusActivated += OnTriggerStimulusActivated;
      _influenceActions.PhraseStimulusActivated += OnPhraseStimulusActivated;

      ResetStates();
    }
    private void OnTriggerStimulusActivated(int pulseCount, bool authoritativeMode)
    {
      ActiveFromAction(pulseCount, authoritativeMode);
    }

    private void OnPhraseStimulusActivated(int pulseCount)
    {
      ActiveFromPhrase(pulseCount);
    }

    /// <summary>
    /// Установка логгера
    /// </summary>
    public void SetResearchLogger(ResearchLogger logger)
    {
      _researchLogger = logger;
    }

    #endregion

    #region Константы и состояния

    private bool _lastStepSuccessResult = true;
    private int _chainCooldownUntilPulse = 0;

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

    // Текущее восприятие ID образов
    private int _activeCurBaseID = 0;                   // ID Базового состояния
    private int _activeCurBaseStyleID = 0;              // ID сочетания базовых контекстов
    private int _activeCurTriggerStimulusID = 0;        // ID текущего полного активного образа сочетаний пусковых стимулов
    private int _activeCurReflexTriggerStimulusID = 0;  // ID текущего частичного активного образа сочетаний пусковых стимулов
    private int _activeGlobalCurTriggerStimulusID = 0;  // ID триггера для логов 

    // Предыдущий образ сочетания пусковых стимулов (причина последующих событий)
    private int _oldActiveCurTriggerStimulusID = 0;
    private int _oldActiveCurTriggerStimulusPulsCount = 0;
    private int _activeConditionReflexID = 0;
    private int _activeGeneticReflexID = 0;

    // Защита от повторных срабатываний
    private int _activatedPulsCount = 0;
    private int _reflexActionDuration = 0;
    private int _weitPulceCount = 0;
    private bool _chainAlreadyActivatedInThisContext = false;
    private int _lastReflexActivationPulse = 0;

    // Текущие условия запуска цепочки
    private int _chainBaseID = 0;
    private int _chainStyleID = 0;

    /// <summary>
    /// Флаг активной цепочки
    /// </summary>
    private bool _isChainActive => _activeChainId > 0;
    private int _activeChainId = 0;

    // Список выполненных рефлексов в текущей цепочке
    private readonly List<int> _completedReflexesInChain = new List<int>();

    // Флаг сна
    private bool _isSleeping = false;

    // Списки рефлексов для выполнения
    private readonly List<int> _geneticReflexesToRun = new List<int>();
    private readonly List<int> _conditionedReflexesToRun = new List<int>();

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

      if (_gomeostas.IsNewConditions || (pulseChainCompleted !=0 && pulseCount > pulseChainCompleted + _reflexActionDuration))
        DeactivateChain();

      ProcessActiveChain(pulseCount);

      if (pulseCount > _chainCooldownUntilPulse)
        _chainCooldownUntilPulse = 0;

      // только если нет активной цепочки, проверяем новые условия
      if (!_isChainActive)
      {
        if (!CanActivate(pulseCount, isSleeping)) return;

        if (_weitPulceCount == 0)
        {
          if (_gomeostas.IsNewConditions)
            ActiveFromConditionChange(pulseCount);
        }
        else
        {
          if (pulseCount > _weitPulceCount + _reflexActionDuration)
            ActiveFromConditionChange(pulseCount);
        }
        CleanupOldTriggers(pulseCount);
      }
      ProcessConditionedReflexFormation(pulseCount);
    }

    /// <summary>
    /// Обработка формирования условных рефлексов на каждом пульсе
    /// </summary>
    private void ProcessConditionedReflexFormation(int pulseCount)
    {
      if (_isSleeping) return;
      if (_gomeostas.GetAgentState().EvolutionStage < 1) return;

      try
      {
        // Очистка устаревших рефлексов (раз в 100 пульсов)
        if (pulseCount % 100 == 0)
          _reflexFormationService.CleanupOldReflexes(pulseCount);
      }
      catch (Exception ex)
      {
        LogError($"Ошибка формирования условных рефлексов: {ex.Message}");
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

      bool psychicBlocked = false;
      if (psychicBlocked)
      {
        LogInfo("Рефлекс заблокирован психикой");
        return;
      }

      ExecuteReflexes(pulseCount);
    }

    /// <summary>
    /// Активация при действиях с Пульта
    /// </summary>  
    private void ActiveFromAction(int pulseCount, bool authoritativeMode = false)
    {
      if (!CanActivate(pulseCount, _isSleeping)) return;

      try
      {
        _activatedPulsCount = pulseCount;
        _weitPulceCount = pulseCount;
        UpdateCurrentStates();
        GetActiveTriggerStimulusImage();

        var conditions = GetCurrentGeneticConditionsArray();
        _reflexTree.ConditionsDetection(conditions);

        bool fullMatchFound = _reflexTree.DetectedLevel == 2;

        bool psychicBlocked = false;
        if (psychicBlocked)
        {
          LogInfo("Рефлекс заблокирован психикой");
          return;
        }

        _chainCooldownUntilPulse = 0;
        _adaptiveActions.ClearActiveAction();
        DeactivateChain();

        if (!fullMatchFound && _reflexTree.DetectedLastNodeID > 0)
        {
          _geneticReflexesToRun.Clear();
          _geneticReflexesToRun.Add(-1);
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
    private void ActiveFromPhrase(int pulseCount)
    {
      if (!CanActivate(pulseCount, _isSleeping)) return;

      try
      {
        _activatedPulsCount = pulseCount;
        _weitPulceCount = pulseCount;
        UpdateCurrentStates();
        GetActiveTriggerStimulusImage();
        var conditions = GetCurrentConditionsArray();
        _reflexTree.ConditionsDetection(conditions);

        bool psychicBlocked = false;
        if (psychicBlocked)
        {
          LogInfo("Рефлекс заблокирован психикой");
          return;
        }

        _chainCooldownUntilPulse = 0;
        _adaptiveActions.ClearActiveAction();
        _adaptiveActions.ClearActivePhrases();
        DeactivateChain();
        ExecuteReflexes(pulseCount);

        if (_activeCurTriggerStimulusID != 0)
        {
          // Сохраняем как условный стимул
          _reflexFormationService.RecordStimulus(
            pulseCount,
            _activeCurTriggerStimulusID,
            _activeCurBaseID,
            _activeCurBaseStyleID,
            0);
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
        LogError($"ActiveFromPhrase: {ex.Message}");
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
        // Пропускаем сбор рефлексов, сразу выполняем рефлекс по умолчанию
        var result = _reflexExecutionService.ExecuteGeneticReflex(-1);
        if (result.Success)
        {
          _activeGeneticReflexID = -1;
          _lastReflexActivationPulse = pulseCount;
        }
        return;
      }

      CollectReflexesForExecution();

      // если установлен рефлекс по умолчанию - запускаем его
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
        List<int> activatedGeneticReflexesFromConditioned = new List<int>();

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

                activatedGeneticReflexesFromConditioned.Add(conditionedReflex.SourceGeneticReflexId);
                LogInfo($"Pulse: {pulseCount}, Условный рефлекс {conditionedReflexId} активировал действие безусловного {conditionedReflex.SourceGeneticReflexId}");
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
            }
          }
        }

        // Проверяем цепочки после выполнения рефлексов
        CheckForChainActivation(pulseCount);

        // проверяем цепочки для безусловных рефлексов, активированных через условные
        if (activatedGeneticReflexesFromConditioned.Any())
          CheckChainsForGeneticReflexes(activatedGeneticReflexesFromConditioned, pulseCount);
      }
      catch (Exception ex)
      {
        LogError($"ExecuteReflexes: {ex.Message}");
      }
    }

    /// <summary>
    /// Проверяет цепочки для списка безусловных рефлексов
    /// </summary>
    private void CheckChainsForGeneticReflexes(List<int> geneticReflexIds, int pulseCount)
    {
      foreach (var geneticReflexId in geneticReflexIds)
      {
        if (geneticReflexId <= 0) continue;

        // Находим безусловный рефлекс
        var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
            .FirstOrDefault(r => r.Id == geneticReflexId);

        if (geneticReflex == null || geneticReflex.ReflexChainID <= 0)
          continue;

        // Ищем узел дерева с этой цепочкой
        var chainNode = FindNodeWithChain(geneticReflex.ReflexChainID);
        if (chainNode != null)
        {
          ExecuteChainFromReflex(chainNode, pulseCount);
        }
      }
    }

    /// <summary>
    /// Находит узел дерева с указанной цепочкой
    /// </summary>
    private ReflexTreeSystem.ReflexNode FindNodeWithChain(int chainId)
    {
      try
      {
        // Получаем все узлы дерева (нужно добавить метод в ReflexTreeSystem)
        var allNodes = GetAllReflexTreeNodes();
        return allNodes?.FirstOrDefault(n => n.ReflexChainID == chainId);
      }
      catch (Exception ex)
      {
        LogError($"FindNodeWithChain: {ex.Message}");
        return null;
      }
    }

    /// <summary>
    /// Получает все узлы дерева рефлексов
    /// </summary>
    private List<ReflexTreeSystem.ReflexNode> GetAllReflexTreeNodes()
    {
      try
      {
        // Если в ReflexTreeSystem есть метод GetAllNodes() - используем его
        // Иначе используем рефлексию или другие способы доступа
        return _reflexTree.GetAllNodes(); // Предполагаем, что такой метод существует
      }
      catch
      {
        // Альтернативный способ: через поиск по ID
        var nodes = new List<ReflexTreeSystem.ReflexNode>();
        for (int i = 1; i <= 1000; i++) // Максимальный ID узлов
        {
          var node = _reflexTree.FindNodeByID(i);
          if (node != null)
            nodes.Add(node);
        }
        return nodes;
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
          LogInfo($"Pulse: {pulseCount}, Активация цепочки заблокирована (задержка или уже активна)");
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

        _chainBaseID = _activeCurBaseID;
        _chainStyleID = _activeCurBaseStyleID;
        _chainAlreadyActivatedInThisContext = true;

        // Рефлекс уже выполнен в ExecuteReflexes()
        bool reflexExecuted = true;

        _gomeostas.Calculator.SetChainActive(true);

        if (reflexExecuted && _reflexTree.ActivateChain(chainId, firstChainLink.ID, GlobalTimer.GlobalPulsCount))
        {
          _activeChainId = chainId;
          LogInfo($"Pulse: {pulseCount}, Цепочка {chainId} активирована после рефлекса, " +
                 $"первое звено цепочки: {firstChainLink.ID}, действие: {firstChainLink.ActionId}");
        }
        else
          LogError($"Pulse: {pulseCount}, Не удалось активировать цепочку {chainId}");
      }
      catch (Exception ex)
      {
        LogError($"Pulse: {pulseCount}, Ошибка запуска цепочки: {ex.Message}");
        DeactivateChain();
      }
    }

    /// <summary>
    /// Проверяет и активирует цепочки после выполнения рефлексов
    /// </summary>
    private void CheckForChainActivation(int pulseCount)
    {
      var detectedNodeId = _reflexTree.DetectedLastNodeID;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null || !detectedNode.IsChainNode) return;

      // Определяем, какой рефлекс был активирован
      int activeReflexId = 0;
      bool isConditionedReflex = false;

      if (_activeConditionReflexID > 0)
      {
        activeReflexId = _activeConditionReflexID;
        isConditionedReflex = true;
      }
      else if (_activeGeneticReflexID > 0)
      {
        activeReflexId = _activeGeneticReflexID;
        isConditionedReflex = false;
      }

      if (activeReflexId <= 0) return;

      // Проверяем, соответствует ли активированный рефлекс узлу дерева
      bool reflexMatchesNode = (isConditionedReflex && detectedNode.ConditionedReflex == activeReflexId) ||
                              (!isConditionedReflex && detectedNode.GeneticReflexID == activeReflexId);

      if (reflexMatchesNode && detectedNode.ReflexChainID > 0)
        ExecuteChainFromReflex(detectedNode, pulseCount);
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
    /// Флаг активности цепочки
    /// </summary>
    public bool IsChainActive => _activeChainId > 0;

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

      // Сохраняем предыдущий полный образ как причину
      SetOldTriggerStimulusValue(_activeCurTriggerStimulusID);
    }

    /// <summary>
    /// Сохранение предыдущего образа пусковых стимулов
    /// </summary>
    private void SetOldTriggerStimulusValue(int value)
    {
      _oldActiveCurTriggerStimulusID = value;
      _oldActiveCurTriggerStimulusPulsCount = GlobalTimer.GlobalPulsCount;
    }

    /// <summary>
    /// Очистка устаревших причин
    /// </summary>
    private void CleanupOldTriggers(int currentPulse)
    {
      if (_oldActiveCurTriggerStimulusID > 0)
      {
        // Проверяем, прошло ли более 10 пульсов (секунд) с момента установки триггера
        if (currentPulse > (_oldActiveCurTriggerStimulusPulsCount + 10))
        {
          _oldActiveCurTriggerStimulusID = 0;
          _oldActiveCurTriggerStimulusPulsCount = 0;
        }
      }
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
        return _isChainActive &&
               _reflexTree.GetActiveChains().ContainsKey(_activeChainId) &&
               (GlobalTimer.GlobalPulsCount >= _reflexTree.GetCurrentChainPulse(_activeChainId) + _reflexActionDuration);
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
    /// Получение текущего массива условий частичного образа пускового триггера [baseID, styleID, actionID]
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

      if (_isChainActive)
        return;

      var detectedNodeId = _reflexTree.DetectedLastNodeID;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null) return;

      // только если это не условный рефлекс
      if (detectedNode.IsChainNode && detectedNode.ConditionedReflex == 0)
      {
        // Цепочка будет обработана в ExecuteReflexes()
        // Собираем только стартовый рефлекс
        if (detectedNode.GeneticReflexID > 0)
          _geneticReflexesToRun.Add(detectedNode.GeneticReflexID);

        return;
      }

      if (_activeCurTriggerStimulusID > 0)
        CollectConditionedReflexes(detectedNode);

      if (_activeCurReflexTriggerStimulusID > 0)
      {
        CollectGeneticReflexesWithTriggers(detectedNode);
        if (!_geneticReflexesToRun.Any())
          _geneticReflexesToRun.Add(-1); // рефлекс по умолчанию
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
          _conditionedReflexesToRun.Add(node.ConditionedReflex);
      }
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
          _geneticReflexesToRun.Add(node.GeneticReflexID);
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
          _geneticReflexesToRun.Add(node.GeneticReflexID);
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
        // Получаем текущие активные стили
        var currentStyles = _gomeostas.GetActiveStyles();
        var currentStyleIds = currentStyles.Select(s => s.Id).ToList();

        // Точное совпадение всех элементов Level2 с текущими активными стилями
        if (!reflex.Level2.All(styleId => currentStyleIds.Contains(styleId)) ||
            !currentStyleIds.All(styleId => reflex.Level2.Contains(styleId)))
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

      // Проверка Level2 - стили поведения
      var currentStyles = _gomeostas.GetActiveStyles();
      var currentStyleIds = currentStyles.Select(s => s.Id).ToList();

      if (currentStyleIds == null || !currentStyleIds.Any())
        return false;

      // Точное совпадение Level2 с текущими активными стилями
      if (!reflex.Level2.All(styleId => currentStyleIds.Contains(styleId)) ||
          !currentStyleIds.All(styleId => reflex.Level2.Contains(styleId)))
        return false;

      // Проверка Level3 - пусковой стимул
      if (reflex.Level3 != _activeCurTriggerStimulusID)
        return false;

      return true;
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

    #region Активация и выполнение цепочек рефлексов

    private int pulseChainCompleted = 0;

    /// <summary>
    /// Выполнение шага активной цепочки
    /// </summary>
    private void ExecuteChainStep(int pulseCount)
    {
      if (!_isChainActive) return;

      if (!CanContinueChain())
      {
        LogInfo($"Pulse: {pulseCount}, Цепочка {_activeChainId} прервана - изменились условия");
        DeactivateChain();
        return;
      }

      var activeChain = _reflexTree.GetActiveChains();
      if (!activeChain.TryGetValue(_activeChainId, out var chain))
      {
        DeactivateChain();
        return;
      }

      int timeSinceChainActivation = pulseCount - chain.StartPulse;

      if (timeSinceChainActivation < _reflexActionDuration)
        return;

      bool previousStepSuccess = GetStepResultFromConsole();

      var result = _reflexTree.ExecuteChainStep(_activeChainId, pulseCount, previousStepSuccess);

      if (!result.Success)
      {
        LogError($"Pulse: {pulseCount}, Ошибка выполнения шага цепочки {_activeChainId}");
        DeactivateChain();
        return;
      }

      if (result.ExecutedActionId > 0)
      {
        var node = _reflexTree.FindNodeByID(_reflexTree.DetectedLastNodeID);
        bool isFromConditionedReflex = node?.ConditionedReflex > 0;

        var actionResult = _reflexExecutionService.ExecuteChainAction(
            result.ExecutedActionId,
            isFromConditionedReflex);

        if (actionResult.Success)
        {
          _completedReflexesInChain.Add(result.ExecutedActionId);
          ResetStepResult();
          LogInfo($"Pulse: {pulseCount}, Выполнено действие {result.ExecutedActionId} из цепочки {_activeChainId}, " +
                 $"результат будет определен на следующем пульсе");
          pulseChainCompleted = pulseCount;
        }
      }

      if (result.ChainCompleted)
      {
        LogInfo($"Pulse: {pulseCount}, Цепочка {_activeChainId} успешно завершена. " +
               $"Выполнено действий в цепочке: {_completedReflexesInChain.Count}");
        // цепочка сбросится в ProcessReflexPulse() - нужно дать время завершить действие
        //DeactivateChain();
      }
    }

    /// <summary>
    /// Получает результат выполнения действия с пульта
    /// </summary>
    private bool GetStepResultFromConsole()
    {
      _lock.EnterReadLock();
      try
      {
        // В реальной реализации здесь будет опрос интерфейса пользователя
        // Пока используем значение из публичного свойства
        return _lastStepSuccessResult;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сбрасывает результат выполнения шага для следующего действия
    /// </summary>
    private void ResetStepResult()
    {
      _lock.EnterWriteLock();
      try
      {
        // Сбрасываем на значение по умолчанию (true)
        // Пользователь должен установить новое значение через интерфейс
        _lastStepSuccessResult = true;
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
      if (_chainBaseID != _activeCurBaseID || _chainStyleID != _activeCurBaseStyleID)
        return false;

      if (_activeCurTriggerStimulusID != 0 || _activeCurReflexTriggerStimulusID != 0)
        return false;

      if (_gomeostas.HasCriticalChanges)
        return false;

      return _reflexTree.IsChainActive(_activeChainId);
    }

    /// <summary>
    /// Проверяет и выполняет активную цепочку в текущем пульсе
    /// </summary>
    private void ProcessActiveChain(int pulseCount)
    {
      if (!_isChainActive) return;

      var activeChain = _reflexTree.GetActiveChains();
      if (activeChain.TryGetValue(_activeChainId, out var chain))
      {
        int requiredTime = _reflexActionDuration;

        if (pulseCount >= (chain.CurrentPulse + requiredTime))
        {
          _adaptiveActions.CleanupExpiredReflexActions();
          ExecuteChainStep(pulseCount);
        }
      }
    }

    /// <summary>
    /// Деактивация цепочки
    /// </summary>
    private void DeactivateChain()
    {
      if (_activeChainId > 0)
      {
        _reflexTree.DeactivateChain(_activeChainId);
        LogInfo($"Цепочка {_activeChainId} деактивирована");
        _chainCooldownUntilPulse = GlobalTimer.GlobalPulsCount + 1;
        pulseChainCompleted = 0;
      }

      _activeChainId = 0;
      _chainBaseID = 0;
      _chainStyleID = 0;
      _chainAlreadyActivatedInThisContext = false;
      _gomeostas.Calculator.SetChainActive(false);
      _completedReflexesInChain.Clear();
    }

    #endregion

    #region Сброс и инициализация

    /// <summary>
    /// Сброс состояний
    /// </summary>
    public void ResetStates()
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_isChainActive)
        {
          _activeCurBaseID = 0;
          _activeCurBaseStyleID = 0;
          DeactivateChain();
        }
        _activeCurTriggerStimulusID = 0;
        _activeCurReflexTriggerStimulusID = 0;
        _activeGlobalCurTriggerStimulusID = 0;
        _activeGeneticReflexID = 0;
        _activeConditionReflexID = 0;
        _oldActiveCurTriggerStimulusID = 0;
        _oldActiveCurTriggerStimulusPulsCount = 0;
        _activatedPulsCount = 0;
        _influenceActions.ActiveCurTriggerStimulusID = 0;
        _influenceActions.ActiveCurReflexTriggerStimulusID = 0;
        _chainAlreadyActivatedInThisContext = false;
        _lastReflexActivationPulse = 0;
        _geneticReflexesToRun.Clear();
        _conditionedReflexesToRun.Clear();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Вспомогательные методы

    private static void LogInfo(string message)
    {
      Debug.WriteLine($"[ReflexesActivator] INFO: {message}");
    }

    private static void LogError(string message)
    {
      FileValidator.LogError($"[ReflexesActivator] ERROR: {message}");
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AdaptiveActionsSystem
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
      }
    }

    #endregion

  }
}