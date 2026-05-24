using System;

namespace ISIDA.Niche
{
  /// <summary>
  /// Маска активных подсистем симбионта (§1.4, §12.2).
  /// </summary>
  [Flags]
  public enum SymbiontSubsystem
  {
    /// <summary>Нет подсистем.</summary>
    None = 0,

    /// <summary>Гомеостаз.</summary>
    Gomeostasis = 1,

    /// <summary>Безусловные рефлексы.</summary>
    GeneticReflexes = 2,

    /// <summary>Условные рефлексы (стадия 1).</summary>
    ConditionedReflexes = 4,

    /// <summary>Реактивные automatisms (не для Niche).</summary>
    ReactiveAutomatisms = 8,

    /// <summary>Психика (не для Niche).</summary>
    Psychic = 16,

    /// <summary>Эпизодическая память (не для Niche).</summary>
    EpisodicMemory = 32,

    /// <summary>Циклы мышления (не для Niche).</summary>
    ThinkingCycles = 64
  }

  /// <summary>
  /// Профиль роли Creature или Niche в срезе эксперимента (§1.4).
  /// Niche: только стадии 0 (БР) и 1 (+ УР); без психики и automatisms.
  /// </summary>
  public sealed class RoleProfile
  {
    /// <summary>Идентификатор профиля для лога.</summary>
    public string ProfileId { get; set; } = "niche_stage_0";

    /// <summary>Активные подсистемы.</summary>
    public SymbiontSubsystem ActiveMask { get; set; }

    /// <summary>True, если подсистема активна в профиле.</summary>
    public bool IsActive(SymbiontSubsystem subsystem)
    {
      return (ActiveMask & subsystem) == subsystem;
    }

    /// <summary>Стадия 0: гомеостаз + безусловные рефлексы (универсальный симбионт Niche).</summary>
    public static RoleProfile NicheStage0 =>
        new RoleProfile
        {
          ProfileId = "niche_stage_0",
          ActiveMask = SymbiontSubsystem.Gomeostasis | SymbiontSubsystem.GeneticReflexes
        };

    /// <summary>Стадия 1: стадия 0 + условные рефлексы.</summary>
    public static RoleProfile NicheStage1 =>
        new RoleProfile
        {
          ProfileId = "niche_stage_1",
          ActiveMask = NicheStage0.ActiveMask | SymbiontSubsystem.ConditionedReflexes
        };

    /// <summary>Алиас <see cref="NicheStage0"/>.</summary>
    public static RoleProfile NicheMinimal => NicheStage0;

    /// <summary>Устарело: эквивалент <see cref="NicheStage0"/> (без automatisms).</summary>
    public static RoleProfile NicheReactive => NicheStage0;

    /// <summary>Полный стек Creature.</summary>
    public static RoleProfile CreatureFull =>
        new RoleProfile
        {
          ProfileId = "creature_full",
          ActiveMask = SymbiontSubsystem.Gomeostasis |
                       SymbiontSubsystem.GeneticReflexes |
                       SymbiontSubsystem.ConditionedReflexes |
                       SymbiontSubsystem.ReactiveAutomatisms |
                       SymbiontSubsystem.Psychic |
                       SymbiontSubsystem.EpisodicMemory |
                       SymbiontSubsystem.ThinkingCycles
        };

    /// <summary>
    /// Разбирает имя профиля из конфига.
    /// </summary>
    public static RoleProfile FromConfigName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return NicheStage0;

      string n = name.Trim().ToLowerInvariant();
      if (n == "niche_stage_1" || n == "niche_stage1")
        return NicheStage1;
      if (n == "creature_full")
        return CreatureFull;
      if (n == "niche_reactive" || n == "niche_minimal" || n == "niche_stage_0" || n == "niche_stage0")
        return NicheStage0;
      return NicheStage0;
    }
  }
}
