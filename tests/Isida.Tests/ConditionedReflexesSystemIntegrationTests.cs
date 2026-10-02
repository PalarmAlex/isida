using System;
using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Интеграционные тесты модели угасания условных рефлексов (Случай 1/2 из DEBUG_CASEBOOK_1.md):
  /// активное угасание только ниже порога γ; «сильные» (≥ γ) активно не угасают.
  /// Наследует <see cref="EngineIntegrationTestBase"/> — детерминированные образы
  /// (без <c>Environment.TickCount</c>) и изоляция состояния между тестами.
  /// </summary>
  public class ConditionedReflexesSystemIntegrationTests : EngineIntegrationTestBase
  {
    public ConditionedReflexesSystemIntegrationTests(EngineFixture engine) : base(engine) { }

    [Fact]
    public void AuthoritativeReflex_AboveThreshold_NotExtinguishedActively()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);

      var reflex = Find(id);
      Assert.NotNull(reflex);
      float before = reflex.AssociationStrength;
      Assert.True(before >= Settings.ActivationThreshold);

      Crs.ApplyActiveExtinctionForStimulus(trigger);

      Assert.Equal(before, Find(id).AssociationStrength, 4);
      Assert.True(Find(id).CanBeActivated());
    }

    [Fact]
    public void WeakReflex_BelowThreshold_ExtinguishedActively()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);

      var reflex = Find(id);
      float before = reflex.AssociationStrength;
      Assert.True(before < Settings.ActivationThreshold);

      Crs.ApplyActiveExtinctionForStimulus(trigger);

      float after = Find(id).AssociationStrength;
      Assert.True(after < before, $"крепость должна упасть: {before} → {after}");
      Assert.True(after >= 0f);
    }

    [Fact]
    public void Extinction_IgnoresOtherTrigger()
    {
      int trigger = NewActionImage();
      int otherTrigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: false);

      float before = Find(id).AssociationStrength;
      Crs.ApplyActiveExtinctionForStimulus(otherTrigger);

      Assert.Equal(before, Find(id).AssociationStrength, 4);
    }

    [Fact]
    public void ActiveExtinction_OnReflex_IsNoOpAboveThreshold()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);

      var reflex = Find(id);
      float before = reflex.AssociationStrength;
      reflex.ApplyActiveExtinction(Settings.ActiveExtinctionRate);

      Assert.Equal(before, reflex.AssociationStrength, 4);
    }

    [Fact]
    public void InitialLifetime_ComesFromSettings_AndDividesByOrder()
    {
      Assert.Equal(Settings.InitialLifetimePulses, Crs.GetInitialLifetimeForOrder(1));

      int expectedSecondary = Math.Max(1,
          (int)(Settings.InitialLifetimePulses / Settings.HigherOrderStrengthReductionCoefficient));
      Assert.Equal(expectedSecondary, Crs.GetInitialLifetimeForOrder(2));
    }

    [Fact]
    public void AddConditionedReflex_Duplicate_ReturnsZero()
    {
      int trigger = NewActionImage();
      int first = AddReflex(trigger, authoritative: true);
      Assert.True(first > 0);

      var (second, warnings) = Crs.AddConditionedReflex(
          0, DefaultLevel2.ToList(), trigger, sourceGeneticReflexId: 1, authoritativeMod: true);

      Assert.Equal(0, second);
      Assert.Contains(warnings, w => w.Contains("уже существует"));
    }

    [Fact]
    public void RemoveConditionedReflex_RemovesFromAll()
    {
      int trigger = NewActionImage();
      int id = AddReflex(trigger, authoritative: true);

      Assert.True(Crs.RemoveConditionedReflex(id));
      Assert.Null(Find(id));
    }

    [Fact]
    public void AddConditionedReflex_InvalidLevel1_Throws()
    {
      int trigger = NewActionImage();

      Assert.Throws<ArgumentException>(() => Crs.AddConditionedReflex(
          5, DefaultLevel2.ToList(), trigger, sourceGeneticReflexId: 1));
    }

    [Fact]
    public void AddConditionedReflex_UnknownTrigger_Throws()
    {
      Assert.Throws<ArgumentException>(() => Crs.AddConditionedReflex(
          0, DefaultLevel2.ToList(), level3: 999999, sourceGeneticReflexId: 1));
    }
  }
}