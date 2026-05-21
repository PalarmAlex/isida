using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ISIDA.Scenarios
{
  /// <summary>Проверка структуры сценария и условий запуска.</summary>
  public static class OperatorScenarioValidator
  {
    /// <summary>Проверяет согласованность шагов, пульсов и ссылок на воздействия.</summary>
    /// <param name="doc">Сценарий.</param>
    /// <param name="influenceActions">Справочник допустимых ID воздействий.</param>
    /// <returns>Сообщение об ошибке или <c>null</c>, если проверка пройдена.</returns>
    public static string ValidateDocument(ScenarioDocument doc, InfluenceActionSystem influenceActions)
    {
      if (doc == null)
        return "Пустой сценарий.";
      if (doc.Header == null)
        return "Нет шапки сценария.";
      if (string.IsNullOrWhiteSpace(doc.Header.Title))
        return "Укажите название сценария.";
      if (doc.Lines == null || doc.Lines.Count == 0)
        return "Добавьте хотя бы одну строку сценария.";
      int psi = doc.Header.PulseStepIncrement;
      if (psi != (int)ScenarioPulseStepIncrement.Sequential
          && psi != (int)ScenarioPulseStepIncrement.ActionHoldPlusOne
          && psi != (int)ScenarioPulseStepIncrement.StateHoldPlusOne
          && psi != (int)ScenarioPulseStepIncrement.ActionHold)
        return "Укажите корректный режим приращения пульса на шаге (1, 2, 3 или 4).";

      int stageForFeatureRules = EffectiveEvolutionStageForValidation(doc);

      var validIds = new HashSet<int>();
      try
      {
        foreach (var a in influenceActions.GetAllInfluenceActions())
          validIds.Add(a.Id);
      }
      catch
      {
        /* ignore */
      }

      var steps = new HashSet<int>();
      var pulses = new HashSet<int>();
      foreach (var row in doc.Lines)
      {
        if (row.StepIndex < 1)
          return $"Номер шага должен быть ≥ 1 (шаг {row.StepIndex}).";
        if (!steps.Add(row.StepIndex))
          return $"Дублируется номер шага {row.StepIndex}.";

        if (row.PulseWithinScenario < 1)
          return $"Номер пульса должен быть ≥ 1 (шаг {row.StepIndex}, пульс {row.PulseWithinScenario}).";
        if (!pulses.Add(row.PulseWithinScenario))
          return $"Дублируется номер пульса {row.PulseWithinScenario} (шаг {row.StepIndex}).";

        if (!AgentVisualColor.IsValidCode(row.VisualColorId))
          return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): код цвета фона должен быть от "
              + AgentVisualColor.MinCode.ToString(CultureInfo.InvariantCulture) + " до "
              + AgentVisualColor.MaxCode.ToString(CultureInfo.InvariantCulture) + ".";

        if (row.Kind == ScenarioLineKind.WaitClick)
        {
          if (row.ActionIds != null && row.ActionIds.Count > 0)
            return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): клик по плашке не сочетается с воздействиями.";
          if (!string.IsNullOrWhiteSpace(row.Phrase))
            return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): клик по плашке не сочетается с фразой.";
          if (row.ResetWaitingPeriod)
            return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): клик по плашке не сочетается со сбросом ожидания.";
          if (row.VisualColorId != AgentVisualColor.White)
            return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): клик по плашке не сочетается с цветом фона.";
          continue;
        }

        bool hasPhrase = !string.IsNullOrWhiteSpace(row.Phrase);
        bool hasActions = row.ActionIds != null && row.ActionIds.Count > 0;
        bool hasVisualColor = row.VisualColorId != AgentVisualColor.White;
        if (row.ResetWaitingPeriod && (hasPhrase || hasActions || hasVisualColor))
          return $"Шаг {row.StepIndex} (пульс {row.PulseWithinScenario}): сброс ожидания не сочетается с фразой, воздействиями или цветом фона.";
        // Пустая строка пульта (без фразы, без воздействий, без сброса) допустима — маркер пульса только для ожидаемого лога.

        if (row.Phrase != null && row.Phrase.IndexOf('|') >= 0)
          return "Символ «|» в тексте фразы недопустим.";

        if (row.ActionIds != null)
        {
          foreach (var id in row.ActionIds)
          {
            if (!validIds.Contains(id))
              return $"Неизвестное воздействие с пульта ID={id} (шаг {row.StepIndex}, пульс {row.PulseWithinScenario}).";
          }
        }

        if (row.ResetWaitingPeriod && stageForFeatureRules < 3)
          return "Сброс времени ожидания доступен со стадии развития 3.";
      }

      var ordered = doc.Lines.OrderBy(r => r.StepIndex).ToList();
      for (int i = 1; i < ordered.Count; i++)
      {
        int prevPulse = ordered[i - 1].PulseWithinScenario;
        int curPulse = ordered[i].PulseWithinScenario;
        if (curPulse <= prevPulse)
        {
          return "Номер пульса на шаге " + ordered[i].StepIndex.ToString(CultureInfo.InvariantCulture)
              + " (" + curPulse.ToString(CultureInfo.InvariantCulture) + ") должен быть больше, чем на предыдущем шаге "
              + ordered[i - 1].StepIndex.ToString(CultureInfo.InvariantCulture)
              + " (" + prevPulse.ToString(CultureInfo.InvariantCulture)
              + "): нельзя задавать пульс, не превышающий уже использованный у предшествующих шагов.";
        }
      }

      return null;
    }

    /// <summary>
    /// Стадия, относительно которой проверяем доступность функций шагов: явная стадия перед запуском (0–5)
    /// из шапки сценария или текущая стадия симбионта при «не менять стадию» (−1).
    /// </summary>
    private static int EffectiveEvolutionStageForValidation(ScenarioDocument doc)
    {
      int t = doc.Header.PreRunTargetStage;
      if (t >= 0 && t <= 5)
        return t;
      return AppGlobalState.EvolutionStage;
    }

    /// <summary>Проверка перед запуском: документ, пульсация, живой симбионт.</summary>
    /// <param name="doc">Сценарий.</param>
    /// <param name="influenceActions">Справочник воздействий.</param>
    /// <param name="pulsationRunning">Состояние пульсации (из хоста, не синглтон таймера).</param>
    /// <param name="agentIsDead">Симбионт мёртв (из хоста, не синглтон гомеостаза).</param>
    /// <returns>Сообщение об ошибке или <c>null</c>, если запуск допустим.</returns>
    public static string ValidateForRun(
        ScenarioDocument doc,
        InfluenceActionSystem influenceActions,
        bool pulsationRunning,
        bool agentIsDead)
    {
      var v = ValidateDocument(doc, influenceActions);
      if (v != null)
        return v;
      if (!pulsationRunning)
        return "Включите пульсацию перед запуском сценария.";
      if (agentIsDead)
        return "Симбионт мёртв — запуск сценария невозможен.";
      if (doc.Lines == null || doc.Lines.Count == 0)
        return "В сценарии нет ни одной строки.";
      return null;
    }
  }
}
