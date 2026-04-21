namespace ISIDA.Research
{
  /// <summary>Идентификаторы встроенных прогонов гомеостаза.</summary>
  public static class HomeostasisHarnessIds
  {
    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.HasCriticalParameterChanges"/>.</summary>
    public const string HasCriticalParameterChanges = "homeostasis.has_critical_parameter_changes";

    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.AnyVitalParameterInHarmfulZone"/>.</summary>
    public const string AnyVitalHarmfulZone = "homeostasis.any_vital_harmful_zone";

    /// <summary>Все известные идентификаторы прогонов (для списков в UI).</summary>
    public static string[] All { get; } =
    {
      HasCriticalParameterChanges,
      AnyVitalHarmfulZone
    };
  }
}
