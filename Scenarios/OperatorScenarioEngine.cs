using ISIDA.Actions;
using System;
using System.Collections.Generic;

namespace ISIDA.Scenarios
{
  /// <summary>
  /// Точка входа движка для сценариев оператора: расписание шагов по пульсам и доступ к настройкам исследования.
  /// Задержка между шагами берётся из <see cref="AdaptiveActionsSystem.ReflexActionDisplayDuration"/> (время отображения рефлекторного действия, пульсов).
  /// </summary>
  public sealed class OperatorScenarioEngine
  {
    private readonly AdaptiveActionsSystem _adaptiveActions;

    /// <param name="adaptiveActions">Подсистема адаптивных действий (длительность отображения рефлекса и др.).</param>
    public OperatorScenarioEngine(AdaptiveActionsSystem adaptiveActions)
    {
      _adaptiveActions = adaptiveActions ?? throw new ArgumentNullException(nameof(adaptiveActions));
    }

    /// <summary>Число пульсов «зазора» между шагами сценария (после стимула до следующего шага).</summary>
    public int PulseGapBetweenSteps => Math.Max(0, _adaptiveActions.ReflexActionDisplayDuration);

    /// <summary>Перенумеровывает шаги и пересчитывает <see cref="ScenarioLineRow.PulseWithinScenario"/> по текущему зазору.</summary>
    /// <param name="lines">Строки сценария (изменяются на месте).</param>
    public void NormalizeSchedule(IList<ScenarioLineRow> lines) =>
      ScenarioPulseSchedule.Normalize(lines, PulseGapBetweenSteps);
  }
}
