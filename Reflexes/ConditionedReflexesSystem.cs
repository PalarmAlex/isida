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
    private int _currentAgentLifetime = 0;

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

      _gomeostas.StyleDeleted += OnStyleDeleted;
      var adaptiveActionsSystem = AdaptiveActionsSystem.Instance;

      try
      {
        EnsureDataDirectory();
        LoadConditionedReflexes();
        LoadConditionedReflexSettings();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
        LastActivation = GetAgentLifetime();
      }

      /// <summary>
      /// Применяет адаптивное затухание с учетом прочности рефлекса
      /// </summary>
      public void ApplyDecay()
      {
        // Применяем затухание только на каждом 100-м пульсе
        int currentPulse = GetAgentLifetime();
        if (currentPulse % 100 != 0)
          return;

        // Адаптивное затухание: чем прочнее рефлекс, тем медленнее затухание
        // Используем формулу: C(t+1) = η^(C(t)) * C(t)
        // где η ∈ (0,1) - коэффициент затухания
        float strengthFactor = Math.Max(0.1f, AssociationStrength);

        // Для слабых рефлексов (strength < 0.3) используем более сильное затухание
        // Для средних (0.3-0.7) - умеренное
        // Для прочных (>0.7) - очень слабое затухание
        float effectiveDecayRate;

        if (AssociationStrength > 0.8f)
          // Прочные рефлексы: почти не затухают
          effectiveDecayRate = 0.998f;
        else if (AssociationStrength > 0.4f)
          // Средние рефлексы: умеренное затухание
          // decayRate^strengthFactor: при strength=0.5, decayRate=0.98
          // effectiveDecayRate = 0.98^0.5 ≈ 0.99
          effectiveDecayRate = (float)Math.Pow(_decayRate, strengthFactor);
        else
          // Слабые рефлексы: нормальное затухание
          effectiveDecayRate = (float)Math.Pow(_decayRate, Math.Sqrt(strengthFactor));

        // Применяем затухание
        float oldStrength = AssociationStrength;
        AssociationStrength *= effectiveDecayRate;

        // Обновляем максимальную достигнутую прочность
        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;

        //Debug.WriteLine($"ApplyDecay: ID={Id}, Old={oldStrength:F3}, New={AssociationStrength:F3}, " +
        //               $"DecayRate={effectiveDecayRate:F5}, Pulse={currentPulse}");
      }

      /// <summary>
      /// Проверяет, должен ли рефлекс быть удален
      /// </summary>
      public bool ShouldBeRemoved(int currentLifetime)
      {
        if (AssociationStrength < _minAssociationStrength)
          return true;

        int referenceTime = LastActivation > 0 ? LastActivation : BirthTime;
        int timeSinceReference = currentLifetime - referenceTime;

        // Для новых рефлексов даем время на "акклиматизацию"
        // Новые рефлексы не удаляем слишком быстро
        if (timeSinceReference < 100) // Минимум 100 пульсов на "акклиматизацию"
          return false;

        return timeSinceReference > CalculatedInactivationTime;
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
      private int GetAgentLifetime()
      {
        try
        {
          return Instance.GetCurrentAgentLifetime();
        }
        catch
        {
          return 0;
        }
      }
    }

    /// <summary>
    /// Получает текущее время жизни агента (кешированное значение)
    /// Для внутреннего использования и доступа из класса ConditionedReflex
    /// </summary>
    internal int GetCurrentAgentLifetime()
    {
      return _currentAgentLifetime;
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

    #region Привязка к ReflexTreeSystem через события

    /// <summary>Событие создания нового условного рефлекса</summary>
    public event Action<ConditionedReflexCreatedEventArgs> ConditionedReflexCreated;

    /// <summary>Событие удаления одиночного условного рефлекса</summary>
    public event Action<int> ConditionedReflexDeleted;

    /// <summary>Событие массового удаления условных рефлексов</summary>
    public event Action<List<int>> MultipleConditionedReflexesDeleted;

    /// <summary>Аргументы события создания условного рефлекса</summary>
    public class ConditionedReflexCreatedEventArgs
    {
      /// <summary>ID созданного рефлекса</summary>
      public int ReflexId { get; }

      /// <summary>Базовое состояние гомеостаза</summary>
      public int Level1 { get; }

      /// <summary>Стили поведения</summary>
      public List<int> Level2 { get; }

      /// <summary>ID образа пускового стимула</summary>
      public int Level3 { get; }

      /// <summary>Создает аргументы события</summary>
      public ConditionedReflexCreatedEventArgs(int reflexId, int level1, List<int> level2, int level3)
      {
        ReflexId = reflexId;
        Level1 = level1;
        Level2 = level2;
        Level3 = level3;
      }
    }

    private void OnConditionedReflexCreated(int reflexId, int level1, List<int> level2, int level3)
    {
      ConditionedReflexCreated?.Invoke(new ConditionedReflexCreatedEventArgs(reflexId, level1, level2, level3));
    }

    private void OnConditionedReflexDeleted(int reflexId)
    {
      ConditionedReflexDeleted?.Invoke(reflexId);
    }

    private void OnMultipleConditionedReflexesDeleted(List<int> reflexIds)
    {
      MultipleConditionedReflexesDeleted?.Invoke(reflexIds);
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
        int sourceGeneticReflexId,
        bool authoritativeMod = false)
    {
      if (AppGlobalState.EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      var warnings = new List<string>();

      var validationResult = ValidateConditionedReflexParameters(level1, level2, level3);
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
        Level3 = level3
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
        int currentLifetime = GetAgentLifetime();
        float _associationStrength = _settings.MinAssociationStrength + 0.1f;

        if (authoritativeMod)
          _associationStrength = 0.95f;

        var conditionedReflex = new ConditionedReflex
        {
          Id = newId,
          Level1 = level1,
          Level2 = level2 ?? new List<int>(),
          Level3 = level3,
          AssociationStrength = _associationStrength,
          LastActivation = currentLifetime,
          BirthTime = currentLifetime,
          SourceGeneticReflexId = sourceGeneticReflexId
        };

        _conditionedReflexes.Add(newId, conditionedReflex);

        try
        {
          OnConditionedReflexCreated(newId, level1, level2, level3);
        }
        catch (Exception ex)
        {
          warnings.Add($"Ошибка при обработке создания условного рефлекса: {ex.Message}");
        }

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

          reflex.LastActivation = GetAgentLifetime();
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
          if (reflex.ShouldBeRemoved(GetAgentLifetime()))
            reflexesToRemove.Add(reflex.Id);
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
        int agentLifetime = GetAgentLifetime();

        foreach (var reflex in _conditionedReflexes.Values)
        {
          // Проверяем временную корреляцию
          if (!IsWithinTimeWindow(agentLifetime, reflex.LastActivation, reflex.TimeWindowPulses))
            continue;

          // Проверка условий активации и порога крепости
          if (IsReflexConditionsMet(reflex, currentConditions) &&
              reflex.AssociationStrength >= _settings.ActivationThreshold)
          {
            _activeConditionedReflexes.Add(reflex);
            reflex.LastActivation = GetAgentLifetime();
          }
        }

        // Сортировка только по крепости связи
        _activeConditionedReflexes.Sort((a, b) =>
            b.AssociationStrength.CompareTo(a.AssociationStrength));
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
      if (!AreStimuliCorrelated(unconditionalStimulusPulse, conditionedStimulusPulse, reflex.TimeWindowPulses))
        return false; // Стимулы не коррелируют во времени

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
      if (AppGlobalState.EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      _lock.EnterWriteLock();
      try
      {
        if (!_conditionedReflexes.ContainsKey(reflexId))
          return false;

        var removed = _conditionedReflexes.Remove(reflexId);
        if (removed)
        {
          _activeConditionedReflexes.RemoveAll(r => r.Id == reflexId);
          OnConditionedReflexDeleted(reflexId);
        }

        return removed;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
      if (!removeAllConditionedReflexes && AppGlobalState.EvolutionStage < 1)
        throw new InvalidOperationException("Условные рефлексы доступны только начиная со стадии 1");

      _lock.EnterWriteLock();
      try
      {
        var deletedReflexIds = _conditionedReflexes.Keys.ToList();

        _conditionedReflexes.Clear();
        _activeConditionedReflexes.Clear();
        _lastConditionedReflexId = 0;

        if (deletedReflexIds.Any())
          OnMultipleConditionedReflexesDeleted(deletedReflexIds);

        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет время жизни агента (вызывается из GlobalTimer при каждом пульсе)
    /// </summary>
  internal void UpdateAgentLifetime()
    {
      try
      {
        _currentAgentLifetime = AppGlobalState.Lifetime;
        ApplyDecay(); // Затухание применяется на каждом пульсе
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        _currentAgentLifetime = 0;
      }
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

    #endregion

    #region Вспомогательные методы

    /// <summary>
    /// Получает текущее значение пульса из глобального таймера
    /// </summary>
    private int GetAgentLifetime()
    {
      return _currentAgentLifetime;
    }

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

    /// <summary>
    /// Получает настройки временного окна корреляции
    /// </summary>
    public int GetTimeWindowPulses()
    {
      return _settings.TimeWindowPulses;
    }

    /// <summary>
    /// Получает минимальную крепость связи
    /// </summary>
    public float GetMinAssociationStrength()
    {
      return _settings.MinAssociationStrength;
    }

    #endregion

    #region Валидация

    private (bool IsValid, string ErrorMessage) ValidateConditionedReflexParameters(
        int level1,
        List<int> level2,
        int level3)
    {
      // Проверка Level1
      var validBaseStates = new[] { -1, 0, 1 };
      if (!validBaseStates.Contains(level1))
        return (false, "Level1 должен быть одним из значений: -1, 0, 1");

      // Проверка Level3 (должен существовать образ восприятия)
      var perceptionImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
      if (!perceptionImages.Any(img => img.Id == level3))
        return (false, $"Level3 (ID образа восприятия {level3}) не найден");

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
            if (parts.Length < 7)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var reflex = new ConditionedReflex
            {
              Id = id,
              Level1 = int.Parse(parts[1]),
              Level2 = AddUtils.ParseIntList(parts[2]),
              Level3 = int.Parse(parts[3]),
              AssociationStrength = float.Parse(parts[4]),
              LastActivation = int.Parse(parts[5]),
              BirthTime = int.Parse(parts[6]),
              SourceGeneticReflexId = parts.Length > 7 ? int.Parse(parts[7]) : 0
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
        Logger.Error(ex.Message);
      }
    }

    private void LoadConditionedReflexSettings()
    {
      string filePath = GetConditionedReflexSettingsFilePath();

      if (!File.Exists(filePath))
      {
        SaveConditionedReflexSettings(); // создаем файл настроек по умолчанию
        return;
      }

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
            case "MaxInactivationTime":
              _settings.MaxInactivationTime = int.Parse(value);
              break;
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
                   $"{reflex.AssociationStrength}|{reflex.LastActivation}|" +
                   $"{reflex.BirthTime}|{reflex.SourceGeneticReflexId}");
        }

        var result = FileValidator.SafeSaveFile(
            GetConditionedReflexesFilePath(),
            lines,
            content => true,
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
            "# MaxInactivationTime: время жизни без активации (пульсы)"
          };

        lines.Add($"LearningRate={_settings.LearningRate}");
        lines.Add($"DecayRate={_settings.DecayRate}");
        lines.Add($"ActivationThreshold={_settings.ActivationThreshold}");
        lines.Add($"TimeWindowMs={_settings.TimeWindowPulses}");
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

        ConditionedReflexCreated = null;
        ConditionedReflexDeleted = null;
        MultipleConditionedReflexesDeleted = null;

        SaveConditionedReflexes();
        SaveConditionedReflexSettings();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion
  }
}