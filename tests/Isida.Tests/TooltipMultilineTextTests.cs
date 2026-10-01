using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты форматирования многострочных подсказок <see cref="TooltipMultilineText"/>.</summary>
  public class TooltipMultilineTextTests
  {
    [Fact]
    public void Format_NullOrWhitespace_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, TooltipMultilineText.Format(null));
      Assert.Equal(string.Empty, TooltipMultilineText.Format("   "));
    }

    [Fact]
    public void Format_NoSemicolon_ReturnsCollapsedText()
    {
      Assert.Equal("одно предложение", TooltipMultilineText.Format("  одно    предложение "));
    }

    [Fact]
    public void Format_SplitsBySemicolon()
    {
      string result = TooltipMultilineText.Format("часть1; часть2; часть3");

      Assert.Equal("часть1" + System.Environment.NewLine + "часть2" + System.Environment.NewLine + "часть3", result);
    }

    [Fact]
    public void Format_CollapsesNewlinesIntoSingleSpace()
    {
      string result = TooltipMultilineText.Format("строка1\r\nстрока2");

      Assert.Equal("строка1 строка2", result);
    }

    [Fact]
    public void FormatEnvironmentMetricBlock_IncludesNameAndMagnitude()
    {
      string result = TooltipMultilineText.FormatEnvironmentMetricBlock(7, "+3", "Свет", null);

      Assert.Contains("Свет", result);
      Assert.Contains("7:+3", result);
    }

    [Fact]
    public void FormatEnvironmentMetricBlock_NoName_FallsBackToId()
    {
      string result = TooltipMultilineText.FormatEnvironmentMetricBlock(7, "-1", null, null);

      Assert.Contains("id=7", result);
    }

    [Fact]
    public void FormatEnvironmentMetricBlock_WithDescription_AppendsIt()
    {
      string result = TooltipMultilineText.FormatEnvironmentMetricBlock(2, "+1", "Шум", "описание детали");

      Assert.Contains("описание детали", result);
    }
  }
}