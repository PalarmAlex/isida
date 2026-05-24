using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Niche
{
  /// <summary>
  /// Загрузка правил реактивных рефлексов Niche из каталога Data/Niche.
  /// </summary>
  public static class NicheReflexLoader
  {
    /// <summary>
    /// Загружает правила из <paramref name="nicheDataFolder"/>/niche_reflexes.dat.
    /// </summary>
    /// <param name="nicheDataFolder">Каталог Data/Niche.</param>
    /// <returns>Список правил.</returns>
    public static List<NicheReflexRule> LoadFromFolder(string nicheDataFolder)
    {
      var list = new List<NicheReflexRule>();
      if (string.IsNullOrWhiteSpace(nicheDataFolder))
        return list;

      string path = Path.Combine(nicheDataFolder, "niche_reflexes.dat");
      if (!File.Exists(path))
        return list;

      list.AddRange(ParseLines(File.ReadAllLines(path)));
      return list;
    }

    /// <summary>
    /// Создаёт шаблон niche_reflexes.dat при отсутствии.
    /// </summary>
    /// <param name="nicheDataFolder">Каталог Data/Niche.</param>
    public static void EnsureTemplateFile(string nicheDataFolder)
    {
      if (string.IsNullOrWhiteSpace(nicheDataFolder))
        return;

      Directory.CreateDirectory(nicheDataFolder);
      string path = Path.Combine(nicheDataFolder, "niche_reflexes.dat");
      if (!File.Exists(path))
        File.WriteAllText(path, FileHeaders.NicheReflexesTemplate);
    }

    /// <summary>
    /// Разбирает строки файла правил.
    /// </summary>
    /// <param name="lines">Строки файла.</param>
    /// <returns>Правила.</returns>
    public static List<NicheReflexRule> ParseLines(string[] lines)
    {
      var list = new List<NicheReflexRule>();
      foreach (var raw in lines)
      {
        string line = raw.Trim();
        if (line.Length == 0 || line.StartsWith("#"))
          continue;

        string[] parts = line.Split('|');
        if (parts.Length < 5)
          continue;

        if (!Enum.TryParse(parts[0].Trim(), true, out NicheReflexTriggerKind kind))
          continue;

        var rule = new NicheReflexRule { TriggerKind = kind };

        if (kind == NicheReflexTriggerKind.CreatureAction)
        {
          if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float actionId))
            continue;
          rule.TriggerValue = actionId;
          if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId))
            continue;
          rule.TargetNicheParamId = targetId;
          if (!float.TryParse(parts[3].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
            continue;
          rule.Delta = delta;
          if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            scale = 1f;
          rule.Scale = scale;
        }
        else
        {
          if (!int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sourceId))
            continue;
          rule.SourceParamId = sourceId;
          if (!float.TryParse(parts[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float threshold))
            continue;
          rule.TriggerValue = threshold;
          if (!int.TryParse(parts[3].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int targetId))
            continue;
          rule.TargetNicheParamId = targetId;
          if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float delta))
            continue;
          rule.Delta = delta;
          if (parts.Length >= 6 &&
              float.TryParse(parts[5].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
            rule.Scale = scale;
        }

        list.Add(rule);
      }

      return list;
    }
  }
}
