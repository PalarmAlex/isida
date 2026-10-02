using System.Collections.Generic;
using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Динамика формирования у-рефлексов через <see cref="ConditionedReflexFormationService"/>:
  /// CS→US в окне τ, накопление C по Рескорла–Вагнеру, активное угасание CS без US,
  /// поведение при шуме между CS и US, сенсорная прекондиция против вторичного CR.
  /// Прямой ответ на проблему «мусорных рефлексов» при быстрой смене стимулов с пульта.
  /// </summary>
  public class ConditionedReflexFormationServiceTests : EngineIntegrationTestBase
  {
    public ConditionedReflexFormationServiceTests(EngineFixture engine) : base(engine) { }

    /// <summary>
    /// Воспроизводит боевой поток <c>RecordStimulus → CheckTemporalCorrelations</c>
    /// (как <c>ReflexesActivator.ActiveFromAction</c>): CS записывается нейтральным,
    /// затем US — как подкрепление.
    /// </summary>
    private void FeedPair(int cs, int usGeneticReflexId, int pulse)
    {
      Formation.RecordStimulus(pulse, cs, 0, 0);
      Formation.RecordStimulus(pulse + 1, 0, 0, 0, usGeneticReflexId);
      Formation.CheckTemporalCorrelations(pulse + 1, authoritativeMode: false);
    }

    [Fact]
    public void CsThenUs_InWindow_CreatesReflex()
    {
      // Ядро: CS→US в пределах τ образует у-рефлекс, наследующий безусловный источник.
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      FeedPair(cs, us, pulse: 1);

      var reflex = Crs.GetAllConditionedReflexes().FirstOrDefault(r => r.Level3 == cs);
      Assert.NotNull(reflex);
      Assert.Equal(us, reflex.SourceGeneticReflexId);
    }

    [Fact]
    public void CsThenUs_OutsideWindow_NoReflex()
    {
      // US пришёл позже окна τ — рефлекс не создаётся.
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;
      int pulse = 1;

      Formation.RecordStimulus(pulse, cs, 0, 0);
      int usPulse = pulse + Settings.TimeWindowPulses + 2;
      Formation.RecordStimulus(usPulse, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(usPulse, authoritativeMode: false);

      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs);
    }

    [Fact]
    public void RepeatedPairs_GrowStrengthByRw()
    {
      // Повторные пары CS→US усиливают C по RW; после 5 пар C ≥ γ и рефлекс активируем.
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 5; i++)
        FeedPair(cs, us, pulse: 1 + i * 10);

      var reflex = Crs.GetAllConditionedReflexes().First(r => r.Level3 == cs);
      Assert.True(reflex.AssociationStrength >= Settings.ActivationThreshold,
          $"после 5 пар C={reflex.AssociationStrength} должен быть ≥ γ={Settings.ActivationThreshold}");
      Assert.True(reflex.CanBeActivated());
    }

    [Fact]
    public void CsBeforeUs_FirstUsStartsReflex_SecondUsStrengthens()
    {
      // Первая пара создаёт рефлекс с C₀, вторая — усиливает его (не дублирует).
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      FeedPair(cs, us, pulse: 1);
      float c1 = Crs.GetAllConditionedReflexes().First(r => r.Level3 == cs).AssociationStrength;

      FeedPair(cs, us, pulse: 20);
      float c2 = Crs.GetAllConditionedReflexes().First(r => r.Level3 == cs).AssociationStrength;

      Assert.True(c2 > c1, $"повторная пара должна усилить C: {c1} → {c2}");
      Assert.Single(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs);
    }

    [Fact]
    public void CsWithoutUs_LowersStrength()
    {
      // Рефлекс ниже γ, затем CS без US (окно τ истекло) → активное угасание снижает C.
      int cs = NewPhraseImage();
      int id = AddReflex(cs, authoritative: false); // C = 0.2 < γ
      float before = Find(id).AssociationStrength;

      for (int i = 0; i < 3; i++)
      {
        int pulse = 100 + i * 20;
        Formation.RecordStimulus(pulse, cs, 0, 0);
        Formation.ProcessPendingExtinction(pulse + Settings.TimeWindowPulses + 1);
      }

      float after = Find(id)?.AssociationStrength ?? 0f;
      Assert.True(after < before, $"активное угасание должно снизить C: {before} → {after}");
    }

    [Fact]
    public void CsWithoutUs_AboveThreshold_NotActivelyExtinguished()
    {
      // Сильный рефлекс (C ≥ γ) активным угасанием НЕ точится — это зона пассивного угасания.
      int cs = NewPhraseImage();
      int id = AddReflex(cs, authoritative: true); // C = 0.95 ≥ γ
      float before = Find(id).AssociationStrength;

      Formation.RecordStimulus(1, cs, 0, 0);
      Formation.ProcessPendingExtinction(1 + Settings.TimeWindowPulses + 1);

      Assert.Equal(before, Find(id).AssociationStrength, 5);
    }

    [Fact]
    public void NoiseBetweenCsAndUs_OnlyLastCsGetsReflex()
    {
      // «Последний побеждает»: между целевым CS и US вставлены шумовые CS — US подкрепляет
      // только последний предшествующий CS. Проверяем, что мусор на промежуточные CS не плодится.
      ActivateSeedStyle();
      int csTarget = NewPhraseImage();
      int csNoise1 = NewPhraseImage();
      int csNoise2 = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      Formation.RecordStimulus(1, csTarget, 0, 0);
      Formation.RecordStimulus(2, csNoise1, 0, 0);
      Formation.RecordStimulus(3, csNoise2, 0, 0);
      Formation.RecordStimulus(4, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(4, authoritativeMode: false);

      var all = Crs.GetAllConditionedReflexes();
      Assert.DoesNotContain(all, r => r.Level3 == csTarget);
      Assert.DoesNotContain(all, r => r.Level3 == csNoise1);
      Assert.Single(all, r => r.Level3 == csNoise2);
    }

    [Fact]
    public void CsThenCsWithoutUs_CreatesSensoryAssociationNotReflex()
    {
      // CS₁→CS₂ без US — это сенсорная прекондиция (второй канал), а не условный рефлекс.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();

      Formation.RecordStimulus(1, cs1, 0, 0);
      Formation.RecordStimulus(2, cs2, 0, 0);

      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs1 || r.Level3 == cs2);
      Assert.True(Sensory.TryGetStrength(cs1, cs2, out float strength));
      Assert.True(strength > 0f);
    }

    [Fact]
    public void ResetHistory_ClearsPendingCs()
    {
      // После ResetHistory следующий US не формирует рефлекс от старого CS.
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      Formation.RecordStimulus(1, cs, 0, 0); // CS pending
      Formation.ResetHistory();

      Formation.RecordStimulus(2, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(2, authoritativeMode: false);

      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs);
    }

    [Fact]
    public void DifferentToneMood_SeparateReflexesPerCombination()
    {
      // CS с разным toneId → разные рефлексы (ключ равенства учитывает ToneId/MoodId).
      ActivateSeedStyle();
      int cs = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      Formation.RecordStimulus(1, cs, 0, 0, toneId: 0, moodId: 0);
      Formation.RecordStimulus(2, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(2, false);

      Formation.RecordStimulus(20, cs, 0, 0, toneId: 1, moodId: 0);
      Formation.RecordStimulus(21, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(21, false);

      var reflexes = Crs.GetAllConditionedReflexes().Where(r => r.Level3 == cs).ToList();
      Assert.Equal(2, reflexes.Count);
    }
  }
}
