using ISIDA.Actions;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using static ISIDA.Actions.AdaptiveActionsSystem;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Сервис выполнения действий автоматизмов
  /// </summary>
  public sealed class AutomatismExecutionService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private AutomatizmSystem _automatizmSystem;
    private PsychicSystem _psychicSystem;
    private OrientationReflexSystem _orientationReflexSystem;
    private bool _disposed = false;

    #region Инициализация

    private static AutomatismExecutionService _instance;

    /// <summary>
    /// Глобальный экземпляр сервиса выполнения действий автоматизмов
    /// </summary>
    public static AutomatismExecutionService Instance => _instance ??
        throw new InvalidOperationException("AutomatismExecutionService не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сервиса выполнения действий автоматизмов
    /// </summary>
    public static void InitializeInstance(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatismExecutionService уже инициализирован.");

      _instance = new AutomatismExecutionService(adaptiveActionsSystem, actionsImagesSystem);
    }

    /// <summary>
    /// Вторичная инициализация с зависимостями от систем психики
    /// </summary>
    public static void InitializeWithDependencies(
        AutomatizmSystem automatizmSystem,
        PsychicSystem psychicSystem,
        OrientationReflexSystem orientationReflexSystem)
    {
      if (_instance == null)
        throw new InvalidOperationException("AutomatismExecutionService должен быть сначала инициализирован через InitializeInstance()");

      _instance.SetDependencies(automatizmSystem, psychicSystem, orientationReflexSystem);
    }

    private AutomatismExecutionService(
        AdaptiveActionsSystem adaptiveActionsSystem,
        ActionsImagesSystem actionsImagesSystem)
    {
      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _actionsImagesSystem = actionsImagesSystem ??
          throw new ArgumentNullException(nameof(actionsImagesSystem));

      // Системы психики будут установлены позже через InitializeWithDependencies
      _automatizmSystem = null;
      _psychicSystem = null;
      _orientationReflexSystem = null;
    }

    /// <summary>
    /// Устанавливает зависимости от систем психики
    /// </summary>
    private void SetDependencies(
        AutomatizmSystem automatizmSystem,
        PsychicSystem psychicSystem,
        OrientationReflexSystem orientationReflexSystem)
    {
      _automatizmSystem = automatizmSystem ??
          throw new ArgumentNullException(nameof(automatizmSystem));
      _psychicSystem = psychicSystem ??
          throw new ArgumentNullException(nameof(psychicSystem));
      _orientationReflexSystem = orientationReflexSystem ??
          throw new ArgumentNullException(nameof(orientationReflexSystem));
    }

    /// <summary>
    /// Проверяет, установлены ли все зависимости
    /// </summary>
    public bool AreDependenciesSet =>
        _automatizmSystem != null &&
        _psychicSystem != null &&
        _orientationReflexSystem != null;

    #endregion

    #region Выполнение автоматизмов

    /// <summary>
    /// Выполняет автоматизм по его ID
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAutomatizm(int automatizmId)
    {
      try
      {
        if (!AreDependenciesSet)
          return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);

        if (automatizm == null)
          return (false, $"Автоматизм с ID {automatizmId} не найден");

        if (automatizm.Usefulness < 0)
          return (false, $"Автоматизм {automatizmId} имеет отрицательную полезность и не может быть выполнен");

        // Получаем действия из автоматизма
        var actionIds = GetActionsForAutomatizm(automatizmId);
        if (actionIds == null || !actionIds.Any())
          return (false, $"Автоматизм {automatizmId} не содержит связанных действий");

        return ExecuteAdaptiveActions(actionIds, ActionActivationSource.Automatizm, automatizmId);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения автоматизма {automatizmId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполняет автоматизм по ID узла дерева
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAutomatizmByNodeId(int nodeId)
    {
      try
      {
        if (!AreDependenciesSet)
          return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

        var automatizm = _psychicSystem.GetAutomatizmFromNode(nodeId);

        if (automatizm == null)
          return (false, $"Автоматизм для узла {nodeId} не найден");

        return ExecuteAutomatizm(automatizm.ID);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения автоматизма по узлу {nodeId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполняет автоматизм из дерева с учетом ориентировочного рефлекса
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAutomatizmWithOrientation(
        int automatizmId,
        int currentEmotionId,
        int actionsImageId)
    {
      try
      {
        if (!AreDependenciesSet)
          return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

        // Проверяем наличие ориентировочного рефлекса
        if (_orientationReflexSystem != null)
        {
          var orientedAutomatizm = _orientationReflexSystem.OrientationReflex(
              automatizmId,
              currentEmotionId,
              actionsImageId);

          if (orientedAutomatizm != null && orientedAutomatizm.ID != automatizmId)
          {
            // Ориентировочный рефлекс предложил другой автоматизм
            Logger.Info($"Ориентировочный рефлекс заменил автоматизм {automatizmId} на {orientedAutomatizm.ID}");
            automatizmId = orientedAutomatizm.ID;
          }
        }

        return ExecuteAutomatizm(automatizmId);
      }
      catch (Exception ex)
      {
        return (false, $"Ошибка выполнения автоматизма с ориентировочным рефлексом {automatizmId}: {ex.Message}");
      }
    }

    /// <summary>
    /// Выполняет последовательность адаптивных действий автоматизма
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveActions(
        List<int> actionIds,
        ActionActivationSource activationSource,
        int automatizmId = 0)
    {
      if (actionIds == null || !actionIds.Any())
        return (false, "Нет действий для выполнения");

      if (!AreDependenciesSet)
        return (false, "Зависимости AutomatismExecutionService не установлены. Вызовите InitializeWithDependencies()");

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

          bool applied = _adaptiveActionsSystem.ApplyAction(actionId);
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

      // Обновляем статистику автоматизма при успешном выполнении
      if (successfulActions.Any() && automatizmId > 0)
        UpdateAutomatizmStatistics(automatizmId, successfulActions.Count == actionIds.Count);

      string message = successfulActions.Any()
          ? $"Успешно выполнено {successfulActions.Count} из {actionIds.Count} действий: {string.Join("; ", results)}"
          : $"Ни одно действие не выполнено: {string.Join("; ", results)}";

      return (successfulActions.Any(), message);
    }

    #endregion

    #region Получение информации об автоматизмах

    /// <summary>
    /// Получает список действий для автоматизма
    /// </summary>
    public List<int> GetActionsForAutomatizm(int automatizmId)
    {
      try
      {
        if (!AreDependenciesSet)
          return new List<int>();

        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
        if (automatizm == null)
          return new List<int>();

        // Автоматизмы в данной архитектуре могут содержать действия через BranchID
        // BranchID > 1000000 - действие с пульта
        // BranchID > 2000000 - фраза
        // Здесь может потребоваться дополнительная логика в зависимости от реализации Automatizm

        // Временная заглушка - возвращаем пустой список
        // TODO: Реализовать получение действий из автоматизма
        return new List<int>();
      }
      catch
      {
        return new List<int>();
      }
    }

    /// <summary>
    /// Получает ID действия из автоматизма
    /// </summary>
    public int GetActionIdFromAutomatizm(int automatizmId)
    {
      var actions = GetActionsForAutomatizm(automatizmId);
      return actions.FirstOrDefault();
    }

    /// <summary>
    /// Получает автоматизм по ID действия
    /// </summary>
    public Automatizm GetAutomatizmForAction(int actionId)
    {
      try
      {
        if (!AreDependenciesSet)
          return null;

        // Ищем автоматизмы, связанные с данным действием
        // TODO: Реализовать поиск автоматизма по действию
        return null;
      }
      catch
      {
        return null;
      }
    }

    #endregion

    #region Обновление статистики

    /// <summary>
    /// Обновляет статистику выполнения автоматизма
    /// </summary>
    private void UpdateAutomatizmStatistics(int automatizmId, bool success)
    {
      try
      {
        var automatizm = _automatizmSystem.GetAutomatizmById(automatizmId);
        if (automatizm == null)
          return;

        // Увеличиваем счетчик использования
        automatizm.Count++;

        // Обновляем полезность в зависимости от успешности выполнения
        if (success && automatizm.Usefulness < 10)
          automatizm.Usefulness++;
        else if (!success && automatizm.Usefulness > -10)
          automatizm.Usefulness--;
      }
      catch (Exception ex)
      {
        Logger.Warning($"Ошибка обновления статистики автоматизма {automatizmId}: {ex.Message}");
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые сервисом выполнения автоматизмов
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
