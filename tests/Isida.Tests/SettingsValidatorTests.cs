using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты валидации настроек <see cref="SettingsValidator"/>.</summary>
  public class SettingsValidatorTests
  {
    [Theory]
    [InlineData(1, true)]
    [InlineData(5, true)]
    [InlineData(10, true)]
    [InlineData(0, false)]
    [InlineData(11, false)]
    public void ValidateRecognitionThreshold_Bounds(int value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateRecognitionThreshold(value).isValid);
    }

    [Fact]
    public void ValidateRecognitionThreshold_Null_Invalid()
    {
      Assert.False(SettingsValidator.ValidateRecognitionThreshold(null).isValid);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(99, true)]
    [InlineData(0, false)]
    [InlineData(100, false)]
    public void ValidateCompareLevel_Bounds(int value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateCompareLevel(value).isValid);
    }

    [Theory]
    [InlineData(0.01f, true)]
    [InlineData(2.0f, true)]
    [InlineData(0.0f, false)]
    [InlineData(2.5f, false)]
    public void ValidateDifSensorPar_Bounds(float value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateDifSensorPar(value).isValid);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(100, true)]
    [InlineData(1, false)]
    [InlineData(101, false)]
    public void ValidateDynamicTime_Bounds(int value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateDynamicTime(value).isValid);
    }

    [Theory]
    [InlineData(0.1f, true)]
    [InlineData(0.3f, true)]
    [InlineData(0.05f, false)]
    [InlineData(0.4f, false)]
    public void ValidateLearningRate_Bounds(float value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateLearningRate(value).isValid);
    }

    [Theory]
    [InlineData(0.5f, true)]
    [InlineData(0.7f, true)]
    [InlineData(0.4f, false)]
    [InlineData(0.8f, false)]
    public void ValidateActivationThreshold_Bounds(float value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateActivationThreshold(value).isValid);
    }

    [Theory]
    [InlineData(3600, true)]
    [InlineData(604800, true)]
    [InlineData(3599, false)]
    [InlineData(604801, false)]
    public void ValidateInitialLifetimePulses_Bounds(int value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateInitialLifetimePulses(value).isValid);
    }

    [Fact]
    public void ValidateBaseInactivationTime_IsAlias()
    {
      Assert.True(SettingsValidator.ValidateBaseInactivationTime(3600).isValid);
      Assert.False(SettingsValidator.ValidateBaseInactivationTime(100).isValid);
    }

    [Theory]
    [InlineData(-20, true)]
    [InlineData(20, true)]
    [InlineData(0, false)]
    [InlineData(-21, false)]
    [InlineData(21, false)]
    public void ValidateSpeedParam_BoundsAndZero(int value, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateSpeedParam(value).isValid);
    }

    [Theory]
    [InlineData("rules.dat", true)]
    [InlineData("C:\\x\\EnvironmentPressureRules.DAT", true)]
    [InlineData("rules.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ValidateEnvironmentPressureRulesFilePath_Extension(string path, bool expected)
    {
      Assert.Equal(expected, SettingsValidator.ValidateEnvironmentPressureRulesFilePath(path).isValid);
    }

    [Fact]
    public void ValidateEnvironmentProbeKey_EmptyAllowed()
    {
      Assert.True(SettingsValidator.ValidateEnvironmentProbeKey("").isValid);
      Assert.True(SettingsValidator.ValidateEnvironmentProbeKey("  ").isValid);
    }

    [Fact]
    public void ValidateEnvironmentProbeKey_PipeForbidden()
    {
      Assert.False(SettingsValidator.ValidateEnvironmentProbeKey("a|b").isValid);
    }

    [Fact]
    public void ValidateEnvironmentProbeKey_TooLong_Invalid()
    {
      Assert.False(SettingsValidator.ValidateEnvironmentProbeKey(new string('a', 129)).isValid);
      Assert.True(SettingsValidator.ValidateEnvironmentProbeKey(new string('a', 128)).isValid);
    }

    [Fact]
    public void ClampChainLinkUsefulness_ClampsToRange()
    {
      Assert.Equal(10, SettingsValidator.ClampChainLinkUsefulness(50));
      Assert.Equal(-10, SettingsValidator.ClampChainLinkUsefulness(-50));
      Assert.Equal(3, SettingsValidator.ClampChainLinkUsefulness(3));
    }

    [Fact]
    public void ValidateSetting_UnknownKey_Valid()
    {
      Assert.True(SettingsValidator.ValidateSetting("UnknownKey", 123).isValid);
    }

    [Fact]
    public void ValidateSetting_KnownKey_Dispatches()
    {
      Assert.True(SettingsValidator.ValidateSetting("RecognitionThreshold", 5).isValid);
      Assert.False(SettingsValidator.ValidateSetting("RecognitionThreshold", 100).isValid);
    }
  }
}
