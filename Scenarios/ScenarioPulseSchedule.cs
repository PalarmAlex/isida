using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ISIDA.Scenarios
{
  /// <summary>
  /// Расчёт номеров пульсов по шагам: следующий пульс = предыдущий + задержка + 1,
  /// где задержка — ReflexActionDisplayDuration (пульсов), время отображения рефлекторного действия.
  /// </summary>
  public static class ScenarioPulseSchedule
  {
    /// <summary>Нормализует порядок строк, перенумеровывает шаги 1..n и задаёт PulseWithinScenario.</summary>
    public static void Normalize(IList<ScenarioLineRow> lines, int delayPulsesBetweenSteps)
    {
      if (lines == null || lines.Count == 0)
        return;
      var sorted = ComputeSorted(lines);
      ApplyStepAndPulse(sorted, delayPulsesBetweenSteps);
      if (lines is List<ScenarioLineRow> list)
      {
        list.Clear();
        foreach (var row in sorted)
          list.Add(row);
        return;
      }
      if (lines is ObservableCollection<ScenarioLineRow> oc)
      {
        oc.Clear();
        foreach (var row in sorted)
          oc.Add(row);
        return;
      }
      for (int i = lines.Count - 1; i >= 0; i--)
        lines.RemoveAt(i);
      foreach (var row in sorted)
        lines.Add(row);
    }

    private static List<ScenarioLineRow> ComputeSorted(IEnumerable<ScenarioLineRow> lines)
    {
      var arr = lines.ToList();
      if (arr.Count == 0)
        return arr;
      if (arr.Any(l => l.StepIndex < 1))
        return arr.OrderBy(l => l.PulseWithinScenario).ToList();
      return arr.OrderBy(l => l.StepIndex).ToList();
    }

    private static void ApplyStepAndPulse(List<ScenarioLineRow> sorted, int delayPulsesBetweenSteps)
    {
      var delay = Math.Max(0, delayPulsesBetweenSteps);
      int pulse = 1;
      for (int i = 0; i < sorted.Count; i++)
      {
        sorted[i].StepIndex = i + 1;
        sorted[i].PulseWithinScenario = pulse;
        pulse = pulse + delay + 1;
      }
    }
  }
}
