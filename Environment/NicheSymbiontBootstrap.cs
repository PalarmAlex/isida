using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ISIDA.Niche
{
  /// <summary>
  /// Каталоги и начальные данные Niche как универсального симбионта (Data/Niche/Gomeostas, Reflexes).
  /// </summary>
  public static class NicheSymbiontBootstrap
  {
    /// <summary>Data/Niche/Gomeostas.</summary>
    public static string GetGomeostasFolder(string nicheDataFolder)
    {
      return Path.Combine(nicheDataFolder ?? string.Empty, "Gomeostas");
    }

    /// <summary>Data/Niche/Reflexes.</summary>
    public static string GetReflexesFolder(string nicheDataFolder)
    {
      return Path.Combine(nicheDataFolder ?? string.Empty, "Reflexes");
    }

    /// <summary>Data/Niche/Actions.</summary>
    public static string GetActionsFolder(string nicheDataFolder)
    {
      return Path.Combine(nicheDataFolder ?? string.Empty, "Actions");
    }

    /// <summary>
    /// Создаёт дерево Data/Niche и при отсутствии VitalParameters — из niche_params или минимального шаблона.
    /// </summary>
    public static void EnsureSymbiontLayout(
        string nicheDataFolder,
        IEnumerable<NicheParameterDef> fallbackNicheParams = null)
    {
      if (string.IsNullOrWhiteSpace(nicheDataFolder))
        return;

      Directory.CreateDirectory(nicheDataFolder);
      string gomeostasFolder = GetGomeostasFolder(nicheDataFolder);
      string reflexesFolder = GetReflexesFolder(nicheDataFolder);
      string actionsFolder = GetActionsFolder(nicheDataFolder);
      Directory.CreateDirectory(gomeostasFolder);
      Directory.CreateDirectory(reflexesFolder);
      Directory.CreateDirectory(actionsFolder);

      NicheReflexLoader.EnsureTemplateFile(reflexesFolder);
      WriteNicheActionsTemplatesIfMissing(actionsFolder);

      string vitalPath = Path.Combine(gomeostasFolder, "VitalParameters.dat");
      if (File.Exists(vitalPath))
        return;

      var defs = fallbackNicheParams != null
          ? new List<NicheParameterDef>(fallbackNicheParams)
          : new List<NicheParameterDef>();

      if (defs.Count == 0)
        defs.AddRange(DefaultNicheParameterDefs());

      WriteVitalParametersFromDefs(vitalPath, defs);
      WriteBehaviorStylesIfMissing(gomeostasFolder);
    }

    /// <summary>Параметры по умолчанию, если нет ни niche_params, ни VitalParameters.</summary>
    public static IEnumerable<NicheParameterDef> DefaultNicheParameterDefs()
    {
      yield return new NicheParameterDef { ParamId = 1, InitialValue = 50f, SpeedPerPulse = 0f };
      yield return new NicheParameterDef { ParamId = 2, InitialValue = 30f, SpeedPerPulse = 0f };
    }

    private static void WriteVitalParametersFromDefs(string vitalPath, IList<NicheParameterDef> defs)
    {
      var sb = new StringBuilder();
      sb.AppendLine("# Формат: ID|Название|Описание|Значение|Вес|Норма|Скорость|Активации стилей|Критический|Мин.значение|Макс.значение");
      sb.AppendLine("# Niche: сгенерировано из niche_params / шаблона симбионта");

      foreach (var d in defs)
      {
        if (d == null || d.ParamId <= 0)
          continue;

        float speed = d.SpeedPerPulse * 100f;
        string name = $"NicheParam{d.ParamId}";
        float value = Clamp(d.InitialValue);
        sb.AppendLine(string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1}|Параметр среды Niche|{2}|50|50|{3}|0:|True|0|100",
            d.ParamId,
            name,
            value,
            speed));
      }

      string dir = Path.GetDirectoryName(vitalPath);
      if (!string.IsNullOrEmpty(dir))
        Directory.CreateDirectory(dir);

      File.WriteAllText(vitalPath, sb.ToString(), Encoding.UTF8);
    }

    private static void WriteBehaviorStylesIfMissing(string gomeostasFolder)
    {
      string stylesPath = Path.Combine(gomeostasFolder, "BehaviorStyles.dat");
      if (File.Exists(stylesPath))
        return;

      const string content =
@"# Формат: ID|Имя|Описание|Антагонисты
1|Поиск|Стратегия поиска|2,3
2|Ступор|Непонимание|1,3
3|Расслабление|Спокойное состояние|1,2
";
      File.WriteAllText(stylesPath, content, Encoding.UTF8);
    }

    private static void WriteNicheActionsTemplatesIfMissing(string actionsFolder)
    {
      string adaptivePath = Path.Combine(actionsFolder, "AdaptiveActions.dat");
      if (!File.Exists(adaptivePath))
      {
        const string adaptive =
@"# Niche: ID|Имя|Описание|Интенсивность|Антагонисты|Target параметры|InfluenceActionId
1|Реакция среды A|Ответ Niche на действие Creature|5||1|1
2|Реакция среды B|Ответ Niche на действие Creature|5||2|2
";
        File.WriteAllText(adaptivePath, adaptive, Encoding.UTF8);
      }

      string influencePath = Path.Combine(actionsFolder, "InfluenceActions.dat");
      if (!File.Exists(influencePath))
      {
        const string influence =
@"# Niche: ID|Имя|Описание|Воздействие|Антагонисты|EnvironmentMetricProbeKey
1|Сдвиг параметра 1|Эффект на param 1|1:2|0|
2|Сдвиг параметра 2|Эффект на param 2|2:-2|0|
";
        File.WriteAllText(influencePath, influence, Encoding.UTF8);
      }
    }

    private static float Clamp(float v)
    {
      if (v < 0f) return 0f;
      if (v > 100f) return 100f;
      return v;
    }
  }
}
