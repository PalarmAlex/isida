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

      Engine.AdvanceToPulse(100); // кратно SensoryDecayPeriodPulses
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
      // По умолчанию γ_tr = γ·k = 0.6·1.5 = 0.9; сила цепи 2 звена (≈0.8² · 0.9) ниже порога.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

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
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int mid = NewPhraseImage();
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: true, level2: new System.Collections.Generic.List<int> { Engine.SeedStyleId });

      Strengthen(poor, mid, 10);
      Strengthen(mid, rich, 10);

      Assert.True(Sensory.TryGetChainStrength(poor, rich, out _, out _));
      Assert.False(Sensory.IsChainActivatable(poor, rich));

      var result = Crs.ResolveHierarchicalConditionedActivation(
          0, new System.Collections.Generic.List<int> { Engine.SeedStyleId }, poor);
      Assert.Empty(result.ReflexesToActivate);
    }
  }
}
