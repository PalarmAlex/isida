using System;
using ISIDA.Common;
using ISIDA.Gomeostas;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Оценка гомео-настроения для инфо-картины.
  /// Mood в информационной среде — интегральная оценка −10…+10 на пульсе; PsyMood — с инерцией к Mood (см. <see cref="AdjustPsyMoodTowardIntegrated"/>).
  /// </summary>
  public static class HomeostasisMoodPerception
  {
    /// <summary>
    /// Интегральное настроение по текущему снимку гомеостаза: боль/радость, интегральное состояние, взвешенные Bad/Well.
    /// </summary>
    /// <param name="agentState">Результат <c>CalculateAgentState</c> на этом пульсе.</param>
    /// <param name="painValue">Величина боли симбионта (как в состоянии гомеостаза).</param>
    /// <param name="joyValue">Величина радости симбионта.</param>
    /// <param name="overallState">Интегральное состояние (уже выставлено в <c>CalculateAgentState</c>).</param>
    /// <returns>Значение в диапазоне −10…+10.</returns>
    public static int EstimateMood(
        GomeostasSystem.AgentHomeostasisState agentState,
        int painValue,
        int joyValue,
        AppGlobalState.HomeostasisState overallState)
    {
      float mood = 0f;

      if (agentState != null)
      {
        mood -= Math.Min(5f, agentState.BadSum / 18f);
        mood += Math.Min(4f, agentState.WellSum / 22f);
      }

      mood -= AddUtils.Clamp(painValue / 18f, 0f, 5f);
      mood += AddUtils.Clamp(joyValue / 20f, 0f, 4f);

      switch (overallState)
      {
        case AppGlobalState.HomeostasisState.Bad:
          mood -= 3f;
          break;
        case AppGlobalState.HomeostasisState.Well:
          mood += 2f;
          break;
      }

      int rounded = (int)Math.Round(mood, MidpointRounding.AwayFromZero);
      return AddUtils.Clamp(rounded, -10, 10);
    }

    /// <summary>
    /// Субъективное настроение: за пульс сдвигается к интегральному не более чем на 2 пункта (инерция «переживания»).
    /// </summary>
    /// <param name="currentPsyMood">Текущее PsyMood.</param>
    /// <param name="integratedMood">Целевое Mood с пульса.</param>
    /// <returns>Новое значение в диапазоне −10…+10.</returns>
    public static int AdjustPsyMoodTowardIntegrated(int currentPsyMood, int integratedMood)
    {
      int diff = integratedMood - currentPsyMood;
      if (diff == 0)
        return AddUtils.Clamp(currentPsyMood, -10, 10);

      int step = Math.Sign(diff) * Math.Min(Math.Abs(diff), 2);
      return AddUtils.Clamp(currentPsyMood + step, -10, 10);
    }
  }
}
