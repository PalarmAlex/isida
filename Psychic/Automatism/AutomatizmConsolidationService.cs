using ISIDA.Common;
using System;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Консолидация и затухание полезности автоматизмов на стадиях 2–3 (крепость через <see cref="Automatizm.Count"/>).
  /// На стадиях 4+ рост полезности — только через оценку оператора в <see cref="AutomatismResultTracker"/>.
  /// </summary>
  public static class AutomatizmConsolidationService
  {
    /// <summary>Базовый «тик» проверки затухания (в пульсах), как у условных рефлексов.</summary>
    public const int DecayPulseInterval = 100;

    /// <summary>
    /// Базовое число тиков между шагами −1 для эхо при Count=0 (2×100 = 200 пульсов до первого шага).
    /// </summary>
    private const int EchoDecayBasePeriodTicks = 2;

    /// <summary>
    /// Базовое число тиков между шагами −1 для сдвига при Count=0 (4×100 = 400 пульсов до первого шага).
    /// </summary>
    private const int ShiftDecayBasePeriodTicks = 4;

    private const double ConsolidationTau = 3.0;
    private const float AssociationStrengthToCountScale = 10f;

    /// <summary>Роль при создании автоматизма для стартовой полезности.</summary>
    public enum AutomatizmCreationRole
    {
      /// <summary>Обычное создание: на 2–3 стадиях полезность 1.</summary>
      Default = 0,
      /// <summary>Сдвиг зеркала: полезность 1.</summary>
      Shift = 1,
      /// <summary>Эхо: полезность 0.</summary>
      Echo = 2
    }

    /// <summary>Начальная полезность по роли и стадии.</summary>
    public static int GetInitialUsefulness(AutomatizmCreationRole role, int evolutionStage)
    {
      if (evolutionStage < 2)
        return 0;
      if (role == AutomatizmCreationRole.Echo)
        return 0;
      if (evolutionStage <= 3)
        return 1;
      return role == AutomatizmCreationRole.Shift ? 1 : 0;
    }

    /// <summary>Максимум полезности консолидации на стадии (2→3, 3→2).</summary>
    public static int GetUsefulnessCap(int evolutionStage)
    {
      if (evolutionStage == 2) return 3;
      if (evolutionStage == 3) return 2;
      return 10;
    }

    /// <summary>Count при миграции у-рефлекса: max(1, round(AssociationStrength * scale)).</summary>
    public static int CountFromAssociationStrength(float associationStrength)
    {
      if (associationStrength <= 0f)
        return 1;
      return Math.Max(1, (int)Math.Round(associationStrength * AssociationStrengthToCountScale));
    }

    /// <summary>Полезность из Count для сдвига (пол 1) или эхо (пол 0).</summary>
    public static int MapCountToUsefulness(int count, bool isEcho, int evolutionStage)
    {
      int cap = GetUsefulnessCap(evolutionStage);
      int floor = isEcho ? 0 : 1;
      if (count <= 0)
        return floor;
      double t = 1.0 - Math.Exp(-count / ConsolidationTau);
      int mapped = floor + (int)Math.Round(t * (cap - floor));
      return Math.Min(cap, Math.Max(floor, mapped));
    }

    /// <summary>Эхо: Belief=0; сдвиг: Belief=2.</summary>
    public static bool IsEchoAutomatizm(Automatizm automatizm)
    {
      if (automatizm == null)
        return false;
      return automatizm.Belief != 2;
    }

    /// <summary>
    /// Оценка оператора на стадиях 2–3: при assessment≥0 — рост Count и пересчёт полезности;
    /// при assessment&lt;0 — штраф; при 0 — без роста.
    /// </summary>
    public static void ApplyOperatorAssessmentStagesTwoThree(
        AutomatizmSystem automatizmSystem,
        int automatizmId,
        int assessment)
    {
      if (automatizmSystem == null || automatizmId <= 0)
        return;

      int stage = AppGlobalState.EvolutionStage;
      if (stage != 2 && stage != 3)
        return;

      AppGlobalState.MarkAssessmentAppliedThisPulse();

      var automatizm = automatizmSystem.GetAutomatizmById(automatizmId);
      if (automatizm == null)
        return;

      if (assessment > 0)
      {
        automatizm.Count++;
        bool isEcho = IsEchoAutomatizm(automatizm);
        automatizm.Usefulness = MapCountToUsefulness(automatizm.Count, isEcho, stage);
      }
      else if (assessment < 0)
      {
        automatizm.Usefulness += assessment;
        automatizm.Usefulness = AddUtils.Clamp(automatizm.Usefulness, -10, 10);
      }

      automatizmSystem.AfterAutomatizmUsefulnessUpdated(automatizmId);
    }

    /// <summary>
    /// Число базовых тиков (<see cref="DecayPulseInterval"/> пульсов) между дискретными шагами −1.
    /// Растёт с <see cref="Automatizm.Count"/>: periodTicks = baseTicks × (1 + Count).
    /// </summary>
    public static int GetDecayPeriodTicks(int count, bool isEcho)
    {
      int c = Math.Max(0, count);
      int baseTicks = isEcho ? EchoDecayBasePeriodTicks : ShiftDecayBasePeriodTicks;
      return baseTicks * (1 + c);
    }

    /// <summary>Интервал в пульсах между шагами затухания для данного автоматизма.</summary>
    public static int GetDecayPulsePeriod(int count, bool isEcho)
    {
      return DecayPulseInterval * GetDecayPeriodTicks(count, isEcho);
    }

    /// <summary>
    /// Периодическое затухание на стадиях 2–3. Не вызывается на пульсе с оценкой оператора.
    /// Шаг −1 не чаще, чем раз в <see cref="GetDecayPulsePeriod"/> для каждого автоматизма.
    /// </summary>
    public static void ApplyDecayOnPulse(
        AutomatizmSystem automatizmSystem,
        MirrorAutomatizmService mirrorService,
        int pulseCount)
    {
      if (automatizmSystem == null)
        return;

      int stage = AppGlobalState.EvolutionStage;
      if (stage != 2 && stage != 3)
        return;

      if (pulseCount <= 0 || pulseCount % DecayPulseInterval != 0)
        return;

      if (AppGlobalState.AssessmentAppliedThisPulse)
        return;

      int tick = pulseCount / DecayPulseInterval;
      bool dialogActive = mirrorService?.IsDialogMirrorActive ?? false;

      foreach (var automatizm in automatizmSystem.GetAllAutomatizms())
      {
        if (automatizm == null)
          continue;

        bool isEcho = IsEchoAutomatizm(automatizm);
        if (dialogActive && isEcho)
          continue;

        int periodTicks = GetDecayPeriodTicks(automatizm.Count, isEcho);
        if (periodTicks <= 0 || tick % periodTicks != 0)
          continue;

        automatizm.Usefulness -= 1;
        automatizmSystem.AfterAutomatizmUsefulnessUpdated(automatizm.ID);
      }
    }
  }
}
