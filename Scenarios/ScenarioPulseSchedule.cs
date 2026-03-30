using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ISIDA.Scenarios
{
  /// <summary>
  /// Расчёт номеров пульсов по шагам: следующий пульс = предыдущий + задержка + 1.
  /// Задержка задаётся режимом <see cref="ScenarioPulseStepIncrement"/> и глобальными настройками (время удержания действий / состояний).
  /// </summary>
  public static class ScenarioPulseSchedule
  {
    /// <summary>Число пульсов между шагами (без учёта «+1» к следующему номеру шага).</summary>
    public static int ResolveDelayBetweenSteps(
        ScenarioPulseStepIncrement mode,
        int reflexActionDisplayDuration,
        int stateHoldDynamicTime)
    {
      switch (mode)
      {
        case ScenarioPulseStepIncrement.Sequential:
          return 0;
        case ScenarioPulseStepIncrement.ActionHoldPlusOne:
          return Math.Max(0, reflexActionDisplayDuration);
        case ScenarioPulseStepIncrement.StateHoldPlusOne:
          return Math.Max(0, stateHoldDynamicTime);
        default:
          return Math.Max(0, reflexActionDisplayDuration);
      }
    }

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
      if (lines is BindingList<ScenarioLineRow> bl)
      {
        bl.RaiseListChangedEvents = false;
        try
        {
          bl.Clear();
          foreach (var row in sorted)
            bl.Add(row);
        }
        finally
        {
          bl.RaiseListChangedEvents = true;
          bl.ResetBindings();
        }
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

    /// <summary>
    /// Нумерует шаги 1…n в порядке строк списка, не меняя <see cref="ScenarioLineRow.PulseWithinScenario"/>.
    /// Используется при загрузке/редактировании, чтобы не затирать заданные номера пульсов.
    /// </summary>
    public static void EnsureSequentialStepIndices(IList<ScenarioLineRow> lines)
    {
      if (lines == null || lines.Count == 0)
        return;
      for (int i = 0; i < lines.Count; i++)
        lines[i].StepIndex = i + 1;
    }
  }
}
