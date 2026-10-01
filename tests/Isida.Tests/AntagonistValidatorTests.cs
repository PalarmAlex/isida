using System.Collections.Generic;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты валидации антагонистических конфликтов <see cref="AntagonistValidator"/>.</summary>
  public class AntagonistValidatorTests
  {
    [Fact]
    public void ValidateAntagonists_NoConflicts_ReturnsEmpty()
    {
      var map = new Dictionary<int, List<int>>
      {
        { 1, new List<int> { 2 } },
        { 2, new List<int> { 1 } },
        { 3, new List<int>() }
      };

      var conflicts = AntagonistValidator.ValidateAntagonists(new[] { 1, 3 }, map);

      Assert.Empty(conflicts);
    }

    [Fact]
    public void ValidateAntagonists_OneWayAntagonism_Detected()
    {
      var map = new Dictionary<int, List<int>>
      {
        { 1, new List<int> { 2 } }
      };

      var conflicts = AntagonistValidator.ValidateAntagonists(new[] { 1, 2 }, map);

      Assert.Single(conflicts);
      Assert.Equal(1, conflicts[0].FirstId);
      Assert.Equal(2, conflicts[0].SecondId);
    }

    [Fact]
    public void ValidateAntagonists_ReverseDirection_Detected()
    {
      var map = new Dictionary<int, List<int>>
      {
        { 2, new List<int> { 1 } }
      };

      var conflicts = AntagonistValidator.ValidateAntagonists(new[] { 1, 2 }, map);

      Assert.Single(conflicts);
      Assert.Equal(1, conflicts[0].FirstId);
      Assert.Equal(2, conflicts[0].SecondId);
    }

    [Fact]
    public void ValidateAntagonists_EachPairReportedOnce()
    {
      var map = new Dictionary<int, List<int>>
      {
        { 1, new List<int> { 2 } },
        { 2, new List<int> { 1 } }
      };

      var conflicts = AntagonistValidator.ValidateAntagonists(new[] { 1, 2 }, map);

      Assert.Single(conflicts);
    }

    [Fact]
    public void ValidateAntagonists_EmptySelection_ReturnsEmpty()
    {
      var map = new Dictionary<int, List<int>>();

      Assert.Empty(AntagonistValidator.ValidateAntagonists(null, map));
      Assert.Empty(AntagonistValidator.ValidateAntagonists(new int[0], map));
    }

    [Fact]
    public void ValidateAntagonists_MissingKey_TreatedAsNoAntagonists()
    {
      var map = new Dictionary<int, List<int>>
      {
        { 1, new List<int> { 2 } }
      };

      var conflicts = AntagonistValidator.ValidateAntagonists(new[] { 1, 9 }, map);

      Assert.Empty(conflicts);
    }

    [Fact]
    public void Conflict_Message_ContainsBothIds()
    {
      var conflict = new AntagonistConflict(3, 7);

      Assert.Contains("3", conflict.Message);
      Assert.Contains("7", conflict.Message);
    }
  }
}
