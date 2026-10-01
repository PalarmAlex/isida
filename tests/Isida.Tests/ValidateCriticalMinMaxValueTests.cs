using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты валидации критических значений параметра гомеостаза.</summary>
  public class ValidateCriticalMinMaxValueTests
  {
    [Fact]
    public void WithoutSaveValidate_OnlyRangeChecked()
    {
      // Без isSaveValidate проверяется только попадание в [0:100].
      Assert.True(SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(50f, 0f, 100f, null).isValid);
      Assert.False(SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(150f, 0f, 100f, null).isValid);
    }

    [Fact]
    public void SaveValidate_DeficitBelowMin_Invalid()
    {
      // speed<0 (дефицит-ориентированный): value не может быть меньше min.
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(10f, 20f, 100f, -5, isSaveValidate: true);

      Assert.False(result.isValid);
    }

    [Fact]
    public void SaveValidate_DeficitWithinRange_Valid()
    {
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(50f, 20f, 100f, -5, isSaveValidate: true);

      Assert.True(result.isValid);
    }

    [Fact]
    public void SaveValidate_ExcessAboveMax_Invalid()
    {
      // speed>0 (избыток-ориентированный): value не может быть больше max.
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(95f, 0f, 80f, 5, isSaveValidate: true);

      Assert.False(result.isValid);
    }

    [Fact]
    public void SaveValidate_MinGreaterThanMax_Invalid()
    {
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(50f, 90f, 10f, -5, isSaveValidate: true);

      Assert.False(result.isValid);
    }

    [Fact]
    public void SaveValidate_NullSpeed_Invalid()
    {
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(50f, 0f, 100f, null, isSaveValidate: true);

      Assert.False(result.isValid);
    }

    [Fact]
    public void SaveValidate_ZeroSpeed_Invalid()
    {
      var result = SettingsValidator
          .ValidateCriticalMinMaxValueParamValue(50f, 0f, 100f, 0, isSaveValidate: true);

      Assert.False(result.isValid);
    }
  }
}
