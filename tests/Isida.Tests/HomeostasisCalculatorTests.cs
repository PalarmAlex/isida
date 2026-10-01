using System.Collections.Generic;
using ISIDA.Gomeostas;
using Xunit;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace Isida.Tests
{
  /// <summary>Тесты расчётов гомеостаза <see cref="HomeostasisCalculator"/>.</summary>
  public class HomeostasisCalculatorTests
  {
    private static ParameterData Param(
        int id, float value, int speed, int norma = 50, int weight = 50, bool vital = false)
    {
      return new ParameterData(id, "P" + id, string.Empty, value, weight, norma, speed, vital);
    }

    // ---------- HasCriticalParameterChanges ----------

    [Fact]
    public void HasCriticalParameterChanges_NullOrEmpty_False()
    {
      var calc = new HomeostasisCalculator();
      var current = new List<ParameterData> { Param(1, 40f, -5, vital: true) };

      Assert.False(calc.HasCriticalParameterChanges(current, null));
      Assert.False(calc.HasCriticalParameterChanges(current, new List<ParameterData>()));
      Assert.False(calc.HasCriticalParameterChanges(null, current));
    }

    [Fact]
    public void HasCriticalParameterChanges_DeficitWorsening_True()
    {
      var calc = new HomeostasisCalculator();
      var previous = new List<ParameterData> { Param(1, 50f, -5, vital: true) };
      var current = new List<ParameterData> { Param(1, 40f, -5, vital: true) };

      Assert.True(calc.HasCriticalParameterChanges(current, previous));
    }

    [Fact]
    public void HasCriticalParameterChanges_DeficitImproving_False()
    {
      var calc = new HomeostasisCalculator();
      var previous = new List<ParameterData> { Param(1, 40f, -5, vital: true) };
      var current = new List<ParameterData> { Param(1, 55f, -5, vital: true) };

      Assert.False(calc.HasCriticalParameterChanges(current, previous));
    }

    [Fact]
    public void HasCriticalParameterChanges_NonVitalIgnored()
    {
      var calc = new HomeostasisCalculator();
      var previous = new List<ParameterData> { Param(1, 50f, -5, vital: false) };
      var current = new List<ParameterData> { Param(1, 20f, -5, vital: false) };

      Assert.False(calc.HasCriticalParameterChanges(current, previous));
    }

    [Fact]
    public void HasCriticalParameterChanges_ChangeWithinNaturalStep_Ignored()
    {
      var calc = new HomeostasisCalculator();
      // naturalStep = |speed|/100 = 0.2; изменение 0.1 ниже порога значимости
      var previous = new List<ParameterData> { Param(1, 50f, -20, vital: true) };
      var current = new List<ParameterData> { Param(1, 49.9f, -20, vital: true) };

      Assert.False(calc.HasCriticalParameterChanges(current, previous));
    }

    // ---------- ComputeOperatorAutomatizmAssessment ----------

    [Fact]
    public void ComputeOperatorAutomatizmAssessment_NoSnapshot_UsesOverallStates()
    {
      var calc = new HomeostasisCalculator();
      var current = new List<ParameterData> { Param(1, 50f, -5, vital: true) };

      Assert.Equal(1, calc.ComputeOperatorAutomatizmAssessment(
          new Dictionary<int, float>(), current, 0,
          AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well));

      Assert.Equal(-1, calc.ComputeOperatorAutomatizmAssessment(
          new Dictionary<int, float>(), current, 0,
          AppGlobalState.HomeostasisState.Well, AppGlobalState.HomeostasisState.Bad));

      Assert.Equal(0, calc.ComputeOperatorAutomatizmAssessment(
          new Dictionary<int, float>(), current, 0,
          AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal));
    }

    [Fact]
    public void ComputeOperatorAutomatizmAssessment_VitalWorsening_ReturnsMinusOne()
    {
      var calc = new HomeostasisCalculator();
      var valuesBefore = new Dictionary<int, float> { { 1, 50f } };
      var current = new List<ParameterData> { Param(1, 40f, -5, vital: true) };

      int result = calc.ComputeOperatorAutomatizmAssessment(
          valuesBefore, current, 0,
          AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal);

      Assert.Equal(-1, result);
    }

    [Fact]
    public void ComputeOperatorAutomatizmAssessment_VitalImproving_ReturnsPlusOne()
    {
      var calc = new HomeostasisCalculator();
      var valuesBefore = new Dictionary<int, float> { { 1, 40f } };
      var current = new List<ParameterData> { Param(1, 55f, -5, vital: true) };

      int result = calc.ComputeOperatorAutomatizmAssessment(
          valuesBefore, current, 0,
          AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal);

      Assert.Equal(1, result);
    }

    // ---------- AnyVitalParameterInHarmfulZone / IsParameterInBadZone ----------

    [Fact]
    public void AnyVitalParameterInHarmfulZone_DeficitBelowNorma_True()
    {
      var calc = new HomeostasisCalculator();
      var parameters = new List<ParameterData> { Param(1, 30f, -5, norma: 50, vital: true) };

      Assert.True(calc.AnyVitalParameterInHarmfulZone(parameters));
    }

    [Fact]
    public void AnyVitalParameterInHarmfulZone_NonVital_Ignored()
    {
      var calc = new HomeostasisCalculator();
      var parameters = new List<ParameterData> { Param(1, 30f, -5, norma: 50, vital: false) };

      Assert.False(calc.AnyVitalParameterInHarmfulZone(parameters));
    }

    [Fact]
    public void AnyVitalParameterInHarmfulZone_Null_False()
    {
      var calc = new HomeostasisCalculator();

      Assert.False(calc.AnyVitalParameterInHarmfulZone(null));
    }

    [Fact]
    public void IsParameterInBadZone_DeficitBelowNorma_True()
    {
      var calc = new HomeostasisCalculator();

      Assert.True(calc.IsParameterInBadZone(Param(1, 40f, -5, norma: 50)));
      Assert.False(calc.IsParameterInBadZone(Param(1, 60f, -5, norma: 50)));
      Assert.False(calc.IsParameterInBadZone(null));
    }

    [Fact]
    public void IsParameterInBadZone_ExcessAboveNorma_True()
    {
      var calc = new HomeostasisCalculator();

      Assert.True(calc.IsParameterInBadZone(Param(1, 60f, 5, norma: 50)));
      Assert.False(calc.IsParameterInBadZone(Param(1, 40f, 5, norma: 50)));
    }

    // ---------- CalculateUrgencyFunction ----------

    [Fact]
    public void CalculateUrgencyFunction_DeficitAtOrAboveNorma_Zero()
    {
      var calc = new HomeostasisCalculator();

      Assert.Equal(0f, calc.CalculateUrgencyFunction(Param(1, 50f, -5, norma: 50)));
      Assert.Equal(0f, calc.CalculateUrgencyFunction(Param(1, 60f, -5, norma: 50)));
    }

    [Fact]
    public void CalculateUrgencyFunction_DeficitBelowNorma_ScaledByWeight()
    {
      var calc = new HomeostasisCalculator();
      // weight=100 => 1.0; (50-25)/50 = 0.5
      float urgency = calc.CalculateUrgencyFunction(Param(1, 25f, -5, norma: 50, weight: 100));

      Assert.Equal(0.5f, urgency, 3);
    }

    [Fact]
    public void CalculateUrgencyFunction_ExcessBelowNorma_Zero()
    {
      var calc = new HomeostasisCalculator();

      Assert.Equal(0f, calc.CalculateUrgencyFunction(Param(1, 40f, 5, norma: 50)));
    }

    [Fact]
    public void CalculateUrgencyFunction_ExcessAboveNorma_ScaledByWeight()
    {
      var calc = new HomeostasisCalculator();
      // (100-50)=50; (75-50)/50 = 0.5
      float urgency = calc.CalculateUrgencyFunction(Param(1, 75f, 5, norma: 50, weight: 100));

      Assert.Equal(0.5f, urgency, 3);
    }

    [Fact]
    public void CalculateUrgencyFunction_Null_Throws()
    {
      var calc = new HomeostasisCalculator();

      Assert.Throws<System.ArgumentNullException>(() => calc.CalculateUrgencyFunction(null));
    }

    // ---------- IsExternalImpactCritical / HasExternalCriticalImpact ----------

    [Fact]
    public void HasExternalCriticalImpact_LargeVitalInfluence_True()
    {
      var calc = new HomeostasisCalculator();
      var influences = new Dictionary<int, int> { { 1, 6 } };
      var parameters = new List<ParameterData> { Param(1, 50f, -5, vital: true) };

      Assert.True(calc.HasExternalCriticalImpact(influences, parameters));
    }

    [Fact]
    public void HasExternalCriticalImpact_SmallInfluence_False()
    {
      var calc = new HomeostasisCalculator();
      var influences = new Dictionary<int, int> { { 1, 5 } };
      var parameters = new List<ParameterData> { Param(1, 50f, -5, vital: true) };

      Assert.False(calc.HasExternalCriticalImpact(influences, parameters));
    }

    [Fact]
    public void HasExternalCriticalImpact_Null_False()
    {
      var calc = new HomeostasisCalculator();
      var parameters = new List<ParameterData> { Param(1, 50f, -5, vital: true) };

      Assert.False(calc.HasExternalCriticalImpact(null, parameters));
    }

    [Fact]
    public void IsExternalImpactCritical_HarmfulNearCritical_True()
    {
      var calc = new HomeostasisCalculator();
      // deficit-oriented (speed<0): value 30 < NormaWell(50)+|speed|(5)=55 => near critical;
      // harmful impact = negative, magnitude 8 > |speed| 5
      var influences = new Dictionary<int, int> { { 1, -8 } };
      var parameters = new List<ParameterData> { Param(1, 30f, -5, norma: 50, vital: true) };

      Assert.True(calc.IsExternalImpactCritical(influences, parameters));
    }

    [Fact]
    public void IsExternalImpactCritical_BeneficialImpact_False()
    {
      var calc = new HomeostasisCalculator();
      var influences = new Dictionary<int, int> { { 1, 8 } };
      var parameters = new List<ParameterData> { Param(1, 30f, -5, norma: 50, vital: true) };

      Assert.False(calc.IsExternalImpactCritical(influences, parameters));
    }
  }
}
