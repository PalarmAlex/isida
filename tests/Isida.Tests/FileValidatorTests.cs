using System.Collections.Generic;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты валидации файлов данных <see cref="FileValidator"/>.</summary>
  public class FileValidatorTests
  {
    // ---------- Валидные примеры ----------

    [Fact]
    public void IsValidStyleFile_ValidRow_True()
    {
      var lines = new List<string>
      {
        "# Формат: ID|Имя|Описание|Антагонисты",
        "1|Спокойствие|описание|2,3"
      };

      Assert.True(FileValidator.IsValidStyleFile(lines));
    }

    [Fact]
    public void IsValidStyleFile_TooFewColumns_False()
    {
      Assert.False(FileValidator.IsValidStyleFile(new List<string> { "1|Спокойствие|описание" }));
    }

    [Fact]
    public void IsValidStyleFile_OnlyHeader_True()
    {
      Assert.True(FileValidator.IsValidStyleFile(new List<string> { "# Формат: ID|Имя|Описание|Антагонисты" }));
    }

    [Fact]
    public void IsValidStyleFile_NullOrEmpty_False()
    {
      Assert.False(FileValidator.IsValidStyleFile((IEnumerable<string>)null));
      Assert.False(FileValidator.IsValidStyleFile(new List<string>()));
    }

    [Fact]
    public void IsValidActionsFile_ValidRow_True()
    {
      // ID|Имя|Описание|Интенсивность|Антагонисты|Target|InfluenceActionId
      var lines = new List<string> { "1|Шаг|описание|5|2|1,3|0" };

      Assert.True(FileValidator.IsValidActionsFile(lines));
    }

    [Fact]
    public void IsValidActionsFile_VigorOutOfRange_False()
    {
      var lines = new List<string> { "1|Шаг|описание|15|2|1,3|0" };

      Assert.False(FileValidator.IsValidActionsFile(lines));
    }

    [Fact]
    public void IsValidGeneticReflexesFile_ValidRow_True()
    {
      var lines = new List<string> { "1|-1|2,3|4|5|6|0" };

      Assert.True(FileValidator.IsValidGeneticReflexesFile(lines));
    }

    [Fact]
    public void IsValidGeneticReflexesFile_TooFewColumns_False()
    {
      Assert.False(FileValidator.IsValidGeneticReflexesFile(new List<string> { "1|-1|2,3" }));
    }

    [Fact]
    public void IsValidAgentParametersFile_ValidRow_True()
    {
      // ID|Название|Описание|Значение|Вес|Норма|Скорость|Активации|Критический|Мин|Макс
      var lines = new List<string> { "1|Сахар|описание|50.0|50|40|-1|1,2|1|0|100" };

      Assert.True(FileValidator.IsValidAgentParametersFile(lines));
    }

    [Fact]
    public void IsValidAgentParametersFile_ValueOutOfRange_False()
    {
      var lines = new List<string> { "1|Сахар|описание|150.0|50|40|-1|1,2|1|0|100" };

      Assert.False(FileValidator.IsValidAgentParametersFile(lines));
    }

    [Fact]
    public void IsValidReflexChainsFile_ChainAndLink_True()
    {
      var lines = new List<string>
      {
        "CHAIN|1|Имя|Описание",
        "LINK|10|1|11|12|Описание"
      };

      Assert.True(FileValidator.IsValidReflexChainsFile(lines));
    }

    [Fact]
    public void IsValidReflexChainsFile_UnknownRowKind_False()
    {
      Assert.False(FileValidator.IsValidReflexChainsFile(new List<string> { "BOGUS|1|2|3|4" }));
    }

    [Fact]
    public void IsValidAutomatizmTreeFile_ValidRow_True()
    {
      // ID|ParentID|BaseID|EmotionID|ActivityID|ToneMoodID|SimbolID|VerbID
      var lines = new List<string> { "1|0|0|1|0|0|0|0" };

      Assert.True(FileValidator.IsValidAutomatizmTreeFile(lines));
    }

    [Fact]
    public void IsValidAutomatizmTreeFile_BaseIdOutOfRange_False()
    {
      Assert.False(FileValidator.IsValidAutomatizmTreeFile(new List<string> { "1|0|5|1|0|0|0|0" }));
    }

    [Fact]
    public void IsValidAutomatizmFile_ValidRow_True()
    {
      // ID|BranchID|Usefulness|ActionsImageID|NextID|Energy|Belief|Count
      var lines = new List<string> { "1|0|5|2|0|5|1|3" };

      Assert.True(FileValidator.IsValidAutomatizmFile(lines));
    }

    [Fact]
    public void IsValidAutomatizmFile_EnergyOutOfRange_False()
    {
      Assert.False(FileValidator.IsValidAutomatizmFile(new List<string> { "1|0|5|2|0|11|1|3" }));
    }

    [Fact]
    public void IsValidSituationTypeFile_ValidRow_True()
    {
      var lines = new List<string> { "1|0|0|5|2" };

      Assert.True(FileValidator.IsValidSituationTypeFile(lines));
    }

    [Fact]
    public void IsValidSituationTypeFile_IdOutOfRange_False()
    {
      Assert.False(FileValidator.IsValidSituationTypeFile(new List<string> { "61|0|0|5|2" }));
    }

    [Fact]
    public void IsValidPurposeImagesFile_ValidRow_True()
    {
      var lines = new List<string> { "1|2|1|3|4" };

      Assert.True(FileValidator.IsValidPurposeImagesFile(lines));
    }

    [Fact]
    public void IsValidPurposeImagesFile_TargetOutOfRange_False()
    {
      Assert.False(FileValidator.IsValidPurposeImagesFile(new List<string> { "1|9|1|3|4" }));
    }

    [Fact]
    public void IsValidPerceptionImagesFile_ValidRow_True()
    {
      var lines = new List<string> { "1|2,3|4,5|6|2" };

      Assert.True(FileValidator.IsValidPerceptionImagesFile(lines));
    }

    [Fact]
    public void IsValidPerceptionImagesFile_InvalidVisualColor_False()
    {
      var lines = new List<string> { "1|2,3|4,5|6|99" };

      Assert.False(FileValidator.IsValidPerceptionImagesFile(lines));
    }

    [Fact]
    public void IsValidThemeImagesFile_ValidRow_True()
    {
      var lines = new List<string> { "1|5|2|10" };

      Assert.True(FileValidator.IsValidThemeImagesFile(lines));
    }

    [Fact]
    public void IsValidThemeImagesFile_WeightOutOfRange_False()
    {
      Assert.False(FileValidator.IsValidThemeImagesFile(new List<string> { "1|11|2|10" }));
    }

    [Fact]
    public void IsValidThemeTypesFile_ValidRow_True()
    {
      var lines = new List<string> { "1|Описание темы|5|1,2,3" };

      Assert.True(FileValidator.IsValidThemeTypesFile(lines));
    }

    [Fact]
    public void IsValidThemeTypesFile_DefaultWeightZero_False()
    {
      Assert.False(FileValidator.IsValidThemeTypesFile(new List<string> { "1|Описание темы|0" }));
    }

    [Fact]
    public void IsValidEnvironmentPressureRulesFile_RequiresHeaderFirst()
    {
      var withHeader = new List<string>
      {
        "# Формат: RuleId|ProbeKey|Имя|Описание|Influences",
        "1|probe|Имя|Описание|1:5"
      };
      var withoutHeader = new List<string> { "1|probe|Имя|Описание|1:5" };

      Assert.True(FileValidator.IsValidEnvironmentPressureRulesFile(withHeader));
      Assert.False(FileValidator.IsValidEnvironmentPressureRulesFile(withoutHeader));
    }
  }
}
