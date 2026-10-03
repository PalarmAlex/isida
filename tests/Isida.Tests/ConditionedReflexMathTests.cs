using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Математика условных рефлексов (Rescorla–Wagner, TTL, пороги, компаунд).
  /// Формулы живут внутри <c>ConditionedReflex</c>/<c>ConditionedReflexesSystem</c> и читают
  /// <c>Instance.Settings</c> и пульс движка, поэтому тесты идут через живой синглтон
  /// (Путь Б из задания) с явным управлением пульсом через фикстуру — без правок production-кода.
  /// Защита от регрессий: «мусорные» УР (удаление ниже C_min/по TTL), «раздражающие формы»
  /// (активация только при C ≥ γ), нестабильная активация (компаунд, tie-break).
  /// </summary>
  public class ConditionedReflexMathTests : EngineIntegrationTestBase
  {
    public ConditionedReflexMathTests(EngineFixture engine) : base(engine) { }

    private static ConditionedReflexesSystem.ConditionedReflex NewReflex(float c = 0f)
    {
      return new ConditionedReflexesSystem.ConditionedReflex { AssociationStrength = c };
    }

    // ---------- 1. RW-усиление: C' = C + α·(β − C) ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    public void StrengthenAssociation_FollowsRwSeries(int steps)
    {
      // Регрессия: усиление через StrengthenAssociation обязано совпадать с рядом RW
      // (иначе «крепость растёт не по модели» → непредсказуемые пороги активации).
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false); // C0 = (C_min + 0.1)/K(1) = 0.2
      var reflex = Find(id);
      float c0 = reflex.AssociationStrength;

      for (int i = 0; i < steps; i++)
        Crs.StrengthenAssociation(id);

      Assert.Equal(ExpectedRwStrength(c0, steps), reflex.AssociationStrength, 5);
    }

    [Fact]
    public void StrengthenAssociation_AtSaturation_NoChange()
    {
      // Насыщение: при C = β дальнейшее усиление не меняет C (защита от «выгорания» > 1).
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true); // C = 0.95
      var reflex = Find(id);
      reflex.AssociationStrength = 1.0f;

      Crs.StrengthenAssociation(id);

      Assert.Equal(1.0f, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void StrengthenAssociation_UpdatesMaxAchievedStrength()
    {
      // MaxAchievedStrength — «память» о пике (IsEstablished); при угасании не снижается.
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);
      var reflex = Find(id);
      float before = reflex.MaxAchievedStrength;

      Crs.StrengthenAssociation(id);
      float peak = reflex.AssociationStrength;
      Assert.True(peak > before);
      Assert.Equal(peak, reflex.MaxAchievedStrength, 5);

      reflex.AssociationStrength = 0.05f; // имитация угасания
      Crs.StrengthenAssociation(id);
      Assert.True(reflex.MaxAchievedStrength >= peak,
          "MaxAchieved не должен падать при последующем усилении со дна");
    }

    [Fact]
    public void SecondaryStrengthening_Order2_RateReducedByK()
    {
      // Вторичный УР (order=2) обучается медленнее: α' = α/K(2) < α/K(1).
      // Честное сравнение: одинаковый старт C0, одинаковое число шагов,
      // прирост order=1 обязан строго превышать прирост order=2.
      const int steps = 5;

      int trigger1 = NewActionImage();
      int id1 = AddReflex(trigger1, authoritative: false); // C0 = (Min+0.1)/K(1) = 0.2, Order=1
      float c0 = Find(id1).AssociationStrength;

      int trigger2 = NewActionImage();
      int id2 = AddReflex(trigger2, authoritative: false); // тот же C0 (Order=1 при создании)
      Find(id2).Order = 2;                                  // понижаем скорость обучения

      for (int i = 0; i < steps; i++)
        Crs.StrengthenAssociation(id1);
      float c1 = Find(id1).AssociationStrength;

      for (int i = 0; i < steps; i++)
        Crs.StrengthenAssociation(id2);
      float c2 = Find(id2).AssociationStrength;

      Assert.True(c1 - c0 > c2 - c0,
          "order=1 должен расти быстрее order=2 за одинаковое число шагов");
    }

    // ---------- 2. Активное угасание: C' = C + α_eff·(0 − C), α_eff = α_ext·(1 + 2·drop) ----------

    [Theory]
    // (C, drop = (γ−C)/γ) — проверяем реальный шаг ApplyActiveExtinction против нелинейной формулы.
    [InlineData(0.3f, 0.5f)]   // C = γ/2 → drop = 0.5 → α_eff = 2α
    [InlineData(0.45f, 0.25f)] // C = 3γ/4 → drop = 0.25 → α_eff = 1.5α
    public void ActiveExtinction_NonlinearityByDrop(float c, float drop)
    {
      // Реальный вызов угасания: фактическое падение C обязано соответствовать
      // α_eff = α_ext·(1 + 2·drop), где drop растёт по мере удаления C вниз от γ.
      var reflex = NewReflex(c: c);
      float alphaEff = Settings.ActiveExtinctionRate * (1f + 2f * drop);
      float expected = c - alphaEff * c;

      reflex.ApplyActiveExtinction(Settings.ActiveExtinctionRate);

      Assert.Equal(expected, reflex.AssociationStrength, 5);
      // Чем ниже C относительно γ, тем сильнее нелинейный множитель (быстрее угасание).
      Assert.True(alphaEff >= Settings.ActiveExtinctionRate);
      Assert.True(alphaEff <= 3f * Settings.ActiveExtinctionRate);
    }

    [Fact]
    public void ActiveExtinction_AtThreshold_IsNoOp()
    {
      // Выше/на пороге активное угасание не применяется (там зона пассивного).
      var reflex = NewReflex(c: Settings.ActivationThreshold);
      reflex.ApplyActiveExtinction(Settings.ActiveExtinctionRate);
      Assert.Equal(Settings.ActivationThreshold, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void ActiveExtinction_BelowThreshold_DecreasesTowardZero()
    {
      float gamma = Settings.ActivationThreshold;
      float c = gamma / 2f;
      var reflex = NewReflex(c: c);

      reflex.ApplyActiveExtinction(Settings.ActiveExtinctionRate);

      float alphaEff = Settings.ActiveExtinctionRate * (1f + 2f * 0.5f);
      Assert.Equal(c - alphaEff * c, reflex.AssociationStrength, 5);
      Assert.InRange(reflex.AssociationStrength, 0f, gamma);
    }

    [Fact]
    public void ActiveExtinction_ManySteps_StaysInZeroGamma()
    {
      // После угасания C обязано остаться в [0, γ) — «отрицательная крепость» ломает все пороги.
      var reflex = NewReflex(c: 0.5f);
      for (int i = 0; i < 200; i++)
      {
        reflex.ApplyActiveExtinction(Settings.ActiveExtinctionRate);
        Assert.InRange(reflex.AssociationStrength, 0f, Settings.ActivationThreshold);
      }
    }

    [Fact]
    public void ActiveExtinction_NegativeRate_Clamped()
    {
      // α_ext выходит за [0,1] — формула обязана клампить (защита от битых настроек).
      var reflex = NewReflex(c: 0.3f);
      reflex.ApplyActiveExtinction(-1f);
      Assert.Equal(0.3f, reflex.AssociationStrength, 5); // alpha=0 → no-op

      var reflex2 = NewReflex(c: 0.3f);
      reflex2.ApplyActiveExtinction(2f);
      Assert.InRange(reflex2.AssociationStrength, 0f, 0.3f);
    }

    // ---------- 3. Пассивное угасание: rate = baseRate·(1 − above)·steps ----------

    [Fact]
    public void ApplyPassiveDecay_AtGamma_DecaysByBaseRate()
    {
      // При C = γ ровно один период пассивного угасания → C·(1 − baseRate).
      var settings = Settings;
      Engine.AdvancePulses(1); // now = 1, чтобы аккумулятор инициализировался ненулевым значением
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true); // C = 0.95
      var reflex = Find(id);
      reflex.AssociationStrength = settings.ActivationThreshold; // ровно γ

      Crs.ApplyPassiveDecay(); // первый вызов только инициализирует аккумулятор (now = 1)
      Engine.AdvancePulses(settings.PassiveDecayPeriodPulses); // ровно один шаг
      Crs.ApplyPassiveDecay();

      float baseRate = 1f - settings.DecayRate;
      Assert.Equal(settings.ActivationThreshold * (1f - baseRate), reflex.AssociationStrength, 5);
    }

    [Fact]
    public void ApplyPassiveDecay_FirstCall_OnlyInitializesAccumulator()
    {
      // Регрессия «двойного счёта»: первый за период вызов лишь задаёт точку отсчёта.
      var settings = Settings;
      Engine.AdvancePulses(1); // now = 1, аккумулятор ещё не инициализирован (0)
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);
      var reflex = Find(id);
      reflex.AssociationStrength = settings.ActivationThreshold;

      Crs.ApplyPassiveDecay();

      Assert.Equal(1, reflex.PassiveDecayAccumulator);
      Assert.Equal(settings.ActivationThreshold, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void ApplyPassiveDecay_AtMaxStrength_NoDecay()
    {
      // C = 1: «полностью подтверждённый» рефлекс пассивно не точится.
      var settings = Settings;
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);
      var reflex = Find(id);
      reflex.AssociationStrength = 1f;

      Crs.ApplyPassiveDecay();
      Engine.AdvancePulses(settings.PassiveDecayPeriodPulses);
      Crs.ApplyPassiveDecay();

      Assert.Equal(1f, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void ApplyPassiveDecay_BelowGamma_NotTouched()
    {
      // Ниже γ пассив не работает (зона активного угасания).
      var settings = Settings;
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false); // C = 0.2 < γ
      var reflex = Find(id);
      float before = reflex.AssociationStrength;

      Crs.ApplyPassiveDecay();
      Engine.AdvancePulses(settings.PassiveDecayPeriodPulses * 3);
      Crs.ApplyPassiveDecay();

      Assert.Equal(before, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void ApplyPassiveDecay_ClampedToMinStrength()
    {
      // Даже при агрессивном пассиве C не опускается ниже C_min (ниже — удаление, а не ноль).
      // Мутацию Settings.PassiveDecayPeriodPulses НЕ оборачиваем в try/finally:
      // EngineFixture.RestoreBaselineSettings() в ResetToCleanState следующего теста
      // вернёт период к эталонному (см. CaptureBaselineSettings в EngineFixture).
      var settings = Settings;
      settings.PassiveDecayPeriodPulses = 1; // ускоряем: шаг угасания каждый пульс
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);
      var reflex = Find(id);
      reflex.AssociationStrength = settings.ActivationThreshold + 0.001f;

      Crs.ApplyPassiveDecay(); // инициализация аккумулятора
      for (int i = 0; i < 60; i++)
      {
        Engine.AdvancePulses(1);
        Crs.ApplyPassiveDecay();
        var alive = Find(id);
        if (alive == null) break; // крепость ушла ниже C_min → удаление (легально)
        Assert.True(alive.AssociationStrength >= settings.MinAssociationStrength,
            $"пассив не должен опускать C ниже C_min: {alive.AssociationStrength} < {settings.MinAssociationStrength}");
      }
    }

    // ---------- 4. TTL ----------

    [Fact]
    public void RenewLifetime_DoublesAndShiftsExpiresAt()
    {
      var reflex = NewReflex();
      reflex.InitializeLifetime(now: 0, initialLifetimePulses: 100);

      reflex.RenewLifetime(now: 50);
      Assert.Equal(200, reflex.LifetimePulses);
      Assert.Equal(250, reflex.ExpiresAt);

      reflex.RenewLifetime(now: 100);
      Assert.Equal(400, reflex.LifetimePulses);
      Assert.Equal(500, reflex.ExpiresAt);
    }

    [Fact]
    public void RenewLifetime_RespectsCap()
    {
      int cap = Crs.GetMaxLifetimePulsesCap();
      var reflex = NewReflex();
      reflex.InitializeLifetime(now: 0, initialLifetimePulses: cap); // уже на потолке

      reflex.RenewLifetime(now: 10);

      Assert.Equal(cap, reflex.LifetimePulses);
      Assert.Equal(10 + cap, reflex.ExpiresAt);
    }

    [Fact]
    public void SafeAddLifetime_SaturatesAtIntMax()
    {
      // Проверка арифметики ExpiresAt через публичный InitializeLifetime у «свежего» рефлекса
      // (SafeAddLifetime internal-статика — проверяем еёObservable поведение end-to-end).
      var reflex = NewReflex();
      reflex.InitializeLifetime(now: int.MaxValue - 10, initialLifetimePulses: 1_000_000);
      Assert.Equal(int.MaxValue, reflex.ExpiresAt);

      // Через RenewLifetime (путь усиления/активации) — та же защита от переполнения.
      reflex.LifetimePulses = 500;
      reflex.RenewLifetime(now: int.MaxValue - 100);
      Assert.Equal(int.MaxValue, reflex.ExpiresAt);
    }

    [Theory]
    [InlineData(1)]   // порядок 1 → полный T0
    [InlineData(2)]   // порядок 2 → T0/K
    [InlineData(3)]   // порядок 3 → T0/(2K)
    [InlineData(4)]   // порядок 4+ → тот же знаменатель 2K (защита от выхода за пределы)
    public void GetInitialLifetimeForOrder_DividesByK(int order)
    {
      int t0 = Settings.InitialLifetimePulses;
      float k = Settings.HigherOrderStrengthReductionCoefficient;

      int expected = order <= 1 ? t0
          : order == 2 ? Math.Max(1, (int)(t0 / k))
          : Math.Max(1, (int)(t0 / (k * 2f)));

      Assert.Equal(expected, Crs.GetInitialLifetimeForOrder(order));
    }

    [Fact]
    public void GetInitialLifetimeForOrder_NonPositiveOrder_FullT0()
    {
      Assert.Equal(Settings.InitialLifetimePulses, Crs.GetInitialLifetimeForOrder(0));
      Assert.Equal(Settings.InitialLifetimePulses, Crs.GetInitialLifetimeForOrder(-3));
    }

    // ---------- 5. Удаление / активация ----------

    [Fact]
    public void ShouldBeRemoved_BelowMinStrength_True()
    {
      var reflex = NewReflex(c: Settings.MinAssociationStrength - 0.01f);
      reflex.InitializeLifetime(now: 0, initialLifetimePulses: 1000);
      Assert.True(reflex.ShouldBeRemoved(currentLifetime: 1));
    }

    [Fact]
    public void ShouldBeRemoved_Expired_True()
    {
      var reflex = NewReflex(c: 0.9f);
      reflex.InitializeLifetime(now: 0, initialLifetimePulses: 100);
      Assert.True(reflex.ShouldBeRemoved(currentLifetime: 100));  // now == ExpiresAt → удаляется
      Assert.False(reflex.ShouldBeRemoved(currentLifetime: 99));
    }

    [Fact]
    public void ShouldBeRemoved_Healthy_False()
    {
      var reflex = NewReflex(c: 0.9f);
      reflex.InitializeLifetime(now: 0, initialLifetimePulses: 100);
      Assert.False(reflex.ShouldBeRemoved(currentLifetime: 50));
    }

    [Fact]
    public void CanBeActivated_AboveThresholdAndFresh_True()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);
      Assert.True(Find(id).CanBeActivated());
    }

    [Fact]
    public void CanBeActivated_BelowThreshold_False()
    {
      // Даже при свежем TTL слабый рефлекс не активируется — «раздражающие формы» из слабых CS.
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);
      Assert.False(Find(id).CanBeActivated());
    }

    [Fact]
    public void CanBeActivated_Expired_False()
    {
      int trigger = NewActionImage();
      // Мутацию InitialLifetimePulses восстанавливает RestoreBaselineSettings()
      // в ResetToCleanState следующего теста (страховка EngineFixture), try/finally не нужен.
      Settings.InitialLifetimePulses = 100; // маленький TTL, чтобы проверить протухание
      int id = AddReflex(trigger, authoritative: true);
      var reflex = Find(id);
      Assert.True(reflex.CanBeActivated());

      Engine.AdvancePulses(100); // now == ExpiresAt
      Assert.False(reflex.CanBeActivated());
    }

    [Fact]
    public void RemoveExpiredReflexes_RemovesOnlyExpired()
    {
      // Намеренно НЕ трогаем глобальный InitialLifetimePulses (протухший рефлекс
      // создаём на пульсе 0 с дефолтным TTL, а живой продлеваем усилением).
      int fresh = AddReflex(NewActionImage(), authoritative: true);

      // Усиление продлевает TTL: LifetimePulses 86400 → 172800, ExpiresAt = now + 172800.
      Crs.StrengthenAssociation(fresh);
      int expiresFresh = Find(fresh).ExpiresAt;
      Assert.True(expiresFresh > Settings.InitialLifetimePulses);

      // «Старый» рефлекс: TTL маленький, ExpiresAt = 0 + 10 — протухнет уже на пульсе 10.
      Settings.InitialLifetimePulses = 10;
      int old = AddReflex(NewActionImage(), authoritative: true);
      Assert.Equal(10, Find(old).ExpiresAt);

      Engine.AdvancePulses(100); // old: 100 ≥ 10 → удаляется; fresh: 100 < 172800 → жив
      Crs.RemoveExpiredReflexes();

      Assert.NotNull(Find(fresh));
      Assert.Null(Find(old));
    }

    [Fact]
    public void RemoveExpiredReflexes_RemovesWeakened()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);
      Find(id).AssociationStrength = Settings.MinAssociationStrength - 0.01f;

      Crs.RemoveExpiredReflexes();

      Assert.Null(Find(id));
    }

    // ---------- 6. Коэффициент порядка ----------

    [Theory]
    [InlineData(1, 1.0)]
    [InlineData(2, 1.5)]
    [InlineData(3, 3.0)]
    [InlineData(4, 3.0)] // защита: выше третичного знаменатель не растёт
    [InlineData(0, 1.0)]
    [InlineData(-1, 1.0)]
    public void GetReductionCoefficientForOrder_MatchesSettings(int order, double expected)
    {
      // K = 1.5 по умолчанию: order 1 → 1; order 2 → K; order ≥3 → 2K.
      float k = Settings.HigherOrderStrengthReductionCoefficient;
      Assert.Equal(1.5f, k, 5);
      Assert.Equal(expected, Crs.GetReductionCoefficientForOrder(order), 5);
    }

    // ---------- 7. Корреляция во времени ----------

    [Theory]
    [InlineData(1, 6, 5, true)]    // ровно на границе τ
    [InlineData(6, 1, 5, true)]    // симметричность
    [InlineData(1, 7, 5, false)]   // на 1 пульс за пределами
    [InlineData(7, 1, 5, false)]
    [InlineData(3, 5, 5, true)]    // внутри окна
    [InlineData(5, 5, 5, true)]    // совпадение
    public void AreStimuliCorrelated_Boundary(int p1, int p2, int tau, bool expected)
    {
      Assert.Equal(expected, Crs.AreStimuliCorrelated(p1, p2, tau));
    }

    [Fact]
    public void TryFormAssociation_OutsideWindow_DoesNotStrengthen()
    {
      // Регрессия «усиление вне окна τ»: ассоциация не должна укрепляться, если US опоздал.
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);
      var reflex = Find(id);
      float before = reflex.AssociationStrength;

      bool ok = Crs.TryFormAssociation(
          unconditionalStimulusPulse: 100, conditionedStimulusPulse: 100 - Settings.TimeWindowPulses - 1, reflex);

      Assert.False(ok);
      Assert.Equal(before, reflex.AssociationStrength, 5);
    }

    [Fact]
    public void TryFormAssociation_InsideWindow_Strengthens()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);
      var reflex = Find(id);
      float before = reflex.AssociationStrength;

      bool ok = Crs.TryFormAssociation(
          unconditionalStimulusPulse: 100, conditionedStimulusPulse: 100 - Settings.TimeWindowPulses, reflex);

      Assert.True(ok);
      Assert.True(reflex.AssociationStrength > before);
    }

    // ---------- 8. Компаунд ----------

    [Fact]
    public void ResolveCompound_Empty_SingleNoActivation()
    {
      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>());
      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Single, result.Mode);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void ResolveCompound_Null_SingleNoActivation()
    {
      var result = Crs.ResolveCompoundActivation(null);
      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Single, result.Mode);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void ResolveCompound_SingleAboveThreshold_Activates()
    {
      int id = AddReflex(NewActionImage(), authoritative: true);
      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex> { Find(id) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Single, result.Mode);
      Assert.Equal(new[] { id }, result.ReflexesToActivate.Select(r => r.Id));
    }

    [Fact]
    public void ResolveCompound_SingleBelowThreshold_NotActivated()
    {
      int id = AddReflex(NewActionImage(), authoritative: false);
      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex> { Find(id) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Single, result.Mode);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void ResolveCompound_OneGroup_SummationActivatesOneByTieBreak()
    {
      // Два УР к одному b/у (один SourceGeneticReflexId) — суммация, активируется ОДИН.
      int g = Engine.NextSeed();
      int a = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: g);
      int b = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: g);
      Find(a).AssociationStrength = 0.25f;
      Find(b).AssociationStrength = 0.25f; // Σ = 0.5 < γ=0.6 — не достаточно

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });
      // Инвариант движка: при Σ < γ режим остаётся Single и никто не активируется
      // (Summation выставляется только при успешной суммации).
      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Single, result.Mode);
      Assert.Empty(result.ReflexesToActivate);

      Find(b).AssociationStrength = 0.4f; // Σ = 0.65 ≥ γ
      result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });
      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.Summation, result.Mode);
      Assert.Single(result.ReflexesToActivate);
      Assert.Equal(b, result.ReflexesToActivate[0].Id); // сильнейший
    }

    [Fact]
    public void ResolveCompound_OneGroup_TieBreakPrefersSmallerId()
    {
      Settings.TieBreakPreferSmallerReflexId = true;
      int g = Engine.NextSeed();
      int a = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: g);
      int b = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: g);
      Find(a).AssociationStrength = 0.35f;
      Find(b).AssociationStrength = 0.35f;

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(b), Find(a) }); // намеренно в обратном порядке

      Assert.Single(result.ReflexesToActivate);
      Assert.Equal(a, result.ReflexesToActivate[0].Id);

      Settings.TieBreakPreferSmallerReflexId = false;
      result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });
      Assert.Equal(b, result.ReflexesToActivate[0].Id);
    }

    [Fact]
    public void ResolveCompound_TwoGroups_NearStrength_MixedResponse()
    {
      // θ = min/max ≥ θ_comp → оба активируются (смешанный ответ).
      float theta = Settings.CompetitionStrengthRatioThreshold; // 0.8
      int a = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int b = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      Find(a).AssociationStrength = 0.9f;
      Find(b).AssociationStrength = 0.9f * theta; // ровно на границе θ

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.MixedResponse, result.Mode);
      Assert.Equal(new[] { a, b }, result.ReflexesToActivate.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public void ResolveCompound_TwoGroups_WeakPartner_CompetitiveSuppression()
    {
      // θ < θ_comp → активируется только сильнейший (слабый подавлен).
      int a = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int b = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      Find(a).AssociationStrength = 0.9f;
      Find(b).AssociationStrength = 0.4f; // θ = 0.44 < 0.8

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.CompetitiveSuppression, result.Mode);
      Assert.Equal(new[] { a }, result.ReflexesToActivate.Select(r => r.Id));
    }

    [Fact]
    public void ResolveCompound_TwoGroups_SuppressionOfWeakBelowThreshold_ActivatesNobody()
    {
      // Сильнейший ниже γ — при конкурентном подавлении не активируется никто.
      int a = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: Engine.NextSeed());
      int b = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: Engine.NextSeed());
      Find(a).AssociationStrength = 0.5f;  // < γ
      Find(b).AssociationStrength = 0.1f;  // θ = 0.2 < θ_comp

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.CompetitiveSuppression, result.Mode);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void ResolveCompound_ThreeGroups_RatioByTwoStrongest_MixedKeepsAboveGamma()
    {
      // 3 группы (3 разных UR). Алгоритм компаунда — двухгрупповой: ratio считается
      // по двум сильнейшим лидерам (groupLeaders[0]/[1]), третий в ratio не участвует.
      // Здесь ratio = 0.85/0.9 ≥ θ_comp → MixedResponse; в активацию попадают только
      // лидеры с Eff ≥ γ, поэтому слабый третий (C = 0.3 < γ) отсекается порогом.
      float theta = Settings.CompetitionStrengthRatioThreshold; // 0.8
      int a = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int b = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int c = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      Find(a).AssociationStrength = 0.9f;
      Find(b).AssociationStrength = 0.85f; // ratio = 0.85/0.9 ≈ 0.94 ≥ θ_comp
      Find(c).AssociationStrength = 0.3f;  // ниже γ — в смешанный ответ не войдёт

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b), Find(c) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.MixedResponse, result.Mode);
      Assert.Equal(new[] { a, b }, result.ReflexesToActivate.Select(r => r.Id).OrderBy(x => x));
    }

    [Fact]
    public void ResolveCompound_ThreeGroups_WeakestOfTopTwoDecidesSuppression()
    {
      // Третий (самый слабый) лидер не «спасает» конкуренцию: ratio = второй/первый.
      // Второй (0.4) сильно ниже первого (0.9) → θ < θ_comp → CompetitiveSuppression,
      // активируется только сильнейший; третий (0.3) полностью игнорируется в расчёте.
      int a = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int b = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      int c = AddReflex(NewActionImage(), authoritative: true, sourceGeneticReflexId: Engine.NextSeed());
      Find(a).AssociationStrength = 0.9f;
      Find(b).AssociationStrength = 0.4f; // ratio = 0.4/0.9 ≈ 0.44 < θ_comp
      Find(c).AssociationStrength = 0.3f;

      var result = Crs.ResolveCompoundActivation(new List<ConditionedReflexesSystem.ConditionedReflex>
          { Find(a), Find(b), Find(c) });

      Assert.Equal(ConditionedReflexesSystem.CompoundActivationMode.CompetitiveSuppression, result.Mode);
      Assert.Equal(new[] { a }, result.ReflexesToActivate.Select(r => r.Id));
    }

    [Fact]
    public void ResolveCompound_EffectiveStrengthOverridesActual()
    {
      // Переопределение эффективной крепости (используется иерархией после суммации):
      // сам рефлекс слабый, но «эффективная» крепость группы ≥ γ → активация.
      int id = AddReflex(NewActionImage(), authoritative: false, sourceGeneticReflexId: Engine.NextSeed());
      var reflex = Find(id);
      var eff = new Dictionary<int, float> { [id] = 0.7f };

      var result = Crs.ResolveCompoundActivation(
          new List<ConditionedReflexesSystem.ConditionedReflex> { reflex }, eff);

      Assert.Equal(new[] { id }, result.ReflexesToActivate.Select(r => r.Id));
    }
  }
}
