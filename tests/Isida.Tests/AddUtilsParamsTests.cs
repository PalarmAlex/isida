using System;
using System.Collections.Generic;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты сериализации/парсинга параметров моторного действия <see cref="AddUtils"/>.</summary>
  public class AddUtilsParamsTests
  {
    #region ParamsToString

    [Fact]
    public void ParamsToString_EmptyOrNull_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, AddUtils.ParamsToString(null));
      Assert.Equal(string.Empty, AddUtils.ParamsToString(new Dictionary<string, string>()));
    }

    [Fact]
    public void ParamsToString_SerializesKeyValuePairs()
    {
      var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        { "width", "120" },
        { "height", "80" }
      };
      string result = AddUtils.ParamsToString(dict);
      // Ключи сортируются регистронезависимо
      Assert.Equal("height=80;width=120", result);
    }

    [Fact]
    public void ParamsToString_ValueWithEquals_SplitsByFirstOnly()
    {
      var dict = new Dictionary<string, string> { { "formula", "a=b+c" } };
      Assert.Equal("formula=a=b+c", AddUtils.ParamsToString(dict));
    }

    [Fact]
    public void ParamsToString_SortedByKeyNameIgnoreCase()
    {
      var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        { "Zebra", "1" },
        { "apple", "2" },
        { "Banana", "3" }
      };
      string result = AddUtils.ParamsToString(dict);
      // apple, Banana, Zebra — сортировка по имени без учёта регистра
      Assert.Equal("apple=2;Banana=3;Zebra=1", result);
    }

    #endregion

    #region ParseParams

    [Fact]
    public void ParseParams_EmptyOrNull_ReturnsEmpty()
    {
      var result = AddUtils.ParseParams(null);
      Assert.NotNull(result);
      Assert.Empty(result);

      result = AddUtils.ParseParams(string.Empty);
      Assert.Empty(result);

      result = AddUtils.ParseParams("   ");
      Assert.Empty(result);
    }

    [Fact]
    public void ParseParams_ParsesKeyValuePairs()
    {
      var result = AddUtils.ParseParams("width=120;height=80");
      Assert.Equal("120", result["width"]);
      Assert.Equal("80", result["height"]);
    }

    [Fact]
    public void ParseParams_CaseInsensitiveKeys()
    {
      var result = AddUtils.ParseParams("Width=120");
      Assert.Equal("120", result["width"]);
      Assert.Equal("120", result["WIDTH"]);
    }

    [Fact]
    public void ParseParams_ValueWithEquals_UsesFirstSplit()
    {
      var result = AddUtils.ParseParams("formula=a=b+c");
      Assert.Equal("a=b+c", result["formula"]);
    }

    [Fact]
    public void ParseParams_SkipsMissingKey()
    {
      // "=value" — нет ключа, пропускается
      var result = AddUtils.ParseParams("=value;valid=123");
      Assert.False(result.ContainsKey(string.Empty));
      Assert.Equal("123", result["valid"]);
    }

    [Fact]
    public void ParseParams_SkipsMissingEquals()
    {
      // "naked" — нет '=', пропускается
      var result = AddUtils.ParseParams("naked;valid=123");
      Assert.False(result.ContainsKey("naked"));
      Assert.Equal("123", result["valid"]);
    }

    [Fact]
    public void ParseParams_SkipsEmptySegments()
    {
      var result = AddUtils.ParseParams("a=1;;b=2;");
      Assert.Equal("1", result["a"]);
      Assert.Equal("2", result["b"]);
      Assert.Equal(2, result.Count);
    }

    #endregion

    #region RoundTrip

    [Fact]
    public void ParamsToString_ParseParams_RoundTrip()
    {
      var original = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
      {
        { "width", "120" },
        { "height", "80" },
        { "depth", "0" }
      };
      string serialized = AddUtils.ParamsToString(original);
      var parsed = AddUtils.ParseParams(serialized);
      Assert.True(AddUtils.DictEquals(original, parsed));
    }

    [Fact]
    public void ParamsToString_ParseParams_EmptyRoundTrip()
    {
      var original = new Dictionary<string, string>();
      string serialized = AddUtils.ParamsToString(original);
      var parsed = AddUtils.ParseParams(serialized);
      Assert.Empty(parsed);
    }

    #endregion

    #region DictEquals

    [Fact]
    public void DictEquals_SameContent_True()
    {
      var d1 = new Dictionary<string, string> { { "a", "1" }, { "b", "2" } };
      var d2 = new Dictionary<string, string> { { "a", "1" }, { "b", "2" } };
      Assert.True(AddUtils.DictEquals(d1, d2));
    }

    [Fact]
    public void DictEquals_DifferentValues_False()
    {
      var d1 = new Dictionary<string, string> { { "a", "1" } };
      var d2 = new Dictionary<string, string> { { "a", "2" } };
      Assert.False(AddUtils.DictEquals(d1, d2));
    }

    [Fact]
    public void DictEquals_DifferentKeys_False()
    {
      var d1 = new Dictionary<string, string> { { "a", "1" } };
      var d2 = new Dictionary<string, string> { { "b", "1" } };
      Assert.False(AddUtils.DictEquals(d1, d2));
    }

    [Fact]
    public void DictEquals_NullAndEmpty_True()
    {
      Assert.True(AddUtils.DictEquals(null, null));
      Assert.True(AddUtils.DictEquals(null, new Dictionary<string, string>()));
      Assert.True(AddUtils.DictEquals(new Dictionary<string, string>(), null));
    }

    [Fact]
    public void DictEquals_NullAndNonEmpty_False()
    {
      Assert.False(AddUtils.DictEquals(null, new Dictionary<string, string> { { "a", "1" } }));
    }

    [Fact]
    public void DictEquals_DifferentSize_False()
    {
      var d1 = new Dictionary<string, string> { { "a", "1" } };
      var d2 = new Dictionary<string, string> { { "a", "1" }, { "b", "2" } };
      Assert.False(AddUtils.DictEquals(d1, d2));
    }

    #endregion
  }
}
