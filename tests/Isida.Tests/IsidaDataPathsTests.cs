using System.IO;
using ISIDA.Common;
using Xunit;

namespace Isida.Tests
{
  /// <summary>Тесты построения путей данных <see cref="IsidaDataPaths"/>.</summary>
  public class IsidaDataPathsTests
  {
    [Fact]
    public void CombineDataSubfolder_NoSubfolders_ReturnsDataFolder()
    {
      string data = Path.Combine("C:", "proj", "Data");

      string result = IsidaDataPaths.CombineDataSubfolder(data);

      Assert.Equal(Path.GetFullPath(data), result);
    }

    [Fact]
    public void CombineDataSubfolder_AppendsSubfolders()
    {
      string data = Path.Combine("C:", "proj", "Data");

      string result = IsidaDataPaths.CombineDataSubfolder(data, "Psychic", "Automatism");

      Assert.Equal(Path.GetFullPath(Path.Combine(data, "Psychic", "Automatism")), result);
    }

    [Fact]
    public void ResolveGomeostasFolder_EndsWithGomeostas()
    {
      string data = Path.Combine("C:", "proj", "Data");

      string result = IsidaDataPaths.ResolveGomeostasFolder(data);

      Assert.EndsWith(Path.Combine("Data", "Gomeostas"), result);
    }

    [Fact]
    public void ResolveActionsFolder_EndsWithActions()
    {
      string data = Path.Combine("C:", "proj", "Data");

      Assert.EndsWith(Path.Combine("Data", "Actions"), IsidaDataPaths.ResolveActionsFolder(data));
    }

    [Fact]
    public void ResolvePsychicSubmoduleFolder_IncludesPsychicPrefix()
    {
      string data = Path.Combine("C:", "proj", "Data");

      string result = IsidaDataPaths.ResolvePsychicSubmoduleFolder(data, "Memory");

      Assert.EndsWith(Path.Combine("Data", "Psychic", "Memory"), result);
    }

    [Fact]
    public void ResolveScenarioReportsFolder_UnderScenariosReports()
    {
      string root = Path.Combine("C:", "proj");

      string result = IsidaDataPaths.ResolveScenarioReportsFolder(root);

      Assert.EndsWith(Path.Combine("Scenarios", "Reports"), result);
    }

    [Fact]
    public void TryGetProjectRootFromDataFolderPath_Matching_ReturnsParent()
    {
      string data = Path.Combine("C:", "proj", "Data");

      bool ok = IsidaDataPaths.TryGetProjectRootFromDataFolderPath(data, out string root);

      Assert.True(ok);
      Assert.Equal(Path.GetFullPath(Path.Combine("C:", "proj")), root);
    }

    [Fact]
    public void TryGetProjectRootFromDataFolderPath_NotData_False()
    {
      Assert.False(IsidaDataPaths.TryGetProjectRootFromDataFolderPath(
          Path.Combine("C:", "proj", "Other"), out _));
    }

    [Fact]
    public void TryGetProjectRootFromDataFolderPath_Empty_False()
    {
      Assert.False(IsidaDataPaths.TryGetProjectRootFromDataFolderPath(null, out _));
      Assert.False(IsidaDataPaths.TryGetProjectRootFromDataFolderPath("  ", out _));
    }
  }
}
