using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Threading;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Common
{
  /// <summary>
  /// Сервис управления переключением между стадиями эволюции агента
  /// Обеспечивает корректную очистку данных при переходе между стадиями
  /// </summary>
  public sealed class EvolutionStageService : IDisposable
  {
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly ConditionedReflexesSystem _conditionedReflexesSystem;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;

    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Инициализация

    private static EvolutionStageService _instance;

    /// <summary>
    /// Глобальный экземпляр системы
    /// </summary>
    public static EvolutionStageService Instance => _instance ??
        throw new InvalidOperationException("EvolutionStageService не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы
    /// </summary>
    public static void InitializeInstance(
        AutomatizmSystem automatizmSystem,
        ConditionedReflexesSystem conditionedReflexesSystem,
        AutomatizmTreeSystem automatizmTreeSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("EvolutionStageService уже инициализирован.");

      _instance = new EvolutionStageService(
        automatizmSystem,
        conditionedReflexesSystem,
        automatizmTreeSystem);
    }

    private EvolutionStageService(
        AutomatizmSystem automatizmSystem,
        ConditionedReflexesSystem conditionedReflexesSystem,
        AutomatizmTreeSystem automatizmTreeSystem)
    {
      _automatizmSystem = automatizmSystem ?? throw new ArgumentNullException(nameof(automatizmSystem));
      _conditionedReflexesSystem = conditionedReflexesSystem ?? throw new ArgumentNullException(nameof(conditionedReflexesSystem));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
    }

    #endregion

    /// <summary>
    /// Переключает агента на указанную стадию эволюции с очисткой данных последующих стадий
    /// </summary>
    /// <param name="targetStage">Целевая стадия (0-5)</param>
    /// <param name="force">Принудительный переход (с подтверждением)</param>
    /// <param name="skipDataClearing">Пропустить очистку данных (только для тестирования)</param>
    /// <returns>Результат операции</returns>
    public EvolutionStageChangeResult ChangeEvolutionStage(int targetStage, bool force = false, bool skipDataClearing = false)
    {
      _lock.EnterWriteLock();
      try
      {
        // Проверка допустимости стадии
        if (targetStage < 0 || targetStage > 5)
        {
          return EvolutionStageChangeResult.CreateFailure(
              $"Недопустимая стадия: {targetStage}. Допустимые значения: 0-5");
        }

        int currentStage = AppGlobalState.EvolutionStage;
        Logger.Info($"Запрошен переход с стадии {currentStage} на стадию {targetStage}");

        // Проверка на попытку перепрыгнуть через стадию вперед
        if (targetStage > currentStage + 1)
        {
          if (!force)
          {
            return EvolutionStageChangeResult.CreateFailure(
                $"Недопустимый переход! Можно переходить только на следующую стадию (с {currentStage} на {currentStage + 1}).");
          }
          else
            Logger.Warning($"Принудительный переход через стадии: с {currentStage} на {targetStage}");
        }

        // Проверка на возврат на предыдущую стадию
        if (targetStage < currentStage && !force)
        {
          return EvolutionStageChangeResult.CreateConfirmationRequired(
              $"Внимание! Возврат на предыдущую стадию ({targetStage}) приведет к очистке данных всех последующих стадий (с {targetStage + 1} по 5). Продолжить?");
        }

        // Проверка остаемся ли на той же стадии
        if (targetStage == currentStage)
        {
          if (!force)
          {
            return EvolutionStageChangeResult.CreateFailure(
                "Агент уже находится на указанной стадии");
          }
          else
            Logger.Info($"Принудительная повторная установка стадии {targetStage}");
        }

        if (!skipDataClearing)
        {
          if (targetStage < currentStage)
            ClearSubsequentStagesData(targetStage);
          else if (targetStage > currentStage && targetStage > currentStage + 1 && force)
            ClearIntermediateStagesData(currentStage + 1, targetStage - 1);
        }
        AppGlobalState.EvolutionStage = targetStage;

        Logger.Info($"Успешный переход на стадию {targetStage}");
        return EvolutionStageChangeResult.CreateSuccess(
            $"Стадия успешно изменена с {currentStage} на {targetStage}",
            targetStage,
            currentStage);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return EvolutionStageChangeResult.CreateFailure(
            $"Ошибка при переключении стадии: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #region Методы очистки стадий

    /// <summary>
    /// Очистка данных последующих стадий
    /// </summary>
    private void ClearSubsequentStagesData(int currentStage)
    {
      Logger.Info($"Очистка данных стадий с {currentStage + 1} по 5");

      for (int stage = currentStage + 1; stage <= 5; stage++)
      {
        try
        {
          ClearStageData(stage);
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
          // Продолжаем очистку остальных стадий
        }
      }

      Logger.Info("Очистка данных последующих стадий завершена");
    }

    /// <summary>
    /// Очистка данных промежуточных стадий при принудительном переходе через стадии
    /// </summary>
    private void ClearIntermediateStagesData(int fromStage, int toStage)
    {
      Logger.Info($"Очистка промежуточных данных стадий с {fromStage} по {toStage}");

      for (int stage = fromStage; stage <= toStage; stage++)
      {
        try
        {
          ClearStageData(stage);
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
          // Продолжаем очистку остальных стадий
        }
      }

      Logger.Info("Очистка промежуточных данных завершена");
    }

    /// <summary>
    /// Очистка данных конкретной стадии
    /// </summary>
    private void ClearStageData(int stage)
    {
      Logger.DebugLog($"Очистка данных стадии {stage}");

      switch (stage)
      {
        case 1:
          ClearConditionedReflexes();
          break;

        case 2:
          ClearAllAutomatizm();
          _automatizmTreeSystem.ClearTree();
          break;

        case 3:
          break;

        case 4:
          break;

        case 5:
          break;

        default:
          Logger.Warning($"Очистка данных для неизвестной стадии: {stage}");
          break;
      }

      Logger.DebugLog($"Данные стадии {stage} очищены");
    }

    /// <summary>
    /// Очищает все условные рефлексы
    /// </summary>
    private void ClearConditionedReflexes()
    {
      try
      {
        if (_conditionedReflexesSystem != null && ConditionedReflexesSystem.IsInitialized)
        {
          bool originalRemoveFlag = _conditionedReflexesSystem.removeAllConditionedReflexes;
          _conditionedReflexesSystem.removeAllConditionedReflexes = true;
          _conditionedReflexesSystem.RemoveAllConditionedReflexes();
          _conditionedReflexesSystem.removeAllConditionedReflexes = originalRemoveFlag;

          var result = _conditionedReflexesSystem.SaveConditionedReflexes();
          if (!result.Success)
            Logger.Warning($"Не удалось обновить файл условных рефлексов после очистки: {result.ErrorMessage}");
          else
            Logger.Info("Условные рефлексы успешно очищены и сохранены");
        }
        else
          Logger.Info("Система условных рефлексов не инициализирована, очистка не требуется");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Очистка автоматизмов
    /// </summary>
    private void ClearAllAutomatizm()
    {
      // чистить дерево автоматизмов не надо - там нет ссылок на автоматизмы
      try
      {
        if (_automatizmSystem != null && AutomatizmSystem.IsInitialized)
        {
          _automatizmSystem.DeleteAllAutomatizm();
          var result = _automatizmSystem.SaveAutomatizm();
          if (!result.Success)
            Logger.Warning($"Не удалось обновить файл автоматизмов после очистки: {result.ErrorMessage}");
          else
            Logger.Info("Автоматизмы успешно очищены");
        }
        else
          Logger.Info("Система автоматизмов не инициализирована, очистка не требуется");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Получение информации о стадии

    /// <summary>
    /// Получает информацию о текущем состоянии переходов между стадиями
    /// </summary>
    public EvolutionStageInfo GetStageInfo()
    {
      _lock.EnterReadLock();
      try
      {
        int currentStage = AppGlobalState.EvolutionStage;

        return new EvolutionStageInfo
        {
          CurrentStage = currentStage,
          CanGoForward = currentStage < 5,
          CanGoBackward = currentStage > 0,
          NextStage = currentStage < 5 ? currentStage + 1 : (int?)null,
          PreviousStage = currentStage > 0 ? currentStage - 1 : (int?)null,
          SystemsInitialized = GetInitializedSystemsInfo(),
          StageDescription = GetStageDescription(currentStage)
        };
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает информацию об инициализированных системах
    /// </summary>
    private Dictionary<string, bool> GetInitializedSystemsInfo()
    {
      return new Dictionary<string, bool>
      {
        { "GomeostasSystem", GomeostasSystem.IsInitialized },
        { "ConditionedReflexesSystem", ConditionedReflexesSystem.IsInitialized },
      };
    }

    /// <summary>
    /// Получает описание стадии
    /// </summary>
    private string GetStageDescription(int stage)
    {
      switch (stage)
      {
        case 0: return "Стадия 0: Безусловные рефлексы";
        case 1: return "Стадия 1: Условные рефлексы";
        case 2: return "Стадия 2: Автоматизмы";
        case 3: return "Стадия 3: Эмоции";
        case 4: return "Стадия 4: Мышление";
        case 5: return "Стадия 5: Сознание";
        default: return "Неизвестная стадия";
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        _lock?.Dispose();
        _disposed = true;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion
  }

  /// <summary>
  /// Результат операции изменения стадии эволюции
  /// </summary>
  public class EvolutionStageChangeResult
  {
    /// <summary>
    /// Успешность операции
    /// </summary>
    public bool Success { get; private set; }

    /// <summary>
    /// Требуется ли подтверждение пользователя
    /// </summary>
    public bool RequiresConfirmation { get; private set; }

    /// <summary>
    /// Сообщение для пользователя
    /// </summary>
    public string Message { get; private set; }

    /// <summary>
    /// Новая стадия (если успешно)
    /// </summary>
    public int? NewStage { get; private set; }

    /// <summary>
    /// Предыдущая стадия
    /// </summary>
    public int? PreviousStage { get; private set; }

    private EvolutionStageChangeResult(bool success, bool requiresConfirmation, string message,
                                      int? newStage = null, int? previousStage = null)
    {
      Success = success;
      RequiresConfirmation = requiresConfirmation;
      Message = message;
      NewStage = newStage;
      PreviousStage = previousStage;
    }

    /// <summary>
    /// Создает успешный результат
    /// </summary>
    public static EvolutionStageChangeResult CreateSuccess(string message, int? newStage = null, int? previousStage = null)
    {
      return new EvolutionStageChangeResult(true, false, message, newStage, previousStage);
    }

    /// <summary>
    /// Создает результат с запросом подтверждения
    /// </summary>
    public static EvolutionStageChangeResult CreateConfirmationRequired(string message)
    {
      return new EvolutionStageChangeResult(false, true, message);
    }

    /// <summary>
    /// Создает результат с ошибкой
    /// </summary>
    public static EvolutionStageChangeResult CreateFailure(string message)
    {
      return new EvolutionStageChangeResult(false, false, message);
    }
  }

  /// <summary>
  /// Информация о текущем состоянии стадий эволюции
  /// </summary>
  public class EvolutionStageInfo
  {
    /// <summary>
    /// Текущая стадия (0-5)
    /// </summary>
    public int CurrentStage { get; set; }

    /// <summary>
    /// Можно ли перейти вперед
    /// </summary>
    public bool CanGoForward { get; set; }

    /// <summary>
    /// Можно ли перейти назад
    /// </summary>
    public bool CanGoBackward { get; set; }

    /// <summary>
    /// Следующая стадия (если есть)
    /// </summary>
    public int? NextStage { get; set; }

    /// <summary>
    /// Предыдущая стадия (если есть)
    /// </summary>
    public int? PreviousStage { get; set; }

    /// <summary>
    /// Информация об инициализированных системах
    /// </summary>
    public Dictionary<string, bool> SystemsInitialized { get; set; }

    /// <summary>
    /// Описание текущей стадии
    /// </summary>
    public string StageDescription { get; set; }

    /// <summary>
    /// Получает описание текущей стадии
    /// </summary>
    public string GetStageDescription()
    {
      return StageDescription;
    }
  }
}