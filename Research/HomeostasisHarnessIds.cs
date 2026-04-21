namespace ISIDA.Research
{
  /// <summary>Идентификаторы встроенных прогонов гомеостаза.</summary>
  public static class HomeostasisHarnessIds
  {
    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.HasCriticalParameterChanges"/>.</summary>
    public const string HasCriticalParameterChanges = "homeostasis.has_critical_parameter_changes";

    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.AnyVitalParameterInHarmfulZone"/>.</summary>
    public const string AnyVitalHarmfulZone = "homeostasis.any_vital_harmful_zone";

    /// <summary>Два булевых флага: порог внешнего воздействия и «критичность по ориентации».</summary>
    public const string ExternalImpactCriticalFlags = "homeostasis.external_impact_critical_flags";

    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.CalculateUrgencyFunction"/>.</summary>
    public const string CalculateUrgencyFunction = "homeostasis.calculate_urgency_function";

    /// <summary>Прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.ComputeOperatorAutomatizmAssessment"/>.</summary>
    public const string ComputeOperatorAutomatizmAssessment = "homeostasis.compute_operator_automatizm_assessment";

    /// <summary>Парный прогон <see cref="ISIDA.Gomeostas.HomeostasisCalculator.FindDominantParameter"/> и <see cref="ISIDA.Gomeostas.HomeostasisCalculator.GetFinalActiveStyles"/>.</summary>
    public const string DominantAndFinalStyles = "homeostasis.dominant_and_final_styles";

    /// <summary>Все известные идентификаторы прогонов (для списков в UI).</summary>
    public static string[] All { get; } =
    {
      HasCriticalParameterChanges,
      AnyVitalHarmfulZone,
      ExternalImpactCriticalFlags,
      CalculateUrgencyFunction,
      ComputeOperatorAutomatizmAssessment,
      DominantAndFinalStyles
    };
  }
}
