using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Сенсорная прекондиция: направленная связь CS₁→CS₂ по Рескорла–Вагнеру.
  /// Второй канал обучения, параллельный формированию у-рефлексов. Именно он питает
  /// иерархическую активацию и, при отсутствии гейта, даёт «раздражающие» срабатывания.
  /// </summary>
  public class SensoryAssociationSystemTests : EngineIntegrationTestBase
  {
    public SensoryAssociationSystemTests(EngineFixture engine) : base(engine) { }

    [Fact]
    public void StrengthenLink_CreatesLink()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      Sensory.StrengthenLink(a, b);

      Assert.True(Sensory.TryGetStrength(a, b, out float s));
      Assert.True(s > 0f);
    }

    [Fact]
    public void StrengthenLink_IsDirectional()
    {
      // Связь направленная: a→b не означает b→a.
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      Sensory.StrengthenLink(a, b);

      Assert.True(Sensory.TryGetStrength(a, b, out _));
      Assert.False(Sensory.TryGetStrength(b, a, out _));
    }

    [Fact]
    public void StrengthenLink_SelfLink_Ignored()
    {
      int a = NewPhraseImage();
      Sensory.StrengthenLink(a, a);
      Assert.False(Sensory.TryGetStrength(a, a, out _));
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    [InlineData(5, 0)]
    [InlineData(5, -1)]
    public void StrengthenLink_NonPositiveId_Ignored(int a, int b)
    {
      Sensory.StrengthenLink(a, b);
      Assert.False(Sensory.TryGetStrength(a, b, out _));
    }

    [Fact]
    public void StrengthenLink_Repeated_GrowsByRw()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      float prev = 0f;
      for (int i = 0; i < 5; i++)
      {
        Sensory.StrengthenLink(a, b);
        Sensory.TryGetStrength(a, b, out float s);
        Assert.True(s > prev, $"C должна расти по RW: {prev} → {s}");
        prev = s;
      }
    }

    [Fact]
    public void IsLinkActivatable_BelowThreshold_False()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      Sensory.StrengthenLink(a, b); // C = 0.2 < γ = 0.6

      Assert.False(Sensory.IsLinkActivatable(a, b));
    }

    [Fact]
    public void IsLinkActivatable_AboveThreshold_True()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(a, b);

      Sensory.TryGetStrength(a, b, out float s);
      Assert.True(s >= Settings.ActivationThreshold,
          $"после 10 усилений C={s} должна быть ≥ γ={Settings.ActivationThreshold}");
      Assert.True(Sensory.IsLinkActivatable(a, b));
    }

    [Fact]
    public void ApplyDecay_OffDecayPulse_NoChange()
    {
      // ApplyDecay срабатывает только на пульсах, кратных 100: на пульсе 50 — выход сразу.
      int a = NewPhraseImage();
      int b = NewPhraseImage();

      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(a, b);
      Sensory.TryGetStrength(a, b, out float before);

      Engine.AdvanceToPulse(50); // не кратно 100
      Sensory.ApplyDecay();
      Sensory.TryGetStrength(a, b, out float after);

      Assert.Equal(before, after, 5);
    }

    [Fact]
    public void ApplyDecay_AtDecayPulse_Decays()
    {
      // На пульсе, кратном 100, крепость связи падает (пассивное угасание второго канала).
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(a, b);
      Sensory.TryGetStrength(a, b, out float before);

      Engine.AdvanceToPulse(100);
      Sensory.ApplyDecay();
      Sensory.TryGetStrength(a, b, out float after);

      Assert.True(after < before, $"на пульсе 100 связь должна ослабнуть: {before} → {after}");
    }

    [Fact]
    public void ResetToCleanState_ClearsLinks()
    {
      // Изоляция: конструктор базового класса чистит _links, поэтому словарь пуст.
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      Sensory.StrengthenLink(a, b);
      Assert.True(Sensory.TryGetStrength(a, b, out _));

      Engine.ResetToCleanState();
      Assert.False(Sensory.TryGetStrength(a, b, out _));
    }
  }
}
