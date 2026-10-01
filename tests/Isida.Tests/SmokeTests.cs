using Xunit;

namespace Isida.Tests
{
  /// <summary>Смок-тест: тестовый проект собирается и видит движок isida.</summary>
  public class SmokeTests
  {
    [Fact]
    public void EngineAssembly_IsReferenced()
    {
      Assert.NotNull(typeof(ISIDA.Common.AddUtils));
    }

    [Fact]
    public void True_IsTrue()
    {
      Assert.True(true);
    }
  }
}
