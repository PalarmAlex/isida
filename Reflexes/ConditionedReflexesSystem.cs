using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Common.FileValidator;

/*
МАТЕМАТИЧЕСКАЯ МОДЕЛЬ УСЛОВНЫХ РЕФЛЕКСОВ

Основана на модели Рескорла-Вагнера с адаптивным экспоненциальным затуханием:

1. Образование ассоциации (модель Рескорла-Вагнера):
   C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
   где:
     C_ij ∈ [0, β] - крепость связи между стимулами
     α ∈ (0,1) - коэффициент обучения (скорость приближения к асимптоте)
     β = 1.0 - асимптотический максимум крепости
     k - номер подтверждённой пары стимулов

2. Адаптивное затухание связи:
   C_ij(t+1) = η^(1/C_ij(t)) * C_ij(t)
   где η ∈ (0,1) - коэффициент затухания
   Чем прочнее рефлекс (выше C_ij), тем медленнее затухание

3. Активация рефлекса:
   Условный рефлекс активируется при C_ij > γ, где γ ∈ (0,1) - порог автономной активации

4. Временное окно корреляции:
   Стимулы S₁ и S₂ считаются коррелированными, если интервал между ними ≤ τ

5. Управление временем жизни:
   - Базовое время жизни: T_base
   - Прочные рефлексы (MaxAchievedStrength > 0.8) живут в 10 раз дольше
   - Расчетное время: T_calculated = T_base * (1 + MaxAchievedStrength * 10)
   - Рефлекс удаляется при C_ij < C_min ИЛИ времени без активации > T_calculated

6. Прочность рефлекса:
   - MaxAchievedStrength отслеживает максимальную достигнутую крепость
   - IsEstablished = (MaxAchievedStrength > 0.8) - флаг установившегося рефлекса

Параметры по умолчанию:
   α = 0.2, β = 1.0, η = 0.98, γ = 0.6, τ = 500 мс, C_min = 0.1, T_base = 1000
*/

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Система управления условными рефлексами агента
  /// </summary>
  public sealed class ConditionedReflexesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GomeostasSystem _gomeostas;
    private readonly PerceptionImagesSystem _perceptionImagesSystem;
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private bool _disposed = false;

    #region Инициализация

    private static ConditionedReflexesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы условных рефлексов
    /// </summary>
    public static ConditionedReflexesSystem Instance => _instance ??
        throw new InvalidOperationException("ConditionedReflexesSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы условных рефлексов
    /// </summary>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexesSystem,
        PerceptionImagesSystem perceptionImagesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("ConditionedReflexesSystem уже инициализирован.");

      if (!AdaptiveActionsSystem.IsInitialized)
        throw new InvalidOperationException("AdaptiveActionsSystem должен быть инициализирован перед ConditionedReflexesSystem");

      _instance = new ConditionedReflexesSystem(gomeostas, geneticReflexesSystem, perceptionImagesSystem);
    }

    private ConditionedReflexesSystem(
        GomeostasSystem gomeostas,
        GeneticReflexesSystem geneticReflexesSystem,
        PerceptionImagesSystem perceptionImagesSystem)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _geneticReflexesSystem = geneticReflexesSystem ?? throw new ArgumentNullException(nameof(geneticReflexesSystem));
      _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));

      // Подписываемся на события удаления
      _gomeostas.StyleDeleted += OnStyleDeleted;
      var adaptiveActionsSystem = AdaptiveActionsSystem.Instance;
      adaptiveActionsSystem.AdaptiveActionDeleted += OnAdaptiveActionDeleted;

      try
      {
        EnsureDataDirectory();
        LoadConditionedReflexes();
        LoadConditionedReflexSettings();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации ConditionedReflexesSystem: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string ConditionedReflexesFileName = "ConditionedReflexes";
    private const string ConditionedReflexSettingsFileName = "ConditionedReflexSettings";

    /// <summary>
    /// Условный рефлекс агента
    /// </summary>
    public class ConditionedReflex
    {
      private float _learningRate = 0.2f;
      private float _decayRate = 0.98f;
      private float _activationThreshold = 0.6f;
      private int _timeWindowPulses = 5;
      private float _minAssociationStrength = 0.1f;
      private int _maxRank = 10;
      private int _baseInactivationTime = 1000;

      /// <summary>
      /// Уникальный идентификатор рефлекса
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Первый уровень: Интегральное базовое состояние гомеостаза
      /// </summary>
      public int Level1 { get; set; }

      /// <summary>
      /// Второй уровень: Контексты реагирования (список ID активных стилей поведения)
      /// Используется список ID вместо ID образа для:
      /// - Прямого сравнения с текущими активными контекстами
      /// - Избежания избыточного создания образов для каждого сочетания
      /// - Упрощения логики сопоставления условий рефлекса
      /// (В отличие от Level3, где сложные сочетания пусковых стимулов требуют оптимизации через образы)
      /// </summary>
      public List<int> Level2 { get; set; } = new List<int>();

      /// <summary>
      /// Третий уровень: ID образа пускового стимула (TriggerStimulusID)
      /// </summary>
      public int Level3 { get; set; }

      /// <summary>
      /// Адаптивные действия рефлекса
      /// </summary>
      public List<int> AdaptiveActions { get; set; } = new List<int>();

      /// <summary>
      /// Ранг рефлекса (число цепочки родителей)
      /// </summary>
      public int Rank { get; set; }

      /// <summary>
      /// Крепость ассоциативной связи. [0, 1]
      /// </summary>
      public float AssociationStrength { get; set; }

      /// <summary>
      /// Время последней активации (в пульсах)
      /// </summary>
      public int LastActivation { get; set; }

      /// <summary>
      /// Время создания рефлекса (в пульсах)
      /// </summary>
      public int BirthTime { get; set; }

      /// <summary>
      /// ID исходного безусловного рефлекса
      /// </summary>
      public int SourceGeneticReflexId { get; set; }

      /// <summary>
      /// Максимальная достигнутая крепость связи
      /// </summary>
      public float MaxAchievedStrength { get; private set; }

      /// <summary>
      /// Флаг установившегося рефлекса (когда-либо достигал высокой прочности)
      /// </summary>
      public bool IsEstablished => MaxAchievedStrength > 0.8f;

      /// <summary>
      /// Расчетное время жизни без активации (зависит от прочности рефлекса)
      /// </summary>
      public int CalculatedInactivationTime
      {
        get
        {
          int baseTime = _baseInactivationTime;

          // Прочные рефлексы живут значительно дольше
          if (IsEstablished)
            baseTime *= 10;

          // Учитываем максимальную достигнутую прочность
          // Максимальное увеличение в 10 раз при MaxAchievedStrength = 1.0
          return (int)(baseTime * (1 + MaxAchievedStrength * 10));
        }
      }

      /// <summary>
      /// Коэффициент обучения α (0.1-0.3)
      /// </summary>
      public float LearningRate
      {
        get => _learningRate;
        set
        {
          var validation = SettingsValidator.ValidateLearningRate(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _learningRate = value;
        }
      }

      /// <summary>
      /// Коэффициент затухания η (0.95-0.99)
      /// </summary>
      public float DecayRate
      {
        get => _decayRate;
        set
        {
          var validation = SettingsValidator.ValidateDecayRate(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _decayRate = value;
        }
      }

      /// <summary>
      /// Порог активации γ (0.5-0.7)
      /// </summary>
      public float ActivationThreshold
      {
        get => _activationThreshold;
        set
        {
          var validation = SettingsValidator.ValidateActivationThreshold(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _activationThreshold = value;
        }
      }

      /// <summary>
      /// Временное окно корреляции τ (пульсов)
      /// </summary>
      public int TimeWindowPulses
      {
        get => _timeWindowPulses;
        set
        {
          var validation = SettingsValidator.ValidateTimeWindowPulses(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _timeWindowPulses = value;
        }
      }

      /// <summary>
      /// Минимальная крепость связи C_min (0.01-0.3)
      /// </summary>
      public float MinAssociationStrength
      {
        get => _minAssociationStrength;
        set
        {
          var validation = SettingsValidator.ValidateMinAssociationStrength(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _minAssociationStrength = value;
        }
      }

      /// <summary>
      /// Максимальный ранг рефлекса (1-50)
      /// </summary>
      public int MaxRank
      {
        get => _maxRank;
        set
        {
          var validation = SettingsValidator.ValidateMaxRank(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _maxRank = value;
        }
      }

      /// <summary>
      /// Базовое время жизни без активации (100-10000 пульсов)
      /// </summary>
      public int BaseInactivationTime
      {
        get => _baseInactivationTime;
        set
        {
          var validation = SettingsValidator.ValidateBaseInactivationTime(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          _baseInactivationTime = value;
        }
      }

      /// <summary>
      /// Усиливает ассоциацию по модели Рескорла-Вагнера
      /// </summary>
      public void StrengthenAssociation()
      {
        // C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
        float beta = 1.0f; // асимптотический максимум
        AssociationStrength = AssociationStrength + _learningRate * (beta - AssociationStrength);

        // Обновляем максимальную достигнутую прочность
        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;

        // Обновляем время последней активации
        LastActivation = GetCurrentPulse();
      }

      /// <summary>
      /// Применяет адаптивное затухание с учетом прочности рефлекса
      /// </summary>
      public void ApplyDecay()
      {
        // C_ij(t+1) = η^(1/C_ij(t)) * C_ij(t)
        // Чем прочнее рефлекс, тем медленнее затухание
        float strengthFactor = Math.Max(0.1f, AssociationStrength);
        float effectiveDecayRate = (float)Math.Pow(_decayRate, 1.0 / strengthFactor);

        AssociationStrength *= effectiveDecayRate;

        // Обновляем максимальную достигнутую прочность (если не изменилась)
        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;
      }

      /// <summary>
      /// Проверяет, должен ли рефлекс быть удален
      /// </summary>
      public bool ShouldBeRemoved(int currentPulse)
      {
        // Основное условие - минимальная крепость
        if (AssociationStrength < _minAssociationStrength)
          return true;

        // Проверка времени без активации с учетом прочности рефлекса
        int timeSinceActivation = currentPulse - LastActivation;
        return timeSinceActivation > CalculatedInactivationTime;
      }

      /// <summary>
      /// Проверяет, может ли рефлекс быть активирован
      /// </summary>
      public bool CanBeActivated()
      {
        return AssociationStrength >= _activationThreshold;
      }

      /// <summary>
      /// Получает текущее значение пульса из глобального таймера
      /// </summary>
      private int GetCurrentPulse()
      {
        return GlobalTimer.GlobalPulsCount;
      }
    }

    /// <summary>
    /// Настройки системы условных рефлексов
    /// </summary>
    public class ConditionedReflexSettings
    {
      /// <summary>
      /// Коэффициент обучения α (0.1-0.3)
      /// </summary>
      public float LearningRate { get; set; } = 0.2f;

      /// <summary>
      /// Максимальная крепость связи β
      /// </summary>
      public float MaxAssociationStrength { get; set; } = 1.0f;

      /// <summary>
      /// Коэффициент затухания η (0.95-0.99)
      /// </summary>
      public float DecayRate { get; set; } = 0.98f;

      /// <summary>
      /// Порог активации γ (0.5-0.7)
      /// </summary>
      public float ActivationThreshold { get; set; } = 0.6f;

      /// <summary>
      /// Минимальная крепость связи C_min
      /// </summary>
      public float MinAssociationStrength { get; set; } = 0.1f;

      /// <summary>
      /// Временное окно корреляции τ (пульсов)
      /// </summary>
      public int TimeWindowPulses { get; set; } = 5;

      /// <summary>
      /// Максимальный ранг рефлекса
      /// </summary>
      public int MaxRank { get; set; } = 10;

      /// <summary>
      /// Время жизни рефлекса без активации (в пульсах)
      /// </summary>
      public int MaxInactivationTime { get; set; } = 1000;
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, ConditionedReflex> _conditionedReflexes = new Dictionary<int, ConditionedReflex>();
    private readonly List<ConditionedReflex> _activeConditionedReflexes = new List<ConditionedReflex>();
    private readonly ConditionedReflexSettings _settings = new ConditionedReflexSettings();
    private int _lastConditionedReflexId = 0;
    private int _currentPulseCount = 0;

    /// <summary>
    /// Получает текущие настройки системы условных рефлексов
    /// </summary>
    public ConditionedReflexSettings Settings => _settings;

    /// <summary>
    /// Получает список активных условных рефлексов
    /// </summary>
    public ReadOnlyCollection<ConditionedReflex> GetActiveConditionedReflexes()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<ConditionedReflex>(_activeConditionedReflexes.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список всех условных рефлексов
    /// </summary>
    public ReadOnlyCollection<ConditionedReflex> GetAllConditionedReflexes()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<ConditionedReflex>(_conditionedReflexes.Values.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    #endregion

    #region Управление условными рефлексами

    /// <summary>
    /// Добавляет новый условный рефлекс
    /// </summary>
    public (int ReflexId, string[] Warnings) AddConditionedReflex(
        int level1,
        List<int> level2,
        int level3,
        List<int> adaptiveActions,
        int sourceGeneticReflexId,
        int rank = 0)
    {
      if (_gomeostas.GetAgentState().EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      var warnings = new List<string>();

      var validationResult = ValidateConditionedReflexParameters(level1, level2, level3, adaptiveActions, rank);
      if (!validationResult.IsValid)
      {
        warnings.Add(validationResult.ErrorMessage);
        throw new ArgumentException(validationResult.ErrorMessage);
      }

      // Проверка дубликатов
      var candidateReflex = new ConditionedReflex
      {
        Level1 = level1,
        Level2 = level2?.OrderBy(x => x).ToList() ?? new List<int>(),
        Level3 = level3,
        AdaptiveActions = adaptiveActions?.OrderBy(x => x).ToList() ?? new List<int>(),
        Rank = rank
      };

      _lock.EnterReadLock();
      try
      {
        bool isDuplicate = _conditionedReflexes.Values.Any(existing =>
            AreConditionedReflexesEqual(existing, candidateReflex));

        if (isDuplicate)
        {
          string errorMsg = "Условный рефлекс с указанными уровнями Level1, Level2, Level3 уже существует.";
          warnings.Add(errorMsg);
          return (0, warnings.ToArray());
        }
      }
      finally
      {
        _lock.ExitReadLock();
      }

      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastConditionedReflexId;
        var conditionedReflex = new ConditionedReflex
        {
          Id = newId,
          Level1 = level1,
          Level2 = level2 ?? new List<int>(),
          Level3 = level3,
          AdaptiveActions = adaptiveActions ?? new List<int>(),
          Rank = rank,
          AssociationStrength = _settings.MinAssociationStrength + 0.1f, // Начальное значение выше минимального
          LastActivation = _currentPulseCount,
          BirthTime = _currentPulseCount,
          SourceGeneticReflexId = sourceGeneticReflexId
        };

        _conditionedReflexes.Add(newId, conditionedReflex);
        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет крепость связи условного рефлекса при подтверждении ассоциации
    /// </summary>
    public void StrengthenAssociation(int reflexId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_conditionedReflexes.TryGetValue(reflexId, out var reflex))
        {
          // C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
          reflex.AssociationStrength = reflex.AssociationStrength +
              _settings.LearningRate * (_settings.MaxAssociationStrength - reflex.AssociationStrength);

          reflex.LastActivation = _currentPulseCount;

          // Ограничение значения
          reflex.AssociationStrength = Math.Min(reflex.AssociationStrength, _settings.MaxAssociationStrength);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Применяет затухание ко всем условным рефлексам
    /// </summary>
    public void ApplyDecay()
    {
      _lock.EnterWriteLock();
      try
      {
        var reflexesToRemove = new List<int>();

        foreach (var reflex in _conditionedReflexes.Values)
        {
          // Применяем затухание с учетом прочности
          reflex.ApplyDecay();

          // Проверяем на удаление
          if (reflex.ShouldBeRemoved(_currentPulseCount))
          {
            reflexesToRemove.Add(reflex.Id);
          }
        }

        // Удаление ослабленных рефлексов
        foreach (var id in reflexesToRemove)
        {
          _conditionedReflexes.Remove(id);
          _activeConditionedReflexes.RemoveAll(r => r.Id == id);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет активные условные рефлексы на основе текущего состояния
    /// </summary>
    public void UpdateActiveReflexes(int[] currentConditions, int currentPulse)
    {
      _lock.EnterWriteLock();
      try
      {
        _activeConditionedReflexes.Clear();

        foreach (var reflex in _conditionedReflexes.Values)
        {
          // Проверяем временную корреляцию
          if (!IsWithinTimeWindow(currentPulse, reflex.LastActivation, reflex.TimeWindowPulses))
            continue;

          // Проверка условий активации и порога крепости
          if (IsReflexConditionsMet(reflex, currentConditions) &&
              reflex.AssociationStrength >= _settings.ActivationThreshold)
          {
            _activeConditionedReflexes.Add(reflex);
            reflex.LastActivation = _currentPulseCount;
          }
        }

        // Сортировка по рангу и крепости связи
        _activeConditionedReflexes.Sort((a, b) =>
        {
          int rankCompare = b.Rank.CompareTo(a.Rank);
          return rankCompare != 0 ? rankCompare : b.AssociationStrength.CompareTo(a.AssociationStrength);
        });
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет, находятся ли события в пределах временного окна
    /// </summary>
    private bool IsWithinTimeWindow(int currentPulse, int lastActivationPulse, int timeWindowPulses)
    {
      return (currentPulse - lastActivationPulse) <= timeWindowPulses;
    }

    /// <summary>
    /// Попытка образования условного рефлекса на основе временной корреляции
    /// </summary>
    public bool TryFormAssociation(
        int unconditionalStimulusPulse,
        int conditionedStimulusPulse,
        ConditionedReflex reflex)
    {
      // Проверяем, находятся ли стимулы в пределах временного окна
      if (!AreStimuliCorrelated(unconditionalStimulusPulse, conditionedStimulusPulse,
                                reflex.TimeWindowPulses))
      {
        return false; // Стимулы не коррелируют во времени
      }

      // Усиливаем ассоциацию
      reflex.StrengthenAssociation();
      return true;
    }

    /// <summary>
    /// Удаляет условный рефлекс по указанному ID
    /// </summary>
    /// <param name="reflexId">ID удаляемого условного рефлекса</param>
    /// <returns>True, если действие было успешно удалено, иначе False</returns>
    public bool RemoveConditionedReflex(int reflexId)
    {
      if (_gomeostas.GetAgentState().EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      _lock.EnterWriteLock();
      try
      {
        if (!_conditionedReflexes.ContainsKey(reflexId))
          return false;

        var removed = _conditionedReflexes.Remove(reflexId);
        if (removed)
          _activeConditionedReflexes.RemoveAll(r => r.Id == reflexId);

        return removed;
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка удаления условного рефлекса {reflexId}: {ex.Message}");
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    internal bool removeAllConditionedReflexes = false;

    /// <summary>
    /// Удаляет все условные рефлексы
    /// </summary>
    /// <returns>True, если действие было успешно удалено, иначе False</returns>
    public bool RemoveAllConditionedReflexes()
    {
      if (!removeAllConditionedReflexes && _gomeostas.GetAgentState().EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      _lock.EnterWriteLock();
      try
      {
        _conditionedReflexes.Clear();
        _activeConditionedReflexes.Clear();
        _lastConditionedReflexId = 0;

        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Увеличивает счетчик пульсов (вызывается извне)
    /// </summary>
    public void IncrementPulse()
    {
      _currentPulseCount++;
      ApplyDecay(); // Затухание применяется на каждом пульсе
    }

    #endregion

    #region Обработчики событий

    private void OnStyleDeleted(int styleId)
    {
      _lock.EnterWriteLock();
      try
      {
        // Удаляем ссылки на стиль из Level2
        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.Level2.Contains(styleId))
            reflex.Level2.Remove(styleId);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private void OnAdaptiveActionDeleted(int actionId)
    {
      _lock.EnterWriteLock();
      try
      {
        // Удаляем ссылки на действие
        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.AdaptiveActions.Contains(actionId))
            reflex.AdaptiveActions.Remove(actionId);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Вспомогательные методы

    private bool AreConditionedReflexesEqual(ConditionedReflex a, ConditionedReflex b)
    {
      if (a == null || b == null) return false;
      if (a.Level1 != b.Level1) return false;
      if (a.Level3 != b.Level3) return false;
      if (!a.Level2.OrderBy(x => x).SequenceEqual(b.Level2.OrderBy(x => x))) return false;
      return true;
    }

    private bool IsReflexConditionsMet(ConditionedReflex reflex, int[] conditions)
    {
      if (conditions.Length < 3) return false;
      if (reflex.Level1 != conditions[0]) return false;

      // Проверка Level2 (образ стилей поведения)
      var currentStyleImage = _perceptionImagesSystem.GetAllBehaviorStyleImagesList()
          .FirstOrDefault(img => img.BehaviorStylesList.SequenceEqual(conditions.Skip(1).Take(conditions.Length - 2)));

      if (currentStyleImage == null) return false;

      // Проверка Level3 (пусковой стимул)
      return reflex.Level3 == conditions[2];
    }

    /// <summary>
    /// Проверяет, находятся ли два стимула в пределах временного окна корреляции
    /// </summary>
    /// <param name="pulse1">Пульс первого стимула</param>
    /// <param name="pulse2">Пульс второго стимула</param>
    /// <param name="timeWindowPulses">Временное окно в пульсах</param>
    public bool AreStimuliCorrelated(int pulse1, int pulse2, int timeWindowPulses)
    {
      return Math.Abs(pulse1 - pulse2) <= timeWindowPulses;
    }

    #endregion

    #region Валидация

    private (bool IsValid, string ErrorMessage) ValidateConditionedReflexParameters(
        int level1,
        List<int> level2,
        int level3,
        List<int> adaptiveActions,
        int rank)
    {
      // Проверка Level1
      var validBaseStates = new[] { -1, 0, 1 };
      if (!validBaseStates.Contains(level1))
        return (false, "Level1 должен быть одним из значений: -1, 0, 1");

      // Проверка Level3 (должен существовать образ восприятия)
      var perceptionImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
      if (!perceptionImages.Any(img => img.Id == level3))
        return (false, $"Level3 (ID образа восприятия {level3}) не найден");

      // Проверка ранга
      if (rank < 0 || rank > _settings.MaxRank)
        return (false, $"Ранг должен быть в диапазоне 0-{_settings.MaxRank}");

      return (true, string.Empty);
    }

    #endregion

    #region Работа с файлами

    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetConditionedReflexesFilePath());
      if (!Directory.Exists(directory))
        Directory.CreateDirectory(directory);
    }

    private string GetConditionedReflexesFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{ConditionedReflexesFileName}.dat");
    }

    private string GetConditionedReflexSettingsFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{ConditionedReflexSettingsFileName}.dat");
    }

    private void LoadConditionedReflexes()
    {
      string filePath = GetConditionedReflexesFilePath();

      if (!File.Exists(filePath))
        return;

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _conditionedReflexes.Clear();
          _lastConditionedReflexId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
              continue;

            var parts = line.Split('|');
            if (parts.Length < 9)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var reflex = new ConditionedReflex
            {
              Id = id,
              Level1 = int.Parse(parts[1]),
              Level2 = ParseIntList(parts[2]),
              Level3 = int.Parse(parts[3]),
              AdaptiveActions = ParseIntList(parts[4]),
              Rank = int.Parse(parts[5]),
              AssociationStrength = float.Parse(parts[6]),
              LastActivation = int.Parse(parts[7]),
              BirthTime = int.Parse(parts[8]),
              SourceGeneticReflexId = parts.Length > 11 ? int.Parse(parts[9]) : 0
            };

            _conditionedReflexes[id] = reflex;
            if (id > _lastConditionedReflexId)
              _lastConditionedReflexId = id;
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        LogError($"LoadConditionedReflexes: Ошибка загрузки условных рефлексов: {ex.Message}");
      }
    }

    private void LoadConditionedReflexSettings()
    {
      string filePath = GetConditionedReflexSettingsFilePath();

      if (!File.Exists(filePath))
        return;

      try
      {
        foreach (var line in File.ReadLines(filePath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

          var parts = line.Split('=');
          if (parts.Length != 2)
            continue;

          var key = parts[0].Trim();
          var value = parts[1].Trim();

          switch (key)
          {
            case "LearningRate":
              _settings.LearningRate = float.Parse(value);
              break;
            case "DecayRate":
              _settings.DecayRate = float.Parse(value);
              break;
            case "ActivationThreshold":
              _settings.ActivationThreshold = float.Parse(value);
              break;
            case "TimeWindowPulses":
              _settings.TimeWindowPulses = int.Parse(value);
              break;
            case "MaxRank":
              _settings.MaxRank = int.Parse(value);
              break;
            case "MaxInactivationTime":
              _settings.MaxInactivationTime = int.Parse(value);
              break;
          }
        }
      }
      catch (Exception ex)
      {
        LogError($"LoadConditionedReflexSettings: Ошибка загрузки настроек условных рефлексов: {ex.Message}");
      }
    }

    /// <summary>
    /// Сохраняет условные рефлексы в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveConditionedReflexes()
    {
      _lock.EnterReadLock();
      try
      {
        var lines = new List<string>
        {
          FileHeaders.ConditionedReflexesFormat,
          FileHeaders.ConditionedReflexesLevel1,
          FileHeaders.ConditionedReflexesLevel2,
          FileHeaders.ConditionedReflexesLevel3,
          FileHeaders.ConditionedReflexesActions
        };

        foreach (var reflex in _conditionedReflexes.Values.OrderBy(r => r.Id))
        {
          lines.Add($"{reflex.Id}|{reflex.Level1}|" +
                   $"{string.Join(",", reflex.Level2)}|{reflex.Level3}|" +
                   $"{string.Join(",", reflex.AdaptiveActions)}|{reflex.Rank}|" +
                   $"{reflex.AssociationStrength}|{reflex.LastActivation}|" +
                   $"{reflex.BirthTime}|{reflex.SourceGeneticReflexId}");
        }

        var result = FileValidator.SafeSaveFile(
            GetConditionedReflexesFilePath(),
            lines,
            content => true, // Упрощенная валидация
            minLinesCount: 2,
            fileDescription: "условных рефлексов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет настройки условных рефлексов в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveConditionedReflexSettings()
    {
      try
      {
        var lines = new List<string>
          {
            "# Настройки системы условных рефлексов",
            "# LearningRate: коэффициент обучения α (0.1-0.3)",
            "# DecayRate: коэффициент затухания η (0.95-0.99)",
            "# ActivationThreshold: порог активации γ (0.5-0.7)",
            "# TimeWindowPulses: временное окно корреляции в пульсах (1-10)",
            "# MaxRank: максимальный ранг рефлекса",
            "# MaxInactivationTime: время жизни без активации (пульсы)"
          };

        lines.Add($"LearningRate={_settings.LearningRate}");
        lines.Add($"DecayRate={_settings.DecayRate}");
        lines.Add($"ActivationThreshold={_settings.ActivationThreshold}");
        lines.Add($"TimeWindowMs={_settings.TimeWindowPulses}");
        lines.Add($"MaxRank={_settings.MaxRank}");
        lines.Add($"MaxInactivationTime={_settings.MaxInactivationTime}");

        var result = FileValidator.SafeSaveFile(
            GetConditionedReflexSettingsFilePath(),
            lines,
            content => true,
            minLinesCount: 2,
            fileDescription: "настроек условных рефлексов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    private List<int> ParseIntList(string listStr)
    {
      if (string.IsNullOrWhiteSpace(listStr))
        return new List<int>();

      return listStr.Split(',')
          .Where(s => !string.IsNullOrWhiteSpace(s))
          .Select(s => int.TryParse(s.Trim(), out int result) ? result : 0)
          .Where(x => x != 0)
          .ToList();
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
        if (_gomeostas != null)
          _gomeostas.StyleDeleted -= OnStyleDeleted;

        if (AdaptiveActionsSystem.IsInitialized)
        {
          var adaptiveActionsSystem = AdaptiveActionsSystem.Instance;
          adaptiveActionsSystem.AdaptiveActionDeleted -= OnAdaptiveActionDeleted;
        }

        SaveConditionedReflexes();
        SaveConditionedReflexSettings();
      }
      catch (Exception ex)
      {
        LogError($"Error during disposal: {ex.Message}");
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    private static void LogError(string message)
    {
      FileValidator.LogError(message);
    }

    #endregion
  }
}