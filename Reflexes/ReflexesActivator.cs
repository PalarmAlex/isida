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
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexesActivator уже инициализирован.");

      _instance = new ReflexesActivator(gomeostas, geneticReflexes, conditionedReflexes, influenceActions, reflexTree, reflexExecution, adaptiveActions);
    }

    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private readonly InfluenceActionSystem _influenceActions;
    private readonly ReflexTreeSystem _reflexTree;
    private readonly ReflexExecutionService _reflexExecutionService;
    private readonly AdaptiveActionsSystem _adaptiveActions;

    private ReflexesActivator(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        InfluenceActionSystem influenceActions,
        ReflexTreeSystem reflexTree,
        ReflexExecutionService reflexExecution,
        AdaptiveActionsSystem adaptiveActions)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
      _influenceActions = influenceActions ?? throw new ArgumentNullException(nameof(influenceActions));
      _reflexTree = reflexTree ?? throw new ArgumentNullException(nameof(reflexTree));
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

      if (!CanActivate(pulseCount, isSleeping)) return;

      if (_weitPulceCount == 0)
      {
        // Только активация по изменению контекстов
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

    /// <summary>
    /// Активация при изменении сочетания стилей реагирования
    /// </summary>
    private void ActiveFromConditionChange(int pulseCount)
    {
      if (_isSleeping) return;

      _activatedPulsCount = pulseCount;
      UpdateCurrentStates(ActivationType.ConditionChange);
      var conditions = GetCurrentGeneticConditionsArray();
      _reflexTree.ConditionsDetection(conditions);

      _weitPulceCount = 0;
      bool psychicBlocked = false;
      if (psychicBlocked)
      {
        LogInfo("Рефлекс заблокирован психикой");
        return;
      }

      ExecuteReflexes();
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
        if (_conditionedReflexesToRun.Any())
        {
          foreach (var reflexId in _conditionedReflexesToRun)
          {
            ExecuteConditionedReflex(reflexId);
            _activeConditionReflexID = reflexId;
            _activeGlobalCurTriggerStimulusID = _activeCurTriggerStimulusID;
          }
        }
        else if (_geneticReflexesToRun.Any())
        {
          foreach (var reflexId in _geneticReflexesToRun)
          {
            ExecuteGeneticReflex(reflexId);
            _activeGeneticReflexID = reflexId;
            _activeGlobalCurTriggerStimulusID = _activeCurReflexTriggerStimulusID;
          }
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка запуска рефлекса: {ex.Message}");
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
    /// Сбор рефлексов для выполнения с учетом иерархии типов рефлексов
    /// </summary>
    private void CollectReflexesForExecution()
    {
      _geneticReflexesToRun.Clear();
      _conditionedReflexesToRun.Clear();

      var detectedNodeId = _reflexTree.DetectedLastNodeID;
      if (detectedNodeId <= 0) return;

      var detectedNode = _reflexTree.FindNodeByID(detectedNodeId);
      if (detectedNode == null) return;

      // 1. Условные рефлексы (высший приоритет) - требуют совпадения всех 3 уровней
      if (_activeCurTriggerStimulusID > 0)
        CollectConditionedReflexes(detectedNode);

      // 2. Безусловные рефлексы (с пусковыми стимулами)
      if (!_conditionedReflexesToRun.Any() && _activeCurReflexTriggerStimulusID > 0)
        CollectGeneticReflexesWithTriggers(detectedNode);

      // 3. Безусловные рефлексы (без пусковых стимулов)
      if (!_conditionedReflexesToRun.Any() && !_geneticReflexesToRun.Any() && _activeCurReflexTriggerStimulusID == 0)
        CollectOldGeneticReflexes(detectedNode);
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
    /// Сбор безусловных рефлексов (без пусковых стимулов)
    /// </summary>
    private void CollectOldGeneticReflexes(ReflexTreeSystem.ReflexNode node)
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

    /// <summary>
    /// Выполнение условного рефлекса
    /// </summary>
    private void ExecuteConditionedReflex(int reflexId)
    {
      try
      {
        var reflex = _conditionedReflexes.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == reflexId);

        if (reflex != null)
        {
          // Усиление ассоциации при успешном выполнении
          _conditionedReflexes.StrengthenAssociation(reflexId);
        }
      }
      catch (Exception ex)
      {
        LogError($"Ошибка выполнения условного рефлекса {reflexId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполнение безусловного рефлекса
    /// </summary>
    private void ExecuteGeneticReflex(int reflexId)
    {
      try
      {
        var reflex = _geneticReflexes.GetAllGeneticReflexes()
            .FirstOrDefault(r => r.Id == reflexId);

        if (reflex != null)
        {
          // Выполняем адаптивные действия рефлекса
          if (reflex.AdaptiveActions?.Any() == true)
          {
            // Устанавливаем источник активации для каждого действия
            foreach (var actionId in reflex.AdaptiveActions)
            {
              var action = _adaptiveActions.GetAllAdaptiveActions()
                  .FirstOrDefault(a => a.Id == actionId);
              if (action != null)
                action.ActivationSource = ActionActivationSource.GeneticReflex;
            }
            var result = _reflexExecutionService.ExecuteAdaptiveActions(reflex.AdaptiveActions);
            if (!result.Success)
              LogError($"Ошибка выполнения безусловного рефлекса {reflexId}");
          }
        }
      }
      catch (Exception ex)
      {
        LogError($"Ошибка выполнения безусловного рефлекса {reflexId}: {ex.Message}");
      }
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