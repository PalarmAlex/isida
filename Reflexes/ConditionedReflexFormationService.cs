using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Сервис формирования условных рефлексов на основе временных корреляций
  /// </summary>
  public sealed class ConditionedReflexFormationService : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GomeostasSystem _gomeostas;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private readonly PerceptionImagesSystem _perceptionImagesSystem;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private bool _disposed = false;

    #region Инициализация

    private static ConditionedReflexFormationService _instance;

    /// <summary>
    /// Глобальный экземпляр сервиса формирования условных рефлексов
    /// </summary>
    public static ConditionedReflexFormationService Instance => _instance ??
        throw new InvalidOperationException("ConditionedReflexFormationService не инициализирован.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр сервиса формирования условных рефлексов
    /// </summary>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        PerceptionImagesSystem perceptionImagesSystem,
        AdaptiveActionsSystem adaptiveActionsSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ConditionedReflexFormationService уже инициализирован.");

      _instance = new ConditionedReflexFormationService(
          gomeostas, geneticReflexes, conditionedReflexes,
          perceptionImagesSystem, adaptiveActionsSystem);
    }

    private ConditionedReflexFormationService(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes,
        PerceptionImagesSystem perceptionImagesSystem,
        AdaptiveActionsSystem adaptiveActionsSystem)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
      _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));
      _adaptiveActionsSystem = adaptiveActionsSystem ?? throw new ArgumentNullException(nameof(adaptiveActionsSystem));
    }

    #endregion

    #region История стимулов

    /// <summary>
    /// Запись о стимуле в истории
    /// </summary>
    public class StimulusRecord
    {
      /// <summary>
      /// Время стимула (пульс)
      /// </summary>
      public int Pulse { get; set; }

      /// <summary>
      /// ID образа восприятия (стимула)
      /// </summary>
      public int StimulusImageId { get; set; }

      /// <summary>
      /// Базовое состояние гомеостаза
      /// </summary>
      public int BaseState { get; set; }

      /// <summary>
      /// ID образа стилей поведения
      /// </summary>
      public int BehaviorStyleImageId { get; set; }

      /// <summary>
      /// Связанные действия (для безусловных рефлексов)
      /// </summary>
      public List<int> AssociatedActions { get; set; } = new List<int>();

      /// <summary>
      /// ID исходного безусловного рефлекса
      /// </summary>
      public int GeneticReflexId { get; set; }
    }

    // История стимулов (циклический буфер)
    private readonly List<StimulusRecord> _stimulusHistory = new List<StimulusRecord>();
    private const int MAX_HISTORY_SIZE = 100;

    // Последний безусловный стимул
    private StimulusRecord _lastUnconditionedStimulus = null;

    /// <summary>
    /// Добавляет стимул в историю
    /// </summary>
    public void RecordStimulus(int pulse, int stimulusImageId, int baseState,
        int behaviorStyleImageId, List<int> associatedActions = null)
    {
      _lock.EnterWriteLock();
      try
      {
        // Создаем новую запись
        var record = new StimulusRecord
        {
          Pulse = pulse,
          StimulusImageId = stimulusImageId,
          BaseState = baseState,
          BehaviorStyleImageId = behaviorStyleImageId,
          AssociatedActions = associatedActions?.ToList() ?? new List<int>()
        };

        // Добавляем в историю
        _stimulusHistory.Insert(0, record); // Новые записи в начало

        // Ограничиваем размер истории
        if (_stimulusHistory.Count > MAX_HISTORY_SIZE)
        {
          _stimulusHistory.RemoveRange(MAX_HISTORY_SIZE, _stimulusHistory.Count - MAX_HISTORY_SIZE);
        }

        // Если это безусловный стимул (есть связанные действия), сохраняем его
        if (associatedActions != null && associatedActions.Any())
        {
          _lastUnconditionedStimulus = record;
          LogInfo($"Записан безусловный стимул ID={stimulusImageId} в пульс {pulse}");
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получает последние стимулы в пределах временного окна
    /// </summary>
    public List<StimulusRecord> GetRecentStimuli(int currentPulse, int timeWindowPulses)
    {
      _lock.EnterReadLock();
      try
      {
        return _stimulusHistory
            .Where(r => (currentPulse - r.Pulse) <= timeWindowPulses)
            .OrderBy(r => r.Pulse)
            .ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Формирование условных рефлексов

    /// <summary>
    /// Обработка нового стимула и поиск корреляций
    /// </summary>
    public void ProcessStimulus(int pulse, int stimulusImageId)
    {
      if (_gomeostas.GetAgentState().EvolutionStage < 1)
        return; // Условные рефлексы только со стадии 1

      try
      {
        // Получаем текущие состояния
        var homeostasisState = _gomeostas.GetHomeostasisState();
        int baseState = (int)homeostasisState.OverallState;
        int behaviorStyleImageId = _gomeostas.ActiveBehaviorStyleImageId;

        // Получаем список адаптивных действий для текущего стимула
        var actions = GetActionsForStimulus(stimulusImageId);

        // Записываем стимул в историю
        RecordStimulus(pulse, stimulusImageId, baseState, behaviorStyleImageId, actions);

        // Проверяем корреляции с предыдущими стимулами
        CheckTemporalCorrelations(pulse);
      }
      catch (Exception ex)
      {
        LogError($"[ProcessStimulus]. Ошибка обработки стимула: {ex.Message}");
      }
    }

    /// <summary>
    /// Проверяет временные корреляции между стимулами
    /// </summary>
    private void CheckTemporalCorrelations(int currentPulse)
    {
      if (_lastUnconditionedStimulus == null)
        return;

      // Получаем настройки системы
      var settings = _conditionedReflexes.Settings;
      int timeWindow = settings.TimeWindowPulses;

      // Проверяем, находится ли последний безусловный стимул в пределах временного окна
      if ((currentPulse - _lastUnconditionedStimulus.Pulse) <= timeWindow)
      {
        // Получаем все условные стимулы в окне
        var conditionedStimuli = _stimulusHistory
            .Where(r => r != _lastUnconditionedStimulus &&
                        (currentPulse - r.Pulse) <= timeWindow &&
                        (!r.AssociatedActions.Any() || r.AssociatedActions.Count == 0))
            .OrderByDescending(r => r.Pulse)
            .ToList();

        foreach (var conditionedStimulus in conditionedStimuli)
        {
          // Проверяем, можно ли создать/усилить условный рефлекс
          ProcessConditionedAssociation(
              conditionedStimulus,
              _lastUnconditionedStimulus,
              currentPulse);
        }
      }

      // Применяем затухание ко всем условным рефлексам
      _conditionedReflexes.ApplyDecay();
    }

    /// <summary>
    /// Обрабатывает ассоциацию между условным и безусловным стимулом
    /// </summary>
    private void ProcessConditionedAssociation(
        StimulusRecord conditionedStimulus,
        StimulusRecord unconditionedStimulus,
        int currentPulse)
    {
      try
      {
        var existingReflexes = _conditionedReflexes.GetAllConditionedReflexes()
            .Where(r => r.Level3 == conditionedStimulus.StimulusImageId)
            .ToList();

        bool foundMatchingReflex = false;

        foreach (var existingReflex in existingReflexes)
        {
          if (existingReflex.Level1 == conditionedStimulus.BaseState &&
              existingReflex.Level2.OrderBy(x => x).SequenceEqual(GetCurrentStyleIds().OrderBy(x => x)))
          {
            _conditionedReflexes.StrengthenAssociation(existingReflex.Id);
            foundMatchingReflex = true;
            LogInfo($"Усилен условный рефлекс ID={existingReflex.Id}");
            break;
          }
        }

        if (!foundMatchingReflex)
        {
          List<int> adaptiveActions = new List<int>();

          if (unconditionedStimulus.GeneticReflexId > 0)
          {
            var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
                .FirstOrDefault(r => r.Id == unconditionedStimulus.GeneticReflexId);

            if (geneticReflex != null)
              adaptiveActions = geneticReflex.AdaptiveActions?.ToList() ?? new List<int>();
          }
          else
            adaptiveActions = unconditionedStimulus.AssociatedActions.ToList();

          var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
              level1: conditionedStimulus.BaseState,
              level2: GetCurrentStyleIds(),
              level3: conditionedStimulus.StimulusImageId,
              adaptiveActions: adaptiveActions,
              sourceGeneticReflexId: unconditionedStimulus.GeneticReflexId);

          if (newReflexId > 0)
            LogInfo($"Создан условный рефлекс ID={newReflexId} от безусловного {unconditionedStimulus.GeneticReflexId}");
        }
      }
      catch (Exception ex)
      {
        LogError($"[ProcessConditionedAssociation]. Ошибка: {ex.Message}");
      }
    }

    /// <summary>
    /// Получает ID текущих активных стилей поведения
    /// </summary>
    private List<int> GetCurrentStyleIds()
    {
      var currentStyles = _gomeostas.GetActiveStyles();
      return currentStyles.Select(s => s.Id).ToList();
    }

    /// <summary>
    /// Получает адаптивные действия для стимула
    /// </summary>
    private List<int> GetActionsForStimulus(int stimulusImageId)
    {
      var currentStyleIds = GetCurrentStyleIds();
      var actions = new List<int>();

      try
      {
        var allGeneticReflexes = _geneticReflexes.GetAllGeneticReflexesList();

        foreach (var reflex in allGeneticReflexes)
        {
          if (reflex.Level2 != null && reflex.Level2.Any())
          {
            if (!reflex.Level2.All(styleId => currentStyleIds.Contains(styleId)))
              continue;
          }

          if (reflex.Level3 != null && reflex.Level3.Contains(stimulusImageId))
          {
            if (reflex.AdaptiveActions != null)
              actions.AddRange(reflex.AdaptiveActions);
          }
        }
      }
      catch (Exception ex)
      {
        LogError($"[GetActionsForStimulus]. Ошибка: {ex.Message}");
      }

      return actions.Distinct().ToList();
    }

    /// <summary>
    /// Добавляет стимул с указанием конкретного безусловного рефлекса
    /// </summary>
    public void RecordStimulusWithReflex(int pulse, int stimulusImageId, int baseState,
        int behaviorStyleImageId, int geneticReflexId, List<int> associatedActions = null)
    {
      _lock.EnterWriteLock();
      try
      {
        // Создаем новую запись
        var record = new StimulusRecord
        {
          Pulse = pulse,
          StimulusImageId = stimulusImageId,
          BaseState = baseState,
          BehaviorStyleImageId = behaviorStyleImageId,
          AssociatedActions = associatedActions?.ToList() ?? new List<int>(),
          GeneticReflexId = geneticReflexId
        };

        // Добавляем в историю
        _stimulusHistory.Insert(0, record);

        // Ограничиваем размер истории
        if (_stimulusHistory.Count > MAX_HISTORY_SIZE)
        {
          _stimulusHistory.RemoveRange(MAX_HISTORY_SIZE, _stimulusHistory.Count - MAX_HISTORY_SIZE);
        }

        // Если есть связанные действия, сохраняем как безусловный стимул
        if (associatedActions != null && associatedActions.Any())
        {
          _lastUnconditionedStimulus = record;
          LogInfo($"Записан безусловный стимул ID={stimulusImageId} от рефлекса {geneticReflexId} в пульс {pulse}");
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Управление жизненным циклом рефлексов

    /// <summary>
    /// Проверяет и удаляет устаревшие рефлексы
    /// </summary>
    public void CleanupOldReflexes(int currentPulse)
    {
      try
      {
        var allReflexes = _conditionedReflexes.GetAllConditionedReflexes();
        var reflexesToRemove = new List<int>();

        foreach (var reflex in allReflexes)
        {
          if (reflex.ShouldBeRemoved(currentPulse))
          {
            reflexesToRemove.Add(reflex.Id);
          }
        }

        foreach (var reflexId in reflexesToRemove)
        {
          _conditionedReflexes.RemoveConditionedReflex(reflexId);
          var (success, errMsg) = _conditionedReflexes.SaveConditionedReflexes();
          if(success)
            LogInfo($"Удален устаревший условный рефлекс ID={reflexId}");
          else
            LogError($"[CleanupOldReflexes]. Не удалось обновить файл условных рефлексов: {errMsg}");
        }
      }
      catch (Exception ex)
      {
        LogError($"[CleanupOldReflexes]. Ошибка очистки рефлексов: {ex.Message}");
      }
    }

    /// <summary>
    /// Сбрасывает историю стимулов
    /// </summary>
    public void ResetHistory()
    {
      _lock.EnterWriteLock();
      try
      {
        _stimulusHistory.Clear();
        _lastUnconditionedStimulus = null;
        LogInfo("История стимулов сброшена");
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
      Debug.WriteLine($"[ConditionedReflexFormationService] INFO: {message}");
    }

    private static void LogError(string message)
    {
      FileValidator.LogError($"ERROR: {message}");
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом ConditionedReflexFormationService
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        _lock?.Dispose();
        ResetHistory();
      }
      finally
      {
        _disposed = true;
      }
    }

    #endregion
  }
}