using System;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты поведения <c>ConditionedReflexesSystem.ConditionedReflex</c> в изоляции
  /// (доступен через <c>[InternalsVisibleTo("Isida.Tests")]</c>).
  /// Проверяются TTL, пороги и значения настроек по умолчанию, где это не требует живого синглтона.
  /// Параметры модели (α, β, γ, τ, C_min, ...) живут только в ConditionedReflexSettings —
  /// на уровне экземпляра рефлекса их копий больше нет (см. DEBUG_CASEBOOK_1, E2).
  /// </summary>
  public class ConditionedReflexModelTests
  {
    // ---------- TTL / ExpiresAt (чистая арифметика) ----------

    [Fact]
    public void InitializeLifetime_SetsExpiresAtFromNow()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();

      reflex.InitializeLifetime(now: 100, initialLifetimePulses: 500);

      Assert.Equal(500, reflex.LifetimePulses);
      Assert.Equal(100, reflex.LastActivation);
      Assert.Equal(600, reflex.ExpiresAt);
    }

    [Fact]
    public void InitializeLifetime_ZeroOrNegative_ClampedToOne()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();

      reflex.InitializeLifetime(now: 10, initialLifetimePulses: 0);

      Assert.Equal(1, reflex.LifetimePulses);
      Assert.Equal(11, reflex.ExpiresAt);
    }

    [Fact]
    public void InitializeLifetime_NearIntMax_Saturates()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();

      reflex.InitializeLifetime(now: int.MaxValue - 5, initialLifetimePulses: 1000);

      Assert.Equal(int.MaxValue, reflex.ExpiresAt);
    }

    // ---------- Синхронизация MaxAchieved ----------

    [Fact]
    public void SyncMaxAchievedFromCurrent_TracksHighest()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex { AssociationStrength = 0.7f };
      reflex.SyncMaxAchievedFromCurrent();

      reflex.AssociationStrength = 0.9f;
      reflex.SyncMaxAchievedFromCurrent();

      reflex.AssociationStrength = 0.5f;
      reflex.SyncMaxAchievedFromCurrent();

      Assert.Equal(0.9f, reflex.MaxAchievedStrength, 3);
    }

    // Примечание: IsEstablished читает порог из Instance.Settings (живой синглтон),
    // поэтому его поведение покрыто интеграционно в ConditionedReflexMathTests.

    // ---------- Модели параметров (значения по умолчанию) ----------

    [Fact]
    public void Settings_Defaults()
    {
      var settings = new ConditionedReflexesSystem.ConditionedReflexSettings();

      Assert.Equal(0.2f, settings.LearningRate, 3);
      Assert.Equal(0.6f, settings.ActivationThreshold, 3);
      Assert.Equal(0.1f, settings.MinAssociationStrength, 3);
      Assert.Equal(5, settings.TimeWindowPulses);
      Assert.Equal(0.05f, settings.ActiveExtinctionRate, 3);
      Assert.Equal(1000, settings.PassiveDecayPeriodPulses);

      // Параметры, ранее захардкоженные в коде и вынесенные в настройки (см. аудит настроек УР):
      Assert.Equal(0.1f, settings.InitialStrengthBonus, 3);
      Assert.Equal(0.95f, settings.AuthoritativeStrength, 3);
      Assert.Equal(0.8f, settings.EstablishedStrengthThreshold, 3);
      Assert.Equal(0.25f, settings.ActivationReinforcementFraction, 3);
      Assert.Equal(88473600, settings.MaxLifetimePulsesCap);
      Assert.Equal(1000, settings.PassiveDecayFallbackPeriodPulses);
      Assert.Equal(100, settings.SensoryDecayPeriodPulses);
      Assert.Equal(0.1f, settings.SensoryStrengthFloor, 3);
      Assert.Equal(0.8f, settings.SensoryHighStrengthThreshold, 3);
      Assert.Equal(0.998f, settings.SensoryHighStrengthDecayRate, 3);
      Assert.Equal(0.4f, settings.SensoryMidStrengthThreshold, 3);
    }
  }
}
