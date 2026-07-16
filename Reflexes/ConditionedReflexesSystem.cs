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

Основана на модели Рескорла-Вагнера с экспоненциальным затуханием:

1. Образование ассоциации (модель Рескорла-Вагнера):
   C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
   где:
     C_ij ∈ [0, β] - крепость связи между стимулами
     α ∈ (0,1) - коэффициент обучения (скорость приближения к асимптоте)
     β = 1.0 - асимптотический максимум крепости
     k - номер подтверждённой пары стимулов

2. Затухание связи:
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

7. Высшие порядки условных рефлексов (second/third-order conditioning):
   Коэффициент понижения K ∈ [1.2, 3.0]:
   - Первичный (порядок 1): образуется от безусловного. Без понижения.
   - Вторичный (порядок 2): образуется от условного 1-го порядка.
     α' = α/K, η' = η^K, начальная крепость /= K
   - Третичный (порядок 3): образуется от условного 2-го порядка.
     α' = α/(K·2), η' = η^(K·2), начальная крепость /= (K·2)
   - При усилении родительского рефлекса каскадно усиливаются дочерние.

8. Суммация крепости при компаундном стимуле (Rescorla, 1997; Weiss, 1972):
   При совместном предъявлении S₁ и S₂, если оба имеют у-рефлексы
   к одному безусловному ответу (одинаковый SourceGeneticReflexId):
   C_combined = min(1.0, Σ C_i)
   Рефлекс активируется, если C_combined ≥ γ (даже если каждый C_i < γ по отдельности)

9. Конкурентное подавление / смешанный ответ (Kamin, 1969; Bouton & Nelson, 1994):
   При совместном предъявлении S₁ и S₂ с у-рефлексами к разным безусловным ответам:
   θ = min(C₁, C₂) / max(C₁, C₂) — отношение крепостей
   Если θ ≥ θ_comp → оба ответа активируются (смешанный ответ)
   Если θ < θ_comp → активируется только сильнейший (конкурентное подавление)

Параметры по умолчанию:
   α = 0.2, β = 1.0, η = 0.98, γ = 0.6, τ = 500 мс, C_min = 0.1, T_base = 1000, K = 1.5, θ_comp = 0.7
