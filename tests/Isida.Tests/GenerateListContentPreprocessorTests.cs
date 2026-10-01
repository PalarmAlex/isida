using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты предобработки содержимого файлов генерации <see cref="GenerateListContentPreprocessor"/>.</summary>
  public class GenerateListContentPreprocessorTests
  {
    [Fact]
    public void Preprocess_Null_ReturnsNull()
    {
      Assert.Null(GenerateListContentPreprocessor.Preprocess(null));
    }

    [Fact]
    public void Preprocess_Empty_ReturnsEmpty()
    {
      Assert.Equal(string.Empty, GenerateListContentPreprocessor.Preprocess(""));
    }

    [Fact]
    public void Preprocess_TrimsOuterWhitespace()
    {
      Assert.Equal("abc", GenerateListContentPreprocessor.Preprocess("   abc \r\n"));
    }

    [Fact]
    public void Preprocess_RemovesBom()
    {
      Assert.Equal("id|1", GenerateListContentPreprocessor.Preprocess("\uFEFFid|1"));
    }

    [Fact]
    public void Preprocess_RemovesZeroWidthChars()
    {
      Assert.Equal("ab", GenerateListContentPreprocessor.Preprocess("a\u200Bb\u200D"));
    }

    [Fact]
    public void Preprocess_ReplacesNonBreakingSpaceRemoval()
    {
      // Неразрывный пробел удаляется целиком (не превращается в обычный).
      Assert.Equal("ab", GenerateListContentPreprocessor.Preprocess("a\u00A0b"));
    }

    [Fact]
    public void Preprocess_KeepsInnerContent()
    {
      Assert.Equal("1|2|3", GenerateListContentPreprocessor.Preprocess("  1|2|3  "));
    }
  }
}
