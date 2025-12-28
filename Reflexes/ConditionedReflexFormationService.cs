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

    // Последний безусловный стимул
    private StimulusRecord _lastUnconditionedStimulus = null;

    // Последний условный стимул
    private StimulusRecord _lastConditionedStimulus = null;

    /// <summary>
    /// Добавление стимула в историю
    /// </summary>
    public void RecordStimulus(
        int pulse,
        int stimulusImageId,
        int baseState,
        int behaviorStyleImageId,
        List<int> associatedActions = null,
        int geneticReflexId = 0)
    {
      _lock.EnterWriteLock();
      try
      {
        var record = new StimulusRecord
        {
          Pulse = pulse,
          StimulusImageId = stimulusImageId,
          BaseState = baseState,
          BehaviorStyleImageId = behaviorStyleImageId,
          AssociatedActions = associatedActions?.ToList() ?? new List<int>(),
          GeneticReflexId = geneticReflexId
        };

        // Если это безусловный стимул (есть связанные действия или ID рефлекса), сохраняем его
        if ((associatedActions != null && associatedActions.Any()) || geneticReflexId > 0)
        {
          _lastUnconditionedStimulus = record;
          LogInfo($"Записан безусловный стимул ID={stimulusImageId} в пульс {pulse}, рефлекс={geneticReflexId}");
        }
        else
          // Сохраняем как условный стимул
          _lastConditionedStimulus = record;
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
        var recentStimuli = new List<StimulusRecord>();

        // Проверяем последний условный стимул
        if (_lastConditionedStimulus != null && (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindowPulses)
          recentStimuli.Add(_lastConditionedStimulus);

        // Проверяем последний безусловный стимул
        if (_lastUnconditionedStimulus != null && (currentPulse - _lastUnconditionedStimulus.Pulse) <= timeWindowPulses)
          recentStimuli.Add(_lastUnconditionedStimulus);

        return recentStimuli.OrderBy(r => r.Pulse).ToList();
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
        return;

      try
      {
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
      if (_lastUnconditionedStimulus == null || _lastConditionedStimulus == null)
        return;

      // Получаем настройки системы
      var settings = _conditionedReflexes.Settings;
      int timeWindow = settings.TimeWindowPulses;

      // Проверяем, находятся ли оба стимула в пределах временного окна
      bool unconditionedInWindow = (currentPulse - _lastUnconditionedStimulus.Pulse) <= timeWindow;
      bool conditionedInWindow = (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindow;

      if (unconditionedInWindow && conditionedInWindow)
      {
        // Проверяем, что условный стимул предшествовал безусловному
        if (_lastConditionedStimulus.Pulse < _lastUnconditionedStimulus.Pulse)
        {
          // Проверяем, можно ли создать/усилить условный рефлекс
          ProcessConditionedAssociation(
              _lastConditionedStimulus,
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
          List<int> reflexStyles = new List<int>();

          if (unconditionedStimulus.GeneticReflexId > 0)
          {
            var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
                .FirstOrDefault(r => r.Id == unconditionedStimulus.GeneticReflexId);

            if (geneticReflex != null)
            {
              adaptiveActions = geneticReflex.AdaptiveActions?.ToList() ?? new List<int>();
              reflexStyles = geneticReflex.Level2?.ToList() ?? new List<int>();
            }
          }
          else
          {
            adaptiveActions = unconditionedStimulus.AssociatedActions.ToList();
            reflexStyles = GetCurrentStyleIds();
          }

          var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
              level1: conditionedStimulus.BaseState,
              level2: reflexStyles,
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
      var perceptionImage = _perceptionImagesSystem
          .GetAllPerceptionImagesList()
          .FirstOrDefault(img => img.Id == stimulusImageId);

      if (perceptionImage == null)
        return new List<int>();

      var influenceActionsFromImage = perceptionImage.InfluenceActionsList ?? new List<int>();
      if (!influenceActionsFromImage.Any() && perceptionImage.PhraseIdList.Any())
        return new List<int>();

      var homeostasisState = _gomeostas.GetHomeostasisState();
      int currentBaseState = (int)homeostasisState.OverallState;
      var currentStyleIds = GetCurrentStyleIds();
      var actions = new List<int>();

      try
      {
        var allGeneticReflexes = _geneticReflexes.GetAllGeneticReflexesList();

        foreach (var reflex in allGeneticReflexes)
        {
          // Проверяем Level1 - базовое состояние гомеостаза
          if (reflex.Level1 != currentBaseState)
            continue;

          // Проверяем Level2 - стили поведения
          if (reflex.Level2 != null && reflex.Level2.Any())
          {
            // Проверяем точное совпадение наборов стилей
            // Сортируем для сравнения независимо от порядка
            var sortedReflexStyles = reflex.Level2.OrderBy(x => x).ToList();
            var sortedCurrentStyles = currentStyleIds.OrderBy(x => x).ToList();

            if (!sortedReflexStyles.SequenceEqual(sortedCurrentStyles))
              continue;
          }
          else if (currentStyleIds.Any())
          {
            // Если у рефлекса нет стилей, а сейчас есть активные стили - не подходит
            continue;
          }

          if (reflex.Level3 != null && reflex.Level3.Any())
          {
            // Для сопоставления нужны идентичные списки воздействий
            // Сравниваем отсортированные списки
            var sortedReflexActions = reflex.Level3.OrderBy(x => x).ToList();
            var sortedImageActions = influenceActionsFromImage.OrderBy(x => x).ToList();

            if (!sortedReflexActions.SequenceEqual(sortedImageActions))
              continue;
          }
          else if (influenceActionsFromImage.Any())
            continue;

          if (reflex.AdaptiveActions != null)
            actions.AddRange(reflex.AdaptiveActions);
        }
      }
      catch (Exception ex)
      {
        LogError($"[GetActionsForStimulus]. Ошибка: {ex.Message}");
      }

      return actions.Distinct().ToList();
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
          if (success)
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
        _lastUnconditionedStimulus = null;
        _lastConditionedStimulus = null;
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