*/

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Система управления условными рефлексами симбионта.
  /// Активация по пусковому образу (<see cref="ConditionedReflex.Level3"/>) использует иерархию
  /// «бедный / богатый стимул» через отношение подмножества на <see cref="PerceptionImagesSystem.PerceptionImage"/>
  /// при наличии выученной направленной связи CS₁→CS₂ в <see cref="SensoryAssociationSystem"/>
  /// (крепость ≥ γ). Точное совпадение обрабатывается деревом рефлексов без гейта связи.
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
    /// Условный рефлекс симбионта
    /// </summary>
    public class ConditionedReflex
    {
      private float _learningRate = 0.2f;
      private float _decayRate = 0.98f;
      private float _activationThreshold = 0.6f;
      private int _timeWindowPulses = 5;
      private float _minAssociationStrength = 0.1f;

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
      /// Третий уровень: ID образа пускового стимула (TriggerStimulusID) в <see cref="PerceptionImagesSystem"/> —
      /// действия с пульта, ID фраз и код зрительного канала. Поддерживается иерархия «полный / частичный» образ
      /// (совпадение по подмножеству модальностей при том же цвете), без отдельной сети ассоциаций «стимул–стимул».
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
      /// ID родительского условного рефлекса (0 для первичных — образованных от безусловного)
      /// </summary>
      public int SourceConditionedReflexId { get; set; }

      /// <summary>
      /// Порядок условного рефлекса: 1 — первичный, 2 — вторичный, 3 — третичный
      /// </summary>
      public int Order { get; set; } = 1;

      /// <summary>
      /// ID тона пускового стимула (фразы с пульта). 0 — нормальный.
      /// </summary>
      public int ToneId { get; set; }

      /// <summary>
      /// ID настроения пускового стимула (фразы с пульта). 0 — нормальное.
      /// </summary>
      public int MoodId { get; set; }

      /// <summary>
      /// Максимальная достигнутая крепость связи
      /// </summary>
      public float MaxAchievedStrength { get; private set; }

      /// <summary>
      /// Флаг установившегося рефлекса (когда-либо достигал высокой прочности)
      /// </summary>
      public bool IsEstablished => MaxAchievedStrength > 0.8f;

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
      /// Усиливает ассоциацию по модели Рескорла-Вагнера
      /// </summary>
      public void StrengthenAssociation()
      {
        float effectiveLearningRate = _learningRate;
        if (Order > 1)
        {
          float reductionCoeff = Instance.GetReductionCoefficientForOrder(Order);
          effectiveLearningRate /= reductionCoeff;
        }

        // C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
        float beta = 1.0f; // асимптотический максимум
        AssociationStrength = AssociationStrength + effectiveLearningRate * (beta - AssociationStrength);

        // Обновляем максимальную достигнутую прочность
        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;

        // Обновляем время последней активации
        LastActivation = GetAgentLifetime();
      }

      /// <summary>
      /// Применяет затухание с учетом прочности рефлекса
      /// </summary>
      public void ApplyDecay()
      {
        // Применяем затухание только на каждом 100-м пульсе
        int currentPulse = GetAgentLifetime();
        if (currentPulse % 100 != 0)
          return;

        // Затухание: чем прочнее рефлекс, тем медленнее затухание
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

        // Для вторичных/третичных: ускоренное затухание
        if (Order > 1)
        {
          float reductionCoeff = Instance.GetReductionCoefficientForOrder(Order);
          effectiveDecayRate = (float)Math.Pow(effectiveDecayRate, reductionCoeff);
        }

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
        return AssociationStrength < _minAssociationStrength;
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
    /// Получает текущее время жизни симбионта (кешированное значение)
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
      /// Коэффициент понижения крепости для вторичных условных рефлексов (1.2-3.0).
      /// Для третичных автоматически удваивается.
      /// Влияет на начальную крепость, скорость обучения и скорость затухания.
      /// </summary>
      public float HigherOrderStrengthReductionCoefficient { get; set; } = 1.5f;

      /// <summary>
      /// Порог отношения крепостей для конкурентного подавления θ_comp (0.5-0.9).
      /// Если min(C₁,C₂)/max(C₁,C₂) >= θ_comp — смешанный ответ (оба активируются).
      /// Если ниже — конкурентное подавление (активируется только сильнейший).
      /// </summary>
      public float CompetitionStrengthRatioThreshold { get; set; } = 0.8f;

      /// <summary>
      /// При равной крепости кандидатов на одном уровне иерархии (или внутри группы UR):
      /// true — предпочитать условный рефлекс с меньшим ID; false — с большим ID.
      /// </summary>
      public bool TieBreakPreferSmallerReflexId { get; set; } = true;
    }

    /// <summary>
    /// Режим активации при компаундном стимуле
    /// </summary>
    public enum CompoundActivationMode
    {
      /// <summary>Одиночный рефлекс (компаунд не обнаружен)</summary>
      Single,
      /// <summary>Суммация крепости: оба у-рефлекса к одному безусловному, объединённая крепость</summary>
      Summation,
      /// <summary>Смешанный ответ: оба у-рефлекса к разным безусловным, близкая крепость</summary>
      MixedResponse,
      /// <summary>Конкурентное подавление: активируется только сильнейший</summary>
      CompetitiveSuppression
    }

    /// <summary>
    /// Результат разрешения компаундной активации
    /// </summary>
    public class CompoundActivationResult
    {
      /// <summary>Список у-рефлексов, отобранных для активации</summary>
      public List<ConditionedReflex> ReflexesToActivate { get; set; } = new List<ConditionedReflex>();

      /// <summary>Режим активации (суммация, смешанный ответ, конкурентное подавление)</summary>
      public CompoundActivationMode Mode { get; set; } = CompoundActivationMode.Single;
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
        bool authoritativeMod = false,
        int toneId = 0,
        int moodId = 0,
        int sourceConditionedReflexId = 0)
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
        Level3 = level3,
        ToneId = toneId,
        MoodId = moodId
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

      int newId = 0;
      List<int> level2Copy = null;

      _lock.EnterWriteLock();
      try
      {
        // Определяем порядок нового рефлекса
        int order = 1;
        if (sourceConditionedReflexId > 0)
        {
          if (_conditionedReflexes.TryGetValue(sourceConditionedReflexId, out var parentReflex))
            order = parentReflex.Order + 1;
          else
            order = 2;

          if (order > 3)
          {
            warnings.Add("Невозможно создать условный рефлекс порядка выше третичного.");
            return (0, warnings.ToArray());
          }
        }

        float reductionCoeff = GetReductionCoefficientForOrder(order);

        newId = ++_lastConditionedReflexId;
        int currentLifetime = GetAgentLifetime();
        float _associationStrength = (_settings.MinAssociationStrength + 0.1f) / reductionCoeff;

        if (authoritativeMod)
          _associationStrength = 0.95f / reductionCoeff;

        var conditionedReflex = new ConditionedReflex
        {
          Id = newId,
          Level1 = level1,
          Level2 = level2 ?? new List<int>(),
          Level3 = level3,
          AssociationStrength = _associationStrength,
          LastActivation = currentLifetime,
          BirthTime = currentLifetime,
          SourceGeneticReflexId = sourceGeneticReflexId,
          SourceConditionedReflexId = sourceConditionedReflexId,
          Order = order,
          ToneId = toneId,
          MoodId = moodId
        };

        _conditionedReflexes.Add(newId, conditionedReflex);
        level2Copy = level2?.ToList();
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      // Событие вызываем после снятия блокировки: подписчик (ReflexTreeSystem) вызывает GetAllConditionedReflexes(), которому нужна блокировка чтения
      if (newId > 0)
      {
        try
        {
          OnConditionedReflexCreated(newId, level1, level2Copy ?? level2 ?? new List<int>(), level3);
        }
        catch (Exception ex)
        {
          warnings.Add($"Ошибка при обработке создания условного рефлекса: {ex.Message}");
        }
      }

      return (newId, warnings.ToArray());
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
          StrengthenReflexInternal(reflex);
          CascadeStrengthenChildren(reflex.Id);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Сбрасывает крепость условного рефлекса к начальной (как при создании без authoritative).
    /// Обычно ниже порога активации γ — рефлекс перестаёт срабатывать, пока снова не окрепнет.
    /// </summary>
    /// <param name="reflexId">ID условного рефлекса</param>
    /// <returns>Успех, сообщение, новая крепость (или −1 при ошибке)</returns>
    public (bool Success, string Message, float NewStrength) ResetAssociationStrengthToInitial(int reflexId)
    {
      if (AppGlobalState.EvolutionStage < 1)
        return (false, "Условные рефлексы доступны только начиная со стадии 1", -1f);

      if (reflexId <= 0)
        return (false, "Некорректный ID условного рефлекса", -1f);

      float newStrength;
      _lock.EnterWriteLock();
      try
      {
        if (!_conditionedReflexes.TryGetValue(reflexId, out var reflex))
          return (false, $"Условный рефлекс ID={reflexId} не найден", -1f);

        float reductionCoeff = GetReductionCoefficientForOrder(reflex.Order);
        newStrength = (_settings.MinAssociationStrength + 0.1f) / reductionCoeff;
        if (newStrength < 0f)
          newStrength = 0f;
        if (newStrength > _settings.MaxAssociationStrength)
          newStrength = _settings.MaxAssociationStrength;

        float oldStrength = reflex.AssociationStrength;
        reflex.AssociationStrength = newStrength;
        Logger.Info(
            $"Крепость у-рефлекса ID={reflexId} сброшена оператором: {oldStrength:F3} → {newStrength:F3}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      var save = SaveConditionedReflexes();
      if (!save.Success)
        return (false, "Крепость изменена в памяти, но не сохранена: " + (save.ErrorMessage ?? "ошибка записи"), newStrength);

      return (true, $"Крепость условного рефлекса ID={reflexId} понижена до {newStrength.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)}", newStrength);
    }

    /// <summary>
    /// Усиливает крепость одного рефлекса (без блокировки, вызывается внутри write-lock)
    /// </summary>
    private void StrengthenReflexInternal(ConditionedReflex reflex)
    {
      float reductionCoeff = GetReductionCoefficientForOrder(reflex.Order);
      float effectiveLearningRate = _settings.LearningRate / reductionCoeff;

      // C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1))
      reflex.AssociationStrength = reflex.AssociationStrength +
          effectiveLearningRate * (_settings.MaxAssociationStrength - reflex.AssociationStrength);

      reflex.LastActivation = GetAgentLifetime();
      reflex.AssociationStrength = Math.Min(reflex.AssociationStrength, _settings.MaxAssociationStrength);
    }

    /// <summary>
    /// Каскадное усиление дочерних рефлексов: при усилении первичного
    /// синхронно усиливаются вторичные (с понижающим коэфф.), а от вторичных — третичные.
    /// Вызывается внутри write-lock.
    /// </summary>
    private void CascadeStrengthenChildren(int parentReflexId)
    {
      foreach (var child in _conditionedReflexes.Values)
      {
        if (child.SourceConditionedReflexId == parentReflexId)
        {
          StrengthenReflexInternal(child);
          if (child.Order < 3)
            CascadeStrengthenChildren(child.Id);
        }
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
    /// Проверяет, находятся ли события в пределах временного окна.
    /// Если рефлекс ещё ни разу не активировался (lastActivationPulse == 0), считаем окно пройденным — рефлекс допускается до первой активации.
    /// </summary>
    private bool IsWithinTimeWindow(int currentPulse, int lastActivationPulse, int timeWindowPulses)
    {
      if (lastActivationPulse == 0)
        return true;
      return (currentPulse - lastActivationPulse) <= timeWindowPulses;
    }

    /// <summary>
    /// Находит все условные рефлексы, чей пусковой стимул (Level3) является
    /// компонентом составного (компаундного) стимула.
    /// Используется для механизмов суммации и конкурентного подавления.
    /// </summary>
    public List<ConditionedReflex> FindReflexesForCompoundStimulus(
        int level1, List<int> level2, int compoundImageId)
    {
      var allImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
      var compoundImage = allImages.FirstOrDefault(img => img.Id == compoundImageId);
      if (compoundImage == null)
        return new List<ConditionedReflex>();

      if (PerceptionImagesSystem.CompoundModalityCount(compoundImage) < 2)
        return new List<ConditionedReflex>();

      if (level2 == null || !level2.Any())
        return new List<ConditionedReflex>();

      var sortedLevel2 = level2.OrderBy(x => x).ToList();
      var result = new List<ConditionedReflex>();

      _lock.EnterReadLock();
      try
      {
        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.Level1 != level1) continue;

          var reflexLevel2 = reflex.Level2?.OrderBy(x => x).ToList() ?? new List<int>();
          if (!reflexLevel2.SequenceEqual(sortedLevel2)) continue;

          var reflexImage = allImages.FirstOrDefault(img => img.Id == reflex.Level3);
          if (reflexImage == null) continue;
          if (reflexImage.Id == compoundImageId) continue;

          if (IsImageComponentOf(reflexImage, compoundImage))
            result.Add(reflex);
        }
      }
      finally
      {
        _lock.ExitReadLock();
      }

      return result;
    }

    /// <summary>
    /// Разрешает конфликт при компаундной активации: суммация, смешанный ответ
    /// или конкурентное подавление.
    /// </summary>
    public CompoundActivationResult ResolveCompoundActivation(List<ConditionedReflex> candidates)
    {
      return ResolveCompoundActivation(candidates, null);
    }

    /// <summary>
    /// То же с эффективными крепостями (например после суммации нескольких у-рефлексов на одном UR на уровне иерархии).
    /// </summary>
    public CompoundActivationResult ResolveCompoundActivation(
        List<ConditionedReflex> candidates,
        Dictionary<int, float> effectiveStrengthByReflexId)
    {
      var result = new CompoundActivationResult();
      float Eff(ConditionedReflex r) =>
          effectiveStrengthByReflexId != null &&
          effectiveStrengthByReflexId.TryGetValue(r.Id, out float e)
              ? e
              : r.AssociationStrength;

      ConditionedReflex PickTie(IEnumerable<ConditionedReflex> seq)
      {
        bool sm = _settings.TieBreakPreferSmallerReflexId;
        return seq
            .OrderByDescending(Eff)
            .ThenBy(r => sm ? r.Id : -r.Id)
            .First();
      }

      if (candidates == null || candidates.Count < 2)
      {
        if (candidates?.Count == 1 && Eff(candidates[0]) >= _settings.ActivationThreshold)
          result.ReflexesToActivate.Add(candidates[0]);
        result.Mode = CompoundActivationMode.Single;
        return result;
      }

      var groups = candidates.GroupBy(r => r.SourceGeneticReflexId).ToList();

      if (groups.Count == 1)
      {
        float combinedStrength = Math.Min(1.0f, candidates.Sum(Eff));

        if (combinedStrength >= _settings.ActivationThreshold)
        {
          var best = PickTie(candidates);
          result.ReflexesToActivate.Add(best);
          result.Mode = CompoundActivationMode.Summation;
        }
        return result;
      }

      var groupLeaders = groups
          .Select(g => PickTie(g))
          .OrderByDescending(Eff)
          .ToList();

      float maxStrength = Eff(groupLeaders[0]);
      float secondStrength = Eff(groupLeaders[1]);

      if (maxStrength <= 0)
      {
        result.Mode = CompoundActivationMode.Single;
        return result;
      }

      float ratio = secondStrength / maxStrength;

      if (ratio >= _settings.CompetitionStrengthRatioThreshold)
      {
        result.ReflexesToActivate = groupLeaders
            .Where(r => Eff(r) >= _settings.ActivationThreshold)
            .ToList();
        result.Mode = CompoundActivationMode.MixedResponse;
      }
      else
      {
        if (Eff(groupLeaders[0]) >= _settings.ActivationThreshold)
          result.ReflexesToActivate.Add(groupLeaders[0]);
        result.Mode = CompoundActivationMode.CompetitiveSuppression;
      }

      return result;
    }

    /// <summary>
    /// Подбор условных рефлексов по иерархии специфичности пускового образа (3 → 2 → 1 модальности).
    /// На каждом уровне: сначала суммация крепостей по группам одного безусловного ответа, затем
    /// <see cref="ResolveCompoundActivation(List{ConditionedReflex}, Dictionary{int, float})"/>; если ни одна
    /// группа не достигла порога — один рефлекс с максимальной индивидуальной крепостью (при ничьей — настройка ID).
    /// </summary>
    public CompoundActivationResult ResolveHierarchicalConditionedActivation(
        int level1, List<int> level2, int stimulusImageId)
    {
      var result = new CompoundActivationResult();
      if (stimulusImageId <= 0 || level2 == null || !level2.Any())
        return result;

      var sortedL2 = level2.OrderBy(x => x).ToList();
      var allImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
      var S = allImages.FirstOrDefault(img => img.Id == stimulusImageId);
      if (S == null) return result;

      List<ConditionedReflex> pool;
      _lock.EnterReadLock();
      try
      {
        pool = _conditionedReflexes.Values
            .Where(r => r.Level1 == level1)
            .Where(r =>
                (r.Level2?.OrderBy(x => x).ToList() ?? new List<int>()).SequenceEqual(sortedL2))
            .ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }

      bool sm = _settings.TieBreakPreferSmallerReflexId;
      ConditionedReflex PickByIndividual(IEnumerable<ConditionedReflex> seq) =>
          seq
              .OrderByDescending(r => r.AssociationStrength)
              .ThenBy(r => sm ? r.Id : -r.Id)
              .First();

      for (int reflexTier = 3; reflexTier >= 1; reflexTier--)
      {
        var tierCandidates = pool
            .Where(r =>
            {
              var img = allImages.FirstOrDefault(i => i.Id == r.Level3);
              if (img == null) return false;
              if (PerceptionImagesSystem.GetTriggerSpecificityTier(img) != reflexTier)
                return false;
              if (PerceptionImagesSystem.PerceptionImagesEqual(S, img))
                return false;
              if (!PerceptionImagesSystem.StimulusImagesHierarchyCompatible(S, img))
                return false;
              if (IsPoorStimulusRichReflex(S, img))
              {
                return SensoryAssociationSystem.IsInitialized &&
                       SensoryAssociationSystem.Instance.IsLinkActivatable(S.Id, r.Level3);
              }
              return false;
            })
            .ToList();

        if (!tierCandidates.Any()) continue;

        var geneticGroups = tierCandidates.GroupBy(r => r.SourceGeneticReflexId).ToList();
        var leaders = new List<ConditionedReflex>();
        var effMap = new Dictionary<int, float>();

        foreach (var g in geneticGroups)
        {
          float sum = Math.Min(1f, g.Sum(x => x.AssociationStrength));
          if (sum < _settings.ActivationThreshold) continue;
          var rep = PickByIndividual(g);
          leaders.Add(rep);
          effMap[rep.Id] = sum;
        }

        if (leaders.Any())
          return ResolveCompoundActivation(leaders, effMap);

        var best = PickByIndividual(tierCandidates);
        if (best.CanBeActivated())
        {
          result.ReflexesToActivate.Add(best);
          result.Mode = CompoundActivationMode.Single;
          return result;
        }
      }

      return result;
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
    /// Обновляет время жизни симбионта (вызывается из GlobalTimer при каждом пульсе)
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

    /// <summary>
    /// Пара CSₐ→CSᵦ относится к сенсорной прекондиции: более ранний образ строго беднее
    /// последующего (подмножество модальностей). Такие пары учатся через
    /// <see cref="SensoryAssociationSystem"/> и иерархический гейт, а не через вторичный CR.
    /// </summary>
    public bool IsSensoryPreconditioningPair(int earlierImageId, int laterImageId)
    {
      if (earlierImageId <= 0 || laterImageId <= 0 || earlierImageId == laterImageId)
        return false;

      var allImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
      var earlier = allImages.FirstOrDefault(img => img.Id == earlierImageId);
      var later = allImages.FirstOrDefault(img => img.Id == laterImageId);
      if (earlier == null || later == null)
        return false;

      return IsPoorStimulusRichReflex(earlier, later);
    }

    /// <summary>
    /// Проверяет, является ли стимул S строго беднее пускового образа рефлекса (подмножество, не равенство).
    /// </summary>
    private static bool IsPoorStimulusRichReflex(
        PerceptionImagesSystem.PerceptionImage stimulus,
        PerceptionImagesSystem.PerceptionImage reflexTrigger)
    {
      if (stimulus == null || reflexTrigger == null)
        return false;

      if (PerceptionImagesSystem.PerceptionImagesEqual(stimulus, reflexTrigger))
        return false;

      int sColor = stimulus.VisualColorId;
      int rColor = reflexTrigger.VisualColorId;
      bool colorStimulusSubsetTrigger =
          sColor == AgentVisualColor.White || sColor == rColor;

      return colorStimulusSubsetTrigger &&
          PerceptionImagesSystem.IsIntListSubset(stimulus.InfluenceActionsList, reflexTrigger.InfluenceActionsList) &&
          PerceptionImagesSystem.IsIntListSubset(stimulus.PhraseIdList, reflexTrigger.PhraseIdList);
    }

    /// <summary>
    /// Проверяет, является ли один образ восприятия компонентом (подмножеством) другого.
    /// Компонент — образ, все действия и фразы которого содержатся в составном образе.
    /// </summary>
    private bool IsImageComponentOf(
        PerceptionImagesSystem.PerceptionImage component,
        PerceptionImagesSystem.PerceptionImage compound)
    {
      if (component.VisualColorId != AgentVisualColor.White &&
          component.VisualColorId != compound.VisualColorId)
        return false;

      bool hasActions = component.InfluenceActionsList.Any();
      bool hasPhrases = component.PhraseIdList.Any();

      if (!hasActions && !hasPhrases)
        return true;

      if (hasActions && !component.InfluenceActionsList.All(
          a => compound.InfluenceActionsList.Contains(a)))
        return false;

      if (hasPhrases && !component.PhraseIdList.All(
          p => compound.PhraseIdList.Contains(p)))
        return false;

      return true;
    }

    private bool AreConditionedReflexesEqual(ConditionedReflex a, ConditionedReflex b)
    {
      if (a == null || b == null) return false;
      if (a.Level1 != b.Level1) return false;
      if (a.Level3 != b.Level3) return false;
      if (a.ToneId != b.ToneId || a.MoodId != b.MoodId) return false;
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
    /// Возвращает коэффициент понижения крепости для указанного порядка рефлекса.
    /// Первичный (1) — без понижения.
    /// Вторичный (2) — K.
    /// Третичный (3) — K * 2.
    /// </summary>
    internal float GetReductionCoefficientForOrder(int order)
    {
      if (order <= 1) return 1f;
      float K = _settings.HigherOrderStrengthReductionCoefficient;
      if (order == 2) return K;
      return K * 2; // order >= 3
    }

    /// <summary>
    /// Определяет порядок условного рефлекса, обходя цепочку родителей (до 3 проходов).
    /// 1 — первичный (родитель — безусловный), 2 — вторичный, 3 — третичный.
    /// Возвращает 0 если рефлекс не найден, -1 если глубина больше допустимой.
    /// </summary>
    public int GetReflexOrder(int conditionedReflexId)
    {
      _lock.EnterReadLock();
      try
      {
        // Проход 1: сам рефлекс
        if (!_conditionedReflexes.TryGetValue(conditionedReflexId, out var reflex))
          return 0;
        if (reflex.SourceConditionedReflexId == 0)
          return 1;

        // Проход 2: родительский условный рефлекс
        if (!_conditionedReflexes.TryGetValue(reflex.SourceConditionedReflexId, out var parent))
          return 0;
        if (parent.SourceConditionedReflexId == 0)
          return 2;

        // Проход 3: родитель родителя
        if (!_conditionedReflexes.TryGetValue(parent.SourceConditionedReflexId, out var grandparent))
          return 0;
        if (grandparent.SourceConditionedReflexId == 0)
          return 3;

        // Глубина больше допустимой
        return -1;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает условный рефлекс по ID
    /// </summary>
    public ConditionedReflex GetConditionedReflexById(int reflexId)
    {
      _lock.EnterReadLock();
      try
      {
        return _conditionedReflexes.TryGetValue(reflexId, out var reflex) ? reflex : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
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
              SourceGeneticReflexId = parts.Length > 7 ? int.Parse(parts[7]) : 0,
              ToneId = parts.Length > 8 && int.TryParse(parts[8], out int tid) ? tid : 0,
              MoodId = parts.Length > 9 && int.TryParse(parts[9], out int mid) ? mid : 0,
              SourceConditionedReflexId = parts.Length > 10 && int.TryParse(parts[10], out int scrid) ? scrid : 0,
              Order = parts.Length > 11 && int.TryParse(parts[11], out int ord) ? ord : 1
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

    /// <summary>
    /// Уведомляет подписчиков (дерево рефлексов) о всех уже загруженных условных рефлексах.
    /// Вызывается ReflexTreeSystem после подписки на ConditionedReflexCreated, чтобы создать узлы для рефлексов, загруженных из файла.
    /// </summary>
    public void NotifyTreeOfLoadedReflexes()
    {
      List<(int Id, int Level1, List<int> Level2, int Level3)> toNotify;
      _lock.EnterReadLock();
      try
      {
        toNotify = _conditionedReflexes.Values
          .Select(r => (r.Id, r.Level1, r.Level2 ?? new List<int>(), r.Level3))
          .ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }

      foreach (var (id, level1, level2, level3) in toNotify)
      {
        try
        {
          OnConditionedReflexCreated(id, level1, level2, level3);
        }
        catch (Exception ex)
        {
          Logger.Error($"Ошибка привязки загруженного условного рефлекса {id} к дереву: {ex.Message}");
        }
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
            case "TimeWindowMs":
              _settings.TimeWindowPulses = int.Parse(value);
              break;
            case "HigherOrderStrengthReductionCoefficient":
              _settings.HigherOrderStrengthReductionCoefficient = float.Parse(value);
              break;
            case "CompetitionStrengthRatioThreshold":
              _settings.CompetitionStrengthRatioThreshold = float.Parse(value);
              break;
            case "TieBreakPreferSmallerReflexId":
              _settings.TieBreakPreferSmallerReflexId =
                  value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                  value == "1";
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
          FileHeaders.ConditionedReflexesActions,
          FileHeaders.ConditionedReflexesToneId,
          FileHeaders.ConditionedReflexesMoodId,
          FileHeaders.ConditionedReflexesSourceConditioned,
          FileHeaders.ConditionedReflexesOrder
        };

        foreach (var reflex in _conditionedReflexes.Values.OrderBy(r => r.Id))
        {
          lines.Add($"{reflex.Id}|{reflex.Level1}|" +
                   $"{string.Join(",", reflex.Level2)}|{reflex.Level3}|" +
                   $"{reflex.AssociationStrength}|{reflex.LastActivation}|" +
                   $"{reflex.BirthTime}|{reflex.SourceGeneticReflexId}|{reflex.ToneId}|{reflex.MoodId}|" +
                   $"{reflex.SourceConditionedReflexId}|{reflex.Order}");
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
            "# HigherOrderStrengthReductionCoefficient: коэфф. понижения крепости вторичных (1.2-3.0)",
            "# CompetitionStrengthRatioThreshold: порог отношения крепостей θ_comp для конкурентного подавления (0.5-0.9)",
            "# TieBreakPreferSmallerReflexId: при равной крепости — меньший ID у-рефлекса (true/false)"
          };

        lines.Add($"LearningRate={_settings.LearningRate}");
        lines.Add($"DecayRate={_settings.DecayRate}");
        lines.Add($"ActivationThreshold={_settings.ActivationThreshold}");
        lines.Add($"TimeWindowPulses={_settings.TimeWindowPulses}");
        lines.Add($"HigherOrderStrengthReductionCoefficient={_settings.HigherOrderStrengthReductionCoefficient}");
        lines.Add($"CompetitionStrengthRatioThreshold={_settings.CompetitionStrengthRatioThreshold}");
        lines.Add($"TieBreakPreferSmallerReflexId={_settings.TieBreakPreferSmallerReflexId}");

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
        _instance = null;
      }
    }

    #endregion
  }
}