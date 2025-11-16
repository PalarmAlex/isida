using ISIDA.Actions;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

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
        InfluenceActionSystem influenceActionSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ReflexExecutionService уже инициализирован.");

      _instance = new ReflexExecutionService(adaptiveActionsSystem, influenceActionSystem);
    }

    private ReflexExecutionService(
        AdaptiveActionsSystem adaptiveActionsSystem,
        InfluenceActionSystem influenceActionSystem)
    {
      _adaptiveActionsSystem = adaptiveActionsSystem ??
          throw new ArgumentNullException(nameof(adaptiveActionsSystem));
      _influenceActionSystem = influenceActionSystem ??
          throw new ArgumentNullException(nameof(influenceActionSystem));
    }

    #endregion

    #region Выполнение рефлексов

    /// <summary>
    /// Выполняет последовательность адаптивных действий рефлекса
    /// </summary>
    public (bool Success, string ErrorMessage) ExecuteAdaptiveActions(List<int> actionIds, int phraseId = 0)
    {
      if (actionIds == null || !actionIds.Any())
        return (false, "Нет действий для выполнения");

      var results = new List<string>();
      var successfulActions = new List<int>();

      foreach (var actionId in actionIds)
      {
        try
        {
          bool applied = _adaptiveActionsSystem.ApplyAction(actionId, phraseId);
          if (applied)
          {
            successfulActions.Add(actionId);
            results.Add($"Действие {actionId} выполнено успешно");
          }
          else
            results.Add($"Действие {actionId} не может быть применено (возможно, недостаточно энергичности)");
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