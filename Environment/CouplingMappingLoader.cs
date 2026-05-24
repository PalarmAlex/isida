using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Niche
{
  /// <summary>
  /// Загрузка конфигурации coupling и параметров host-Niche из каталога Environment.
  /// </summary>
  public static class CouplingMappingLoader
  {
    /// <summary>
    /// Загружает конфигурацию триады из каталога <paramref name="environmentFolder"/>.
    /// </summary>
    /// <param name="environmentFolder">Путь к каталогу Environment (action_coupling.dat и др.).</param>
    /// <returns>Конфигурация эксперимента; пустая при отсутствии каталога или файлов.</returns>
    public static TriadExperimentConfig LoadFromFolder(string environmentFolder)
    {
      var config = new TriadExperimentConfig();
      if (string.IsNullOrWhiteSpace(environmentFolder) || !Directory.Exists(environmentFolder))
        return config;

      string triadConfigPath = Path.Combine(environmentFolder, "triad_config.dat");
      if (File.Exists(triadConfigPath))
        ApplyTriadConfigLines(File.ReadAllLines(triadConfigPath), config);

      string actionPath = Path.Combine(environmentFolder, "action_coupling.dat");
      if (File.Exists(actionPath))
        config.ActionCoupling.AddRange(ParseActionCoupling(File.ReadAllLines(actionPath)));

      string mappingPath = Path.Combine(environmentFolder, "niche_creature_mapping.dat");
      if (File.Exists(mappingPath))
        config.NicheToCreature.AddRange(ParseNicheCreatureMapping(File.ReadAllLines(mappingPath)));

      string nicheParamsPath = Path.Combine(environmentFolder, "niche_params.dat");
      if (File.Exists(nicheParamsPath))
        config.NicheParameters.AddRange(ParseNicheParams(File.ReadAllLines(nicheParamsPath)));

      string operatorNichePath = Path.Combine(environmentFolder, "operator_niche_coupling.dat");
      if (File.Exists(operatorNichePath))
        config.OperatorNicheCoupling.AddRange(ParseOperatorNicheCoupling(File.ReadAllLines(operatorNichePath)));

      string contourPath = Path.Combine(environmentFolder, "contour_probes.dat");
      if (File.Exists(contourPath))
        config.ContourProbes.AddRange(ParseContourProbes(File.ReadAllLines(contourPath)));

      return config;
    }

    /// <summary>
    /// Создаёт шаблонные файлы конфигурации в каталоге Environment.
    /// </summary>
    /// <param name="environmentFolder">Целевой каталог.</param>
    public static void EnsureTemplateFiles(string environmentFolder)
    {
      if (string.IsNullOrWhiteSpace(environmentFolder))
        return;

      Directory.CreateDirectory(environmentFolder);

      WriteIfMissing(Path.Combine(environmentFolder, "triad_config.dat"), FileHeaders.TriadConfigTemplate);
      WriteIfMissing(Path.Combine(environmentFolder, "action_coupling.dat"), FileHeaders.ActionCouplingTemplate);
      WriteIfMissing(Path.Combine(environmentFolder, "niche_creature_mapping.dat"), FileHeaders.NicheCreatureMappingTemplate);
      WriteIfMissing(Path.Combine(environmentFolder, "niche_params.dat"), FileHeaders.NicheParamsTemplate);
      WriteIfMissing(Path.Combine(environmentFolder, "operator_niche_coupling.dat"), FileHeaders.OperatorNicheCouplingTemplate);
      WriteIfMissing(Path.Combine(environmentFolder, "contour_probes.dat"), FileHeaders.ContourProbesTemplate);
    }

    private static void WriteIfMissing(string path, string content)
    {
      if (!File.Exists(path))
        File.WriteAllText(path, content);
    }

    private static void ApplyTriadConfigLines(string[] lines, TriadExperimentConfig config)
    {
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 4)
          continue;

        if (Enum.TryParse(parts[0].Trim(), true, out TriadPhase phase))
          config.Phase = phase;
        config.ContourId = parts[1].Trim();
        config.SpontaneousDriftEnabled = parts[2].Trim() == "1";
        if (int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ver))
          config.CouplingMappingVersion = ver;

        if (parts.Length >= 5 && int.TryParse(parts[4].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int baselineN))
          config.AoeSettings.BaselineWindowN = baselineN;
        if (parts.Length >= 6 && float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float threshold))
          config.AoeSettings.ResponseThreshold = threshold;
        if (parts.Length >= 7 && int.TryParse(parts[6].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int k))
          config.AoeSettings.CorrelationHorizonK = k;
        if (parts.Length >= 8 && int.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int wEval))
          config.AoeSettings.EvalWindowPulses = wEval;
        if (parts.Length >= 9 && int.TryParse(parts[8].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int useEngine))
          config.UseFullNicheEngine = useEngine != 0;
        if (parts.Length >= 10)
          config.NicheRoleProfileId = parts[9].Trim();
        if (parts.Length >= 11 && int.TryParse(parts[10].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int useProbe))
          config.UseProbeContour = useProbe != 0;
        return;
      }
    }

    private static List<CouplingTarget> ParseActionCoupling(string[] lines)
    {
      var list = new List<CouplingTarget>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 4)
          continue;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int actionId))
          continue;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nicheParamId))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
          continue;
        if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
          scale = 1f;

        list.Add(new CouplingTarget
        {
          ActionId = actionId,
          NicheParamId = nicheParamId,
          Delta = delta,
          Scale = scale
        });
      }

      return list;
    }

    private static List<NicheCreatureMapping> ParseNicheCreatureMapping(string[] lines)
    {
      var list = new List<NicheCreatureMapping>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 4)
          continue;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nicheId))
          continue;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int creatureId))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
          scale = 1f;
        if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lag))
          lag = 0;

        list.Add(new NicheCreatureMapping
        {
          NicheParamId = nicheId,
          CreatureParamId = creatureId,
          Scale = scale,
          LagPulses = lag
        });
      }

      return list;
    }

    private static List<NicheParameterDef> ParseNicheParams(string[] lines)
    {
      var list = new List<NicheParameterDef>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 3)
          continue;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int paramId))
          continue;
        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float speed))
          speed = 0f;

        list.Add(new NicheParameterDef
        {
          ParamId = paramId,
          InitialValue = ClampParam(value),
          SpeedPerPulse = speed
        });
      }

      return list;
    }

    private static List<OperatorNicheCouplingTarget> ParseOperatorNicheCoupling(string[] lines)
    {
      var list = new List<OperatorNicheCouplingTarget>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 4)
          continue;

        if (!int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int influenceId))
          continue;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nicheParamId))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
          continue;
        if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
          scale = 1f;

        list.Add(new OperatorNicheCouplingTarget
        {
          InfluenceActionId = influenceId,
          NicheParamId = nicheParamId,
          Delta = delta,
          Scale = scale
        });
      }

      return list;
    }

    /// <summary>
    /// Сохраняет фазу триады в triad_config.dat (первая строка данных).
    /// </summary>
    /// <param name="environmentFolder">Каталог Environment.</param>
    /// <param name="phase">Новая фаза A/B/C.</param>
    /// <param name="errorMessage">Текст ошибки при неуспехе.</param>
    /// <returns>True, если файл обновлён.</returns>
    public static bool TrySaveTriadPhase(string environmentFolder, TriadPhase phase, out string errorMessage)
    {
      errorMessage = null;
      if (string.IsNullOrWhiteSpace(environmentFolder))
      {
        errorMessage = "Не указан каталог Environment.";
        return false;
      }

      EnsureTemplateFiles(environmentFolder);
      var config = LoadFromFolder(environmentFolder);
      config.Phase = phase;
      return TrySaveExperimentConfig(environmentFolder, config, out errorMessage);
    }

    /// <summary>
    /// Сохраняет полную конфигурацию триады в каталог Environment.
    /// </summary>
    /// <param name="environmentFolder">Каталог Environment.</param>
    /// <param name="config">Конфигурация для записи.</param>
    /// <param name="errorMessage">Текст ошибки при неуспехе.</param>
    /// <returns>True, если все файлы сохранены.</returns>
    public static bool TrySaveExperimentConfig(
        string environmentFolder,
        TriadExperimentConfig config,
        out string errorMessage)
    {
      errorMessage = null;
      if (config == null)
      {
        errorMessage = "Конфигурация не задана.";
        return false;
      }

      if (string.IsNullOrWhiteSpace(environmentFolder))
      {
        errorMessage = "Не указан каталог Environment.";
        return false;
      }

      if (!ValidateExperimentConfig(config, out errorMessage))
        return false;

      EnsureTemplateFiles(environmentFolder);

      var triadLines = BuildTriadConfigLines(config);
      var actionLines = BuildActionCouplingLines(config.ActionCoupling);
      var mappingLines = BuildNicheCreatureMappingLines(config.NicheToCreature);
      var nicheParamLines = BuildNicheParamsLines(config.NicheParameters);
      var operatorLines = BuildOperatorNicheCouplingLines(config.OperatorNicheCoupling);
      var contourLines = BuildContourProbeLines(config.ContourProbes);

      if (!SaveLinesFile(Path.Combine(environmentFolder, "triad_config.dat"), triadLines, "triad_config.dat", out errorMessage))
        return false;
      if (!SaveLinesFile(Path.Combine(environmentFolder, "action_coupling.dat"), actionLines, "action_coupling.dat", out errorMessage))
        return false;
      if (!SaveLinesFile(Path.Combine(environmentFolder, "niche_creature_mapping.dat"), mappingLines, "niche_creature_mapping.dat", out errorMessage))
        return false;
      if (!SaveLinesFile(Path.Combine(environmentFolder, "niche_params.dat"), nicheParamLines, "niche_params.dat", out errorMessage))
        return false;
      if (!SaveLinesFile(Path.Combine(environmentFolder, "operator_niche_coupling.dat"), operatorLines, "operator_niche_coupling.dat", out errorMessage))
        return false;
      if (!SaveLinesFile(Path.Combine(environmentFolder, "contour_probes.dat"), contourLines, "contour_probes.dat", out errorMessage))
        return false;

      return true;
    }

    /// <summary>
    /// Проверяет конфигурацию перед сохранением.
    /// </summary>
    public static bool ValidateExperimentConfig(TriadExperimentConfig config, out string errorMessage)
    {
      errorMessage = null;
      if (config == null)
      {
        errorMessage = "Конфигурация не задана.";
        return false;
      }

      if (string.IsNullOrWhiteSpace(config.ContourId))
      {
        errorMessage = "ContourId не может быть пустым.";
        return false;
      }

      if (config.CouplingMappingVersion < 0)
      {
        errorMessage = "CouplingMappingVersion не может быть отрицательным.";
        return false;
      }

      var aoe = config.AoeSettings ?? new TriadAoeSettings();
      if (aoe.BaselineWindowN <= 0 || aoe.CorrelationHorizonK <= 0 || aoe.EvalWindowPulses <= 0)
      {
        errorMessage = "Параметры AOE (BaselineN, K, W_eval) должны быть > 0.";
        return false;
      }

      if (aoe.ResponseThreshold < 0f)
      {
        errorMessage = "AOE Threshold не может быть отрицательным.";
        return false;
      }

      if (!TriadPhaseStagePolicy.IsPhaseAllowed(config.Phase, AppGlobalState.EvolutionStage, out errorMessage))
        return false;

      foreach (var row in config.ActionCoupling ?? Enumerable.Empty<CouplingTarget>())
      {
        if (row.ActionId <= 0 || row.NicheParamId <= 0)
        {
          errorMessage = "action_coupling: ActionId и NicheParamId должны быть > 0.";
          return false;
        }
      }

      foreach (var row in config.NicheToCreature ?? Enumerable.Empty<NicheCreatureMapping>())
      {
        if (row.NicheParamId <= 0 || row.CreatureParamId <= 0)
        {
          errorMessage = "niche_creature_mapping: Niche и Creature param id должны быть > 0.";
          return false;
        }
        if (row.LagPulses < 0)
        {
          errorMessage = "niche_creature_mapping: Lag не может быть отрицательным.";
          return false;
        }
      }

      foreach (var row in config.OperatorNicheCoupling ?? Enumerable.Empty<OperatorNicheCouplingTarget>())
      {
        if (row.InfluenceActionId <= 0 || row.NicheParamId <= 0)
        {
          errorMessage = "operator_niche_coupling: InfluenceId и NicheParamId должны быть > 0.";
          return false;
        }
      }

      foreach (var row in config.NicheParameters ?? Enumerable.Empty<NicheParameterDef>())
      {
        if (row.ParamId <= 0)
        {
          errorMessage = "niche_params: ParamId должен быть > 0.";
          return false;
        }
        if (row.InitialValue < 0f || row.InitialValue > 100f)
        {
          errorMessage = "niche_params: InitialValue должно быть в диапазоне 0…100.";
          return false;
        }
      }

      foreach (var row in config.ContourProbes ?? Enumerable.Empty<ContourProbeCoupling>())
      {
        if (string.IsNullOrWhiteSpace(row.ProbeKey) || row.NicheParamId <= 0)
        {
          errorMessage = "contour_probes: probeKey и NicheParamId обязательны.";
          return false;
        }
      }

      return true;
    }

    private static bool SaveLinesFile(string path, List<string> lines, string description, out string errorMessage)
    {
      var result = SafeSaveFile(
          path,
          lines,
          p => File.Exists(p) && File.ReadAllLines(p).Any(l => !string.IsNullOrWhiteSpace(l)),
          minLinesCount: 1,
          fileDescription: description);
      errorMessage = result.Success ? null : result.ErrorMessage;
      return result.Success;
    }

    private static List<string> BuildTriadConfigLines(TriadExperimentConfig config)
    {
      var aoe = config.AoeSettings ?? new TriadAoeSettings();
      return new List<string>
      {
        FileHeaders.TriadConfigFormat,
        FileHeaders.TriadConfigAoeParams,
        FileHeaders.TriadConfigEngineParams,
        string.Join("|",
            config.Phase.ToString(),
            (config.ContourId ?? string.Empty).Trim(),
            config.SpontaneousDriftEnabled ? "1" : "0",
            config.CouplingMappingVersion.ToString(CultureInfo.InvariantCulture),
            aoe.BaselineWindowN.ToString(CultureInfo.InvariantCulture),
            aoe.ResponseThreshold.ToString(CultureInfo.InvariantCulture),
            aoe.CorrelationHorizonK.ToString(CultureInfo.InvariantCulture),
            aoe.EvalWindowPulses.ToString(CultureInfo.InvariantCulture),
            config.UseFullNicheEngine ? "1" : "0",
            (config.NicheRoleProfileId ?? "niche_minimal").Trim(),
            config.UseProbeContour ? "1" : "0")
      };
    }

    private static List<string> BuildActionCouplingLines(IEnumerable<CouplingTarget> rows)
    {
      var lines = new List<string> { FileHeaders.ActionCouplingFormat };
      if (rows == null)
        return lines;
      foreach (var row in rows)
      {
        lines.Add(string.Join("|",
            row.ActionId.ToString(CultureInfo.InvariantCulture),
            row.NicheParamId.ToString(CultureInfo.InvariantCulture),
            row.Delta.ToString(CultureInfo.InvariantCulture),
            row.Scale.ToString(CultureInfo.InvariantCulture)));
      }

      return lines;
    }

    private static List<string> BuildNicheCreatureMappingLines(IEnumerable<NicheCreatureMapping> rows)
    {
      var lines = new List<string> { FileHeaders.NicheCreatureMappingFormat };
      if (rows == null)
        return lines;
      foreach (var row in rows)
      {
        lines.Add(string.Join("|",
            row.NicheParamId.ToString(CultureInfo.InvariantCulture),
            row.CreatureParamId.ToString(CultureInfo.InvariantCulture),
            row.Scale.ToString(CultureInfo.InvariantCulture),
            row.LagPulses.ToString(CultureInfo.InvariantCulture)));
      }

      return lines;
    }

    private static List<string> BuildNicheParamsLines(IEnumerable<NicheParameterDef> rows)
    {
      var lines = new List<string> { FileHeaders.NicheParamsFormat };
      if (rows == null)
        return lines;
      foreach (var row in rows)
      {
        lines.Add(string.Join("|",
            row.ParamId.ToString(CultureInfo.InvariantCulture),
            ClampParam(row.InitialValue).ToString(CultureInfo.InvariantCulture),
            row.SpeedPerPulse.ToString(CultureInfo.InvariantCulture)));
      }

      return lines;
    }

    private static List<string> BuildOperatorNicheCouplingLines(IEnumerable<OperatorNicheCouplingTarget> rows)
    {
      var lines = new List<string> { FileHeaders.OperatorNicheCouplingFormat };
      if (rows == null)
        return lines;
      foreach (var row in rows)
      {
        lines.Add(string.Join("|",
            row.InfluenceActionId.ToString(CultureInfo.InvariantCulture),
            row.NicheParamId.ToString(CultureInfo.InvariantCulture),
            row.Delta.ToString(CultureInfo.InvariantCulture),
            row.Scale.ToString(CultureInfo.InvariantCulture)));
      }

      return lines;
    }

    private static List<string> BuildContourProbeLines(IEnumerable<ContourProbeCoupling> rows)
    {
      var lines = new List<string> { FileHeaders.ContourProbesFormat };
      if (rows == null)
        return lines;
      foreach (var row in rows)
      {
        lines.Add(string.Join("|",
            (row.ProbeKey ?? string.Empty).Trim(),
            row.NicheParamId.ToString(CultureInfo.InvariantCulture),
            row.Delta.ToString(CultureInfo.InvariantCulture)));
      }

      return lines;
    }

    private static List<ContourProbeCoupling> ParseContourProbes(string[] lines)
    {
      var list = new List<ContourProbeCoupling>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 3)
          continue;

        string key = parts[0].Trim();
        if (key.Length == 0)
          continue;
        if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int nicheParamId))
          continue;
        if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
          continue;

        list.Add(new ContourProbeCoupling
        {
          ProbeKey = key,
          NicheParamId = nicheParamId,
          Delta = delta
        });
      }

      return list;
    }

    private static float ClampParam(float v)
    {
      if (v < 0f) return 0f;
      if (v > 100f) return 100f;
      return v;
    }
  }
}
