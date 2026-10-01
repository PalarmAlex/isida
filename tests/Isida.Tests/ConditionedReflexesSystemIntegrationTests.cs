using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Интеграционные тесты модели угасания условных рефлексов (Случай 1/2 из DEBUG_CASEBOOK_1.md):
  /// активное угасание только ниже порога γ; «сильные» (≥ γ) не угашают активно.
  /// </summary>
  [Collection("EngineIntegration")]
  public class ConditionedReflexesSystemIntegrationTests
  {
    private readonly EngineFixture _engine;

    public ConditionedReflexesSystemIntegrationTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 1;
    }

    private static int NewTriggerImage()
    {
      int actionId = Math.Abs(Environment.TickCount ^ Guid.NewGuid().GetHashCode()) % 100000 + 1;
      int id = PerceptionImagesSystem.Instance.AddPerceptionImage(new List<int> { actionId }, null);
      Assert.True(id > 0, "не удалось создать образ восприятия");
      return id;
    }

    private static ConditionedReflexesSystem.ConditionedReflex Find(int reflexId) =>
        ConditionedReflexesSystem.Instance.GetAllConditionedReflexes()
            .FirstOrDefault(r => r.Id == reflexId);

    private static int AddReflex(int trigger, bool authoritative) =>
        ConditionedReflexesSystem.Instance.AddConditionedReflex(
            0, new List<int>(), trigger, sourceGeneticReflexId: 1,
            authoritativeMod: authoritative).ReflexId;

    [Fact]
    public void AuthoritativeReflex_AboveThreshold_NotExtinguishedActively()
    {
      int trigger = NewTriggerImage();
      int id = AddReflex(trigger, authoritative: true);
      Assert.True(id > 0);

      var reflex = Find(id);
      Assert.NotNull(reflex);
      float before = reflex.AssociationStrength;
      Assert.True(before >= ConditionedReflexesSystem.Instance.Settings.ActivationThreshold);

      ConditionedReflexesSystem.Instance.ApplyActiveExtinctionForStimulus(trigger);

      Assert.Equal(before, Find(id).AssociationStrength, 4);
      Assert.True(Find(id).CanBeActivated());
    }

    [Fact]
    public void WeakReflex_BelowThreshold_ExtinguishedActively()
    {
      int trigger = NewTriggerImage();
      int id = AddReflex(trigger, authoritative: false);

      var reflex = Find(id);
      float before = reflex.AssociationStrength;
      Assert.True(before < ConditionedReflexesSystem.Instance.Settings.ActivationThreshold);

      ConditionedReflexesSystem.Instance.ApplyActiveExtinctionForStimulus(trigger);

      float after = Find(id).AssociationStrength;
      Assert.True(after < before, $"крепость должна упасть: {before} → {after}");
      Assert.True(after >= 0f);
    }

    [Fact]
    public void Extinction_IgnoresOtherTrigger()
    {
      int trigger = NewTriggerImage();
      int otherTrigger = NewTriggerImage();
      int id = AddReflex(trigger, authoritative: false);

      float before = Find(id).AssociationStrength;
      ConditionedReflexesSystem.Instance.ApplyActiveExtinctionForStimulus(otherTrigger);

      Assert.Equal(before, Find(id).AssociationStrength, 4);
    }

    [Fact]
    public void ActiveExtinction_OnReflex_IsNoOpAboveThreshold()
    {
      int trigger = NewTriggerImage();
      int id = AddReflex(trigger, authoritative: true);

      var reflex = Find(id);
      float before = reflex.AssociationStrength;
      reflex.ApplyActiveExtinction(ConditionedReflexesSystem.Instance.Settings.ActiveExtinctionRate);

      Assert.Equal(before, reflex.AssociationStrength, 4);
    }

    [Fact]
    public void InitialLifetime_ComesFromSettings_AndDividesByOrder()
    {
      var settings = ConditionedReflexesSystem.Instance.Settings;

      Assert.Equal(settings.InitialLifetimePulses,
          ConditionedReflexesSystem.Instance.GetInitialLifetimeForOrder(1));

      int expectedSecondary = Math.Max(1,
          (int)(settings.InitialLifetimePulses / settings.HigherOrderStrengthReductionCoefficient));
      Assert.Equal(expectedSecondary, ConditionedReflexesSystem.Instance.GetInitialLifetimeForOrder(2));
    }

    [Fact]
    public void AddConditionedReflex_Duplicate_ReturnsZero()
    {
      int trigger = NewTriggerImage();
      int first = AddReflex(trigger, authoritative: true);
      Assert.True(first > 0);

      var (second, warnings) = ConditionedReflexesSystem.Instance.AddConditionedReflex(
          0, new List<int>(), trigger, sourceGeneticReflexId: 1, authoritativeMod: true);

      Assert.Equal(0, second);
      Assert.Contains(warnings, w => w.Contains("уже существует"));
    }

    [Fact]
    public void RemoveConditionedReflex_RemovesFromAll()
    {
      int trigger = NewTriggerImage();
      int id = AddReflex(trigger, authoritative: true);

      Assert.True(ConditionedReflexesSystem.Instance.RemoveConditionedReflex(id));
      Assert.Null(Find(id));
    }

    [Fact]
    public void AddConditionedReflex_InvalidLevel1_Throws()
    {
      int trigger = NewTriggerImage();

      Assert.Throws<ArgumentException>(() => ConditionedReflexesSystem.Instance.AddConditionedReflex(
          5, new List<int>(), trigger, sourceGeneticReflexId: 1));
    }

    [Fact]
    public void AddConditionedReflex_UnknownTrigger_Throws()
    {
      Assert.Throws<ArgumentException>(() => ConditionedReflexesSystem.Instance.AddConditionedReflex(
          0, new List<int>(), level3: 999999, sourceGeneticReflexId: 1));
    }
  }
}
