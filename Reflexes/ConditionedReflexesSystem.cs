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

Основана на модели Рескорла–Вагнера (обучение и активное угасание) плюс
пассивное протухание по динамическому TTL (как в BOT: лимит простоя, а не
ритмический half-life крепости на каждом пульсе).

1. Образование ассоциации (Rescorla–Wagner, λ = β):
   C(k) = C(k-1) + α·(β − C(k-1))
   где:
     C ∈ [0, β] — крепость связи
     α ∈ (0,1) — коэффициент обучения
     β = 1.0 — асимптотический максимум
     k — номер подтверждённой пары CS–US (или вторичного подкрепления)

2. Активное угасание (Rescorla–Wagner, λ = 0):
   C ← C + α_ext·(0 − C)
   Применяется только когда предъявлен CS (первый стимул пары), а US (второй)
   не пришёл в окне τ. Действует на все УР с данным Level3, включая сильные.

3. Пассивное протухание (динамический TTL):
   У каждого УР есть LifetimePulses (текущий лимит простоя) и ExpiresAt
   (абсолютный Lifetime симбионта, после которого рефлекс удаляется).
   При создании: LifetimePulses = T0 (для порядка ≥2: T0/K), ExpiresAt = now + LifetimePulses.
   При каждой активации / усилении: LifetimePulses = min(2·LifetimePulses, MaxCap),
   ExpiresAt = now + LifetimePulses.
   Протухание: now ≥ ExpiresAt → удаление. Крепость C при простое не точится.

4. Активация:
   УР активируется при C ≥ γ и now < ExpiresAt
   (и при компаунде — по правилам суммации/конкуренции).

5. Временное окно корреляции:
   CS и US коррелированы, если интервал между ними ≤ τ пульсов.

6. Удаление:
   Рефлекс удаляется при C < C_min или при now ≥ ExpiresAt.

7. Прочность / консолидация:
   MaxAchievedStrength — максимум достигнутой крепости (не снижается при угасании).
   IsEstablished = (MaxAchievedStrength > 0.8).

8. Высшие порядки (second/third-order conditioning):
   K ∈ [1.2, 3.0]:
   - Порядок 1: от безусловного, без понижения.
   - Порядок 2: α' = α/K, начальная C /= K, начальный TTL /= K.
   - Порядок 3: α' = α/(K·2), начальная C /= (K·2), начальный TTL /= (K·2).
   - При усилении родителя каскадно усиливаются дочерние.

9. Суммация при компаунде (Rescorla, 1997; Weiss, 1972):
   C_combined = min(1.0, Σ C_i); активация при C_combined ≥ γ.

10. Конкурентное подавление / смешанный ответ (Kamin, 1969; Bouton & Nelson, 1994):
    θ = min(C₁,C₂)/max(C₁,C₂); при θ ≥ θ_comp — оба ответа, иначе только сильнейший.

