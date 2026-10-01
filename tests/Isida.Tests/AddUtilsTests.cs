using System.Collections.Generic;
using System.Globalization;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты утилит работы со списками <see cref="AddUtils"/>.</summary>
  public class AddUtilsTests
  {
    [Fact]
    public void ParseIntList_EmptyOrWhitespace_ReturnsEmpty()
    {
      Assert.Empty(AddUtils.ParseIntList(null));
      Assert.Empty(AddUtils.ParseIntList(""));
      Assert.Empty(AddUtils.ParseIntList("   "));
    }

    [Fact]
    public void ParseIntList_Values_SplitsAndTrims()
    {
      Assert.Equal(new List<int> { 1, 2, 3 }, AddUtils.ParseIntList("1, 2 ,3"));
    }

    [Fact]
    public void ParseIntList_NonNumeric_BecomesZero()
    {
      Assert.Equal(new List<int> { 5, 0, 7 }, AddUtils.ParseIntList("5,abc,7"));
    }

    [Fact]
    public void ParseIntList_InnerSeparators_SkipsEmptySegments()
    {
      // Пустые сегменты (между/по краям разделителей) отбрасываются, а не превращаются в 0.
      Assert.Equal(new List<int> { 1, 2 }, AddUtils.ParseIntList("1,,2"));
    }

    [Fact]
    public void ParseIntList_TrailingSeparators_SkipsEmptySegments()
    {
      Assert.Equal(new List<int> { 1, 2 }, AddUtils.ParseIntList("1,2,,"));
    }

    [Fact]
    public void IntListToString_EmptyOrNull_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, AddUtils.IntListToString(null));
      Assert.Equal(string.Empty, AddUtils.IntListToString(new List<int>()));
    }

    [Fact]
    public void IntListToString_JoinsWithComma()
    {
      Assert.Equal("1,2,3", AddUtils.IntListToString(new List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void ParseDoubleList_WholeNumbers_SplitByComma()
    {
      Assert.Equal(new List<double> { 1.0, 2.0, 3.0 }, AddUtils.ParseDoubleList("1,2,3"));
    }

    [Fact]
    public void ParseDoubleList_UsesInvariantCulture_RegardlessOfCurrentCulture()
    {
      // Дробная часть разбирается по инвариантной культуре (точка), запятая — только разделитель списка.
      var previous = CultureInfo.CurrentCulture;
      try
      {
        CultureInfo.CurrentCulture = new CultureInfo("ru-RU");
        Assert.Equal(new List<double> { 1.5 }, AddUtils.ParseDoubleList("1.5"));
      }
      finally
      {
        CultureInfo.CurrentCulture = previous;
      }
    }

    [Fact]
    public void ParseDoubleList_CommaIsListSeparator_NotDecimal()
    {
      Assert.Equal(new List<double> { 1.0, 5.0 }, AddUtils.ParseDoubleList("1,5"));
    }

    [Fact]
    public void ParseDoubleList_SkipsEmptySegments()
    {
      Assert.Equal(new List<double> { 1.0, 2.0 }, AddUtils.ParseDoubleList("1,,2"));
    }

    [Fact]
    public void ParseDoubleList_NonNumeric_BecomesZero()
    {
      Assert.Equal(new List<double> { 0.0, 7.0 }, AddUtils.ParseDoubleList("x,7"));
    }

    [Fact]
    public void DoubleListToString_EmptyOrNull_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, AddUtils.DoubleListToString(null));
      Assert.Equal(string.Empty, AddUtils.DoubleListToString(new List<double>()));
    }

    [Fact]
    public void AreListsEqual_OrderIndependent()
    {
      Assert.True(AddUtils.AreListsEqual(new List<int> { 3, 1, 2 }, new List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void AreListsEqual_DifferentContent_False()
    {
      Assert.False(AddUtils.AreListsEqual(new List<int> { 1, 2 }, new List<int> { 1, 3 }));
    }

    [Fact]
    public void AreListsEqual_DifferentLength_False()
    {
      Assert.False(AddUtils.AreListsEqual(new List<int> { 1, 2 }, new List<int> { 1, 2, 3 }));
    }

    [Fact]
    public void AreListsEqual_NullAndEmpty_True()
    {
      Assert.True(AddUtils.AreListsEqual(null, null));
      Assert.True(AddUtils.AreListsEqual(null, new List<int>()));
      Assert.True(AddUtils.AreListsEqual(new List<int>(), null));
    }

    [Fact]
    public void AreListsEqual_NullAndNonEmpty_False()
    {
      Assert.False(AddUtils.AreListsEqual(null, new List<int> { 1 }));
    }

    [Fact]
    public void FloatLessOrEqual_WithinTolerance_True()
    {
      Assert.True(AddUtils.FloatLessOrEqual(1.00005f, 1.0f));
      Assert.True(AddUtils.FloatLessOrEqual(1.0f, 1.0f));
    }

    [Fact]
    public void FloatLessOrEqual_GreaterBeyondTolerance_False()
    {
      Assert.False(AddUtils.FloatLessOrEqual(1.01f, 1.0f));
    }

    [Fact]
    public void ClampInt_BoundsValue()
    {
      Assert.Equal(5, AddUtils.Clamp(5, 0, 10));
      Assert.Equal(0, AddUtils.Clamp(-3, 0, 10));
      Assert.Equal(10, AddUtils.Clamp(99, 0, 10));
    }

    [Fact]
    public void ClampFloat_BoundsValue()
    {
      Assert.Equal(0.5f, AddUtils.Clamp(0.5f, 0f, 1f));
      Assert.Equal(0f, AddUtils.Clamp(-0.1f, 0f, 1f));
      Assert.Equal(1f, AddUtils.Clamp(2f, 0f, 1f));
    }
  }
}
