using System.Collections.Generic;
using ISIDA.Scenarios;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты моделей строк/шапки/документа сценария (клонирование, отображение, парсинг).</summary>
  public class ScenarioModelTests
  {
    // ---------- ScenarioLineRow ----------

    [Fact]
    public void LineRow_ActionIdsText_ParsesAndTrims()
    {
      var row = new ScenarioLineRow { ActionIdsText = "1, 2 ,3" };

      Assert.Equal(new List<int> { 1, 2, 3 }, row.ActionIds);
      Assert.Equal("1,2,3", row.ActionIdsText);
    }

    [Fact]
    public void LineRow_ActionIdsText_Empty_ResetsList()
    {
      var row = new ScenarioLineRow { ActionIds = new List<int> { 9 } };

      row.ActionIdsText = "";

      Assert.Empty(row.ActionIds);
    }

    [Fact]
    public void LineRow_KindCode_RoundTrip()
    {
      var row = new ScenarioLineRow { KindCode = "W" };
      Assert.Equal(ScenarioLineKind.WaitClick, row.Kind);
      Assert.Equal("W", row.KindCode);

      row.KindCode = "P";
      Assert.Equal(ScenarioLineKind.Pult, row.Kind);
      Assert.Equal("P", row.KindCode);

      row.KindCode = null; // по умолчанию P
      Assert.Equal(ScenarioLineKind.Pult, row.Kind);
    }

    [Fact]
    public void LineRow_Clone_DeepCopiesLists()
    {
      var row = new ScenarioLineRow
      {
        StepIndex = 3,
        PulseWithinScenario = 7,
        Kind = ScenarioLineKind.WaitClick,
        ToneId = 2,
        MoodId = 1,
        VisualColorId = 4,
        ActionIds = new List<int> { 1, 2 },
        EnvironmentProbes = new List<ScenarioEnvironmentProbeEntry>
        {
          new ScenarioEnvironmentProbeEntry { ActionId = 5, IsPressure = true }
        },
        Phrase = "привет"
      };

      var clone = row.Clone();
      clone.ActionIds.Add(99);
      clone.EnvironmentProbes[0].ActionId = 100;

      Assert.Equal(3, clone.StepIndex);
      Assert.Equal(7, clone.PulseWithinScenario);
      Assert.Equal(ScenarioLineKind.WaitClick, clone.Kind);
      Assert.Equal(2, row.ActionIds.Count);       // исходный не изменился
      Assert.Equal(3, clone.ActionIds.Count);     // копия — независимый список
      Assert.Equal(5, row.EnvironmentProbes[0].ActionId);
      Assert.Equal("привет", clone.Phrase);
    }

    [Fact]
    public void LineRow_StepIndex_RaisesPropertyChanged()
    {
      var row = new ScenarioLineRow();
      string changed = null;
      row.PropertyChanged += (_, e) => changed = e.PropertyName;

      row.StepIndex = 5;

      Assert.Equal(nameof(ScenarioLineRow.StepIndex), changed);
    }

    // ---------- ScenarioHeader ----------

    [Fact]
    public void Header_DescriptionShort_FlattensAndTruncates()
    {
      var header = new ScenarioHeader { Description = new string('a', 150) };

      var shortText = header.DescriptionShort;

      Assert.Equal(101, shortText.Length); // 100 символов + «…»
      Assert.EndsWith("…", shortText);
    }

    [Fact]
    public void Header_DescriptionShort_ReplacesNewlines()
    {
      var header = new ScenarioHeader { Description = "строка1\r\nстрока2" };

      Assert.Equal("строка1 строка2", header.DescriptionShort);
    }

    [Fact]
    public void Header_DescriptionShort_Empty_ReturnsEmpty()
    {
      Assert.Equal("", new ScenarioHeader().DescriptionShort);
    }

    [Theory]
    [InlineData(-1, "")]
    [InlineData(0, "0")]
    [InlineData(5, "5")]
    [InlineData(6, "")]
    public void Header_PreRunStageNumberDisplay(int stage, string expected)
    {
      var header = new ScenarioHeader { PreRunTargetStage = stage };
      Assert.Equal(expected, header.PreRunStageNumberDisplay);
    }

    [Fact]
    public void Header_Clone_CopiesAllFields()
    {
      var header = new ScenarioHeader
      {
        Id = 7,
        Title = "T",
        Description = "D",
        InitialHomeostasisValues = "1=50",
        PreRunTargetStage = 2,
        PreRunClearAgentData = true,
        PulseStepIncrement = 3,
        RunPulseTimingCoefficient = 10
      };

      var clone = header.Clone();
      clone.Title = "T2";

      Assert.Equal(7, clone.Id);
      Assert.Equal("T", header.Title);
      Assert.Equal("1=50", clone.InitialHomeostasisValues);
      Assert.Equal(2, clone.PreRunTargetStage);
      Assert.True(clone.PreRunClearAgentData);
      Assert.Equal(3, clone.PulseStepIncrement);
      Assert.Equal(10, clone.RunPulseTimingCoefficient);
    }

    // ---------- ScenarioDocument ----------

    [Fact]
    public void Document_Clone_DeepCopiesLinesAndExpectations()
    {
      var document = new ScenarioDocument
      {
        Header = new ScenarioHeader { Id = 1 },
        Lines = new List<ScenarioLineRow> { new ScenarioLineRow { ActionIds = new List<int> { 1 } } },
        LogExpectations = new List<ScenarioLogExpectationRow> { new ScenarioLogExpectationRow { StateText = "1" } }
      };

      var clone = document.Clone();
      clone.Lines[0].ActionIds.Add(2);
      clone.LogExpectations[0].StateText = "9";
      clone.Header.Id = 42;

      Assert.Single(document.Lines[0].ActionIds);
      Assert.Equal("1", document.LogExpectations[0].StateText);
      Assert.Equal(1, document.Header.Id);
    }

    // ---------- ScenarioLogExpectationRow / ColumnSkips ----------

    [Fact]
    public void ExpectationRow_Defaults_AreDash()
    {
      var row = new ScenarioLogExpectationRow();

      Assert.Equal("-", row.StateText);
      Assert.Equal("-", row.EnvironmentProbesText);
    }

    [Fact]
    public void ExpectationRow_Clone_Independent()
    {
      var row = new ScenarioLogExpectationRow { StepIndex = 2, StateText = "3" };
      var clone = row.Clone();
      clone.StateText = "9";

      Assert.Equal("3", row.StateText);
      Assert.Equal(2, clone.StepIndex);
    }

    [Fact]
    public void ColumnSkips_Clone_Independent()
    {
      var skips = new ScenarioLogExpectationColumnSkips { SkipState = true, SkipTheme = true };
      var clone = skips.Clone();
      clone.SkipState = false;

      Assert.True(skips.SkipState);
      Assert.True(clone.SkipTheme);
      Assert.False(clone.SkipState);
    }

    [Fact]
    public void ColumnSkips_RaisesPropertyChanged()
    {
      var skips = new ScenarioLogExpectationColumnSkips();
      string changed = null;
      skips.PropertyChanged += (_, e) => changed = e.PropertyName;

      skips.SkipDanger = true;

      Assert.Equal(nameof(ScenarioLogExpectationColumnSkips.SkipDanger), changed);
    }
  }
}
