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

    /// <summary>Реактивные automatisms (стадия 2, без AOE оператора).</summary>
    ReactiveAutomatisms = 8,

    /// <summary>Психика (стадия 2+).</summary>
    Psychic = 16,

    /// <summary>Эпизодическая память (стадия 4+).</summary>
    EpisodicMemory = 32,

    /// <summary>Циклы мышления (стадия 4+).</summary>
    ThinkingCycles = 64
  }

  /// <summary>
  /// Профиль роли Creature или Niche в срезе эксперимента (§1.4).
  /// </summary>
  public sealed class RoleProfile
  {
    /// <summary>Идентификатор профиля для лога.</summary>
    public string ProfileId { get; set; } = "niche_minimal";

    /// <summary>Активные подсистемы.</summary>
    public SymbiontSubsystem ActiveMask { get; set; }

    /// <summary>True, если подсистема активна в профиле.</summary>
    /// <param name="subsystem">Подсистема.</param>
    /// <returns>True, если бит установлен.</returns>
    public bool IsActive(SymbiontSubsystem subsystem)
    {
      return (ActiveMask & subsystem) == subsystem;
    }

    /// <summary>Минимальный стек Niche: гомеостаз + БР + опц. УР (§1.4).</summary>
    public static RoleProfile NicheMinimal =>
        new RoleProfile
        {
          ProfileId = "niche_minimal",
          ActiveMask = SymbiontSubsystem.Gomeostasis |
                       SymbiontSubsystem.GeneticReflexes |
                       SymbiontSubsystem.ConditionedReflexes
        };

    /// <summary>Niche с реактивными automatisms (post-MVP расширение).</summary>
    public static RoleProfile NicheReactive =>
        new RoleProfile
        {
          ProfileId = "niche_reactive",
          ActiveMask = NicheMinimal.ActiveMask | SymbiontSubsystem.ReactiveAutomatisms
        };

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
    /// <param name="name">Имя: niche_minimal, niche_reactive, creature_full.</param>
    /// <returns>Профиль или NicheMinimal по умолчанию.</returns>
    public static RoleProfile FromConfigName(string name)
    {
      if (string.IsNullOrWhiteSpace(name))
        return NicheMinimal;

      string n = name.Trim().ToLowerInvariant();
      if (n == "niche_reactive")
        return NicheReactive;
      if (n == "creature_full")
        return CreatureFull;
      return NicheMinimal;
    }
  }
}
