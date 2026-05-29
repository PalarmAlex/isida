using ISIDA.Common;
using System;

namespace ISIDA.Niche
{
  /// <summary>
  /// Допустимый диапазон фазы триады по стадии эволюции Creature (§4.1).
  /// Стадия задаёт ceiling (и floor на 4+); фаза в конфиге не может выходить за диапазон.
  /// </summary>
  public static class TriadPhaseStagePolicy
  {
    /// <summary>
    /// Максимально допустимая фаза для стадии (ceiling).
    /// </summary>
    /// <param name="evolutionStage">Стадия эволюции 0–5.</param>
    /// <returns>Фаза A, B или C.</returns>
    public static TriadPhase GetMaxPhaseForStage(int evolutionStage)
    {
      if (evolutionStage <= 1)
        return TriadPhase.A;
      if (evolutionStage <= 3)
        return TriadPhase.B;
      return TriadPhase.C;
    }

    /// <summary>
    /// Минимально допустимая фаза для стадии (floor на стадии 4+ — только C).
    /// </summary>
    /// <param name="evolutionStage">Стадия эволюции 0–5.</param>
    /// <returns>Фаза A или C.</returns>
    public static TriadPhase GetMinPhaseForStage(int evolutionStage)
    {
      if (evolutionStage >= 4)
        return TriadPhase.C;
      return TriadPhase.A;
    }

    /// <summary>
    /// Приводит фазу к допустимому диапазону для стадии.
    /// </summary>
    /// <param name="requested">Запрошенная фаза.</param>
    /// <param name="evolutionStage">Стадия эволюции 0–5.</param>
    /// <returns>Фаза в допустимом диапазоне [min, max].</returns>
    public static TriadPhase ClampPhase(TriadPhase requested, int evolutionStage)
    {
      TriadPhase min = GetMinPhaseForStage(evolutionStage);
      TriadPhase max = GetMaxPhaseForStage(evolutionStage);
      if (requested < min)
        return min;
      if (requested > max)
        return max;
      return requested;
    }

    /// <summary>
    /// Приводит фазу к допустимому диапазону для текущей стадии симбионта.
    /// </summary>
    /// <param name="requested">Запрошенная фаза.</param>
    /// <returns>Фаза в допустимом диапазоне.</returns>
    public static TriadPhase ClampPhaseForCurrentStage(TriadPhase requested)
    {
      return ClampPhase(requested, AppGlobalState.EvolutionStage);
    }

    /// <summary>
    /// Проверяет, допустима ли фаза на указанной стадии (без clamp).
    /// </summary>
    /// <param name="phase">Фаза триады.</param>
    /// <param name="evolutionStage">Стадия эволюции 0–5.</param>
    /// <param name="errorMessage">Причина отказа.</param>
    /// <returns>True, если фаза в допустимом диапазоне.</returns>
    public static bool IsPhaseAllowed(TriadPhase phase, int evolutionStage, out string errorMessage)
    {
      errorMessage = null;
      TriadPhase min = GetMinPhaseForStage(evolutionStage);
      TriadPhase max = GetMaxPhaseForStage(evolutionStage);

      if (phase < min)
      {
        errorMessage = FormatPhaseNotAllowed(phase, evolutionStage, min, max,
            "на стадии " + evolutionStage + " минимальная фаза — " + min + ".");
        return false;
      }

      if (phase > max)
      {
        errorMessage = FormatPhaseNotAllowed(phase, evolutionStage, min, max,
            "на стадии " + evolutionStage + " максимальная фаза — " + max + ".");
        return false;
      }

      return true;
    }

    /// <summary>
    /// Краткое описание допустимого диапазона фаз для стадии.
    /// </summary>
    /// <param name="evolutionStage">Стадия эволюции 0–5.</param>
    /// <returns>Текст для UI или лога.</returns>
    public static string FormatAllowedRange(int evolutionStage)
    {
      TriadPhase min = GetMinPhaseForStage(evolutionStage);
      TriadPhase max = GetMaxPhaseForStage(evolutionStage);
      if (min == max)
        return "стадия " + evolutionStage + ": только фаза " + min;
      return "стадия " + evolutionStage + ": фазы " + min + "–" + max;
    }

    private static string FormatPhaseNotAllowed(
        TriadPhase phase,
        int evolutionStage,
        TriadPhase min,
        TriadPhase max,
        string reason)
    {
      return "Фаза " + phase + " недопустима: " + reason
          + " Допустимо: " + (min == max ? min.ToString() : min + "–" + max) + ".";
    }
  }
}
