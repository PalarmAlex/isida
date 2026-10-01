using ISIDA.Gomeostas;
using Xunit;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace Isida.Tests
{
  /// <summary>Тесты модели параметра гомеостаза <see cref="ParameterData"/> (валидация сеттеров).</summary>
  public class ParameterDataTests
  {
    [Fact]
    public void DefaultConstructor_HasSaneDefaults()
    {
      var param = new ParameterData();

      Assert.Equal(50f, param.Value);
      Assert.Equal(50, param.Weight);
      Assert.Equal(50, param.NormaWell);
      Assert.Equal(-1, param.Speed);
      Assert.False(param.IsVital);
    }

    [Fact]
    public void Speed_Zero_Throws()
    {
      var param = new ParameterData();

      Assert.Throws<System.ArgumentOutOfRangeException>(() => param.Speed = 0);
    }

    [Fact]
    public void Speed_OutOfRange_Throws()
    {
      var param = new ParameterData();

      Assert.Throws<System.ArgumentOutOfRangeException>(() => param.Speed = 21);
    }

    [Fact]
    public void Weight_OutOfRange_Throws()
    {
      var param = new ParameterData();

      Assert.Throws<System.ArgumentOutOfRangeException>(() => param.Weight = 101);
    }

    [Fact]
    public void NormaWell_OutOfRange_Throws()
    {
      var param = new ParameterData();

      Assert.Throws<System.ArgumentOutOfRangeException>(() => param.NormaWell = 0);
    }

    [Fact]
    public void Value_OutOfRange_Throws()
    {
      var param = new ParameterData();

      Assert.Throws<System.ArgumentOutOfRangeException>(() => param.Value = 150f);
    }

    [Fact]
    public void Value_Change_UpdatesPreviousValue()
    {
      var param = new ParameterData(1, "P", string.Empty, 50f, 50, 50, -5);

      param.Value = 40f;

      Assert.Equal(40f, param.Value);
      Assert.Equal(50f, param.PreviousValue);
    }

    [Fact]
    public void StyleActivations_DefaultZonesPresent()
    {
      var param = new ParameterData();

      Assert.True(param.StyleActivations.ContainsKey(0));
      Assert.True(param.StyleActivations.ContainsKey(6));
    }
  }
}