Параметры по умолчанию:
   α = 0.2, β = 1.0, γ = 0.6, τ = 5 пульсов, C_min = 0.1, K = 1.5, θ_comp = 0.8,
   InitialLifetimePulses = 86400, α_ext = 0.05
   (DecayRate η — только для сенсорных ассоциаций CS→CS.)
   Угасание нелинейно: активное — только ниже порога γ (тем быстрее, чем ниже C),
   пассивное — только выше порога γ (раз в PassiveDecayPeriodPulses пульсов, тем медленнее, чем выше C).
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
        // Настройки читаем ДО рефлексов: LoadConditionedReflexes использует _settings
        // (GetInitialLifetimeForOrder/MinAssociationStrength), иначе первый запуск
        // считает ExpiresAt по дефолтам, а не по файлу (см. DEBUG_CASEBOOK_2).
        LoadConditionedReflexSettings();
        LoadConditionedReflexes();
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
      // ВНИМАНИЕ: все параметры модели (α, β, γ, τ, C_min, η, периоды) читаются
      // ИСКЛЮЧИТЕЛЬНО из ConditionedReflexesSystem.Settings (ConditionedReflexSettings.dat).
      // На уровне экземпляра рефлекса хранить их копии нельзя — это порождало рассинхрон
      // «правка файла настроек ничего не меняет» (см. DEBUG_CASEBOOK_1, случай 1 / E2).

      /// <summary>
      /// Накопленный счётчик пульсов жизни для пассивного угасания.
      /// </summary>
      public int PassiveDecayAccumulator { get; set; }

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
      /// Время последней активации (в пульсах жизни симбионта)
      /// </summary>
      public int LastActivation { get; set; }

      /// <summary>
      /// Время создания рефлекса (в пульсах жизни симбионта)
      /// </summary>
      public int BirthTime { get; set; }

      /// <summary>
      /// Текущий лимит простоя (пульсы). При каждой активации удваивается (с потолком).
      /// </summary>
      public int LifetimePulses { get; set; }

      /// <summary>
      /// Абсолютный Lifetime симбионта, при достижении которого рефлекс протухает.
      /// </summary>
      public int ExpiresAt { get; set; }

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
      /// Флаг установившегося рефлекса (когда-либо достигал высокой прочности).
      /// Порог консолидации — из настроек (не захардкожен).
      /// </summary>
      public bool IsEstablished => MaxAchievedStrength > Instance.Settings.EstablishedStrengthThreshold;

      /// <summary>
      /// Усиливает ассоциацию по модели Рескорла-Вагнера
      /// </summary>
      public void StrengthenAssociation()
      {
        // Поправка на порядок (α/K) уже учтена в StrengthenReflexInternal
        // через GetReductionCoefficientForOrder — здесь повторно не делим.
        float effectiveLearningRate = Instance.Settings.LearningRate;

        // C_ij(k) = C_ij(k-1) + α·(β - C_ij(k-1)); β — асимптотический максимум из настроек,
        // как в StrengthenReflexInternal (иначе два пути усиления разойдутся при β≠1).
        float beta = Instance.Settings.MaxAssociationStrength;
        AssociationStrength = AssociationStrength + effectiveLearningRate * (beta - AssociationStrength);
        AssociationStrength = Math.Min(AssociationStrength, beta);

        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;

        RenewLifetime(GetAgentLifetime());
      }

      /// <summary>
      /// Синхронизирует MaxAchievedStrength с текущей крепостью (создание / загрузка).
      /// </summary>
      public void SyncMaxAchievedFromCurrent()
      {
        if (AssociationStrength > MaxAchievedStrength)
          MaxAchievedStrength = AssociationStrength;
      }

      /// <summary>
      /// Задаёт начальный TTL при создании рефлекса.
      /// </summary>
      public void InitializeLifetime(int now, int initialLifetimePulses)
      {
        LifetimePulses = Math.Max(1, initialLifetimePulses);
        LastActivation = now;
        ExpiresAt = SafeAddLifetime(now, LifetimePulses);
      }

      /// <summary>
      /// Продлевает жизнь: удваивает LifetimePulses (с потолком) и сдвигает ExpiresAt от now.
      /// </summary>
      public void RenewLifetime(int now)
      {
        int maxCap = Instance.GetMaxLifetimePulsesCap();
        if (LifetimePulses <= 0)
          LifetimePulses = Math.Max(1, Instance.Settings.InitialLifetimePulses);

        if (LifetimePulses < maxCap)
        {
          long doubled = (long)LifetimePulses * 2L;
          LifetimePulses = doubled > maxCap ? maxCap : (int)doubled;
        }

        LastActivation = now;
        ExpiresAt = SafeAddLifetime(now, LifetimePulses);
      }

      /// <summary>
      /// Активное угасание (RW, λ=0): C ← C + α_ext·(0 − C), нелинейное по крепости.
      /// Чем ниже C относительно порога γ — тем больше скорость угасания.
      /// Применяется только к УР с крепостью ниже порога (γ).
      /// </summary>
      public void ApplyActiveExtinction(float activeExtinctionRate)
      {
        float gamma = Instance.Settings.ActivationThreshold;

        // Выше порога активное угасание не применяется — там действует пассивное.
        if (AssociationStrength >= gamma)
          return;

        float alpha = Math.Min(1f, Math.Max(0f, activeExtinctionRate));
        float gammaSafe = gamma > 1e-6f ? gamma : 1f;

        // Нелинейный множитель: чем ниже C от порога, тем выше скорость (от 1× до 3×).
        float drop = Math.Min(1f, Math.Max(0f, (gamma - AssociationStrength) / gammaSafe));
        float alphaEffective = alpha * (1f + 2f * drop);

        AssociationStrength = AssociationStrength + alphaEffective * (0f - AssociationStrength);
        if (AssociationStrength < 0f)
          AssociationStrength = 0f;
      }

      /// <summary>
      /// True, если рефлекс протух по TTL или крепость ниже C_min.
      /// </summary>
      public bool ShouldBeRemoved(int currentLifetime)
      {
        return AssociationStrength < Instance.Settings.MinAssociationStrength || currentLifetime >= ExpiresAt;
      }

      /// <summary>
      /// True, если крепость ≥ γ и TTL ещё не истёк.
      /// </summary>
      public bool CanBeActivated()
      {
        int now = GetAgentLifetime();
        if (ExpiresAt > 0 && now >= ExpiresAt)
          return false;
        return AssociationStrength >= Instance.Settings.ActivationThreshold;
      }

      private static int SafeAddLifetime(int now, int lifetimePulses)
      {
        long sum = (long)now + lifetimePulses;
        return sum >= int.MaxValue ? int.MaxValue : (int)sum;
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
      /// Коэффициент затухания η для сенсорных ассоциаций CS→CS (0.95-0.99).
      /// Пассив УР — через InitialLifetimePulses (динамический TTL).
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
      /// Начальный лимит простоя УР (пульсы жизни). При каждой активации удваивается.
      /// </summary>
      public int InitialLifetimePulses { get; set; } = 86400;

      /// <summary>
      /// Скорость активного угасания α_ext при CS без US (0.01-0.2).
      /// </summary>
      public float ActiveExtinctionRate { get; set; } = 0.05f;

      /// <summary>
      /// Период пассивного угасания в пульсах (по умолчанию 1 раз в 1000).
      /// </summary>
      public int PassiveDecayPeriodPulses { get; set; } = 1000;

      /// <summary>
      /// Коэффициент понижения крепости для вторичных условных рефлексов (1.2-3.0).
      /// Для третичных автоматически удваивается.
      /// Влияет на начальную крепость, скорость обучения и начальный TTL.
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

      /// <summary>
      /// Включает конкурентный слой обучения (Kamin blocking / ΣV в ошибке Рескорла–Вагнера):
      /// подкрепление λ при CS→US распределяется с учётом того, что тот же US уже
      /// предсказывается другими, более крепкими CS в пределах окна τ. Если false —
      /// прежнее поведение: каждый CS обучается независимо полным α·(β−C) (модель без ΣV).
      /// </summary>
      public bool EnableCompetitiveLearning { get; set; } = true;

      /// <summary>
      /// Доля подавления подкрепления конкурирующими CS (0..1), если
      /// <see cref="EnableCompetitiveLearning"/> включён. 0 — блокировки нет;
      /// 1 — полностью подавляющий конкурент гасит подкрепление шумового CS.
      /// </summary>
      public float CompetitionSuppressionCoefficient { get; set; } = 1.0f;

      /// <summary>
      /// Прибавка к C_min при вычислении стартовой крепости нового УР:
      /// C₀ = (C_min + InitialStrengthBonus) / K(order).
      /// </summary>
      public float InitialStrengthBonus { get; set; } = 0.1f;

      /// <summary>
      /// Крепость, присваиваемая рефлексу при авторитарной (ручной) записи оператора,
      /// до понижающего коэффициента порядка: C₀ = AuthoritativeStrength / K(order).
      /// </summary>
      public float AuthoritativeStrength { get; set; } = 0.95f;

      /// <summary>
      /// Порог консолидации: MaxAchievedStrength выше этого значения ⇒ рефлекс «установившийся»
      /// (IsEstablished).
      /// </summary>
      public float EstablishedStrengthThreshold { get; set; } = 0.8f;

      /// <summary>
      /// Доля α, используемая как слабое подкрепление при успешной активации УР
      /// (α_rein = LearningRate · ActivationReinforcementFraction / K(order)).
      /// </summary>
      public float ActivationReinforcementFraction { get; set; } = 0.25f;

      /// <summary>
      /// Потолок удвоения TTL (LifetimePulses) при активации/усилении.
      /// </summary>
      public int MaxLifetimePulsesCap { get; set; } = 88473600;

      /// <summary>
      /// Резервный период пассивного угасания (пульсов), используемый, если
      /// PassiveDecayPeriodPulses задан неверно (≤ 0).
      /// </summary>
      public int PassiveDecayFallbackPeriodPulses { get; set; } = 1000;

      // ---------- Параметры сенсорных ассоциаций CS→CS (SensoryAssociationSystem) ----------

      /// <summary>
      /// Период затухания сенсорных связей CS→CS в пульсах (ApplyDecay срабатывает на кратных).
      /// </summary>
      public int SensoryDecayPeriodPulses { get; set; } = 100;

      /// <summary>
      /// Нижний предел эффективной крепости для расчёта кривой затухания CS→CS
      /// (страховка от log/sqrt на нуле).
      /// </summary>
      public float SensoryStrengthFloor { get; set; } = 0.1f;

      /// <summary>
      /// Верхняя зона крепости CS→CS: выше порога связь считается устойчивой и точится
      /// по SensoryHighStrengthDecayRate независимо от η.
      /// </summary>
      public float SensoryHighStrengthThreshold { get; set; } = 0.8f;

      /// <summary>
      /// Эффективный коэффициент затухания для устойчивых (высоких) связей CS→CS.
      /// </summary>
      public float SensoryHighStrengthDecayRate { get; set; } = 0.998f;

      /// <summary>
      /// Средняя зона крепости CS→CS: от этого порога до верхней — затухание по η^C,
      /// ниже — по η^√C.
      /// </summary>
      public float SensoryMidStrengthThreshold { get; set; } = 0.4f;

      // ---------- Транзитивное обучение и композиция последовательных CS-пар ----------

      /// <summary>
      /// Включает транзитивное обучение: обход цепочек CS→CS… (A→B и B→C ⇒ A косвенно
      /// предвещает C). Если false — активация допускается только по прямому звену
      /// <see cref="SensoryAssociationSystem.IsLinkActivatable"/> (прежнее поведение).
      /// </summary>
      public bool EnableTransitiveLearning { get; set; } = true;

      /// <summary>
      /// Максимальная глубина обхода цепочки CS-звеньев (число рёбер). Ограничивает
      /// комбинаторный взрыв путей и ложные активации по длинным цепям.
      /// </summary>
      public int TransitiveMaxDepth { get; set; } = 3;

      /// <summary>
      /// Деградация силы пути на каждое звено сверх первого (δ ∈ (0..1]):
      /// сила цепи = Π Cᵢ · δ^(hops−1). Меньше δ — сильнее штраф за длину.
      /// </summary>
      public float TransitiveDecayPerHop { get; set; } = 0.9f;

      /// <summary>
      /// Коэффициент повышения порога для транзитивной цепи (k ≥ 1):
      /// γ_tr = ActivationThreshold · k. Прямое звено остаётся на γ. Больше k —
      /// строже допуск по цепочке (защита от ложных активаций).
      /// </summary>
      public float TransitiveGammaCoefficient { get; set; } = 1.5f;
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
        float _associationStrength =
            (_settings.MinAssociationStrength + _settings.InitialStrengthBonus) / reductionCoeff;

        if (authoritativeMod)
          _associationStrength = _settings.AuthoritativeStrength / reductionCoeff;

        var conditionedReflex = new ConditionedReflex
        {
          Id = newId,
          Level1 = level1,
          Level2 = level2 ?? new List<int>(),
          Level3 = level3,
          AssociationStrength = _associationStrength,
          BirthTime = currentLifetime,
          SourceGeneticReflexId = sourceGeneticReflexId,
          SourceConditionedReflexId = sourceConditionedReflexId,
          Order = order,
          ToneId = toneId,
          MoodId = moodId
        };
        conditionedReflex.SyncMaxAchievedFromCurrent();
        conditionedReflex.InitializeLifetime(currentLifetime, GetInitialLifetimeForOrder(order));

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
    /// Усиливает ассоциацию условного рефлекса по модели Рескорла–Вагнера с явно заданной
    /// скоростью обучения (используется конкурентным слоем: α_eff = α/K · (1 − подавление)).
    /// Каскадно усиливает дочерние рефлексы их штатной скоростью.
    /// </summary>
    public void StrengthenAssociationWithRate(int reflexId, float effectiveLearningRate)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_conditionedReflexes.TryGetValue(reflexId, out var reflex))
        {
          float rate = Math.Min(1f, Math.Max(0f, effectiveLearningRate));
          reflex.AssociationStrength = reflex.AssociationStrength +
              rate * (_settings.MaxAssociationStrength - reflex.AssociationStrength);
          reflex.AssociationStrength = Math.Min(reflex.AssociationStrength, _settings.MaxAssociationStrength);
          reflex.SyncMaxAchievedFromCurrent();
          reflex.RenewLifetime(GetAgentLifetime());
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
        newStrength = (_settings.MinAssociationStrength + _settings.InitialStrengthBonus) / reductionCoeff;
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

      reflex.AssociationStrength = reflex.AssociationStrength +
          effectiveLearningRate * (_settings.MaxAssociationStrength - reflex.AssociationStrength);

      reflex.AssociationStrength = Math.Min(reflex.AssociationStrength, _settings.MaxAssociationStrength);
      reflex.SyncMaxAchievedFromCurrent();
      reflex.RenewLifetime(GetAgentLifetime());
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
    /// Пассивное угасание: раз в PassiveDecayPeriodPulses пульсов, только для УР
    /// с крепостью не ниже порога γ. Чем выше C — тем меньше скорость угасания.
    /// </summary>
    public void ApplyPassiveDecay()
    {
      _lock.EnterWriteLock();
      try
      {
        int now = GetAgentLifetime();
        float gamma = _settings.ActivationThreshold;
        float baseRate = 1f - _settings.DecayRate;

        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.AssociationStrength < gamma)
            continue;

          int period = _settings.PassiveDecayPeriodPulses > 0
              ? _settings.PassiveDecayPeriodPulses
              : _settings.PassiveDecayFallbackPeriodPulses;

          if (reflex.PassiveDecayAccumulator <= 0)
          {
            reflex.PassiveDecayAccumulator = now;
            continue;
          }

          int delta = now - reflex.PassiveDecayAccumulator;
          if (delta < period)
            continue;

          int steps = delta / period;
          reflex.PassiveDecayAccumulator += steps * period;

          // Чем выше C над порогом — тем меньше доля угасания (линейно, до 0 у максимума).
          float above = Math.Min(1f, Math.Max(0f, (reflex.AssociationStrength - gamma) / (1f - gamma)));
          float factor = 1f - above;
          float rate = Math.Min(1f, Math.Max(0f, baseRate * factor)) * steps;

          float oldStrength = reflex.AssociationStrength;
          reflex.AssociationStrength = reflex.AssociationStrength * (1f - rate);
          if (reflex.AssociationStrength < _settings.MinAssociationStrength)
            reflex.AssociationStrength = _settings.MinAssociationStrength;

          Logger.Info($"Крепость у-рефлекса ID={reflex.Id} угашена пассивно: {oldStrength:F3} → {reflex.AssociationStrength:F3}");
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Применяет пассивное угасание «сильных» УР и удаляет протухшие/ослабевшие рефлексы.
    /// </summary>
    public void ApplyDecay()
    {
      ApplyPassiveDecay();
      RemoveExpiredReflexes();
    }

    /// <summary>
    /// Удаляет протухшие и слишком слабые условные рефлексы.
    /// </summary>
    public void RemoveExpiredReflexes()
    {
      _lock.EnterWriteLock();
      try
      {
        int now = GetAgentLifetime();
        var reflexesToRemove = new List<int>();

        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.ShouldBeRemoved(now))
            reflexesToRemove.Add(reflex.Id);
        }

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
    /// Скорость слабого подкрепления при успешной активации (доля от α).
    /// Успешный отклик — не полное подкрепление (US не предъявлен), но частичное
    /// подтверждение предсказания: C подрастает, угасание откладывается.
    /// Доля задаётся настройкой ActivationReinforcementFraction (не захардкожена).
    /// </summary>

    /// <summary>
    /// Реакция на успешную активацию условного рефлекса: продлевает TTL и
    /// добавляет слабое подкрепление C ← C + α_rein·(β − C), α_rein = α/4.
    /// Отклик на CS — частичное подтверждение предсказания, поэтому связь
    /// чуть крепнет. MaxAchievedStrength не повышается, чтобы не «перекрыть»
    /// реальное подкрепление US.
    /// </summary>
    public void NotifyConditionedReflexActivated(int reflexId)
    {
      if (reflexId <= 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        if (_conditionedReflexes.TryGetValue(reflexId, out var reflex))
        {
          reflex.RenewLifetime(GetAgentLifetime());

          // C ← C + α_rein·(β − C), α_rein = α/4 (с понижением по порядку)
          float reductionCoeff = GetReductionCoefficientForOrder(reflex.Order);
          float alphaRein = (_settings.LearningRate * _settings.ActivationReinforcementFraction) / reductionCoeff;
          reflex.AssociationStrength = Math.Min(
              _settings.MaxAssociationStrength,
              reflex.AssociationStrength + alphaRein * (_settings.MaxAssociationStrength - reflex.AssociationStrength));
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Активное угасание всех УР с данным пусковым образом (CS без US в окне τ).
    /// </summary>
    public void ApplyActiveExtinctionForStimulus(int stimulusImageId, int toneId = 0, int moodId = 0)
    {
      if (stimulusImageId <= 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        float alphaExt = _settings.ActiveExtinctionRate;
        var reflexesToRemove = new List<int>();
        int now = GetAgentLifetime();

        foreach (var reflex in _conditionedReflexes.Values)
        {
          if (reflex.Level3 != stimulusImageId)
            continue;
          if (reflex.ToneId != toneId || reflex.MoodId != moodId)
            continue;

          // Активное угасание — только ниже порога; выше порога действует пассивное.
          if (reflex.AssociationStrength >= _settings.ActivationThreshold)
            continue;

          float oldStrength = reflex.AssociationStrength;
          reflex.ApplyActiveExtinction(alphaExt);
          Logger.Info($"Крепость у-рефлекса ID={reflex.Id} угашена активным угасанием: {oldStrength:F3} → {reflex.AssociationStrength:F3}");

          if (reflex.ShouldBeRemoved(now))
            reflexesToRemove.Add(reflex.Id);
        }

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
    /// Начальный TTL с учётом порядка рефлекса (для ≥2 делится на K).
    /// </summary>
    internal int GetInitialLifetimeForOrder(int order)
    {
      int initial = Math.Max(1, _settings.InitialLifetimePulses);
      if (order <= 1)
        return initial;

      float reductionCoeff = GetReductionCoefficientForOrder(order);
      return Math.Max(1, (int)(initial / reductionCoeff));
    }

    /// <summary>
    /// Потолок удвоения TTL.
    /// </summary>
    internal int GetMaxLifetimePulsesCap()
    {
      return _settings.MaxLifetimePulsesCap;
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
          if (!IsWithinTimeWindow(agentLifetime, reflex.LastActivation, _settings.TimeWindowPulses))
            continue;

          // Проверка условий активации и порога крепости
          if (IsReflexConditionsMet(reflex, currentConditions) &&
              reflex.CanBeActivated())
          {
            _activeConditionedReflexes.Add(reflex);
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
                if (!SensoryAssociationSystem.IsInitialized)
                  return false;
                // Прямое звено (гейт γ) либо транзитивная цепь (гейт γ_tr = γ·k).
                return SensoryAssociationSystem.Instance.IsLinkActivatable(S.Id, r.Level3) ||
                       SensoryAssociationSystem.Instance.IsChainActivatable(S.Id, r.Level3);
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
      if (!AreStimuliCorrelated(unconditionalStimulusPulse, conditionedStimulusPulse, _settings.TimeWindowPulses))
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

      bool removed;
      _lock.EnterWriteLock();
      try
      {
        if (!_conditionedReflexes.ContainsKey(reflexId))
          return false;

        removed = _conditionedReflexes.Remove(reflexId);
        if (removed)
          _activeConditionedReflexes.RemoveAll(r => r.Id == reflexId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      // Событие — вне write-lock: подписчик (ReflexTreeSystem) обращается к
      // GetAllConditionedReflexes(), которому нужен read-lock.
      if (removed)
      {
        try
        {
          OnConditionedReflexDeleted(reflexId);
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }

      return removed;
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

      List<int> deletedReflexIds;
      _lock.EnterWriteLock();
      try
      {
        deletedReflexIds = _conditionedReflexes.Keys.ToList();

        _conditionedReflexes.Clear();
        _activeConditionedReflexes.Clear();
        _lastConditionedReflexId = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      // Событие — вне write-lock (см. RemoveConditionedReflex).
      if (deletedReflexIds.Any())
      {
        try
        {
          OnMultipleConditionedReflexesDeleted(deletedReflexIds);
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }

      return true;
    }
    
    /// <summary>
    /// Обновляет время жизни симбионта (вызывается из GlobalTimer при каждом пульсе)
    /// </summary>
  internal void UpdateAgentLifetime()
    {
      try
      {
        int previousLifetime = _currentAgentLifetime;
        _currentAgentLifetime = AppGlobalState.Lifetime;

        // Пассивное угасание: настраиваемая периодичность, по умолчанию раз в 1000 пульсов.
        int period = _settings.PassiveDecayPeriodPulses > 0
            ? _settings.PassiveDecayPeriodPulses
            : _settings.PassiveDecayFallbackPeriodPulses;
        if (_currentAgentLifetime - previousLifetime >= period)
          ApplyPassiveDecay();

        RemoveExpiredReflexes();
        if (ConditionedReflexFormationService.IsInitialized)
          ConditionedReflexFormationService.Instance.ProcessPendingExtinction(_currentAgentLifetime);
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
            reflex.SyncMaxAchievedFromCurrent();
            reflex.PassiveDecayAccumulator = reflex.LastActivation;

            if (parts.Length > 13 &&
                int.TryParse(parts[12], out int lifetimePulses) &&
                int.TryParse(parts[13], out int expiresAt))
            {
              reflex.LifetimePulses = Math.Max(1, lifetimePulses);
              reflex.ExpiresAt = expiresAt;
            }
            else
            {
              int initial = GetInitialLifetimeForOrder(reflex.Order);
              reflex.LifetimePulses = initial;
              long expires = (long)reflex.LastActivation + initial;
              reflex.ExpiresAt = expires >= int.MaxValue ? int.MaxValue : (int)expires;
            }

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

          // Все числовые значения читаются через InvariantCulture: файл — машинный формат,
          // десятичный разделитель обязан быть точкой независимо от культуры ОС
          // (см. DEBUG_CASEBOOK_1, случай 3 / E4).
          var inv = System.Globalization.CultureInfo.InvariantCulture;
          float ParseF() => float.Parse(value, inv);
          int ParseI() => int.Parse(value, inv);

          switch (key)
          {
            case "LearningRate":
              _settings.LearningRate = ParseF();
              break;
            case "DecayRate":
              _settings.DecayRate = ParseF();
              break;
            case "ActivationThreshold":
              _settings.ActivationThreshold = ParseF();
              break;
            case "MinAssociationStrength":
              _settings.MinAssociationStrength = ParseF();
              break;
            case "TimeWindowPulses":
            case "TimeWindowMs":
              _settings.TimeWindowPulses = ParseI();
              break;
            case "PassiveDecayProtectionRatio":
            case "PassiveDecayHalfLifePulses":
              // устаревшие ключи half-life пассива — игнорируем
              break;
            case "InitialLifetimePulses":
            case "BaseInactivationTime":
              _settings.InitialLifetimePulses = ParseI();
              break;
            case "ActiveExtinctionRate":
              _settings.ActiveExtinctionRate = ParseF();
              break;
            case "PassiveDecayPeriodPulses":
              _settings.PassiveDecayPeriodPulses = ParseI();
              break;
            case "HigherOrderStrengthReductionCoefficient":
              _settings.HigherOrderStrengthReductionCoefficient = ParseF();
              break;
            case "CompetitionStrengthRatioThreshold":
              _settings.CompetitionStrengthRatioThreshold = ParseF();
              break;
            case "TieBreakPreferSmallerReflexId":
              _settings.TieBreakPreferSmallerReflexId =
                  value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                  value == "1";
              break;
            case "EnableCompetitiveLearning":
              _settings.EnableCompetitiveLearning =
                  value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                  value == "1";
              break;
            case "CompetitionSuppressionCoefficient":
              _settings.CompetitionSuppressionCoefficient = ParseF();
              break;
            case "InitialStrengthBonus":
              _settings.InitialStrengthBonus = ParseF();
              break;
            case "AuthoritativeStrength":
              _settings.AuthoritativeStrength = ParseF();
              break;
            case "EstablishedStrengthThreshold":
              _settings.EstablishedStrengthThreshold = ParseF();
              break;
            case "ActivationReinforcementFraction":
              _settings.ActivationReinforcementFraction = ParseF();
              break;
            case "MaxLifetimePulsesCap":
              _settings.MaxLifetimePulsesCap = ParseI();
              break;
            case "PassiveDecayFallbackPeriodPulses":
              _settings.PassiveDecayFallbackPeriodPulses = ParseI();
              break;
            case "SensoryDecayPeriodPulses":
              _settings.SensoryDecayPeriodPulses = ParseI();
              break;
            case "SensoryStrengthFloor":
              _settings.SensoryStrengthFloor = ParseF();
              break;
            case "SensoryHighStrengthThreshold":
              _settings.SensoryHighStrengthThreshold = ParseF();
              break;
            case "SensoryHighStrengthDecayRate":
              _settings.SensoryHighStrengthDecayRate = ParseF();
              break;
            case "SensoryMidStrengthThreshold":
              _settings.SensoryMidStrengthThreshold = ParseF();
              break;
            case "EnableTransitiveLearning":
              _settings.EnableTransitiveLearning =
                  value.Equals("true", StringComparison.OrdinalIgnoreCase) || value == "1";
              break;
            case "TransitiveMaxDepth":
              _settings.TransitiveMaxDepth = ParseI();
              break;
            case "TransitiveDecayPerHop":
              _settings.TransitiveDecayPerHop = ParseF();
              break;
            case "TransitiveGammaCoefficient":
              _settings.TransitiveGammaCoefficient = ParseF();
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
          FileHeaders.ConditionedReflexesOrder,
          FileHeaders.ConditionedReflexesLifetime
        };

        foreach (var reflex in _conditionedReflexes.Values.OrderBy(r => r.Id))
        {
          lines.Add($"{reflex.Id}|{reflex.Level1}|" +
                   $"{string.Join(",", reflex.Level2)}|{reflex.Level3}|" +
                   $"{reflex.AssociationStrength}|{reflex.LastActivation}|" +
                   $"{reflex.BirthTime}|{reflex.SourceGeneticReflexId}|{reflex.ToneId}|{reflex.MoodId}|" +
                   $"{reflex.SourceConditionedReflexId}|{reflex.Order}|{reflex.LifetimePulses}|{reflex.ExpiresAt}");
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
            "# DecayRate: η для сенсорных ассоциаций CS→CS (0.95-0.99)",
            "# ActivationThreshold: порог активации γ (0.5-0.7)",
            "# MinAssociationStrength: минимальная крепость C_min (0.01-0.3)",
            "# TimeWindowPulses: временное окно корреляции в пульсах (1-10)",
            "# InitialLifetimePulses: начальный лимит простоя УР; при активации удваивается (3600-604800)",
            "# ActiveExtinctionRate: α_ext активного угасания при CS без US (0.01-0.2)",
            "# PassiveDecayPeriodPulses: период пассивного угасания в пульсах (>=1, по умолчанию 1000)",
            "# HigherOrderStrengthReductionCoefficient: коэфф. понижения крепости вторичных (1.2-3.0)",
            "# CompetitionStrengthRatioThreshold: порог отношения крепостей θ_comp для конкурентного подавления (0.5-0.9)",
            "# TieBreakPreferSmallerReflexId: при равной крепости — меньший ID у-рефлекса (true/false)",
            "# EnableCompetitiveLearning: конкурентный слой обучения (ΣV / Kamin blocking) — true/false",
            "# CompetitionSuppressionCoefficient: доля подавления подкрепления конкурирующими CS (0..1)",
            "# InitialStrengthBonus: прибавка к C_min в стартовой крепости C0=(C_min+bonus)/K",
            "# AuthoritativeStrength: крепость авторитарной записи (до понижения по порядку)",
            "# EstablishedStrengthThreshold: порог MaxAchievedStrength для IsEstablished",
            "# ActivationReinforcementFraction: доля α при слабом подкреплении успешной активации",
            "# MaxLifetimePulsesCap: потолок удвоения TTL при активации/усилении",
            "# PassiveDecayFallbackPeriodPulses: резервный период пассива при PassiveDecayPeriodPulses<=0",
            "# SensoryDecayPeriodPulses: период затухания сенсорных связей CS→CS (пульсы)",
            "# SensoryStrengthFloor: нижний предел крепости для кривой затухания CS→CS",
            "# SensoryHighStrengthThreshold: верхняя зона CS→CS (затухание по SensoryHighStrengthDecayRate)",
            "# SensoryHighStrengthDecayRate: эффективный коэффициент затухания устойчивых связей CS→CS",
            "# SensoryMidStrengthThreshold: средняя зона CS→CS (выше — η^C, ниже — η^√C)",
            "# EnableTransitiveLearning: транзитивное обучение цепочкам CS→CS… (true/false)",
            "# TransitiveMaxDepth: максимальная глубина обхода цепочки CS-звеньев (число рёбер)",
            "# TransitiveDecayPerHop: деградация силы цепи на звено сверх первого δ∈(0..1]",
            "# TransitiveGammaCoefficient: коэффициент повышения порога цепи k (γ_tr = γ·k)"
          };

        // Все значения сериализуются через InvariantCulture (точка как десятичный разделитель) —
        // формат файла не зависит от культуры ОС (см. DEBUG_CASEBOOK_1, случай 3 / E4).
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string F(float v) => v.ToString(inv);

        lines.Add($"LearningRate={F(_settings.LearningRate)}");
        lines.Add($"DecayRate={F(_settings.DecayRate)}");
        lines.Add($"ActivationThreshold={F(_settings.ActivationThreshold)}");
        lines.Add($"MinAssociationStrength={F(_settings.MinAssociationStrength)}");
        lines.Add($"TimeWindowPulses={_settings.TimeWindowPulses.ToString(inv)}");
        lines.Add($"InitialLifetimePulses={_settings.InitialLifetimePulses.ToString(inv)}");
        lines.Add($"ActiveExtinctionRate={F(_settings.ActiveExtinctionRate)}");
        lines.Add($"PassiveDecayPeriodPulses={_settings.PassiveDecayPeriodPulses.ToString(inv)}");
        lines.Add($"HigherOrderStrengthReductionCoefficient={F(_settings.HigherOrderStrengthReductionCoefficient)}");
        lines.Add($"CompetitionStrengthRatioThreshold={F(_settings.CompetitionStrengthRatioThreshold)}");
        lines.Add($"TieBreakPreferSmallerReflexId={_settings.TieBreakPreferSmallerReflexId}");
        lines.Add($"EnableCompetitiveLearning={_settings.EnableCompetitiveLearning}");
        lines.Add($"CompetitionSuppressionCoefficient={F(_settings.CompetitionSuppressionCoefficient)}");
        lines.Add($"InitialStrengthBonus={F(_settings.InitialStrengthBonus)}");
        lines.Add($"AuthoritativeStrength={F(_settings.AuthoritativeStrength)}");
        lines.Add($"EstablishedStrengthThreshold={F(_settings.EstablishedStrengthThreshold)}");
        lines.Add($"ActivationReinforcementFraction={F(_settings.ActivationReinforcementFraction)}");
        lines.Add($"MaxLifetimePulsesCap={_settings.MaxLifetimePulsesCap.ToString(inv)}");
        lines.Add($"PassiveDecayFallbackPeriodPulses={_settings.PassiveDecayFallbackPeriodPulses.ToString(inv)}");
        lines.Add($"SensoryDecayPeriodPulses={_settings.SensoryDecayPeriodPulses.ToString(inv)}");
        lines.Add($"SensoryStrengthFloor={F(_settings.SensoryStrengthFloor)}");
        lines.Add($"SensoryHighStrengthThreshold={F(_settings.SensoryHighStrengthThreshold)}");
        lines.Add($"SensoryHighStrengthDecayRate={F(_settings.SensoryHighStrengthDecayRate)}");
        lines.Add($"SensoryMidStrengthThreshold={F(_settings.SensoryMidStrengthThreshold)}");
        lines.Add($"EnableTransitiveLearning={_settings.EnableTransitiveLearning}");
        lines.Add($"TransitiveMaxDepth={_settings.TransitiveMaxDepth.ToString(inv)}");
        lines.Add($"TransitiveDecayPerHop={F(_settings.TransitiveDecayPerHop)}");
        lines.Add($"TransitiveGammaCoefficient={F(_settings.TransitiveGammaCoefficient)}");

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