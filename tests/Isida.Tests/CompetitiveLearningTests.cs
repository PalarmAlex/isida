using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Конкурентный слой обучения (Kamin blocking, ΣV в ошибке Рескорла–Вагнера):
  /// подкрепление λ при CS→US распределяется с учётом того, что тот же US уже
  /// предсказывается более крепким CS в тех же условиях. Это лечение «мусорных»
  /// у-рефлексов: шумовой CS, предъявляемый ПОСЛЕ окрепшего целевого, почти не учится.
  /// При <c>Settings.EnableCompetitiveLearning = false</c> поведение возвращается к
  /// независимому обучению (каждый CS растёт полным α·(β−C)) — регрессионный контроль.
  /// </summary>
  public class CompetitiveLearningTests : EngineIntegrationTestBase
  {
    public CompetitiveLearningTests(EngineFixture engine) : base(engine) { }

    private void FeedPair(int cs, int us, int pulse, bool authoritative = false)
    {
      Formation.RecordStimulus(pulse, cs, 0, 0);
      Formation.RecordStimulus(pulse + 1, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(pulse + 1, authoritativeMode: authoritative);
    }

    private float StrengthOf(int level3) =>
        Crs.GetAllConditionedReflexes().First(r => r.Level3 == level3).AssociationStrength;

    [Fact]
    public void Enabled_TargetBlocksNoise_NoiseStaysBelowThreshold()
    {
      // Целевой CS захватывает предсказание US, после чего шумовой CS к тому же US
      // почти не обучается (ΣV ≈ C_target). Итог: шумовой рефлекс заметно слабее
      // целевого и не достигает порога активации γ.
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedPair(noise, us, 200 + i * 10);
      float cNoise = StrengthOf(noise);

      Assert.True(cNoise < cTarget,
          $"шумовой CS при блокировке обязан отставать: target={cTarget}, noise={cNoise}");
      Assert.True(cNoise < Settings.ActivationThreshold,
          $"шумовой CS при блокировке не должен стать активируемым: C={cNoise} ≥ γ={Settings.ActivationThreshold}");
    }

    [Fact]
    public void Disabled_EachCsLearnsIndependently_NoBlocking()
    {
      // Отключённый слой — прежняя модель без ΣV: шумовой CS идёт по тому же RW-ряду,
      // что и целевой, и догоняет его. Регрессионный контроль на обратимость настройки.
      ActivateSeedStyle();
      Settings.EnableCompetitiveLearning = false;
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedPair(noise, us, 200 + i * 10);
      float cNoise = StrengthOf(noise);

      float c0 = Settings.MinAssociationStrength + 0.1f;
      float expected = ExpectedRwStrength(c0, steps: 5, order: 1);

      Assert.True(System.Math.Abs(expected - cNoise) < 5e-4f,
          $"без блокировки шумовой CS должен идти по RW: C={cNoise}, ожидалось {expected}");
      Assert.True(System.Math.Abs(cTarget - cNoise) < 5e-4f,
          $"без блокировки шумовой CS догоняет целевой: target={cTarget}, noise={cNoise}");
    }

    [Fact]
    public void AuthoritativeMode_NotSuppressed()
    {
      // Явное подтверждение оператором (authoritativeMode) от блокировки защищено:
      // даже при окрепшем целевом шумовой CS, записанный авторитарно, стартует с 0.95.
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10, authoritative: true);
      FeedPair(noise, us, 200, authoritative: true);

      var noiseReflex = Crs.GetAllConditionedReflexes().First(r => r.Level3 == noise);
      Assert.True(noiseReflex.CanBeActivated(),
          "авторитарная запись не должна блокироваться конкурентом");
      Assert.True(noiseReflex.AssociationStrength >= 0.9f);
    }

    [Fact]
    public void LastCsWins_IntermediateNoise_NotLearned()
    {
      // Арбитраж внутри одного испытания: US подкрепляет только последний предшествующий CS.
      // Промежуточные шумовые CS не получают обучения (ни через создание, ни через блокировку).
      ActivateSeedStyle();
      int noise1 = NewPhraseImage();
      int noise2 = NewPhraseImage();
      int target = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      Formation.RecordStimulus(1, noise1, 0, 0);
      Formation.RecordStimulus(2, noise2, 0, 0);
      Formation.RecordStimulus(3, target, 0, 0);
      Formation.RecordStimulus(4, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(4, false);

      var all = Crs.GetAllConditionedReflexes();
      Assert.DoesNotContain(all, r => r.Level3 == noise1);
      Assert.DoesNotContain(all, r => r.Level3 == noise2);
      Assert.Single(all, r => r.Level3 == target);
    }

    [Fact]
    public void PartialSuppression_NoiseGrowsSlowerButNotBlocked()
    {
      // coefficient = 0.5: частичное подавление. Шумовой CS не блокируется полностью
      // (растёт выше C_min), но идёт заметно медленнее целевого (α_eff = α·(1−suppression)).
      // Это средний режим между Enabled (suppression→1, блокировка) и Disabled (0, независимость).
      ActivateSeedStyle();
      Settings.CompetitionSuppressionCoefficient = 0.5f;
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedPair(noise, us, 200 + i * 10);
      float cNoise = StrengthOf(noise);

      Assert.True(cNoise > Settings.MinAssociationStrength,
          $"при частичном подавлении шумовый CS обязан всё же учиться: C={cNoise} ≤ C_min");
      Assert.True(cNoise < cTarget,
          $"но слабее целевого (частичное подавление): target={cTarget}, noise={cNoise}");
    }

    [Fact]
    public void EnableCompetitiveLearning_RoundTripsThroughFile()
    {
      // Новый флаг + коэффициент обязаны переживать цикл Save → Load (иначе после
      // рестарта движка конкурентный слой «включается сам» независимо от сохранённой настройки).
      Settings.EnableCompetitiveLearning = false;
      Settings.CompetitionSuppressionCoefficient = 0.5f;

      var saveResult = Crs.SaveConditionedReflexSettings();
      Assert.True(saveResult.Success, saveResult.ErrorMessage);

      // Портим значения в памяти, затем реально перезагружаем из файла приватным
      // LoadConditionedReflexSettings() — проверяем не текст файла, а восстановление настроек.
      Settings.EnableCompetitiveLearning = true;
      Settings.CompetitionSuppressionCoefficient = 1.0f;

      typeof(ConditionedReflexesSystem)
          .GetMethod("LoadConditionedReflexSettings",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
          ?.Invoke(Crs, null);

      Assert.False(Settings.EnableCompetitiveLearning,
          "EnableCompetitiveLearning=false обязан восстановиться из файла");
      Assert.Equal(0.5f, Settings.CompetitionSuppressionCoefficient, 5);
    }

    [Fact]
    public void NoisePairs_DoNotWeakenExistingTargetReflex()
    {
      // Пока идут шумовые пары (и они блокируются), целевой рефлекс не угасает:
      // он подкреплялся US и не является «CS без подкрепления».
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      int targetId = Crs.GetAllConditionedReflexes().First(r => r.Level3 == target).Id;
      float before = Find(targetId).AssociationStrength;

      for (int i = 0; i < 3; i++)
        FeedPair(noise, us, 200 + i * 10);

      var alive = Find(targetId);
      Assert.NotNull(alive);
      Assert.True(alive.AssociationStrength >= before,
          $"целевой рефлекс не должен слабнуть от шумовых пар: {before} → {alive.AssociationStrength}");
    }
  }
}
