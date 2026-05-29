using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ISIDA.Niche
{
  /// <summary>
  /// Миграция legacy niche_params / niche_reflexes → VitalParameters / GeneticReflexes.
  /// </summary>
  public static class NicheSymbiontMigration
  {
    /// <summary>
    /// Выполняет миграции при инициализации симбионта Niche.
    /// </summary>
    public static void MigrateLegacyFiles(string nicheDataFolder)
    {
      if (string.IsNullOrWhiteSpace(nicheDataFolder))
        return;

      string reflexesFolder = NicheSymbiontBootstrap.GetReflexesFolder(nicheDataFolder);
      string geneticPath = Path.Combine(reflexesFolder, "GeneticReflexes.dat");
      string legacyReflexPath = Path.Combine(nicheDataFolder, "niche_reflexes.dat");
      string legacyReflexInReflexes = Path.Combine(reflexesFolder, "niche_reflexes.dat");

      if (!File.Exists(geneticPath))
      {
        if (File.Exists(legacyReflexInReflexes))
          MigrateNicheReflexesFile(legacyReflexInReflexes, geneticPath);
        else if (File.Exists(legacyReflexPath))
          MigrateNicheReflexesFile(legacyReflexPath, geneticPath);
      }
    }

    private static void MigrateNicheReflexesFile(string sourcePath, string geneticPath)
    {
      var rules = NicheReflexLoader.LoadFromFolder(Path.GetDirectoryName(sourcePath));
      if (rules.Count == 0)
        return;

      var sb = new StringBuilder();
      sb.AppendLine("# Миграция из niche_reflexes.dat → GeneticReflexes.dat");
      sb.AppendLine("# Level3 для CreatureAction = ID действия Creature; Param* → Level3 пуст, срабатывание через Influence");

      int nextId = 1;
      foreach (var rule in rules)
      {
        if (rule.TriggerKind == NicheReflexTriggerKind.CreatureAction)
        {
          int actionId = (int)Math.Round(rule.TriggerValue);
          sb.AppendLine(string.Format(
              CultureInfo.InvariantCulture,
              "{0}|0||{1}|1|0",
              nextId++,
              actionId));
          continue;
        }

        if (rule.TriggerKind == NicheReflexTriggerKind.ParamBelow ||
            rule.TriggerKind == NicheReflexTriggerKind.ParamAbove)
        {
          sb.AppendLine(string.Format(
              CultureInfo.InvariantCulture,
              "# legacy {0} param {1} threshold {2} → target {3} delta {4}",
              rule.TriggerKind,
              rule.SourceParamId,
              rule.TriggerValue,
              rule.TargetNicheParamId,
              rule.Delta * rule.Scale));
        }
      }

      string dir = Path.GetDirectoryName(geneticPath);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);
      File.WriteAllText(geneticPath, sb.ToString(), Encoding.UTF8);
      Logger.Info($"NicheSymbiontMigration: создан {geneticPath} из {sourcePath}");
    }
  }
}
