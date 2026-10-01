using System.IO;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты шаблона каталогов проекта данных и путей настроек.</summary>
  public class ProjectDirectoryTemplateTests
  {
    [Fact]
    public void TemplateNode_NullChildren_BecomesEmptyList()
    {
      var node = new ProjectDirectoryTemplateNode("Logs");

      Assert.Equal("Logs", node.Name);
      Assert.Empty(node.Children);
    }

    [Fact]
    public void TemplateNode_NullName_BecomesEmptyString()
    {
      var node = new ProjectDirectoryTemplateNode(null);

      Assert.Equal(string.Empty, node.Name);
    }

    [Fact]
    public void GetProjectDirectoryTemplateRoot_HasRequiredFolders()
    {
      var root = SettingsValidator.GetProjectDirectoryTemplateRoot();

      foreach (string required in SettingsValidator.RequiredProjectRootFolderNames)
      {
        Assert.Contains(root.Children, c => c.Name == required);
      }
    }

    [Fact]
    public void GetFolderSegmentsForPathSettingKey_KnownKeys()
    {
      Assert.Equal(new[] { "Settings" }, SettingsValidator.GetFolderSegmentsForPathSettingKey("SettingsPath"));
      Assert.Equal(new[] { "Logs" }, SettingsValidator.GetFolderSegmentsForPathSettingKey("LogsFolderPath"));
      Assert.Equal(new[] { "Data" }, SettingsValidator.GetFolderSegmentsForPathSettingKey("DataFolderPath"));
      Assert.Equal(new[] { "Scenarios", "Reports" },
          SettingsValidator.GetFolderSegmentsForPathSettingKey("ScenarioReportsFolderPath"));
    }

    [Fact]
    public void GetFolderSegmentsForPathSettingKey_UnknownOrNull_Null()
    {
      Assert.Null(SettingsValidator.GetFolderSegmentsForPathSettingKey("Nope"));
      Assert.Null(SettingsValidator.GetFolderSegmentsForPathSettingKey(null));
    }

    [Fact]
    public void TryGetProjectRootFromSettingsPath_Matching_ReturnsParent()
    {
      string settings = Path.Combine("C:", "proj", "Settings");

      bool ok = SettingsValidator.TryGetProjectRootFromSettingsPath(settings, out string root);

      Assert.True(ok);
      Assert.Equal(Path.GetFullPath(Path.Combine("C:", "proj")), root);
    }

    [Fact]
    public void TryGetProjectRootFromSettingsPath_NotSettings_False()
    {
      Assert.False(SettingsValidator.TryGetProjectRootFromSettingsPath(
          Path.Combine("C:", "proj", "Other"), out _));
      Assert.False(SettingsValidator.TryGetProjectRootFromSettingsPath(null, out _));
    }

    [Fact]
    public void GetExpectedFolderPathForSetting_CombinesSegments()
    {
      string root = Path.Combine("C:", "proj");

      string expected = SettingsValidator.GetExpectedFolderPathForSetting(root, "ScenarioReportsFolderPath");

      Assert.Equal(Path.GetFullPath(Path.Combine(root, "Scenarios", "Reports")), expected);
    }

    [Fact]
    public void GetExpectedFolderPathForSetting_UnknownKey_Throws()
    {
      Assert.Throws<System.ArgumentException>(() =>
          SettingsValidator.GetExpectedFolderPathForSetting(Path.Combine("C:", "proj"), "Nope"));
    }

    [Fact]
    public void IsFolderPathMatchingProjectTemplate_MatchesAndMismatches()
    {
      string root = Path.Combine("C:", "proj");
      string matching = Path.Combine(root, "Data");
      string mismatching = Path.Combine(root, "Other");

      Assert.True(SettingsValidator.IsFolderPathMatchingProjectTemplate(root, matching, "DataFolderPath"));
      Assert.False(SettingsValidator.IsFolderPathMatchingProjectTemplate(root, mismatching, "DataFolderPath"));
    }

    [Fact]
    public void MandatoryProjectRootFoldersExist_DetectsMissing()
    {
      string temp = Path.Combine(Path.GetTempPath(), "isida-tests-" + System.Guid.NewGuid().ToString("N"));
      try
      {
        Directory.CreateDirectory(temp);
        Directory.CreateDirectory(Path.Combine(temp, "Logs"));
        Directory.CreateDirectory(Path.Combine(temp, "Data"));

        bool ok = SettingsValidator.MandatoryProjectRootFoldersExist(temp, out var missing);

        Assert.False(ok);
        Assert.Contains("BootData", missing);
        Assert.Contains("Settings", missing);
        Assert.Contains("Scenarios", missing);
      }
      finally
      {
        if (Directory.Exists(temp))
          Directory.Delete(temp, true);
      }
    }

    [Fact]
    public void EnsureProjectDirectoryStructure_CreatesRequiredFolders()
    {
      string temp = Path.Combine(Path.GetTempPath(), "isida-tests-" + System.Guid.NewGuid().ToString("N"));
      try
      {
        SettingsValidator.EnsureProjectDirectoryStructure(temp);

        Assert.True(SettingsValidator.MandatoryProjectRootFoldersExist(temp, out _));
        Assert.True(Directory.Exists(Path.Combine(temp, "Data", "Psychic")));
      }
      finally
      {
        if (Directory.Exists(temp))
          Directory.Delete(temp, true);
      }
    }
  }
}
