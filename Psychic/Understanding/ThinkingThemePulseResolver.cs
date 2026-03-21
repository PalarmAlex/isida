using ISIDA.Actions;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Psychic.Understanding
{
  /// <summary>
  /// Один раз на пульс: по стимулам предыдущего пульса (событие агента, воздействия с пульта) и текущему настроению
  /// выбирает тип темы мышления: сначала максимальный вес темы, при равенстве — воздействие, затем событие, затем настроение.
  /// После выбора сбрасывает буферы стимулов в <see cref="AppGlobalState"/>.
  /// </summary>
  public static class ThinkingThemePulseResolver
  {
    /// <summary>Вызывать в начале обработки пульса (до изменения состояния агента), с текущим GlobalPulsCount.</summary>
    public static void ResolveAtPulseStart(int currentPulse)
    {
      if (currentPulse <= 0) return;
      if (!ThemeImageSystem.IsInitialized || !SituationTypeSystem.IsInitialized) return;

      int prevPulse = currentPulse - 1;

      int evPulse = 0, evCode = 0, infPulse = 0;
      IReadOnlyList<int> infIds = Array.Empty<int>();
      AppGlobalState.TakeStimulusSnapshotForThemeResolution(out evPulse, out evCode, out infPulse, out infIds);

      bool useEvent = evPulse == prevPulse && evCode > 0;
      bool useInf = infPulse == prevPulse && infIds != null && infIds.Count > 0;

      int moodId = AppGlobalState.CurrentStimulusMoodId;
      if (moodId < 0) moodId = 0;

      int themeInfluence = 0, wInf = -1;
      int bestMag = -1, bestTw = -1;
      int firstIdx = int.MaxValue;
      if (useInf && InfluenceActionSystem.IsInitialized)
      {
        for (int i = 0; i < infIds.Count; i++)
        {
          int aid = infIds[i];
          if (aid <= 0) continue;
          int mag = InfluenceActionSystem.Instance.GetInfluenceMagnitudeSum(aid);
          int sitId = SituationTypeSystem.IsInitialized
            ? SituationTypeSystem.Instance.GetIdByInfluenceId(aid)
            : 0;
          int tt = sitId > 0 && SituationTypeSystem.IsInitialized
            ? SituationTypeSystem.Instance.GetThemeTypeIdBySituationTypeId(sitId)
            : 0;
          if (tt <= 0) continue;
          int tw = ThemeImageSystem.Instance.GetDefaultWeightForThemeType(tt);

          bool better = false;
          if (mag > bestMag) better = true;
          else if (mag == bestMag)
          {
            if (tw > bestTw) better = true;
            else if (tw == bestTw && i < firstIdx) better = true;
          }
          if (better)
          {
            bestMag = mag;
            bestTw = tw;
            firstIdx = i;
            themeInfluence = tt;
            wInf = tw;
          }
        }
      }

      int themeEvent = 0, wEv = -1;
      if (useEvent && SituationTypeSystem.IsInitialized)
      {
        themeEvent = SituationTypeSystem.Instance.GetThemeTypeIdByAgentEventCode(evCode);
        if (themeEvent > 0)
          wEv = ThemeImageSystem.Instance.GetDefaultWeightForThemeType(themeEvent);
      }

      int themeMood = 0, wMd = -1;
      if (SituationTypeSystem.IsInitialized)
      {
        int moodSitId = SituationTypeSystem.Instance.GetIdByMoodId(moodId);
        if (moodSitId > 0)
        {
          themeMood = SituationTypeSystem.Instance.GetThemeTypeIdBySituationTypeId(moodSitId);
          if (themeMood > 0)
            wMd = ThemeImageSystem.Instance.GetDefaultWeightForThemeType(themeMood);
        }
      }

      int chosen = 0;
      var candidates = new List<(int ThemeTypeId, int Weight, int Tier)>();
      if (themeInfluence > 0 && wInf >= 0) candidates.Add((themeInfluence, wInf, 3));
      if (themeEvent > 0 && wEv >= 0) candidates.Add((themeEvent, wEv, 2));
      if (themeMood > 0 && wMd >= 0) candidates.Add((themeMood, wMd, 1));

      if (candidates.Count > 0)
      {
        int maxW = candidates.Max(c => c.Weight);
        chosen = candidates.Where(c => c.Weight == maxW).OrderByDescending(c => c.Tier).First().ThemeTypeId;
      }

      if (chosen <= 0)
        chosen = ThemeImageSystem.Instance.DefaultThemeTypeId;

      AppGlobalState.ResolvedThinkingThemeTypeId = chosen > 0 ? chosen : 0;
      AppGlobalState.ClearStimulusBuffersAfterThemeResolution();
    }
  }
}
