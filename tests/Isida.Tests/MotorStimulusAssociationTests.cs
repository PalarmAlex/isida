using System;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты системы мотор→стимул ассоциаций: RW-обучение, порог активации, обучение
  /// на живом потоке <see cref="ConditionedReflexFormationService"/>, затухание и очистка.
  /// </summary>
  public class MotorStimulusAssociationTests : EngineIntegrationTestBase
  {
    /// <summary>Создаёт тестовый экземпляр на общей фикстуре живого движка.</summary>
    /// <param name="engine">Общая фикстура движка.</param>
    public MotorStimulusAssociationTests(EngineFixture engine) : base(engine) { }

    private static MotorStimulusAssociationSystem MotorAssoc =>
        MotorStimulusAssociationSystem.Instance;

    [Fact]
    public void StrengthenLink_ByRescorlaWagner_MatchesFormula()
    {
      int motor = Engine.NextSeed();
      int stimulus = NewPhraseImage();

      const int steps = 3;
      for (int i = 0; i < steps; i++)
        MotorAssoc.StrengthenLink(motor, stimulus);

      float alpha = Settings.MotorAssociationLearningRate;
      float expected = 1f - (float)Math.Pow(1f - alpha, steps);

      Assert.True(MotorAssoc.TryGetStrength(motor, stimulus, out float actual));
      Assert.Equal(expected, actual, 5);
    }

    [Fact]
    public void IsLinkActivatable_RespectsThreshold()
    {
      int motor = Engine.NextSeed();
      int stimulus = NewPhraseImage();

      MotorAssoc.StrengthenLink(motor, stimulus);
      Assert.False(MotorAssoc.IsLinkActivatable(motor, stimulus),
          "после одного усиления (C = α) связь ниже порога γ");

      for (int i = 0; i < 4; i++)
        MotorAssoc.StrengthenLink(motor, stimulus);
      Assert.True(MotorAssoc.IsLinkActivatable(motor, stimulus),
          "после пяти усилений связь должна превысить порог γ");
    }

    [Fact]
    public void RecordStimulus_AfterMotor_StrengthensMotorStimulusLink()
    {
      // Обучение мотор→стимул через боевой поток: мотор фиксируется SetLastMotorAction,
      // следующий нейтральный CS в окне τ усиливает связь.
      int motor = Engine.NextSeed();
      int c5 = NewPhraseImage();

      for (int i = 0; i < 5; i++)
      {
        int p = 10 + i * 20;
        Formation.SetLastMotorAction(motor, p);
        Formation.RecordStimulus(p + 1, c5, 0, 0, crFired: true);
      }

      Assert.True(MotorAssoc.IsLinkActivatable(motor, c5),
          "после повторений мотор→стимул связь должна достичь порога γ");
    }

    [Fact]
    public void RecordStimulus_SecondCsInWindow_DoesNotStealMotor()
    {
      int motor = Engine.NextSeed();
      int first = NewPhraseImage();
      int second = NewPhraseImage();

      Formation.SetLastMotorAction(motor, 10);
      Formation.RecordStimulus(11, first, 0, 0, crFired: true);
      Formation.RecordStimulus(12, second, 0, 0, crFired: true);

      Assert.True(MotorAssoc.TryGetStrength(motor, first, out _),
          "первый нейтральный CS после мотора должен связаться с мотором");
      Assert.False(MotorAssoc.TryGetStrength(motor, second, out _),
          "второй CS в том же окне не должен перехватывать мотор");
    }

    [Fact]
    public void ApplyDecay_ReducesStrength()
    {
      int motor = Engine.NextSeed();
      int stimulus = NewPhraseImage();

      for (int i = 0; i < 5; i++)
        MotorAssoc.StrengthenLink(motor, stimulus);

      Assert.True(MotorAssoc.TryGetStrength(motor, stimulus, out float before));

      Engine.AdvanceToPulse(Settings.MotorAssociationDecayPeriodPulses);
      MotorAssoc.ApplyDecay();

      Assert.True(MotorAssoc.TryGetStrength(motor, stimulus, out float after));
      Assert.True(after < before, "крепость связи должна уменьшиться после ApplyDecay");
    }

    [Fact]
    public void ClearAll_RemovesLinks()
    {
      int motor = Engine.NextSeed();
      int stimulus = NewPhraseImage();

      MotorAssoc.StrengthenLink(motor, stimulus);
      Assert.True(MotorAssoc.TryGetStrength(motor, stimulus, out _));

      MotorAssoc.ClearAll();

      Assert.False(MotorAssoc.TryGetStrength(motor, stimulus, out _),
          "после ClearAll связей быть не должно");
    }
  }
}
