using System.Collections.Generic;
using System.Linq;
using ISIDA.Scenarios;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты поддержки Command-паттернов в моделях сценария:
  /// хранение ID, отображаемая подпись и независимое клонирование.
  /// </summary>
  public class ScenarioCommandPatternTests
  {
    // ---------- ScenarioLineRow.CommandPatternIds / CommandPatternsDisplay ----------

    [Fact]
    public void LineRow_CommandPatternIds_DefaultsToEmpty()
    {
      var row = new ScenarioLineRow();

      Assert.NotNull(row.CommandPatternIds);
      Assert.Empty(row.CommandPatternIds);
      Assert.Equal("", row.CommandPatternsDisplay);
    }

    [Fact]
    public void LineRow_CommandPatternIds_Clone_DeepCopies()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 1, 2 } };

      var clone = row.Clone();
      clone.CommandPatternIds.Add(99);

      Assert.Equal(2, row.CommandPatternIds.Count);    // исходный не изменился
      Assert.Equal(3, clone.CommandPatternIds.Count);  // копия — независимый список
    }

    [Fact]
    public void LineRow_CommandPatternIds_Clone_PreservesOrder()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 7, 3, 5 } };

      var clone = row.Clone();

      Assert.Equal(new List<int> { 7, 3, 5 }, clone.CommandPatternIds);
    }

    [Fact]
    public void LineRow_RefreshCommandPatternNames_UsesLookup()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 1, 2 } };
      var lookup = new Dictionary<int, string> { { 1, "вперёд" }, { 2, "назад" } };

      row.RefreshCommandPatternNames(lookup);

      Assert.Equal("вперёд, назад", row.CommandPatternsDisplay);
    }

    [Fact]
    public void LineRow_RefreshCommandPatternNames_FallsBackToId_WhenTextMissing()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 1, 2 } };
      var lookup = new Dictionary<int, string> { { 1, "вперёд" }, { 2, "" } };

      row.RefreshCommandPatternNames(lookup);

      Assert.Equal("вперёд, 2", row.CommandPatternsDisplay);
    }

    [Fact]
    public void LineRow_RefreshCommandPatternNames_NullLookup_UsesRawIds()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 4, 5 } };

      row.RefreshCommandPatternNames(null);

      Assert.Equal("4, 5", row.CommandPatternsDisplay);
    }

    [Fact]
    public void LineRow_RefreshCommandPatternNames_Empty_ClearsDisplay()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 1 } };
      row.RefreshCommandPatternNames(new Dictionary<int, string> { { 1, "вперёд" } });

      row.CommandPatternIds = new List<int>();
      row.RefreshCommandPatternNames(new Dictionary<int, string> { { 1, "вперёд" } });

      Assert.Equal("", row.CommandPatternsDisplay);
    }

    [Fact]
    public void LineRow_RefreshCommandPatternNames_RaisesPropertyChanged()
    {
      var row = new ScenarioLineRow { CommandPatternIds = new List<int> { 1 } };
      string changed = null;
      row.PropertyChanged += (_, e) => changed = e.PropertyName;

      row.RefreshCommandPatternNames(new Dictionary<int, string> { { 1, "вперёд" } });

      Assert.Equal(nameof(ScenarioLineRow.CommandPatternsDisplay), changed);
    }

    // ---------- ScenarioLogExpectationRow.CommandPatternsText ----------

    [Fact]
    public void ExpectationRow_CommandPatternsText_DefaultsToDash()
    {
      var row = new ScenarioLogExpectationRow();

      Assert.Equal("-", row.CommandPatternsText);
    }

    [Fact]
    public void ExpectationRow_CommandPatternsText_Clone_Independent()
    {
      var row = new ScenarioLogExpectationRow { StepIndex = 2, CommandPatternsText = "вперёд" };

      var clone = row.Clone();
      clone.CommandPatternsText = "назад";

      Assert.Equal("вперёд", row.CommandPatternsText);
      Assert.Equal("назад", clone.CommandPatternsText);
      Assert.Equal(2, clone.StepIndex);
    }

    [Fact]
    public void ExpectationRow_CommandPatternsText_Clone_PreservesValue()
    {
      var row = new ScenarioLogExpectationRow { CommandPatternsText = "a, b" };

      var clone = row.Clone();

      Assert.Equal("a, b", clone.CommandPatternsText);
    }

    [Fact]
    public void ExpectationRow_CommandPatternsText_RaisesPropertyChanged()
    {
      var row = new ScenarioLogExpectationRow();
      string changed = null;
      row.PropertyChanged += (_, e) => changed = e.PropertyName;

      row.CommandPatternsText = "вперёд";

      Assert.Equal(nameof(ScenarioLogExpectationRow.CommandPatternsText), changed);
    }

    // ---------- ScenarioDocument.Clone с паттернами ----------

    [Fact]
    public void Document_Clone_DeepCopiesCommandPatternIds()
    {
      var document = new ScenarioDocument
      {
        Lines = new List<ScenarioLineRow>
        {
          new ScenarioLineRow { CommandPatternIds = new List<int> { 1 } }
        }
      };

      var clone = document.Clone();
      clone.Lines[0].CommandPatternIds.Add(2);

      Assert.Single(document.Lines[0].CommandPatternIds);
      Assert.Equal(2, clone.Lines[0].CommandPatternIds.Count);
      Assert.Equal(new List<int> { 1 }, document.Lines.SelectMany(l => l.CommandPatternIds).ToList());
    }
  }
}