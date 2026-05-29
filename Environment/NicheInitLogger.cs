using ISIDA.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ISIDA.Niche
{
  /// <summary>
  /// Запись протокола инициализации Niche (§6.11).
  /// </summary>
  public static class NicheInitLogger
  {
    /// <summary>
    /// Имя файла лога инициализации в каталоге Environment.
    /// </summary>
    public const string InitLogFileName = "niche_init_log.jsonl";

    /// <summary>
    /// Добавляет запись снимка инициализации в <paramref name="environmentFolder"/>/niche_init_log.jsonl.
    /// </summary>
    /// <param name="environmentFolder">Каталог Environment.</param>
    /// <param name="snapshot">Снимок инициализации.</param>
    public static void AppendSnapshot(string environmentFolder, NicheInitSnapshot snapshot)
    {
      if (string.IsNullOrWhiteSpace(environmentFolder) || snapshot == null)
        return;

      try
      {
        Directory.CreateDirectory(environmentFolder);
        string path = Path.Combine(environmentFolder, InitLogFileName);

        var payload = new Dictionary<string, object>
        {
          ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
          ["event"] = "niche_init_snapshot",
          ["experimentRunId"] = snapshot.ExperimentRunId ?? string.Empty,
          ["environmentFolder"] = snapshot.EnvironmentFolder ?? environmentFolder,
          ["phase"] = snapshot.Config != null ? snapshot.Config.Phase.ToString() : string.Empty,
          ["contourId"] = snapshot.Config != null ? snapshot.Config.ContourId ?? string.Empty : string.Empty,
          ["couplingMappingVersion"] = snapshot.Config != null ? snapshot.Config.CouplingMappingVersion : 0,
          ["spontaneousDriftEnabled"] = snapshot.Config != null && snapshot.Config.SpontaneousDriftEnabled,
          ["initialNicheParams"] = snapshot.InitialNicheParams ?? new Dictionary<int, float>(),
          ["initialCreatureParams"] = snapshot.InitialCreatureParams ?? new Dictionary<int, float>()
        };

        File.AppendAllText(path, JsonConvert.SerializeObject(payload) + Environment.NewLine);
        Logger.Info($"NicheInitSnapshot записан: {path}, run={snapshot.ExperimentRunId}");
      }
      catch (Exception ex)
      {
        Logger.Warning($"NicheInitLogger: {ex.Message}");
      }
    }
  }
}
