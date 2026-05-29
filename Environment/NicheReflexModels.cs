namespace ISIDA.Niche
{
  /// <summary>
  /// Тип триггера реактивного рефлекса Niche.
  /// </summary>
  public enum NicheReflexTriggerKind
  {
    /// <summary>Действие Creature (actionId).</summary>
    CreatureAction = 0,

    /// <summary>Параметр Niche ниже порога.</summary>
    ParamBelow = 1,

    /// <summary>Параметр Niche выше порога.</summary>
    ParamAbove = 2
  }

  /// <summary>
  /// Правило реактивного рефлекса Niche (§1.4, §6.7).
  /// </summary>
  public sealed class NicheReflexRule
  {
    /// <summary>Тип триггера.</summary>
    public NicheReflexTriggerKind TriggerKind { get; set; }

    /// <summary>ID действия или порог параметра.</summary>
    public float TriggerValue { get; set; }

    /// <summary>ID параметра-источника (для ParamBelow/Above).</summary>
    public int SourceParamId { get; set; }

    /// <summary>Целевой параметр Niche.</summary>
    public int TargetNicheParamId { get; set; }

    /// <summary>Дельта при срабатывании.</summary>
    public float Delta { get; set; }

    /// <summary>Множитель.</summary>
    public float Scale { get; set; } = 1f;
  }
}
