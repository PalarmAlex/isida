using ISIDA.Psychic.Importance;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты модели экстремальной значимости <see cref="ExtremImportance"/>.</summary>
  public class ExtremImportanceTests
  {
    [Fact]
    public void DefaultConstructor_Zeroed()
    {
      var importance = new ExtremImportance();

      Assert.Equal(0, importance.ObjId);
      Assert.Equal(0, importance.ExtremVal);
    }

    [Fact]
    public void ParameterizedConstructor_SetsValues()
    {
      var importance = new ExtremImportance(7, -3);

      Assert.Equal(7, importance.ObjId);
      Assert.Equal(-3, importance.ExtremVal);
    }
  }
}
