using ISIDA.Actions;
using System;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты модели влияния действия на параметр <see cref="ParameterInfluence"/>.</summary>
  public class ParameterInfluenceTests
  {
    [Theory]
    [InlineData(-10)]
    [InlineData(0)]
    [InlineData(10)]
    public void Effect_WithinRange_IsStored(int value)
    {
      var influence = new ParameterInfluence { Effect = value };

      Assert.Equal(value, influence.Effect);
    }

    [Theory]
    [InlineData(-11)]
    [InlineData(11)]
    [InlineData(100)]
    public void Effect_OutOfRange_Throws(int value)
    {
      var influence = new ParameterInfluence();

      Assert.Throws<ArgumentOutOfRangeException>(() => influence.Effect = value);
    }

    [Fact]
    public void IdAndName_AreStored()
    {
      var influence = new ParameterInfluence { Id = 7, Name = "Сахар" };

      Assert.Equal(7, influence.Id);
      Assert.Equal("Сахар", influence.Name);
    }
  }
}
