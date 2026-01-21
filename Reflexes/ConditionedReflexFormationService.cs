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
        ConditionedReflexesSystem conditionedReflexes)
    {
      if (_instance != null)
        throw new InvalidOperationException("ConditionedReflexFormationService уже инициализирован.");

      _instance = new ConditionedReflexFormationService(gomeostas, geneticReflexes, conditionedReflexes);
    }

    private ConditionedReflexFormationService(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));
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
          GeneticReflexId = geneticReflexId
        };

        if (geneticReflexId > 0)
          _lastUnconditionedStimulus = record;
        else
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
    /// Проверяет временные корреляции между стимулами
    /// </summary>
    internal void CheckTemporalCorrelations(int currentPulse, bool authoritativeMode = false)
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
              currentPulse,
              authoritativeMode);
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
        int currentPulse,
        bool authoritativeMode = false)
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
            Logger.Info($"Усилен условный рефлекс ID={existingReflex.Id}");
            break;
          }
        }

        if (!foundMatchingReflex)
        {
          List<int> reflexStyles = new List<int>();

          if (unconditionedStimulus.GeneticReflexId > 0)
          {
            var geneticReflex = _geneticReflexes.GetAllGeneticReflexesList()
                .FirstOrDefault(r => r.Id == unconditionedStimulus.GeneticReflexId);

            if (geneticReflex != null)
              reflexStyles = geneticReflex.Level2?.ToList() ?? new List<int>();
          }
          else
          {
            // Если нет ID безусловного рефлекса, берем текущие стили
            reflexStyles = GetCurrentStyleIds();
          }

          var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
              level1: conditionedStimulus.BaseState,
              level2: reflexStyles,
              level3: conditionedStimulus.StimulusImageId,
              sourceGeneticReflexId: unconditionedStimulus.GeneticReflexId,
              authoritativeMod: authoritativeMode);

          if (newReflexId > 0)
            Logger.Info($"Создан условный рефлекс ID={newReflexId} от безусловного {unconditionedStimulus.GeneticReflexId}");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }

    /// <summary>
    /// Получает ID текущих активных стилей поведения
    /// </summary>
    private List<int> GetCurrentStyleIds()
    {
      var currentStyles = AppGlobalState.ActiveStyles;
      return currentStyles.Select(s => s.Id).ToList();
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
            reflexesToRemove.Add(reflex.Id);
        }

        foreach (var reflexId in reflexesToRemove)
        {
          _conditionedReflexes.RemoveConditionedReflex(reflexId);
          var (success, errMsg) = _conditionedReflexes.SaveConditionedReflexes();
          if (success)
            Logger.Info($"Удален устаревший условный рефлекс ID={reflexId}");
          else
            Logger.Warning($"{errMsg}");
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
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
      }
      finally
      {
        _lock.ExitWriteLock();
      }
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