using System;
using System.IO;

namespace ISIDA.Common
{
  /// <summary>
  /// Имена подкаталогов данных ISIDA относительно корня <c>Data</c> и корня установки.
  /// </summary>
  public static class IsidaDataPaths
  {
    /// <summary>Имя корневого каталога данных: <c>Data</c>.</summary>
    public const string DataFolderName = "Data";
    /// <summary>Подкаталог данных гомеостаза.</summary>
    public const string GomeostasSubfolder = "Gomeostas";
    /// <summary>Подкаталог адаптивных и внешних действий.</summary>
    public const string ActionsSubfolder = "Actions";
    /// <summary>Подкаталог вербальных сенсоров.</summary>
    public const string SensorsSubfolder = "Sensors";
    /// <summary>Подкаталог безусловных и условных рефлексов.</summary>
    public const string ReflexesSubfolder = "Reflexes";
    /// <summary>Подкаталог данных психики.</summary>
    public const string PsychicSubfolder = "Psychic";
    /// <summary>Каталог файлов сценариев на уровне корня ISIDA (реестры, строки прогона).</summary>
    public const string ScenariosSubfolder = "Scenarios";
    /// <summary>Подкаталог вывода Research Harness.</summary>
    public const string ResearchHarnessSubfolder = "ResearchHarness";
    /// <summary>Подкаталог HTML-отчётов прогона сценариев.</summary>
    public const string ReportsSubfolder = "Reports";
    /// <summary>Корень данных ISIDA по умолчанию: <c>%ProgramData%\ISIDA</c>.</summary>
    public static string GetDefaultIsidaRoot()
    {
      return Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
          "ISIDA");
    }

    /// <summary>Каталог <c>Data</c> по умолчанию: <c>%ProgramData%\ISIDA\Data</c>.</summary>
    public static string GetDefaultDataFolder()
    {
      return Path.Combine(GetDefaultIsidaRoot(), DataFolderName);
    }

    /// <summary>Собирает путь к подкаталогу внутри <paramref name="dataFolder"/>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; при null или пустой строке — <see cref="GetDefaultDataFolder"/>.</param>
    /// <param name="subfolders">Сегменты пути относительно <paramref name="dataFolder"/>.</param>
    /// <returns>Полный канонизированный путь к каталогу.</returns>
    public static string CombineDataSubfolder(string dataFolder, params string[] subfolders)
    {
      string path = string.IsNullOrWhiteSpace(dataFolder)
          ? GetDefaultDataFolder()
          : dataFolder.Trim();
      if (subfolders == null || subfolders.Length == 0)
        return Path.GetFullPath(path);
      for (int i = 0; i < subfolders.Length; i++)
        path = Path.Combine(path, subfolders[i]);
      return Path.GetFullPath(path);
    }

    /// <summary>Возвращает путь к <c>Data\Gomeostas</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу гомеостаза.</returns>
    public static string ResolveGomeostasFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, GomeostasSubfolder);

    /// <summary>Возвращает путь к <c>Data\Actions</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу действий.</returns>
    public static string ResolveActionsFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, ActionsSubfolder);

    /// <summary>Возвращает путь к <c>Data\Sensors</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу сенсоров.</returns>
    public static string ResolveSensorsFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, SensorsSubfolder);

    /// <summary>Возвращает путь к <c>Data\Reflexes</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу рефлексов.</returns>
    public static string ResolveReflexesFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, ReflexesSubfolder);

    /// <summary>Возвращает путь к <c>Data\Psychic</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу психики.</returns>
    public static string ResolvePsychicFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, PsychicSubfolder);

    /// <summary>Каталог файлов сценариев: <c>{isidaRoot}\Scenarios</c>.</summary>
    /// <param name="isidaRoot">Корень ISIDA; null — <see cref="GetDefaultIsidaRoot"/>.</param>
    /// <returns>Полный путь к каталогу сценариев.</returns>
    public static string ResolveScenariosFolder(string isidaRoot = null)
    {
      string root = string.IsNullOrWhiteSpace(isidaRoot) ? GetDefaultIsidaRoot() : isidaRoot.Trim();
      return Path.GetFullPath(Path.Combine(root, ScenariosSubfolder));
    }

    /// <summary>Возвращает путь к <c>Data\ResearchHarness</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null — каталог по умолчанию.</param>
    /// <returns>Полный путь к каталогу Research Harness.</returns>
    public static string ResolveResearchHarnessFolder(string dataFolder = null) =>
        CombineDataSubfolder(dataFolder, ResearchHarnessSubfolder);

    /// <summary>Возвращает путь к подкаталогу внутри <c>Data\Psychic</c>.</summary>
    /// <param name="dataFolder">Корень <c>Data</c>; null или пустая строка — каталог по умолчанию.</param>
    /// <param name="psychicSubfolders">Дополнительные сегменты после <see cref="PsychicSubfolder"/> (например <c>Automatism</c>).</param>
    /// <returns>Полный путь к каталогу модуля психики.</returns>
    public static string ResolvePsychicSubmoduleFolder(string dataFolder, params string[] psychicSubfolders) =>
        CombineDataSubfolder(
            string.IsNullOrWhiteSpace(dataFolder) ? GetDefaultDataFolder() : dataFolder,
            MergePsychicSubfolders(psychicSubfolders));

    private static string[] MergePsychicSubfolders(string[] psychicSubfolders)
    {
      if (psychicSubfolders == null || psychicSubfolders.Length == 0)
        return new[] { PsychicSubfolder };
      var merged = new string[psychicSubfolders.Length + 1];
      merged[0] = PsychicSubfolder;
      for (int i = 0; i < psychicSubfolders.Length; i++)
        merged[i + 1] = psychicSubfolders[i];
      return merged;
    }

    /// <summary>Каталог HTML-отчётов сценариев: <c>{isidaRoot}\Scenarios\Reports</c>.</summary>
    /// <param name="isidaRoot">Корень ISIDA; null — <see cref="GetDefaultIsidaRoot"/>.</param>
    /// <returns>Полный путь к каталогу отчётов.</returns>
    public static string ResolveScenarioReportsFolder(string isidaRoot = null)
    {
      string root = string.IsNullOrWhiteSpace(isidaRoot) ? GetDefaultIsidaRoot() : isidaRoot.Trim();
      return Path.GetFullPath(Path.Combine(root, ScenariosSubfolder, ReportsSubfolder));
    }

    /// <summary>Определяет корень проекта по пути к каталогу <c>Data</c>.</summary>
    /// <param name="dataFolderPath">Полный путь к каталогу <c>Data</c>.</param>
    /// <param name="projectRoot">При успехе — родительский каталог (корень проекта данных).</param>
    /// <returns><c>true</c>, если корень определён.</returns>
    public static bool TryGetProjectRootFromDataFolderPath(string dataFolderPath, out string projectRoot)
    {
      projectRoot = null;
      if (string.IsNullOrWhiteSpace(dataFolderPath))
        return false;

      try
      {
        string trimmed = dataFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(trimmed);
        if (!string.Equals(Path.GetFileName(full), DataFolderName, StringComparison.OrdinalIgnoreCase))
          return false;
        projectRoot = Path.GetDirectoryName(full);
        return !string.IsNullOrEmpty(projectRoot);
      }
      catch
      {
        return false;
      }
    }
  }
}
