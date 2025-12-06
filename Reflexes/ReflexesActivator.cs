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
        AdaptiveActionsSystem adaptiveActions)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexesActivator уже инициализирован.");

      _instance = new ReflexesActivator(gomeostas, geneticReflexes, conditionedReflexes, influenceActions, reflexTree, reflexChainsSystem, reflexExecution, adaptiveActions);
    }

    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private readonly InfluenceActionSystem _influenceActions;
    private readonly ReflexTreeSystem _reflexTree;
    private readonly ReflexExecutionService _reflexExecutionService;
    private readonly AdaptiveActionsSystem _adaptiveActions;
    private readonly ReflexChainsSystem _reflexChainsSystem;

    private ReflexesActivator(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        InfluenceActionSystem influenceActions,
        ReflexTreeSystem reflexTree,
        ReflexChainsSystem reflexChainsSystem,
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
      _influenceActions = influenceActions ?? throw new ArgumentNullException(nameof(influenceActions));
      _reflexTree = reflexTree ?? throw new ArgumentNullException(nameof(reflexTree));
      _reflexChainsSystem = reflexChainsSystem ?? throw new ArgumentNullException(nameof(reflexChainsSystem));
      _reflexExecutionService = reflexExecution ?? throw new ArgumentNullException(nameof(reflexExecution));
      _adaptiveActions = adaptiveActions ?? throw new ArgumentNullException(nameof(adaptiveActions));

      _reflexActionDuration = _adaptiveActions.ReflexActionDisplayDuration;

      _influenceActions.TriggerStimulusActivated += OnTriggerStimulusActivated;
      _influenceActions.PhraseStimulusActivated += OnPhraseStimulusActivated;

      ResetStates();
    }
    private void OnTriggerStimulusActivated(int pulseCount)
    {
      ActiveFromAction(pulseCount);
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

    /// <summary>
    /// Типы активации дерева рефлексов
    /// </summary>
    public enum ActivationType
    {
      /// <summary>
      /// Изменение сочетания базовых контекстов
      /// </summary>
      ConditionChange = 1,

      /// <summary>
      /// Действия с Пульта
      /// </summary>
      Action = 2,

      /// <summary>
      /// Фраза с Пульта
      /// </summary>
      Phrase = 3
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

    // Текущие условия запуска цепочки
    private int _chainBaseID = 0;
    private int _chainStyleID = 0;
    private int _chainActionID = 0;

    // Флаг активной цепочки
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
    /// Главный метод обработки пульса - вызывается из GlobalTimer.ProcessAgentPulse()
    /// </summary>
    public void ProcessReflexPulse(int pulseCount, bool isSleeping)
    {
      _isSleeping = isSleeping;

      ProcessActiveChain(pulseCount);

      //if (!CanActivate(pulseCount, isSleeping)) return;

      //if (_weitPulceCount == 0)
      //{
      //  // Только активация по изменению контекстов
      //  if (_gomeostas.IsNewConditions)
      //    ActiveFromConditionChange(pulseCount);
      //}
      //else
      //{
      //  if (pulseCount > _weitPulceCount + _reflexActionDuration)
      //    ActiveFromConditionChange(pulseCount);
      //}

      CleanupOldTriggers(pulseCount);
    }

    /// <summary>
    /// Активация при действиях с Пульта
    /// </summary>  
    private void ActiveFromAction(int pulseCount)
    {
      if (!CanActivate(pulseCount, _isSleeping)) return;

      try
      {
        _activatedPulsCount = pulseCount;
        _weitPulceCount = pulseCount;
        UpdateCurrentStates(ActivationType.Action);
        GetActiveTriggerStimulusImage();
        var conditions = GetCurrentGeneticConditionsArray();
        _reflexTree.ConditionsDetection(conditions);

        bool psychicBlocked = false;
        if (psychicBlocked)
        {
          LogInfo("Рефлекс заблокирован психикой");
          return;
        }
        _adaptiveActions.ClearActiveAction();
        ExecuteReflexes();

        if(_activeGeneticReflexID != 0)
        {
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
        UpdateCurrentStates(ActivationType.Phrase);
        GetActiveTriggerStimulusImage();
        var conditions = GetCurrentConditionsArray();
        _reflexTree.ConditionsDetection(conditions);

        bool psychicBlocked = false;
        if (psychicBlocked)
        {
          LogInfo("Рефлекс заблокирован психикой");
          return;
        }
        _adaptiveActions.ClearActivePhrases();
        ExecuteReflexes();

        if(_activeConditionReflexID != 0)
        {
          _researchLogger.LogSystemState(pulseCount);
          // чтобы не попало в логи на следующем пульсе
          _activeGlobalCurTriggerStimulusID = 0;
          _activeConditionReflexID = 0;
        }
      }
      finally
      {

      }
    }

    // Выполнение рефлексов
    // обнуление ID рефлексов и триггеров через ResetStates() в ProcessAgentPulse()
    private void ExecuteReflexes()
    {
      CollectReflexesForExecution();
      try
      {
        // Проверяем, нужно ли запускать цепочку
        var detectedNodeId = _reflexTree.DetectedLastNodeID;
        if (detectedNodeId > 0)
        {
          var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
          if (detectedNode != null && detectedNode.IsChainNode)
          {
            // Запускаем цепочку, начиная с текущего рефлекса
            ExecuteChainFromReflex(detectedNode);
            return; // Цепочка запущена, одиночные рефлексы не выполняем
          }
        }

        // Если цепочки нет, выполняем одиночные рефлексы
        if (_conditionedReflexesToRun.Any())
        {
          foreach (var reflexId in _conditionedReflexesToRun)
          {
            var result = _reflexExecutionService.ExecuteConditionedReflex(reflexId);
            if (result.Success)
            {
              _activeConditionReflexID = reflexId;
              _activeGlobalCurTriggerStimulusID = _activeCurTriggerStimulusID;
            }
          }
        }
        else if (_geneticReflexesToRun.Any())
        {
          foreach (var reflexId in _geneticReflexesToRun)
          {
            var result = _reflexExecutionService.ExecuteGeneticReflex(reflexId);
            if (result.Success)
            {
              _activeGeneticReflexID = reflexId;
            }
          }
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка запуска рефлекса: {ex.Message}");
      }
    }

    /// <summary>
    /// Запускает цепочку рефлексов, начиная с текущего рефлекса
    /// </summary>
    private void ExecuteChainFromReflex(ReflexTreeSystem.ReflexNode node)
    {
      try
      {
        int chainId = node.ReflexChainID;
        int startReflexId = node.ActiveReflex; // Может быть как условным, так и безусловным
        bool isConditioned = node.ConditionedReflex > 0;

        if (chainId <= 0 || startReflexId <= 0)
          return;

        // Получаем цепочку
        var chain = _reflexChainsSystem.GetChain(chainId);
        if (chain == null || !chain.Links.Any())
          return;

        // Получаем действие для стартового рефлекса
        int startActionId = _reflexExecutionService.GetActionIdForReflex(startReflexId, isConditioned);
        if (startActionId <= 0)
        {
          LogError($"Не найдено действие для рефлекса {startReflexId}");
          return;
        }

        // Ищем звено, соответствующее стартовому действию
        var startLink = chain.Links.FirstOrDefault(link => startActionId == link.ActionId);

        if (startLink == null)
        {
          LogError($"Не найдено звено для действия {startActionId} в цепочке {chainId}");
          return;
        }

        // Запоминаем условия запуска цепочки
        _chainBaseID = _activeCurBaseID;
        _chainStyleID = _activeCurBaseStyleID;
        _chainActionID = _activeCurReflexTriggerStimulusID;

        // Сначала выполняем стартовый рефлекс через сервис
        if (isConditioned)
        {
          var result = _reflexExecutionService.ExecuteConditionedReflex(node.ConditionedReflex);
          if (result.Success)
          {
            _activeConditionReflexID = node.ConditionedReflex;
            _activeGlobalCurTriggerStimulusID = _activeCurTriggerStimulusID;
          }
        }
        else
        {
          var result = _reflexExecutionService.ExecuteGeneticReflex(node.GeneticReflexID);
          if (result.Success)
          {
            _activeGeneticReflexID = node.GeneticReflexID;
          }
        }

        // Активируем цепочку для продолжения выполнения
        if (_reflexTree.ActivateChain(chainId, startLink.ID, GlobalTimer.GlobalPulsCount))
        {
          _activeChainId = chainId;
          _completedReflexesInChain.Add(startReflexId);

          LogInfo($"Цепочка {chainId} запущена от рефлекса {startReflexId}, стартовое звено: {startLink.ID}");
        }
        else
        {
          LogError($"Не удалось активировать цепочку {chainId}");
        }
      }
      catch (Exception ex)
      {
        LogError($"Ошибка запуска цепочки от рефлекса: {ex.Message}");
        DeactivateChain();
      }
    }

    #endregion

    #region Вспомогательные методы

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
    private void UpdateCurrentStates(ActivationType activationType)
    {
      // Базовое состояние гомеостаза
      var homeostasisState = _gomeostas.GetHomeostasisState();
      _activeCurBaseID = (int)homeostasisState.OverallState;

      // Образ сочетания базовых контекстов (стилей поведения)
      _activeCurBaseStyleID = _gomeostas.ActiveBehaviorStyleImageId;
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

    #region Активация и выполнение рефлексов

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

      // Если уже есть активная цепочка - ничего не делаем
      if (_isChainActive)
        return;

      var detectedNodeId = _reflexTree.DetectedLastNodeID;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null) return;

      // Проверяем, содержит ли узел цепочку
      if (detectedNode.IsChainNode)
      {
        // Цепочка будет обработана в ExecuteReflexes()
        // Собираем только стартовый рефлекс
        if (detectedNode.ConditionedReflex > 0)
        {
          _conditionedReflexesToRun.Add(detectedNode.ConditionedReflex);
        }
        else if (detectedNode.GeneticReflexID > 0)
        {
          _geneticReflexesToRun.Add(detectedNode.GeneticReflexID);
        }
        return;
      }

      // Если цепочки нет, собираем обычные рефлексы
      if (_activeCurTriggerStimulusID > 0)
        CollectConditionedReflexes(detectedNode);

      if (!_conditionedReflexesToRun.Any() && _activeCurReflexTriggerStimulusID > 0)
        CollectGeneticReflexesWithTriggers(detectedNode);
    }

    /// <summary>
    /// Сбор условных и безусловных одиночных рефлексов
    /// </summary>
    private void CollectSingleReflexes(ReflexTreeSystem.ReflexNode node)
    {
      if (_activeCurTriggerStimulusID > 0)
        CollectConditionedReflexes(node);

      if (!_conditionedReflexesToRun.Any() && _activeCurReflexTriggerStimulusID > 0)
        CollectGeneticReflexesWithTriggers(node);
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
        if (reflex != null && reflex.CanBeActivated() && IsReflexConditionsMet(reflex))
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
        {
          _geneticReflexesToRun.Add(node.GeneticReflexID);
        }
      }
    }

    /// <summary>
    /// Проверка условий для безусловного рефлекса (строгое совпадение)
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
    /// Проверка условий для условного рефлекса (строгое совпадение)
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

      // Проверка Level3 - пусковой стимул (образ восприятия)
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

    /// <summary>
    /// Выполнение цепочки рефлексов
    /// </summary>
    private void ExecuteChain(int chainId, int startLinkId)
    {
      try
      {
        // Запоминаем условия запуска цепочки
        _chainBaseID = _activeCurBaseID;
        _chainStyleID = _activeCurBaseStyleID;
        _chainActionID = _activeCurReflexTriggerStimulusID;

        // Активируем цепочку
        if (_reflexTree.ActivateChain(chainId, startLinkId, GlobalTimer.GlobalPulsCount))
        {
          _activeChainId = chainId;
          _completedReflexesInChain.Clear();

          // Выполняем первый шаг цепочки
          ExecuteChainStep(GlobalTimer.GlobalPulsCount);

          LogInfo($"Цепочка {chainId} активирована, стартовое звено: {startLinkId}");
        }
        else
        {
          LogError($"Не удалось активировать цепочку {chainId}");
        }
      }
      catch (Exception ex)
      {
        LogError($"Ошибка выполнения цепочки {chainId}: {ex.Message}");
        DeactivateChain();
      }
    }

    /// <summary>
    /// Выполнение шага активной цепочки
    /// </summary>
    private void ExecuteChainStep(int pulseCount)
    {
      if (!_isChainActive) return;

      // Проверяем, не изменились ли условия
      if (!CanContinueChain())
      {
        LogInfo($"Цепочка {_activeChainId} прервана - изменились условия");
        DeactivateChain();
        return;
      }

      // TODO: Заглушка - всегда считаем предыдущий шаг успешным
      // В реальности нужно получать подтверждение от оператора
      bool previousStepSuccess = true;

      // Проверяем ограничения циклических повторений
      var chain = _reflexChainsSystem.GetChain(_activeChainId);
      if (chain != null)
      {
        // Получаем текущее активное звено из дерева рефлексов
        var activeLinkId = _reflexTree.GetCurrentChainLink(_activeChainId);
        if (activeLinkId > 0)
        {
          var currentLink = chain.Links.FirstOrDefault(l => l.ID == activeLinkId);
          if (currentLink != null)
          {
            // Проверяем следующее звено на предмет циклического перехода
            int nextLinkId = previousStepSuccess ? currentLink.SuccessNextLink : currentLink.FailureNextLink;

            // Если следующее звено это сам звено (повтор) или предыдущее звено
            if (nextLinkId == currentLink.ID || (nextLinkId > 0 && nextLinkId < currentLink.ID))
            {
              // Проверяем, не превышено ли максимальное количество повторений
              if (_reflexChainsSystem.HasReachedMaxRepetitions(_activeChainId, currentLink.ID, nextLinkId))
              {
                LogInfo($"Цепочка {_activeChainId} прервана - достигнут лимит повторений звена {currentLink.ID}");
                DeactivateChain();
                return;
              }
            }
          }
        }
      }

      // Выполняем шаг цепочки
      var result = _reflexTree.ExecuteChainStep(_activeChainId, pulseCount, previousStepSuccess);

      if (!result.Success)
      {
        LogError($"Ошибка выполнения шага цепочки {_activeChainId}");
        DeactivateChain();
        return;
      }

      // Выполняем действие текущего звена
      if (result.ExecutedActionId > 0)
      {
        _reflexExecutionService.ExecuteAdaptiveAction(result.ExecutedActionId);
        _completedReflexesInChain.Add(result.ExecutedActionId);
      }

      // Если цепочка завершена
      if (result.ChainCompleted)
      {
        LogInfo($"Цепочка {_activeChainId} успешно завершена. " +
               $"Выполнено рефлексов: {_completedReflexesInChain.Count}");
        DeactivateChain();
      }
      // Если есть следующий шаг - продолжаем в следующем пульсе
      else if (result.NextLinkId > 0)
      {
        LogInfo($"Цепочка {_activeChainId} переходит к звену {result.NextLinkId}");
      }
    }

    /// <summary>
    /// Проверка возможности продолжения цепочки
    /// </summary>
    private bool CanContinueChain()
    {
      // Проверяем, что условия не изменились с момента запуска цепочки
      if (_chainBaseID != _activeCurBaseID ||
          _chainStyleID != _activeCurBaseStyleID ||
          _chainActionID != _activeCurReflexTriggerStimulusID)
      {
        return false;
      }

      // Проверяем, что нет критических изменений
      if (_gomeostas.HasCriticalChanges)
      {
        return false;
      }

      // Проверяем, что цепочка еще активна в дереве
      return _reflexTree.IsChainActive(_activeChainId);
    }

    /// <summary>
    /// Проверяет и выполняет активную цепочку в текущем пульсе
    /// </summary>
    private void ProcessActiveChain(int pulseCount)
    {
      if (!_isChainActive) return;

      // Проверяем, прошло ли достаточно времени с момента активации цепочки
      var activeChain = _reflexTree.GetActiveChains();
      if (activeChain.TryGetValue(_activeChainId, out var chain))
      {
        // Выполняем шаг каждые N пульсов (например, каждую секунду)
        if (pulseCount >= (chain.CurrentPulse + _reflexActionDuration))
        {
          // TODO: Заглушка - всегда считаем предыдущий шаг успешным
          bool previousStepSuccess = true; // TODO: заменить на реальную проверку

          // Проверяем ограничения циклических повторений
          var chainInfo = _reflexChainsSystem.GetChain(_activeChainId);
          if (chainInfo != null)
          {
            var currentLinkId = _reflexTree.GetCurrentChainLink(_activeChainId);
            if (currentLinkId > 0)
            {
              var currentLink = chainInfo.Links.FirstOrDefault(l => l.ID == currentLinkId);
              if (currentLink != null)
              {
                // Проверяем следующее звено на предмет циклического перехода
                int nextLinkId = previousStepSuccess ? currentLink.SuccessNextLink : currentLink.FailureNextLink;

                // Если следующее звено это сам звено (повтор) или предыдущее звено
                if (nextLinkId == currentLink.ID || (nextLinkId > 0 && nextLinkId < currentLink.ID))
                {
                  // Проверяем, не превышено ли максимальное количество повторений
                  if (_reflexChainsSystem.HasReachedMaxRepetitions(_activeChainId, currentLink.ID, nextLinkId))
                  {
                    LogInfo($"Цепочка {_activeChainId} прервана - достигнут лимит повторений звена {currentLink.ID}");
                    DeactivateChain();
                    return;
                  }
                }
              }
            }
          }

          var result = _reflexTree.ExecuteChainStep(_activeChainId, pulseCount, previousStepSuccess);

          if (!result.Success)
          {
            LogError($"Ошибка выполнения шага цепочки {_activeChainId}");
            DeactivateChain();
            return;
          }

          // Выполняем действие текущего звена (кроме стартового, которое уже выполнено)
          if (result.ExecutedActionId > 0 && !_completedReflexesInChain.Contains(result.ExecutedActionId))
          {
            var actionResult = _reflexExecutionService.ExecuteAdaptiveAction(result.ExecutedActionId);
            if (actionResult.Success)
              _completedReflexesInChain.Add(result.ExecutedActionId);
          }

          // Если цепочка завершена
          if (result.ChainCompleted)
          {
            LogInfo($"Цепочка {_activeChainId} успешно завершена. " +
                   $"Выполнено действий: {_completedReflexesInChain.Count}");
            DeactivateChain();
          }
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
        // Сбрасываем счетчики повторений при деактивации цепочки
        _reflexChainsSystem.ResetChainRepetitions(_activeChainId);

        _reflexTree.DeactivateChain(_activeChainId);
        LogInfo($"Цепочка {_activeChainId} деактивирована");
      }

      _activeChainId = 0;
      _chainBaseID = 0;
      _chainStyleID = 0;
      _chainActionID = 0;
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
        DeactivateChain();

        _activeCurBaseID = 0;
        _activeCurBaseStyleID = 0;
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
        _geneticReflexesToRun.Clear();
        _conditionedReflexesToRun.Clear();
        _completedReflexesInChain.Clear();
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