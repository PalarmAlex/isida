using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Сервис выполнения действий рефлексов
  /// </summary>
  public sealed class ReflexExecutionService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly InfluenceActionSystem _influenceActionSystem;
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private readonly ConditionedReflexesSystem _conditionedReflexesSystem;
    private bool _disposed = false;

    #region Инициализация

    private static ReflexExecutionService _instance;

    /// <summary>
    /// Глобальный экземпляр сервиса выполнения действий рефлексов
    /// </summary>
    public static ReflexExecutionService Instance => _instance ??
        throw new InvalidOperationException("ReflexExecutionService не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сервиса выполнения действий рефлексов
    /// </summary>
    public static void InitializeInstance(
        AdaptiveActionsSystem adaptiveActionsSystem,
        InfluenceActionSystem influenceActionSystem,
        GeneticReflexesSystem geneticReflexesSystem,
        ConditionedReflexesSystem conditionedReflexesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexExecutionService уже инициализирован.");

      _instance = new ReflexExecutionService(adaptiveActionsSystem, influenceActionSystem,
          geneticReflexesSystem, conditionedReflexesSystem);
    }

    private ReflexExecutionService(
        AdaptiveActionsSystem adaptiveActionsSystem,
        InfluenceActionSystem influenceActionSystem,
        GeneticReflexesSystem geneticReflexesSystem,
        ConditionedReflexesSystem conditionedReflexesSystem)
    {
      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _influenceActionSystem = influenceActionSystem ??
          throw new ArgumentNullException(nameof(influenceActionSystem));
      _geneticReflexesSystem = geneticReflexesSystem ??
          throw new ArgumentNullException(nameof(geneticReflexesSystem));
      _conditionedReflexesSystem = conditionedReflexesSystem ??
          throw new ArgumentNullException(nameof(conditionedReflexesSystem));
    }

    #endregion

    #region Выполнение рефлексов

    /// <summary>
    /// Выполняет безусловный рефлекс по его ID
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteGeneticReflex(int reflexId)
    {
      try
      {
        var actionIds = new List<int>();

        if (reflexId == -1) // рефлекс по умолчанию
          actionIds.Add(_adaptiveActionsSystem.DefaultAdaptiveActionId);
        else
        {
          var reflex = _geneticReflexesSystem.GetAllGeneticReflexesList()
                .FirstOrDefault(r => r.Id == reflexId);

          if (reflex == null)
            return (false, $"Безусловный рефлекс с ID {reflexId} не найден");

          actionIds = GetActionsForGeneticReflex(reflexId);
          if (actionIds == null || !actionIds.Any())
            return (false, $"Безусловный рефлекс {reflexId} не содержит действий");
        }

        return ExecuteAdaptiveActions(actionIds, ActionActivationSource.GeneticReflex);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения безусловного рефлекса {reflexId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполняет условный рефлекс по его ID
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteConditionedReflex(int conditionReflexId)
    {
      try
      {
        var conditionReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == conditionReflexId);

        if (conditionReflex == null)
          return (false, $"Условный рефлекс с ID {conditionReflexId} не найден");

        // Получаем действия из исходного безусловного рефлекса
        var actions = GetActionsForGeneticReflex(conditionReflex.SourceGeneticReflexId);
        if (!actions.Any())
          return (false, $"Исходный безусловный рефлекс {conditionReflex.SourceGeneticReflexId} не содержит связанных действий");

        // Выполняем действия рефлекса с указанием источника
        var result = ExecuteAdaptiveActions(actions, ActionActivationSource.ConditionedReflex);

        // Усиление ассоциации при успешном выполнении
        if (result.Success)
          _conditionedReflexesSystem.StrengthenAssociation(conditionReflexId);

        return result;
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения условного рефлекса {conditionReflexId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполняет последовательность адаптивных действий рефлекса с указанием источника
    /// </summary>
    /// <param name="actionIds">Список ID действий для выполнения</param>
    /// <param name="activationSource">Источник активации действий</param>
    /// <param name="phraseId">ID фразы (0 по умолчанию)</param>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveActions(
        List<int> actionIds,
        ActionActivationSource activationSource,
        int phraseId = 0)
    {
      if (actionIds == null || !actionIds.Any())
        return (false, "Нет действий для выполнения");

      var results = new List<string>();
      var successfulActions = new List<int>();

      foreach (var actionId in actionIds)
      {
        try
        {
          // Устанавливаем источник активации перед выполнением
          var action = _adaptiveActionsSystem.GetAllAdaptiveActions()
              .FirstOrDefault(a => a.Id == actionId);

          if (action != null)
          {
            action.ActivationSource = activationSource;
            action.ActivationPulse = GlobalTimer.GlobalPulsCount;
          }

          bool applied = _adaptiveActionsSystem.ApplyAction(actionId, phraseId);
          if (applied)
          {
            successfulActions.Add(actionId);
            results.Add($"Действие {actionId} выполнено успешно (Источник: {activationSource})");
          }
          else
            results.Add($"Действие {actionId} не может быть применено");
        }
        catch (Exception ex)
        {
          results.Add($"Ошибка выполнения действия {actionId}: {ex.Message}");
        }
      }

      // Обновляем образ сочетаний действий
      if (successfulActions.Any())
        UpdateActivedTerminalImage(successfulActions);

      string message = successfulActions.Any()
          ? $"Успешно выполнено {successfulActions.Count} из {actionIds.Count} действий: {string.Join("; ", results)}"
          : $"Ни одно действие не выполнено: {string.Join("; ", results)}";

      return (successfulActions.Any(), message);
    }

    /// <summary>
    /// Выполняет адаптивное действие по его ID с указанием источника
    /// </summary>
    /// <param name="actionId">ID действия</param>
    /// <param name="activationSource">Источник активации</param>
    /// <param name="phraseId">ID фразы (0 по умолчанию)</param>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveAction(
        int actionId,
        ActionActivationSource activationSource,
        int phraseId = 0)
    {
      return ExecuteAdaptiveActions(new List<int> { actionId }, activationSource, phraseId);
    }

    /// <summary>
    /// Выполняет действие цепочки рефлексов
    /// </summary>
    /// <param name="actionId">ID действия</param>
    /// <param name="isFromConditionedReflex">True если цепочка запущена от условного рефлекса</param>
    public (bool Success, string ErrorMessage) ExecuteChainAction(
        int actionId,
        bool isFromConditionedReflex = false)
    {
      // Определяем источник активации для цепочки
      var activationSource = isFromConditionedReflex
          ? ActionActivationSource.ConditionedReflex
          : ActionActivationSource.GeneticReflex;

      return ExecuteAdaptiveAction(actionId, activationSource);
    }

    #endregion

    #region Преобразование рефлексов в действия

    /// <summary>
    /// Получает список действий для безусловного рефлекса
    /// </summary>
    /// <param name="reflexId">ID безусловного рефлекса</param>
    /// <returns>Список ID адаптивных действий</returns>
    public List<int> GetActionsForGeneticReflex(int reflexId)
    {
      try
      {
        var reflex = _geneticReflexesSystem.GetAllGeneticReflexesList()
            .FirstOrDefault(r => r.Id == reflexId);

        if (reflex == null)
          return new List<int>();

        return reflex.AdaptiveActions?.ToList() ?? new List<int>();
      }
      catch
      {
        return new List<int>();
      }
    }

    /// <summary>
    /// Получает ID действия для любого рефлекса (безусловного или условного)
    /// </summary>
    /// <param name="reflexId">ID рефлекса</param>
    /// <param name="isConditioned">True для условного рефлекса, False для безусловного</param>
    /// <returns>ID действия или первый ID из списка действий</returns>
    public int GetActionIdForReflex(int reflexId, bool isConditioned = false)
    {
      if (isConditioned)
      {
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == reflexId);

        if (conditionedReflex == null || conditionedReflex.SourceGeneticReflexId <= 0)
          return 0;

        // Получаем действия из исходного безусловного рефлекса
        var actions = GetActionsForGeneticReflex(conditionedReflex.SourceGeneticReflexId);
        return actions.FirstOrDefault();
      }
      else
      {
        var actions = GetActionsForGeneticReflex(reflexId);
        return actions.FirstOrDefault();
      }
    }

    /// <summary>
    /// Получает список действий для любого рефлекса (безусловного или условного)
    /// </summary>
    /// <param name="reflexId">ID рефлекса</param>
    /// <param name="isConditioned">True для условного рефлекса, False для безусловного</param>
    /// <returns>Список ID адаптивных действий</returns>
    public List<int> GetActionsForReflex(int reflexId, bool isConditioned = false)
    {
      if (isConditioned)
      {
        // Для условных рефлексов получаем действия из ассоциированного безусловного рефлекса
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == reflexId);

        if (conditionedReflex == null || conditionedReflex.SourceGeneticReflexId <= 0)
          return new List<int>();

        return GetActionsForGeneticReflex(conditionedReflex.SourceGeneticReflexId);
      }
      else
      {
        return GetActionsForGeneticReflex(reflexId);
      }
    }

    /// <summary>
    /// Получает список действий для условного рефлекса из исходного безусловного рефлекса
    /// </summary>
    /// <param name="conditionedReflexId">ID условного рефлекса</param>
    /// <returns>Список ID адаптивных действий или пустой список</returns>
    public List<int> GetActionsForConditionedReflexFromSource(int conditionedReflexId)
    {
      try
      {
        var conditionedReflex = _conditionedReflexesSystem.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == conditionedReflexId);

        if (conditionedReflex == null || conditionedReflex.SourceGeneticReflexId <= 0)
          return new List<int>();

        // Получаем действия из исходного безусловного рефлекса
        return GetActionsForGeneticReflex(conditionedReflex.SourceGeneticReflexId);
      }
      catch
      {
        return new List<int>();
      }
    }

    #endregion

    #region Обработка состояний

    /// <summary>
    /// Обновляет образ сочетаний выполненных действий
    /// </summary>
    private void UpdateActivedTerminalImage(List<int> actionsIdArr)
    {
      var oldImage = ActivedTerminalImage?.ToList() ?? new List<int>();
      OldActivedTerminalImage = oldImage;

      ActivedTerminalImage = actionsIdArr.OrderBy(x => x).ToList();

      UpdateNewsConditions(0); // rank = 0 для безусловных рефлексов
    }

    /// <summary>
    /// Детектор новых условий
    /// </summary>
    private void UpdateNewsConditions(int rank)
    {
      // TODO: Реализовать логику обнаружения новых условий для создания условных рефлексов
      // Это будет использоваться для обучения и создания новых рефлексов высшего ранга
    }

    #endregion

    #region Свойства состояний

    /// <summary>
    /// Текущий образ сочетаний действий агента
    /// </summary>
    public List<int> ActivedTerminalImage { get; private set; } = new List<int>();

    /// <summary>
    /// Предыдущий образ сочетаний действий агента
    /// </summary>
    public List<int> OldActivedTerminalImage { get; private set; } = new List<int>();

    /// <summary>
    /// Флаг пробуждения
    /// </summary>
    public bool WakeUpping { get; private set; }

    #endregion

    #region Блокировка выполнения

    /// <summary>
    /// Проверяет, заблокированы ли моторные действия
    /// </summary>
    public bool IsBlockingMotorsAction()
    {
      // TODO: Интегрировать с психической системой когда она будет готова
      // bool notAllow1 = psychic.NotAllowReflexesAction();
      // if (notAllow1 || IsSlipping) return true;

      return false; // Временно разрешено
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AdaptiveActionsSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _lock?.Dispose();
      _disposed = true;
    }

    #endregion
  }
}