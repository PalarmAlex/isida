using ISIDA.Common;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ISIDA.Niche
{
  /// <summary>
  /// Документация прогона триады в каталоге логов (этап 6.5).
  /// </summary>
  public static class TriadRunManifestLogger
  {
    /// <summary>Текущий манифест прогона в каталоге логов.</summary>
    public const string ManifestFileName = "triad_run_manifest.json";

    /// <summary>История событий прогона (append-only).</summary>
    public const string HistoryFileName = "triad_run_history.jsonl";

    /// <summary>
    /// Записывает актуальный манифест и добавляет строку в историю.
    /// </summary>
    /// <param name="logsFolder">Каталог логов проекта.</param>
    /// <param name="manifest">Данные прогона.</param>
    public static void WriteCurrent(string logsFolder, TriadRunManifest manifest)
    {
      if (string.IsNullOrWhiteSpace(logsFolder) || manifest == null)
        return;

      try
      {
        Directory.CreateDirectory(logsFolder);
        manifest.UpdatedAtUtc = DateTime.UtcNow;
        manifest.LogsFolder = logsFolder;

        string manifestPath = Path.Combine(logsFolder, ManifestFileName);
        string json = JsonConvert.SerializeObject(manifest, Formatting.Indented);
        File.WriteAllText(manifestPath, json);

        AppendHistory(logsFolder, manifest);
      }
      catch (Exception ex)
      {
        Logger.Warning($"TriadRunManifestLogger: {ex.Message}");
      }
    }

    /// <summary>
    /// Читает текущий манифест из каталога логов.
    /// </summary>
    /// <param name="logsFolder">Каталог логов.</param>
    /// <param name="manifest">Прочитанный манифест.</param>
    /// <returns>True, если файл найден и разобран.</returns>
    public static bool TryReadCurrent(string logsFolder, out TriadRunManifest manifest)
    {
      manifest = null;
      if (string.IsNullOrWhiteSpace(logsFolder))
        return false;

      string path = Path.Combine(logsFolder, ManifestFileName);
      if (!File.Exists(path))
        return false;

      try
      {
        manifest = JsonConvert.DeserializeObject<TriadRunManifest>(File.ReadAllText(path));
        return manifest != null;
      }
      catch
      {
        return false;
      }
    }

    private static void AppendHistory(string logsFolder, TriadRunManifest manifest)
    {
      string historyPath = Path.Combine(logsFolder, HistoryFileName);
      var payload = new Dictionary<string, object>
      {
        ["time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        ["event"] = manifest.Event ?? string.Empty,
        ["experimentRunId"] = manifest.ExperimentRunId ?? string.Empty,
        ["couplingMappingVersion"] = manifest.CouplingMappingVersion,
        ["phase"] = manifest.Phase ?? string.Empty,
        ["contourId"] = manifest.ContourId ?? string.Empty,
        ["environmentFolder"] = manifest.EnvironmentFolder ?? string.Empty
      };

      File.AppendAllText(historyPath, JsonConvert.SerializeObject(payload) + Environment.NewLine);
    }
  }

  /// <summary>
  /// Манифест текущего прогона эксперимента триады (§6.11, этап 6.5).
  /// </summary>
  public sealed class TriadRunManifest
  {
    /// <summary>experiment_run_id.</summary>
    public string ExperimentRunId { get; set; } = string.Empty;

    /// <summary>coupling_mapping_version из triad_config.dat.</summary>
    public int CouplingMappingVersion { get; set; }

    /// <summary>Фаза A/B/C.</summary>
    public string Phase { get; set; } = string.Empty;

    /// <summary>ContourId.</summary>
    public string ContourId { get; set; } = string.Empty;

    /// <summary>Каталог Environment.</summary>
    public string EnvironmentFolder { get; set; } = string.Empty;

    /// <summary>Каталог логов.</summary>
    public string LogsFolder { get; set; } = string.Empty;

    /// <summary>RoleProfile Niche.</summary>
    public string NicheRoleProfileId { get; set; } = string.Empty;

    /// <summary>UseProbeContour (0/1).</summary>
    public bool UseProbeContour { get; set; }

    /// <summary>Момент последнего обновления (UTC).</summary>
    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>
    /// Событие записи: engine_start, run_start, config_reload, dyad_reset, dyad_hard_reset.
    /// </summary>
    public string Event { get; set; } = string.Empty;
  }
}
