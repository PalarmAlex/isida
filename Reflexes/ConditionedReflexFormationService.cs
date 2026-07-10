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
  /// Сервис формирования условных рефлексов на основе временных корреляций.
  /// Два разведённых контура обучения:
  /// <list type="bullet">
  /// <item><description>Сенсорная прекондиция — нейтральные пары CS1-CS2 в <see cref="SensoryAssociationSystem"/>,
  /// перенос по иерархии «бедный→богатый» при C ≥ γ.</description></item>
  /// <item><description>Вторичное обусловливание — CR порядка ≥2 на CS1 при активации CR на CS2
  /// (CS2 как обусловленный подкрепитель); только для пар вне отношения «бедный⊂богатый».</description></item>
  /// </list>
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

      /// <summary>
      /// ID тона пускового стимула (для условного стимула — фраза с пульта). 0 — нормальный.
      /// </summary>
      public int ToneId { get; set; }

      /// <summary>
      /// ID настроения пускового стимула (для условного стимула). 0 — нормальное.
      /// </summary>
      public int MoodId { get; set; }
    }

    // Последний безусловный стимул
    private StimulusRecord _lastUnconditionedStimulus = null;

    // Последний условный стимул
    private StimulusRecord _lastConditionedStimulus = null;

    /// <summary>
    /// Добавление стимула в историю
    /// </summary>
    /// <param name="pulse">Время стимула (пульс)</param>
    /// <param name="stimulusImageId">ID образа восприятия (стимула)</param>
    /// <param name="baseState">Базовое состояние гомеостаза</param>
    /// <param name="behaviorStyleImageId">ID образа стилей поведения</param>
    /// <param name="geneticReflexId">ID исходного безусловного рефлекса (0 для условного стимула)</param>
    /// <param name="toneId">ID тона (для условного стимула — фраза с пульта). 0 — по умолчанию.</param>
    /// <param name="moodId">ID настроения (для условного стимула). 0 — по умолчанию.</param>
    public void RecordStimulus(
        int pulse,
        int stimulusImageId,
        int baseState,
        int behaviorStyleImageId,
        int geneticReflexId = 0,
        int toneId = 0,
        int moodId = 0)
    {
      _lock.EnterWriteLock();
      try
      {
        if (geneticReflexId == 0)
          TryStrengthenNeutralSequentialLink(pulse, stimulusImageId);

        var record = new StimulusRecord
        {
          Pulse = pulse,
          StimulusImageId = stimulusImageId,
          BaseState = baseState,
          BehaviorStyleImageId = behaviorStyleImageId,
          GeneticReflexId = geneticReflexId,
          ToneId = toneId,
          MoodId = moodId
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
    /// Укрепляет CS₁→CS₂ при нейтральной последовательной паре в окне τ.
    /// </summary>
    private void TryStrengthenNeutralSequentialLink(int currentPulse, int currentImageId)
    {
      if (_lastConditionedStimulus == null || !SensoryAssociationSystem.IsInitialized)
        return;

      int prevPulse = _lastConditionedStimulus.Pulse;
      int prevImageId = _lastConditionedStimulus.StimulusImageId;
      int timeWindow = _conditionedReflexes.Settings.TimeWindowPulses;

      if (prevPulse >= currentPulse)
        return;

      if (currentPulse > prevPulse + timeWindow)
        return;

      if (prevImageId == currentImageId)
        return;

      if (_lastUnconditionedStimulus != null &&
          _lastUnconditionedStimulus.Pulse > prevPulse &&
          _lastUnconditionedStimulus.Pulse < currentPulse)
        return;

      SensoryAssociationSystem.Instance.StrengthenLink(prevImageId, currentImageId);
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

      // Применяем затухание ко всем условным рефлексам и сенсорным связям
      _conditionedReflexes.ApplyDecay();
      if (SensoryAssociationSystem.IsInitialized)
        SensoryAssociationSystem.Instance.ApplyDecay();
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
              existingReflex.Level2.OrderBy(x => x).SequenceEqual(GetCurrentStyleIds().OrderBy(x => x)) &&
              existingReflex.ToneId == conditionedStimulus.ToneId &&
              existingReflex.MoodId == conditionedStimulus.MoodId)
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
              authoritativeMod: authoritativeMode,
              toneId: conditionedStimulus.ToneId,
              moodId: conditionedStimulus.MoodId);

          if (newReflexId > 0)
            Logger.Info($"Создан условный рефлекс ID={newReflexId} от безусловного {unconditionedStimulus.GeneticReflexId}");
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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

    /// <summary>
    /// Проверяет возможность формирования вторичного условного рефлекса.
    /// Вызывается когда стимул активировал существующий у-рефлекс (CS₂ как обусловленный подкрепитель).
    /// Если перед ним (в окне τ) был другой CS₁ и пара не относится к сенсорной прекондиции,
    /// для CS₁ создаётся/усиливается CR порядка ≥2 (exact match, не иерархия).
    /// </summary>
    /// <param name="currentPulse">Текущий пульс</param>
    /// <param name="reinforcingStimulusImageId">ID образа стимула, активировавшего условный рефлекс</param>
    /// <param name="baseState">Базовое состояние гомеостаза</param>
    /// <param name="behaviorStyleImageId">ID образа стилей поведения</param>
    /// <param name="activatedConditionedReflexId">ID условного рефлекса, который был активирован</param>
    /// <param name="authoritativeMode">Флаг авторитарной записи</param>
    /// <param name="reinforcingToneId">Тон стимула, активировавшего рефлекс</param>
    /// <param name="reinforcingMoodId">Настроение стимула, активировавшего рефлекс</param>
    internal void CheckSecondaryConditioning(
        int currentPulse,
        int reinforcingStimulusImageId,
        int baseState,
        int behaviorStyleImageId,
        int activatedConditionedReflexId,
        bool authoritativeMode,
        int reinforcingToneId,
        int reinforcingMoodId)
    {
      if (_lastConditionedStimulus == null)
        return;

      // Проверяем, что родительский условный рефлекс достаточно силён
      var parentReflex = _conditionedReflexes.GetConditionedReflexById(activatedConditionedReflexId);
      if (parentReflex == null || !parentReflex.CanBeActivated())
        return;

      // Не создаём рефлексы порядка выше третичного
      if (parentReflex.Order >= 3)
        return;

      var settings = _conditionedReflexes.Settings;
      int timeWindow = settings.TimeWindowPulses;

      bool previousCSInWindow = (currentPulse - _lastConditionedStimulus.Pulse) <= timeWindow;

      if (!previousCSInWindow)
        return;

      // Предыдущий CS должен предшествовать текущему подкрепляющему стимулу
      if (_lastConditionedStimulus.Pulse >= currentPulse)
        return;

      // Убеждаемся, что предыдущий CS — это другой стимул (не тот же самый)
      if (_lastConditionedStimulus.StimulusImageId == reinforcingStimulusImageId)
        return;

      // Пара «бедный CSₐ ⊂ богатый CSᵦ» — зона сенсорной прекондиции (связь + иерархия), не вторичный CR.
      if (_conditionedReflexes.IsSensoryPreconditioningPair(
              _lastConditionedStimulus.StimulusImageId,
              reinforcingStimulusImageId))
        return;

      ProcessSecondaryConditionedAssociation(
          _lastConditionedStimulus,
          parentReflex,
          authoritativeMode);
    }

    /// <summary>
    /// Вторичное обусловливание: CR на CSₐ, подкреплённый активацией CR на последующем CSᵦ.
    /// Активация — exact match по Level3; иерархия и SensoryAssociationSystem не задействуются.
    /// </summary>
    private void ProcessSecondaryConditionedAssociation(
        StimulusRecord conditionedStimulus,
        ConditionedReflexesSystem.ConditionedReflex parentConditionedReflex,
        bool authoritativeMode)
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
              existingReflex.Level2.OrderBy(x => x).SequenceEqual(GetCurrentStyleIds().OrderBy(x => x)) &&
              existingReflex.ToneId == conditionedStimulus.ToneId &&
              existingReflex.MoodId == conditionedStimulus.MoodId)
          {
            _conditionedReflexes.StrengthenAssociation(existingReflex.Id);
            foundMatchingReflex = true;
            Logger.Info($"Усилен вторичный условный рефлекс ID={existingReflex.Id} " +
                       $"(порядок {existingReflex.Order}) от условного {parentConditionedReflex.Id}");
            break;
          }
        }

        if (!foundMatchingReflex)
        {
          List<int> reflexStyles = GetCurrentStyleIds();

          var (newReflexId, warnings) = _conditionedReflexes.AddConditionedReflex(
              level1: conditionedStimulus.BaseState,
              level2: reflexStyles,
              level3: conditionedStimulus.StimulusImageId,
              sourceGeneticReflexId: parentConditionedReflex.SourceGeneticReflexId,
              authoritativeMod: authoritativeMode,
              toneId: conditionedStimulus.ToneId,
              moodId: conditionedStimulus.MoodId,
              sourceConditionedReflexId: parentConditionedReflex.Id);

          if (newReflexId > 0)
          {
            int newOrder = parentConditionedReflex.Order + 1;
            Logger.Info($"Создан условный рефлекс {newOrder}-го порядка ID={newReflexId} " +
                       $"от условного {parentConditionedReflex.Id} " +
                       $"(безусловный источник: {parentConditionedReflex.SourceGeneticReflexId})");
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

    #region Управление жизненным циклом рефлексов

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
        // Сброс истории под блокировкой до Dispose — иначе EnterWriteLock на уничтоженном ReaderWriterLockSlim.
        ResetHistory();
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}