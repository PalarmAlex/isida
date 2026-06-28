using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ISIDA.Scenarios
{
  /// <summary>Одно воздействие метрики среды в шаге сценария: давление (+) или отпускание (−).</summary>
  public sealed class ScenarioEnvironmentProbeEntry
  {
    /// <summary>ID записи из InfluenceActions.dat с непустым ProbeKey.</summary>
    public int ActionId { get; set; }

    /// <summary><c>true</c> — давление метрики (+), <c>false</c> — отпускание (−).</summary>
    public bool IsPressure { get; set; }

    /// <summary>Копия записи.</summary>
    public ScenarioEnvironmentProbeEntry Clone() =>
        new ScenarioEnvironmentProbeEntry { ActionId = ActionId, IsPressure = IsPressure };
  }

  /// <summary>Сериализация списка воздействий среды в строку вида «+5,-3,+7».</summary>
  public static class ScenarioEnvironmentProbeFormat
  {
    /// <summary>Сохраняет записи в строку «+id,-id,…».</summary>
    public static string Serialize(IReadOnlyList<ScenarioEnvironmentProbeEntry> entries)
    {
      if (entries == null || entries.Count == 0)
        return "";
      return string.Join(",",
          entries.Select(e =>
              (e.IsPressure ? "+" : "-") + e.ActionId.ToString(CultureInfo.InvariantCulture)));
    }

    /// <summary>Разбирает строку «+id,-id,…» в список записей.</summary>
    public static List<ScenarioEnvironmentProbeEntry> Parse(string s)
    {
      var list = new List<ScenarioEnvironmentProbeEntry>();
      if (string.IsNullOrWhiteSpace(s))
        return list;
      foreach (var part in s.Split(','))
      {
        var t = part.Trim();
        if (t.Length < 2)
          continue;
        bool isPressure;
        if (t[0] == '+')
          isPressure = true;
        else if (t[0] == '-')
          isPressure = false;
        else
          continue;
        if (!int.TryParse(t.Substring(1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
            || id <= 0)
          continue;
        list.Add(new ScenarioEnvironmentProbeEntry { ActionId = id, IsPressure = isPressure });
      }
      return list;
    }
  }
}
