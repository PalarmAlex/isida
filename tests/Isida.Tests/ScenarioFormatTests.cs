using System.Collections.Generic;
using ISIDA.Scenarios;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты сериализации сценарных форматов: воздействия среды и начальный гомеостаз.</summary>
  public class ScenarioFormatTests
  {
    // ---------- ScenarioEnvironmentProbeFormat ----------

    [Fact]
    public void ProbeFormat_Serialize_Empty_ReturnsEmpty()
    {
      Assert.Equal("", ScenarioEnvironmentProbeFormat.Serialize(null));
      Assert.Equal("", ScenarioEnvironmentProbeFormat.Serialize(new List<ScenarioEnvironmentProbeEntry>()));
    }

    [Fact]
    public void ProbeFormat_Serialize_SignsAndIds()
    {
      var entries = new List<ScenarioEnvironmentProbeEntry>
      {
        new ScenarioEnvironmentProbeEntry { ActionId = 5, IsPressure = true },
        new ScenarioEnvironmentProbeEntry { ActionId = 3, IsPressure = false }
      };

      Assert.Equal("+5,-3", ScenarioEnvironmentProbeFormat.Serialize(entries));
    }

    [Fact]
    public void ProbeFormat_Parse_Empty_ReturnsEmpty()
    {
      Assert.Empty(ScenarioEnvironmentProbeFormat.Parse(null));
      Assert.Empty(ScenarioEnvironmentProbeFormat.Parse("  "));
    }

    [Fact]
    public void ProbeFormat_RoundTrip()
    {
      var parsed = ScenarioEnvironmentProbeFormat.Parse("+5, -3 , +7");

      Assert.Equal(3, parsed.Count);
      Assert.True(parsed[0].IsPressure);
      Assert.Equal(5, parsed[0].ActionId);
      Assert.False(parsed[1].IsPressure);
      Assert.Equal(3, parsed[1].ActionId);
      Assert.True(parsed[2].IsPressure);
      Assert.Equal(7, parsed[2].ActionId);
    }

    [Fact]
    public void ProbeFormat_Parse_IgnoresInvalidParts()
    {
      // без знака, нулевой id (в т.ч. со знаком), мусор, одиночный знак — всё отбрасывается
      Assert.Empty(ScenarioEnvironmentProbeFormat.Parse("5,+0,-0,abc,+,-0"));
    }

    [Fact]
    public void ProbeFormat_Parse_AcceptsNegativeSignedId()
    {
      var parsed = ScenarioEnvironmentProbeFormat.Parse("-2");

      Assert.Single(parsed);
      Assert.Equal(2, parsed[0].ActionId);
      Assert.False(parsed[0].IsPressure);
    }

    [Fact]
    public void ProbeEntry_Clone_IsIndependent()
    {
      var entry = new ScenarioEnvironmentProbeEntry { ActionId = 4, IsPressure = false };
      var clone = entry.Clone();

      clone.ActionId = 9;

      Assert.Equal(4, entry.ActionId);
      Assert.Equal(9, clone.ActionId);
    }

    // ---------- ScenarioHomeostasisValuesFormat ----------

    [Fact]
    public void HomeostasisFormat_Serialize_Empty_ReturnsEmpty()
    {
      Assert.Equal("", ScenarioHomeostasisValuesFormat.Serialize(null));
      Assert.Equal("", ScenarioHomeostasisValuesFormat.Serialize(new Dictionary<int, float>()));
    }

    [Fact]
    public void HomeostasisFormat_Serialize_OrderedByKey()
    {
      var values = new Dictionary<int, float> { { 3, 70f }, { 1, 50f }, { 2, 60f } };

      Assert.Equal("1=50;2=60;3=70", ScenarioHomeostasisValuesFormat.Serialize(values));
    }

    [Fact]
    public void HomeostasisFormat_RoundTrip()
    {
      // значения — через точку (инвариантная культура); запятая/мусор/пустое значение отбрасываются
      var parsed = ScenarioHomeostasisValuesFormat.Parse("1=50; 2=60.5 ; bad; 3=");

      Assert.Equal(2, parsed.Count);
      Assert.Equal(50f, parsed[1]);
      Assert.Equal(60.5f, parsed[2]);
    }

    [Fact]
    public void HomeostasisFormat_Parse_RejectsCommaDecimal()
    {
      // "60,5" не является числом в инвариантной культуре — запись пропускается
      Assert.Empty(ScenarioHomeostasisValuesFormat.Parse("2=60,5"));
    }

    [Fact]
    public void HomeostasisFormat_Parse_UsesInvariantCulture()
    {
      var parsed = ScenarioHomeostasisValuesFormat.Parse("1=1.5");
      Assert.Equal(1.5f, parsed[1]);
    }
  }
}
