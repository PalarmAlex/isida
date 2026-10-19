using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Транзитивное обучение и композиция последовательных CS-пар: обход цепочек
  /// CS→CS… (A→B и B→C ⇒ A косвенно предвещает C) по индексу исходящих рёбер.
  /// Сила пути = Π Cᵢ · δ^(hops−1); гейт активации цепи — γ_tr = γ·k (строже прямого звена).
  /// Проверяются сам обход (<see cref="SensoryAssociationSystem.TryGetChainStrength"/>),
  /// гейт (<see cref="SensoryAssociationSystem.IsChainActivatable"/>), защита от циклов,
  /// ограничение глубины, согласованность индекса и интеграция с иерархией активации УР.
  /// </summary>
  public class SensoryTransitiveChainTests : EngineIntegrationTestBase
  {
    public SensoryTransitiveChainTests(EngineFixture engine) : base(engine) { }

    /// <summary>RW-усиление звена <paramref name="n"/> раз (α=0.2, β=1 ⇒ Cₙ = 1 − 0.8ⁿ).</summary>
    private void Strengthen(int earlier, int later, int n)
    {
      for (int i = 0; i < n; i++)
        Sensory.StrengthenLink(earlier, later);
    }

    [Fact]
    public void TryGetChainStrength_TwoHop_ReturnsProductWithDecay()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Sensory.TryGetStrength(a, b, out float cab);
      Sensory.TryGetStrength(b, c, out float cbc);

      Assert.True(Sensory.TryGetChainStrength(a, c, out float strength, out int hops));
      Assert.Equal(2, hops);
      // Сила цепи = Π Cᵢ · δ^(hops−1) = C_ab · C_bc · δ.
      float expected = cab * cbc * Settings.TransitiveDecayPerHop;
      Assert.Equal(expected, strength, 5);
    }

    [Fact]
    public void TryGetChainStrength_DirectLinkOnly_NotAChain()
    {
      // Прямое звено (1 ребро) не считается цепью: длина должна быть ≥ 2.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      Strengthen(a, b, 10);

      Assert.False(Sensory.TryGetChainStrength(a, b, out _, out _));
    }

    [Fact]
    public void TryGetChainStrength_NoConnectingPath_False()
    {
      // A→B и C→D изолированы: пути A→…→D нет.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      int d = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(c, d, 5);

      Assert.False(Sensory.TryGetChainStrength(a, d, out _, out _));
    }

    [Fact]
    public void TryGetChainStrength_ReversedDirection_False()
    {
      // Цепочка направленная: A→B→C не даёт C→…→A.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Assert.True(Sensory.TryGetChainStrength(a, c, out _, out _));
      Assert.False(Sensory.TryGetChainStrength(c, a, out _, out _));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void TryGetChainStrength_NonPositiveId_False(int a, int b)
    {
      Assert.False(Sensory.TryGetChainStrength(a, b, out _, out _));
    }

    [Fact]
    public void TryGetChainStrength_SameNode_False()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, a, 5);

      Assert.False(Sensory.TryGetChainStrength(a, a, out _, out _));
    }

    [Fact]
    public void TryGetChainStrength_PicksStrongestPath()
    {
      // Два пути A→B→C и A→D→C: выбирается путь с большим произведением крепостей.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      int d = NewPhraseImage();
      Strengthen(a, b, 10); // сильное первое звено
      Strengthen(b, c, 10);
      Strengthen(a, d, 2);  // слабое альтернативное звено
      Strengthen(d, c, 2);

      Sensory.TryGetStrength(a, b, out float cab);
      Sensory.TryGetStrength(b, c, out float cbc);
      float delta = Settings.TransitiveDecayPerHop;

      Assert.True(Sensory.TryGetChainStrength(a, c, out float strength, out int hops));
      Assert.Equal(2, hops);
      Assert.Equal(cab * cbc * delta, strength, 5);
    }

    [Fact]
    public void TryGetChainStrength_RespectsMaxDepth()
    {
      // A→B→C→D: при maxDepth=3 путь из 3 рёбер найден, при maxDepth=2 — нет.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      int d = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);
      Strengthen(c, d, 5);

      Settings.TransitiveMaxDepth = 3;
      Assert.True(Sensory.TryGetChainStrength(a, d, out _, out int hops));
      Assert.Equal(3, hops);

      Settings.TransitiveMaxDepth = 2;
      Assert.False(Sensory.TryGetChainStrength(a, d, out _, out _));
    }

    [Fact]
    public void TryGetChainStrength_CycleProtected()
    {
      // A↔B с обратным ребром B→A и продолжением B→C: обход не зацикливается
      // (visited по текущему пути), путь A→B→C длиной 2 находится.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, a, 5);
      Strengthen(b, c, 5);

      Assert.True(Sensory.TryGetChainStrength(a, c, out _, out int hops));
      Assert.Equal(2, hops);
    }

    [Fact]
    public void TryGetChainStrength_DecayPerHopReducesStrength()
    {
      // δ штрафует длину: для одного и того же графа δ=0.5 даёт силу вдвое меньше δ=1.0.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Settings.TransitiveDecayPerHop = 1.0f;
      Sensory.TryGetChainStrength(a, c, out float noDecay, out _);

      Settings.TransitiveDecayPerHop = 0.5f;
      Sensory.TryGetChainStrength(a, c, out float halfDecay, out _);

      Assert.Equal(noDecay * 0.5f, halfDecay, 5);
    }

    [Fact]
    public void TryGetChainStrength_IndexUpdatedOnStrengthen()
    {
      // Индекс исходящих рёбер синхронизируется инкрементально при усилении.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 3);
      Strengthen(b, c, 3);
      Sensory.TryGetChainStrength(a, c, out float before, out _);

      Strengthen(a, b, 5); // усиление первого звена после построения индекса
      Assert.True(Sensory.TryGetChainStrength(a, c, out float after, out _));
      Assert.True(after > before, $"после усиления звена сила цепи должна вырасти: {before} → {after}");
    }

    [Fact]
    public void TryGetChainStrength_IndexRebuiltOnLoad()
    {
      // Load() строит индекс один раз после чтения всего графа — цепочка работает после перезагрузки.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Assert.True(Sensory.Save().Success);
      Sensory.Load();

      Assert.True(Sensory.TryGetChainStrength(a, c, out _, out int hops));
      Assert.Equal(2, hops);
    }

    [Fact]
    public void TryGetChainStrength_IndexConsistentAfterDecay()
    {
      // ApplyDecay() полностью перестраивает индекс после удаления слабых звеньев;
      // крепкие звенья остаются, цепочка по-прежнему обходится.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Engine.AdvanceToPulse(1000); // кратно SensoryDecayPeriodPulses
      Sensory.ApplyDecay();

      Assert.True(Sensory.TryGetChainStrength(a, c, out _, out int hops));
      Assert.Equal(2, hops);
    }

    [Fact]
    public void IsChainActivatable_Disabled_False()
    {
      // Гейт транзитивного обучения выключен — цепь не участвует в активации.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Settings.EnableTransitiveLearning = false;
      Assert.False(Sensory.IsChainActivatable(a, c));
    }

    [Fact]
    public void IsChainActivatable_NoChain_False()
    {
      // Прямое звено A→C (1 ребро) не даёт транзитивной активации.
      int a = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, c, 10);

      Assert.True(Sensory.IsLinkActivatable(a, c));
      Assert.False(Sensory.IsChainActivatable(a, c));
    }

    [Fact]
    public void IsChainActivatable_BelowGammaTr_False()
    {
      // По умолчанию γ_tr = γ·k = 0.6·1.0 = 0.6; слабая цепь (4 усиления) ниже порога.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 4);
      Strengthen(b, c, 4);

      Sensory.TryGetChainStrength(a, c, out float strength, out _);
      float gammaTr = Settings.ActivationThreshold * Settings.TransitiveGammaCoefficient;
      Assert.True(strength < gammaTr, $"сила цепи {strength} должна быть ниже γ_tr={gammaTr}");
      Assert.False(Sensory.IsChainActivatable(a, c));
    }

    [Fact]
    public void IsChainActivatable_AboveGammaTr_True()
    {
      // Ослабляем гейт (k=1) и убираем штраф за длину (δ=1): сильная цепь проходит порог γ.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Settings.TransitiveGammaCoefficient = 1.0f;
      Settings.TransitiveDecayPerHop = 1.0f;

      Sensory.TryGetChainStrength(a, c, out float strength, out _);
      float gammaTr = Settings.ActivationThreshold * Settings.TransitiveGammaCoefficient;
      Assert.True(strength >= gammaTr, $"сила цепи {strength} должна быть ≥ γ_tr={gammaTr}");
      Assert.True(Sensory.IsChainActivatable(a, c));
    }

    [Fact]
    public void IsChainActivatable_DirectGateUnaffected()
    {
      // Транзитивные настройки не меняют порог прямого звена: γ остаётся γ.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      Strengthen(a, b, 5); // C ≈ 0.67 ≥ γ=0.6

      Settings.TransitiveGammaCoefficient = 3.0f;
      Settings.TransitiveMaxDepth = 1;

      Assert.True(Sensory.IsLinkActivatable(a, b));
    }

    [Fact]
    public void HierarchicalActivation_TransitiveChain_ActivatesRichReflex()
    {
      // Бедный стимул активирует богатый рефлекс через цепь poor→mid→rich
      // (прямой связи poor→rich нет). Гейт цепи ослаблен, чтобы сильная цепь прошла γ_tr.
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int mid = NewPhraseImage();
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      int id = AddReflex(rich, authoritative: true, level2: new System.Collections.Generic.List<int> { Engine.SeedStyleId });

      Settings.TransitiveGammaCoefficient = 1.0f;
      Settings.TransitiveDecayPerHop = 1.0f;
      Strengthen(poor, mid, 10);
      Strengthen(mid, rich, 10);

      Assert.False(Sensory.TryGetStrength(poor, rich, out _)); // прямой связи нет
      Assert.True(Sensory.IsChainActivatable(poor, rich));

      var result = Crs.ResolveHierarchicalConditionedActivation(
          0, new System.Collections.Generic.List<int> { Engine.SeedStyleId }, poor);
      Assert.Contains(result.ReflexesToActivate, r => r.Id == id);
    }

    [Fact]
    public void HierarchicalActivation_ChainBelowGammaTr_NotActivated()
    {
      // Цепь poor→mid→rich существует, но её сила ниже γ_tr по умолчанию — иерархия не срабатывает.
      // При k=1 (дефолт) γ_tr = 0.6; 4 усиления дают силу цепи ≈ 0.31 < 0.6.
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int mid = NewPhraseImage();
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: true, level2: new System.Collections.Generic.List<int> { Engine.SeedStyleId });

      Strengthen(poor, mid, 4);
      Strengthen(mid, rich, 4);

      Assert.True(Sensory.TryGetChainStrength(poor, rich, out _, out _));
      Assert.False(Sensory.IsChainActivatable(poor, rich));

      var result = Crs.ResolveHierarchicalConditionedActivation(
          0, new System.Collections.Generic.List<int> { Engine.SeedStyleId }, poor);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void IsChainActivatable_GammaCoefficientBelowOne_ClampedToGamma()
    {
      // При TransitiveGammaCoefficient < 1 метод IsChainActivatable клемпит k до 1:
      // γ_tr = γ·k, k<1 → k=1. Цепь с силой ≥ γ должна стать активируемой,
      // так как эффективный порог = γ, а не γ·k.
      // Регресс: «k<1 ломает гейт» — проверка корректности клемпинга.
      // Усиливаем 10 раз (C≈0.893), δ=1.0: цепь = 0.893·0.893·1 = 0.797 ≥ γ=0.6 → активна.
      Settings.TransitiveGammaCoefficient = 0.5f;
      Settings.TransitiveDecayPerHop = 1.0f;

      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Sensory.TryGetChainStrength(a, c, out float strength, out _);
      float gamma = Settings.ActivationThreshold; // γ_tr = γ·1 = γ при k клемпится до 1
      Assert.True(strength >= gamma,
          $"сила цепи {strength} должна быть ≥ γ={gamma} при k=0.5 (кемп 1)");
      Assert.True(Sensory.IsChainActivatable(a, c),
          "цепь должна быть активируема: γ_tr клемпится до γ, сила ≥ γ");
    }

    [Fact]
    public void TryGetChainStrength_DecayPerHopOutOfRange_DefaultsToOne()
    {
      // δ вне (0, 1] → клемпится к 1 методом FindBestChain.
      // Сила цепи при δ=0, δ=-1, δ=2 должна совпадать с δ=1.0. Регресс: «битые δ ломают силу».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Settings.TransitiveDecayPerHop = 1.0f;
      Sensory.TryGetChainStrength(a, c, out float strengthNormal, out _);

      Settings.TransitiveDecayPerHop = 0f;
      Sensory.TryGetChainStrength(a, c, out float strengthZero, out _);

      Settings.TransitiveDecayPerHop = -1f;
      Sensory.TryGetChainStrength(a, c, out float strengthNeg, out _);

      Settings.TransitiveDecayPerHop = 2f;
      Sensory.TryGetChainStrength(a, c, out float strengthHigh, out _);

      Assert.Equal(strengthNormal, strengthZero, 5);
      Assert.Equal(strengthNormal, strengthNeg, 5);
      Assert.Equal(strengthNormal, strengthHigh, 5);
    }

    [Fact]
    public void TryGetChainStrength_MaxDepthNonPositive_DefaultsToThree()
    {
      // TransitiveMaxDepth ≤ 0 → дефолт = 3. Путь A→B→C→D (3 ребра) должен найтись.
      // Регресс: «maxDepth=0 блокирует цепь любой длины».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      int d = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);
      Strengthen(c, d, 5);

      Settings.TransitiveMaxDepth = 0;
      Assert.True(Sensory.TryGetChainStrength(a, d, out _, out int hopsZero),
          "maxDepth=0 → дефолт 3, путь из 3 рёбер должен найтись");
      Assert.Equal(3, hopsZero);

      Settings.TransitiveMaxDepth = -5;
      Assert.True(Sensory.TryGetChainStrength(a, d, out _, out int hopsNeg),
          "maxDepth=-5 → дефолт 3, путь из 3 рёбер должен найтись");
      Assert.Equal(3, hopsNeg);
    }

    [Fact]
    public void TryGetChainStrength_ThreeHop_ProductWithQuadraticDecay()
    {
      // A→B→C→D по 5 усилений. При hops=3 показатель степени δ равен hops−1 = 2,
      // то есть проверяется квадратичный штраф: сила = C_ab · C_bc · C_cd · δ².
      // Регресс: «hops=3 даёт δ¹ вместо δ²» — неверный показатель степени штрафа за длину.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      int d = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);
      Strengthen(c, d, 5);

      Settings.TransitiveDecayPerHop = 0.5f;
      Sensory.TryGetStrength(a, b, out float cab);
      Sensory.TryGetStrength(b, c, out float cbc);
      Sensory.TryGetStrength(c, d, out float ccd);

      Sensory.TryGetChainStrength(a, d, out float strength, out int hops);
      Assert.Equal(3, hops);
      // δ² = 0.5² = 0.25.
      float expected = cab * cbc * ccd * 0.25f;
      Assert.Equal(expected, strength, 5);
    }

    [Fact]
    public void TryGetAssociability_TransitiveDisabled_ReturnsFalse()
    {
      // При EnableTransitiveLearning=false TryGetAssociability не смотрит на цепь —
      // возвращает false, если прямого звена нет. Регресс: «отключённое транзитивное обучение
      // всё ещё находит цепь через TryGetAssociability».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Settings.EnableTransitiveLearning = false;

      Assert.False(Sensory.TryGetAssociability(a, c, out float assoc),
          "при отключённом транзитивном обучении готовность по цепи должна быть false");
      Assert.Equal(0f, assoc);
    }

    [Fact]
    public void PenalizeLinkByOperator_TransitiveChain_DisablesChainActivation()
    {
      // Штраф транзитивной цепи (через PenalizeChainEdges) умножает каждое ребро на 0.3.
      // После штрафа сила цепи падает ниже γ_tr → IsChainActivatable = false.
      // Регресс: «PenalizeLinkByOperator не влияет на IsChainActivatable».
      Settings.TransitiveGammaCoefficient = 1.0f;
      Settings.TransitiveDecayPerHop = 1.0f;

      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Assert.True(Sensory.IsChainActivatable(a, c), "цепь должна быть активируема до штрафа");

      var result = Sensory.PenalizeLinkByOperator(a, c);
      Assert.True(result.Success, "PenalizeLinkByOperator должен сработать по транзитивной цепи");

      Assert.False(Sensory.IsChainActivatable(a, c),
          "после штрафа сила цепи падает ниже γ_tr → неактивируема");
    }

    [Fact]
    public void PenalizeLinkByOperator_TransitiveChain_RemovesWeakEdgeKeepsStrong()
    {
      // Штраф цепи умножает крепость каждого звена на 0.3 и удаляет звенья ниже C_min.
      // A→B усилено 1 раз (C=0.2 → после штрафа 0.06 < 0.1, удаляется),
      // B→C усилено 10 раз (C≈0.893 → 0.268 ≥ 0.1, остаётся).
      // Регресс: «штраф цепи не удаляет слабое звено» и «штраф рушит сильное звено».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 1);
      Strengthen(b, c, 10);

      var result = Sensory.PenalizeLinkByOperator(a, c);
      Assert.True(result.Success, "штраф по транзитивной цепи должен сработать");

      Assert.False(Sensory.TryGetStrength(a, b, out _), "слабое звено A→B должно быть удалено");
      Assert.True(Sensory.TryGetStrength(b, c, out _), "сильное звено B→C должно остаться");
      Assert.False(Sensory.IsChainActivatable(a, c), "после удаления слабого звена цепь разрушена");
    }

    [Fact]
    public void PenalizeLinkByOperator_TransitiveDisabled_MissingLink_Fails()
    {
      // Прямое звено A→C отсутствует, EnableTransitiveLearning=false →
      // PenalizeLinkByOperator не ищет цепь, возвращает (false, ...).
      // Регресс: «PenalizeLinkByOperator ищет цепь даже при отключённом транзитивном обучении».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 5);
      Strengthen(b, c, 5);

      Settings.EnableTransitiveLearning = false;

      var result = Sensory.PenalizeLinkByOperator(a, c);
      Assert.False(result.Success,
          "при отключённом транзитивном обучении и отсутствии прямого звена штраф должен провалиться");
    }
  }
}
