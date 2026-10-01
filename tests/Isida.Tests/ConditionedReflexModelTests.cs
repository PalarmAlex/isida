using System;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты поведения <c>ConditionedReflexesSystem.ConditionedReflex</c> в изоляции
  /// (доступен через <c>[InternalsVisibleTo("Isida.Tests")]</c>).
  /// Проверяются сеттеры-валидаторы, TTL и пороги, где это не требует живого синглтона.
  /// </summary>
  public class ConditionedReflexModelTests
  {
    // ---------- Валидаторы настроек на уровне рефлекса ----------

    [Fact]
    public void LearningRate_OutOfRange_Throws()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.LearningRate = 0.05f);
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.LearningRate = 0.5f);
    }

    [Fact]
    public void ActivationThreshold_OutOfRange_Throws()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.ActivationThreshold = 0.4f);
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.ActivationThreshold = 0.8f);
    }

    [Fact]
    public void TimeWindowPulses_OutOfRange_Throws()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.TimeWindowPulses = 0);
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.TimeWindowPulses = 11);
    }

    [Fact]
    public void MinAssociationStrength_OutOfRange_Throws()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.MinAssociationStrength = 0f);
      Assert.Throws<ArgumentOutOfRangeException>(() => reflex.MinAssociationStrength = 0.5f);
    }

    [Fact]
    public void LearningRate_InRange_Stored()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex();
      reflex.LearningRate = 0.2f;
      Assert.Equal(0.2f, reflex.LearningRate, 3);
    }

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
      Assert.True(reflex.IsEstablished);
    }

    [Fact]
    public void IsEstablished_FalseWhenNeverHigh()
    {
      var reflex = new ConditionedReflexesSystem.ConditionedReflex { AssociationStrength = 0.5f };
      reflex.SyncMaxAchievedFromCurrent();

      Assert.False(reflex.IsEstablished);
    }

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
    }
  }
}
