using ISIDA.Gomeostas;
using Xunit;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты внутреннего метода <see cref="HomeostasisCalculator.GetStateForStyleActivation"/>
  /// (доступен благодаря <c>[InternalsVisibleTo("Isida.Tests")]</c>): определение зоны стиля (0–6).
  /// </summary>
  public class HomeostasisStyleZoneTests
  {
    private static ParameterData Deficit(float value, int norma = 50)
        => new ParameterData(1, "P", string.Empty, value, 50, norma, -5);

    private static ParameterData Excess(float value, int norma = 50)
        => new ParameterData(1, "P", string.Empty, value, 50, norma, 5);

    [Fact]
    public void NormalState_NoDeviation_ZoneTwo()
    {
      var calc = new HomeostasisCalculator();

      var (zone, details) = calc.GetStateForStyleActivation(Deficit(50f), ParameterState.Normal);

      Assert.Equal(2, zone);
      Assert.StartsWith("1|", details);
    }

    [Fact]
    public void WellState_NoDeviation_ZoneOne()
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Deficit(50f), ParameterState.Well);

      Assert.Equal(1, zone);
    }

    [Fact]
    public void BadState_NoDeviation_ZoneZero()
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Deficit(50f), ParameterState.Bad);

      Assert.Equal(0, zone);
    }

    [Theory]
    [InlineData(48f, 3)] // 4% отклонения
    [InlineData(45f, 4)] // 10%
    [InlineData(40f, 5)] // 20%
    [InlineData(30f, 6)] // 40%
    public void Deficit_BelowNorma_DeviationZones(float value, int expectedZone)
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Deficit(value), ParameterState.Normal);

      Assert.Equal(expectedZone, zone);
    }

    [Fact]
    public void Deficit_AboveNorma_NoDeviation_StaysBaseZone()
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Deficit(70f), ParameterState.Normal);

      Assert.Equal(2, zone);
    }

    [Theory]
    [InlineData(55f, 4)] // 10% избытка
    [InlineData(70f, 6)] // 40%
    public void Excess_AboveNorma_DeviationZones(float value, int expectedZone)
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Excess(value), ParameterState.Normal);

      Assert.Equal(expectedZone, zone);
    }

    [Fact]
    public void Excess_BelowNorma_NoDeviation_StaysBaseZone()
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Excess(30f), ParameterState.Normal);

      Assert.Equal(2, zone);
    }

    [Fact]
    public void Deficit_ZeroValue_NormaOne_FullDeviationZoneSix()
    {
      var calc = new HomeostasisCalculator();

      var (zone, _) = calc.GetStateForStyleActivation(Deficit(0f, norma: 1), ParameterState.Normal);

      Assert.Equal(6, zone);
    }
  }
}
