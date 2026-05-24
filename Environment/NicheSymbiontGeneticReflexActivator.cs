using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Niche
{
  /// <summary>
  /// Активация безусловных рефлексов Niche после coupling (Level1/2 — гомеостаз Niche, Level3 — ID действия Creature).
  /// </summary>
  public static class NicheSymbiontGeneticReflexActivator
  {
    /// <summary>
    /// Проверяет БР и применяет воздействия на параметры Niche.
    /// </summary>
    /// <returns>Число сработавших рефлексов.</returns>
    public static int ApplyAfterCreatureAction(
        NicheSymbiontContext context,
        INicheParameterState nicheState,
        int creatureActionId)
    {
      if (context?.GeneticReflexes == null || context.InfluenceActions == null || context.AdaptiveActions == null || nicheState == null)
        return 0;

      if (!context.RoleProfile.IsActive(SymbiontSubsystem.GeneticReflexes))
        return 0;

      var slice = context.Gomeostas.DetachedGetHomeostasisSlice();
      var triggers = BuildTriggers(creatureActionId);
      int applied = 0;

      foreach (var reflex in context.GeneticReflexes.GetAllGeneticReflexesList())
      {
        if (!IsReflexConditionsMet(reflex, slice.BaseStateId, slice.ActiveStyleIds, triggers))
          continue;

        if (ApplyReflexActions(context, reflex))
          applied++;
      }

      return applied;
    }

    /// <summary>Применяет один БР Niche по ID (для условных рефлексов).</summary>
    public static bool ApplyGeneticReflexById(NicheSymbiontContext context, int geneticReflexId)
    {
      if (context?.GeneticReflexes == null || geneticReflexId <= 0)
        return false;

      var reflex = context.GeneticReflexes.GetGeneticReflex(geneticReflexId);
      if (reflex == null)
        return false;

      return ApplyReflexActions(context, reflex);
    }

    private static bool ApplyReflexActions(NicheSymbiontContext context, GeneticReflexesSystem.GeneticReflex reflex)
    {
      if (reflex.AdaptiveActions == null || reflex.AdaptiveActions.Count == 0)
        return false;

      bool any = false;
      foreach (int actionId in reflex.AdaptiveActions)
      {
        var adaptive = context.AdaptiveActions.GetAdaptiveAction(actionId);
        if (adaptive == null || adaptive.InfluenceActionId <= 0)
          continue;

        if (context.InfluenceActions.ApplyInfluenceToNicheHost(adaptive.InfluenceActionId))
          any = true;
      }

      return any;
    }

    private static bool IsReflexConditionsMet(
        GeneticReflexesSystem.GeneticReflex reflex,
        int baseStateId,
        IReadOnlyList<int> activeStyleIds,
        IReadOnlyList<int> triggers)
    {
      if (reflex.Level1 != baseStateId)
        return false;

      if (reflex.Level2 != null && reflex.Level2.Count > 0)
      {
        if (activeStyleIds == null || activeStyleIds.Count == 0)
          return false;

        if (!reflex.Level2.All(styleId => activeStyleIds.Contains(styleId)) ||
            !activeStyleIds.All(styleId => reflex.Level2.Contains(styleId)))
          return false;
      }

      if (reflex.Level3 != null && reflex.Level3.Count > 0)
      {
        if (triggers == null || triggers.Count == 0)
          return false;

        if (!reflex.Level3.All(t => triggers.Contains(t)) ||
            !triggers.All(t => reflex.Level3.Contains(t)))
          return false;
      }

      return true;
    }

    private static List<int> BuildTriggers(int creatureActionId)
    {
      if (creatureActionId <= 0)
        return new List<int>();

      return new List<int> { creatureActionId };
    }
  }
}
