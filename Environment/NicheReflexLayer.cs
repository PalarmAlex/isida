using System;
using System.Collections.Generic;

namespace ISIDA.Niche
{
  /// <summary>
  /// Реактивные рефлексы Niche: вторичные изменения state после действия Creature (§1.4, §6.7).
  /// </summary>
  public sealed class NicheReflexLayer
  {
    private readonly List<NicheReflexRule> _rules = new List<NicheReflexRule>();
    private RoleProfile _roleProfile;

    /// <summary>
    /// Создаёт слой рефлексов Niche.
    /// </summary>
    /// <param name="roleProfile">Профиль роли Niche.</param>
    public NicheReflexLayer(RoleProfile roleProfile)
    {
      _roleProfile = roleProfile ?? RoleProfile.NicheStage0;
    }

    /// <summary>Обновляет профиль роли после перезагрузки конфигурации.</summary>
    public void SetRoleProfile(RoleProfile roleProfile)
    {
      if (roleProfile != null)
        _roleProfile = roleProfile;
    }

    /// <summary>Число загруженных правил.</summary>
    public int RuleCount => _rules.Count;

    /// <summary>
    /// Загружает правила из каталога Data/Niche.
    /// </summary>
    /// <param name="nicheDataFolder">Каталог данных Niche.</param>
    public void LoadRules(string nicheDataFolder)
    {
      _rules.Clear();
      if (!_roleProfile.IsActive(SymbiontSubsystem.GeneticReflexes))
        return;

      _rules.AddRange(NicheReflexLoader.LoadFromFolder(nicheDataFolder));
    }

    /// <summary>
    /// Применяет сработавшие рефлексы после coupling-действия Creature.
    /// </summary>
    /// <param name="nicheState">Состояние Niche.</param>
    /// <param name="creatureActionId">ID действия Creature на такте (0 если не было).</param>
    /// <returns>Число применённых правил.</returns>
    public int ApplyReactiveReflexes(INicheParameterState nicheState, int creatureActionId)
    {
      if (nicheState == null || _rules.Count == 0)
        return 0;

      if (!_roleProfile.IsActive(SymbiontSubsystem.GeneticReflexes))
        return 0;

      var values = nicheState.GetCurrentValues();
      int applied = 0;

      foreach (var rule in _rules)
      {
        if (!ShouldFire(rule, values, creatureActionId))
          continue;

        float delta = rule.Delta * rule.Scale;
        nicheState.ApplyCouplingDelta(rule.TargetNicheParamId, delta);
        applied++;
        values = nicheState.GetCurrentValues();
      }

      return applied;
    }

    private static bool ShouldFire(
        NicheReflexRule rule,
        IReadOnlyDictionary<int, float> nicheValues,
        int creatureActionId)
    {
      switch (rule.TriggerKind)
      {
        case NicheReflexTriggerKind.CreatureAction:
          return creatureActionId > 0 && Math.Abs(creatureActionId - rule.TriggerValue) < 0.001f;

        case NicheReflexTriggerKind.ParamBelow:
          if (!nicheValues.TryGetValue(rule.SourceParamId, out float belowVal))
            return false;
          return belowVal < rule.TriggerValue;

        case NicheReflexTriggerKind.ParamAbove:
          if (!nicheValues.TryGetValue(rule.SourceParamId, out float aboveVal))
            return false;
          return aboveVal > rule.TriggerValue;

        default:
          return false;
      }
    }
  }
}
