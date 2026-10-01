using System.Collections.Generic;
using System.ComponentModel;
using ISIDA.Scenarios;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты расчёта номеров пульсов по шагам <see cref="ScenarioPulseSchedule"/>.</summary>
  public class ScenarioPulseScheduleTests
  {
    [Fact]
    public void ResolveDelay_Sequential_Zero()
    {
      Assert.Equal(0, ScenarioPulseSchedule.ResolveDelayBetweenSteps(
          ScenarioPulseStepIncrement.Sequential, 100, 200));
    }

    [Theory]
    [InlineData(ScenarioPulseStepIncrement.ActionHold)]
    [InlineData(ScenarioPulseStepIncrement.ActionHoldPlusOne)]
    public void ResolveDelay_ActionHold_UsesReflexDuration(ScenarioPulseStepIncrement mode)
    {
      Assert.Equal(30, ScenarioPulseSchedule.ResolveDelayBetweenSteps(mode, 30, 200));
    }

    [Fact]
    public void ResolveDelay_StateHold_UsesStateDuration()
    {
      Assert.Equal(200, ScenarioPulseSchedule.ResolveDelayBetweenSteps(
          ScenarioPulseStepIncrement.StateHoldPlusOne, 30, 200));
    }

    [Fact]
    public void ResolveDelay_NegativeInput_ClampedToZero()
    {
      Assert.Equal(0, ScenarioPulseSchedule.ResolveDelayBetweenSteps(
          ScenarioPulseStepIncrement.ActionHold, -5, -5));
    }

    [Fact]
    public void TailPulse_OnlyActionHoldIsZero()
    {
      Assert.Equal(0, ScenarioPulseSchedule.TailPulseAfterGap(ScenarioPulseStepIncrement.ActionHold));
      Assert.Equal(1, ScenarioPulseSchedule.TailPulseAfterGap(ScenarioPulseStepIncrement.Sequential));
      Assert.Equal(1, ScenarioPulseSchedule.TailPulseAfterGap(ScenarioPulseStepIncrement.ActionHoldPlusOne));
      Assert.Equal(1, ScenarioPulseSchedule.TailPulseAfterGap(ScenarioPulseStepIncrement.StateHoldPlusOne));
    }

    [Fact]
    public void PulseDelta_Sequential_IsOne()
    {
      Assert.Equal(1, ScenarioPulseSchedule.PulseDeltaBetweenConsecutiveSteps(
          ScenarioPulseStepIncrement.Sequential, 100, 200));
    }

    [Fact]
    public void PulseDelta_ActionHold_NoTail()
    {
      Assert.Equal(30, ScenarioPulseSchedule.PulseDeltaBetweenConsecutiveSteps(
          ScenarioPulseStepIncrement.ActionHold, 30, 200));
    }

    [Fact]
    public void PulseDelta_ActionHoldPlusOne_WithTail()
    {
      Assert.Equal(31, ScenarioPulseSchedule.PulseDeltaBetweenConsecutiveSteps(
          ScenarioPulseStepIncrement.ActionHoldPlusOne, 30, 200));
    }

    [Fact]
    public void PulseDelta_NeverBelowOne()
    {
      Assert.Equal(1, ScenarioPulseSchedule.PulseDeltaBetweenConsecutiveSteps(
          ScenarioPulseStepIncrement.ActionHold, 0, 0));
    }

    [Fact]
    public void Normalize_NumbersStepsAndPulses()
    {
      var lines = new List<ScenarioLineRow>
      {
        new ScenarioLineRow { StepIndex = 1 },
        new ScenarioLineRow { StepIndex = 2 },
        new ScenarioLineRow { StepIndex = 3 }
      };

      ScenarioPulseSchedule.Normalize(lines, delayPulsesBetweenSteps: 5);

      Assert.Equal(1, lines[0].StepIndex);
      Assert.Equal(1, lines[0].PulseWithinScenario);
      Assert.Equal(2, lines[1].StepIndex);
      Assert.Equal(7, lines[1].PulseWithinScenario);
      Assert.Equal(3, lines[2].StepIndex);
      Assert.Equal(13, lines[2].PulseWithinScenario);
    }

    [Fact]
    public void Normalize_SortsByPulseWhenStepIndexInvalid()
    {
      var lines = new List<ScenarioLineRow>
      {
        new ScenarioLineRow { StepIndex = 0, PulseWithinScenario = 5 },
        new ScenarioLineRow { StepIndex = 0, PulseWithinScenario = 2 }
      };

      ScenarioPulseSchedule.Normalize(lines, delayPulsesBetweenSteps: 0);

      // отсортировано по исходному PulseWithinScenario (2, 5), затем перенумеровано
      Assert.Equal(1, lines[0].PulseWithinScenario);
      Assert.Equal(2, lines[1].PulseWithinScenario);
      Assert.Equal(1, lines[0].StepIndex);
      Assert.Equal(2, lines[1].StepIndex);
    }

    [Fact]
    public void Normalize_NullOrEmpty_NoThrow()
    {
      ScenarioPulseSchedule.Normalize(null, 0);
      ScenarioPulseSchedule.Normalize(new List<ScenarioLineRow>(), 0);
    }

    [Fact]
    public void Normalize_BindingList_KeepsCollectionType()
    {
      var lines = new BindingList<ScenarioLineRow>
      {
        new ScenarioLineRow { StepIndex = 2 },
        new ScenarioLineRow { StepIndex = 1 }
      };

      ScenarioPulseSchedule.Normalize(lines, delayPulsesBetweenSteps: 0);

      Assert.Equal(2, lines.Count);
      Assert.Equal(1, lines[0].StepIndex);
      Assert.Equal(2, lines[1].StepIndex);
    }

    [Fact]
    public void EnsureSequentialStepIndices_DoesNotTouchPulses()
    {
      var lines = new List<ScenarioLineRow>
      {
        new ScenarioLineRow { StepIndex = 9, PulseWithinScenario = 42 },
        new ScenarioLineRow { StepIndex = 3, PulseWithinScenario = 77 }
      };

      ScenarioPulseSchedule.EnsureSequentialStepIndices(lines);

      Assert.Equal(1, lines[0].StepIndex);
      Assert.Equal(42, lines[0].PulseWithinScenario);
      Assert.Equal(2, lines[1].StepIndex);
      Assert.Equal(77, lines[1].PulseWithinScenario);
    }
  }
}
