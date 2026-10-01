using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты зрительного канала <see cref="AgentVisualColor"/>.</summary>
  public class AgentVisualColorTests
  {
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(8, true)]
    [InlineData(-1, false)]
    [InlineData(9, false)]
    public void IsValidCode_Bounds(int code, bool expected)
    {
      Assert.Equal(expected, AgentVisualColor.IsValidCode(code));
    }

    [Fact]
    public void GetDisplayName_KnownCodes()
    {
      Assert.Equal("Белый", AgentVisualColor.GetDisplayName(0));
      Assert.Equal("Чёрный", AgentVisualColor.GetDisplayName(1));
      Assert.Equal("Фиолетовый", AgentVisualColor.GetDisplayName(8));
    }

    [Fact]
    public void GetDisplayName_UnknownCode_FallsBack()
    {
      Assert.Equal("Код 42", AgentVisualColor.GetDisplayName(42));
    }
  }
}
