using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Конкурентный слой обучения и анти-мусорные механизмы.
  /// <para>
  /// Kamин blocking (ΣV в ошибке Рескорла–Вагнера) по классике действует ТОЛЬКО при
  /// совместном предъявлении CS (компаунд): блокуется новый CS, когда в том же испытании
  /// рядом стоит уже окрепший предсказатель US. Раздельные испытания обязаны учиться
  /// независимо (spacing effect) — иначе к одному подкреплению нельзя прицепить несколько
  /// разных стимулов. Это фиксирует <see cref="SeparateTrials_EachCsLearnsIndependently_NoBlocking"/>.
  /// </para>
  /// <para>
  /// «Мусор» (стимулы, не несущие информации) отсекается не чужой крепостью, а латентным
  /// торможением (Lubov–Moore, 1959): CS, многократно предъявленный без US, обучается
  /// медленнее. см. <see cref="LatentInhibition_UnreinforcedCsLearnsSlowerThanControl"/>.
  /// </para>
  /// </summary>
  public class CompetitiveLearningTests : EngineIntegrationTestBase
  {
    public CompetitiveLearningTests(EngineFixture engine) : base(engine) { }

    /// <summary>Раздельное испытание: один CS, затем US в окне τ.</summary>
    private void FeedPair(int cs, int us, int pulse, bool authoritative = false)
    {
      Formation.RecordStimulus(pulse, cs, 0, 0);
      Formation.RecordStimulus(pulse + 1, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(pulse + 1, authoritativeMode: authoritative);
    }

    /// <summary>
    /// Компаунд-испытание: два CS предъявлены СОВМЕСТНО (в одном окне τ) перед US.
    /// Подкрепление получает последний из них — именно он и должен блокироваться
    /// окрепшим со-предъявленным конкурентом. Первый CS помечен <c>crFired</c>: в живом
    /// потоке это окрепший активируемый стимул (его УР срабатывает), поэтому он не
    /// закрывается как «CS без US» и не угасает посреди испытания.
    /// </summary>
    private void FeedCompound(int csFirst, int csReinforced, int us, int pulse)
    {
      Formation.RecordStimulus(pulse, csFirst, 0, 0, crFired: true);
      Formation.RecordStimulus(pulse + 1, csReinforced, 0, 0);
      Formation.RecordStimulus(pulse + 2, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(pulse + 2);
    }

    private float StrengthOf(int level3) =>
        Crs.GetAllConditionedReflexes().First(r => r.Level3 == level3).AssociationStrength;

    [Fact]
    public void Compound_TargetBlocksNoise_NoiseStaysBelowThreshold()
    {
      // Классический Kamин: целевой CS окреп, затем шумовой предъявляется СОVMЕСТНО с ним
      // перед тем же US — новый CS блокируется (ΣV ≈ C_target).
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedCompound(target, noise, us, 200 + i * 10);
      float cNoise = StrengthOf(noise);

      Assert.True(cNoise < cTarget,
          $"шумовой CS при компаунд-блокировке обязан отставать: target={cTarget}, noise={cNoise}");
      Assert.True(cNoise < Settings.ActivationThreshold,
          $"шумовой CS при блокировке не должен стать активируемым: C={cNoise} ≥ γ={Settings.ActivationThreshold}");
    }

    [Fact]
    public void SeparateTrials_EachCsLearnsIndependently_NoBlocking()
    {
      // РЕГРЕСС НА БАГ: Kamин blocking не имеет права действовать между испытаниями.
      // Раздельные CS→US (никогда не предъявляются вместе) обязаны идти полным темпом RW,
      // иначе «один прицепленный стимул подавляет попытку прицепить другой».
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int second = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedPair(second, us, 200 + i * 10);
      float cSecond = StrengthOf(second);

      float c0 = Settings.MinAssociationStrength + Settings.InitialStrengthBonus;
      float expected = ExpectedRwStrength(c0, steps: 5, order: 1);

      Assert.True(System.Math.Abs(expected - cSecond) < 5e-4f,
          $"раздельное испытание обязано идти чистым RW: C={cSecond}, ожидалось {expected}");
      Assert.True(System.Math.Abs(cTarget - cSecond) < 5e-4f,
          $"второй стимул обязан догнать первый: target={cTarget}, second={cSecond}");
      Assert.True(cSecond >= Settings.ActivationThreshold,
          $"оба стимула должны стать активируемыми: C={cSecond} < γ={Settings.ActivationThreshold}");
    }

    [Fact]
    public void MultipleStimuli_OnSameReinforcer_AllBecomeActivatable()
    {
      // Прямая проверка жалобы пользователя: к ОДНОМУ подкреплению прицепляем три разных
      // стимула раздельными испытаниями — все обязаны окрепнуть до активации.
      ActivateSeedStyle();
      int us = Engine.SeedGeneticReflexId;
      int csA = NewPhraseImage();
      int csB = NewPhraseImage();
      int csC = NewPhraseImage();

      for (int i = 0; i < 6; i++)
      {
        FeedPair(csA, us, 1 + i * 10);
        FeedPair(csB, us, 100 + i * 10);
        FeedPair(csC, us, 300 + i * 10);
      }

      var all = Crs.GetAllConditionedReflexes().Where(r => r.SourceGeneticReflexId == us).ToList();
      Assert.Equal(3, all.Count);
      foreach (var r in all)
        Assert.True(r.CanBeActivated(),
            $"стимул Level3={r.Level3} обязан стать активируемым: C={r.AssociationStrength}");
    }

    [Fact]
    public void LegacyBetweenTrialsMode_WhenCompoundRequirementDisabled()
    {
      // Обратимость настройки: CompetitiveLearningRequiresCompound=false возвращает
      // прежний междуиспытательный режим (лечение «мусора» ценой блокировки раздельных
      // trials) — регрессионный контроль на то, что старый режим не потерян безвозвратно.
      ActivateSeedStyle();
      Settings.CompetitiveLearningRequiresCompound = false;
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
          $"в междуиспытательном режиме шумовой CS обязан блокироваться: target={cTarget}, noise={cNoise}");
      Assert.True(cNoise < Settings.ActivationThreshold,
          $"в междуиспытательном режиме шумовой CS не должен активируаться: C={cNoise}");
    }

    [Fact]
    public void Disabled_EachCsLearnsIndependently_NoBlocking()
    {
      // Отключённый конкурентный слой — прежняя модель без ΣV: шумовой CS идёт по тому же
      // RW-ряду, что и целевой, и догоняет его. Регрессионный контроль на обратимость.
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

      // target после 6 подкреплений ≈ 0.74; независимый noise догоняет его за 6 trials.
      float c0 = Settings.MinAssociationStrength + Settings.InitialStrengthBonus;
      float expected = ExpectedRwStrength(c0, steps: 5, order: 1);

      Assert.True(System.Math.Abs(expected - cNoise) < 5e-4f,
          $"раздельные испытания обязаны идти чистым RW: C={cNoise}, ожидалось {expected}");
      Assert.True(System.Math.Abs(cTarget - cNoise) < 5e-4f,
          $"раздельный noise обязан догнать target (блокировки нет): target={cTarget}, noise={cNoise}");

      // Оба стимула становятся активируемыми — именно этого не давал старый
      // междуиспытательный blocking («прицепить второй CS к тому же подкреплению нельзя»).
      Assert.True(cTarget >= Settings.ActivationThreshold && cNoise >= Settings.ActivationThreshold,
          $"оба УР обязаны перешагнуть γ при раздельном обучении: target={cTarget}, noise={cNoise}");
    }

    [Fact]
    public void AuthoritativeMode_NotSuppressed()
    {
      // Явное подтверждение оператором (authoritativeMode) от подавления защищено: даже при
      // окрепшем со-предъявленном конкуренте шумовой CS, записанный авторитарно, стартует с 0.95.
      ActivateSeedStyle();
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10, authoritative: true);

      Formation.RecordStimulus(200, target, 0, 0);
      Formation.RecordStimulus(201, noise, 0, 0);
      Formation.RecordStimulus(202, 0, 0, 0, us);
      Formation.CheckTemporalCorrelations(202, authoritativeMode: true);

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
      // coefficient = 0.5: частичное подавление в компаунде. Шумовой CS не блокируется
      // полностью (растёт выше C_min), но идёт заметно медленнее целевого
      // (α_eff = α·(1−suppression)). Средний режим между полной блокировкой и независимостью.
      ActivateSeedStyle();
      Settings.CompetitionSuppressionCoefficient = 0.5f;
      int target = NewPhraseImage();
      int noise = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      for (int i = 0; i < 6; i++)
        FeedPair(target, us, 1 + i * 10);
      float cTarget = StrengthOf(target);

      for (int i = 0; i < 6; i++)
        FeedCompound(target, noise, us, 200 + i * 10);
      float cNoise = StrengthOf(noise);

      Assert.True(cNoise > Settings.MinAssociationStrength,
          $"при частичном подавлении шумовый CS обязан всё же учиться: C={cNoise} ≤ C_min");
      Assert.True(cNoise < cTarget,
          $"но слабее целевого (частичное подавление): target={cTarget}, noise={cNoise}");
    }

    [Fact]
    public void LatentInhibition_UnreinforcedCsLearnsSlowerThanControl()
    {
      // Анти-мусорный фильтр, не зависящий от компаунда: CS, многократно предъявленный
      // БЕЗ US («известная константа среды»), обусловливается медленнее контрольного.
      // Это и есть природное лечение мусора после отказа от междуиспытательного blocking.
      ActivateSeedStyle();
      int us = Engine.SeedGeneticReflexId;
      int noisy = NewPhraseImage();
      int control = NewPhraseImage();

      // Накопление латентного торможения: 8 предъявлений noisy без подкрепления.
      int pulse = 1;
      for (int i = 0; i < 8; i++)
      {
        Formation.RecordStimulus(pulse, noisy, 0, 0);
        Formation.ProcessPendingExtinction(pulse + Settings.TimeWindowPulses + 1);
        pulse += Settings.TimeWindowPulses + 2;
      }

      // Обучение шумового и контрольного CS раздельными испытаниями (без блокировки).
      for (int i = 0; i < 4; i++)
      {
        FeedPair(noisy, us, 200 + i * 10);
        FeedPair(control, us, 500 + i * 10);
      }

      float cNoisy = StrengthOf(noisy);
      float cControl = StrengthOf(control);

      Assert.True(cNoisy < cControl,
          $"латентно заторможенный CS обязан отставать: noisy={cNoisy}, control={cControl}");
      Assert.True(cNoisy < Settings.ActivationThreshold,
          $"латентное торможение должно не дать шумовому CS стать активируемым: C={cNoisy}");
      Assert.True(cControl >= Settings.ActivationThreshold ||
                  cControl > cNoisy + 0.1f,
          $"контрольный CS обязан идти заметно быстрее: control={cControl}, noisy={cNoisy}");
    }

    [Fact]
    public void LatentInhibition_DisabledByZeroStrength()
    {
      // Обратимость: LatentInhibitionStrength = 0 выключает торможение — заторможенный CS
      // идёт тем же полным RW-рядом, что и контрольный.
      ActivateSeedStyle();
      Settings.LatentInhibitionStrength = 0f;
      int us = Engine.SeedGeneticReflexId;
      int noisy = NewPhraseImage();
      int control = NewPhraseImage();

      int pulse = 1;
      for (int i = 0; i < 8; i++)
      {
        Formation.RecordStimulus(pulse, noisy, 0, 0);
        Formation.ProcessPendingExtinction(pulse + Settings.TimeWindowPulses + 1);
        pulse += Settings.TimeWindowPulses + 2;
      }

      for (int i = 0; i < 4; i++)
      {
        FeedPair(noisy, us, 200 + i * 10);
        FeedPair(control, us, 500 + i * 10);
      }

      Assert.True(System.Math.Abs(StrengthOf(noisy) - StrengthOf(control)) < 5e-4f,
          $"при S=0 латентное торможение не должно работать: " +
          $"noisy={StrengthOf(noisy)}, control={StrengthOf(control)}");
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
    public void CompoundGateAndLatentInhibitionSettings_RoundTripThroughFile()
    {
      // Компаунд-гейт и параметры латентного торможения — новые рычаги модели; если они
      // не переживут Save → Load, после рестарта движка Kamин снова станет
      // междуиспытательным, а «мусор» перестанет тормозиться.
      Settings.CompetitiveLearningRequiresCompound = false;
      Settings.LatentInhibitionStrength = 0.35f;
      Settings.LatentInhibitionMinFactor = 0.2f;

      var saveResult = Crs.SaveConditionedReflexSettings();
      Assert.True(saveResult.Success, saveResult.ErrorMessage);

      Settings.CompetitiveLearningRequiresCompound = true;
      Settings.LatentInhibitionStrength = 0f;
      Settings.LatentInhibitionMinFactor = 1f;

      typeof(ConditionedReflexesSystem)
          .GetMethod("LoadConditionedReflexSettings",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
          ?.Invoke(Crs, null);

      Assert.False(Settings.CompetitiveLearningRequiresCompound,
          "CompetitiveLearningRequiresCompound=false обязан восстановиться из файла");
      Assert.Equal(0.35f, Settings.LatentInhibitionStrength, 5);
      Assert.Equal(0.2f, Settings.LatentInhibitionMinFactor, 5);
    }

    [Fact]
    public void NewSettings_RoundTripThroughFile_InvariantCulture()
    {
      // Параметры, вынесенные из кода в ConditionedReflexSettings.dat, обязаны переживать
      // цикл Save → Load. Значения подобраны с дробной частью, чтобы поймать рассинхрон
      // культуры: сериализация/парсинг идут через InvariantCulture (десятичный разделитель — точка).
      Settings.InitialStrengthBonus = 0.15f;
      Settings.AuthoritativeStrength = 0.92f;
      Settings.EstablishedStrengthThreshold = 0.77f;
      Settings.ActivationReinforcementFraction = 0.3f;
      Settings.MaxLifetimePulsesCap = 123456;
      Settings.PassiveDecayFallbackPeriodPulses = 250;
      Settings.SensoryDecayPeriodPulses = 50;
      Settings.SensoryStrengthFloor = 0.2f;
      Settings.SensoryHighStrengthThreshold = 0.7f;
      Settings.SensoryHighStrengthDecayRate = 0.99f;
      Settings.SensoryMidStrengthThreshold = 0.35f;

      var saveResult = Crs.SaveConditionedReflexSettings();
      Assert.True(saveResult.Success, saveResult.ErrorMessage);

      // Портим значения, затем реально перезагружаем из файла.
      Settings.InitialStrengthBonus = 0f;
      Settings.AuthoritativeStrength = 0f;
      Settings.EstablishedStrengthThreshold = 0f;
      Settings.ActivationReinforcementFraction = 0f;
      Settings.MaxLifetimePulsesCap = 0;
      Settings.PassiveDecayFallbackPeriodPulses = 0;
      Settings.SensoryDecayPeriodPulses = 0;
      Settings.SensoryStrengthFloor = 0f;
      Settings.SensoryHighStrengthThreshold = 0f;
      Settings.SensoryHighStrengthDecayRate = 0f;
      Settings.SensoryMidStrengthThreshold = 0f;

      typeof(ConditionedReflexesSystem)
          .GetMethod("LoadConditionedReflexSettings",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
          ?.Invoke(Crs, null);

      Assert.Equal(0.15f, Settings.InitialStrengthBonus, 5);
      Assert.Equal(0.92f, Settings.AuthoritativeStrength, 5);
      Assert.Equal(0.77f, Settings.EstablishedStrengthThreshold, 5);
      Assert.Equal(0.3f, Settings.ActivationReinforcementFraction, 5);
      Assert.Equal(123456, Settings.MaxLifetimePulsesCap);
      Assert.Equal(250, Settings.PassiveDecayFallbackPeriodPulses);
      Assert.Equal(50, Settings.SensoryDecayPeriodPulses);
      Assert.Equal(0.2f, Settings.SensoryStrengthFloor, 5);
      Assert.Equal(0.7f, Settings.SensoryHighStrengthThreshold, 5);
      Assert.Equal(0.99f, Settings.SensoryHighStrengthDecayRate, 5);
      Assert.Equal(0.35f, Settings.SensoryMidStrengthThreshold, 5);
    }

    [Fact]
    public void NoisePairs_DoNotWeakenExistingTargetReflex()
    {
      // Пока идут пары с другим стимулом, целевой рефлекс не угасает: он подкреплялся US
      // и не является «CS без подкрепления».
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
          $"целевой рефлекс не должен слабнуть от чужих пар: {before} → {alive.AssociationStrength}");
    }

    /// <summary>
    /// Сценарный профиль «CS-пачка + редкий US» (профиль Scenario_8). Три CS идут подряд
    /// (пачка), затем в окне τ приходит US — подкрепляется последний CS пачки (last-CS-wins).
    /// Предыдущие CS пачки закрываются НОВЫМ CS, а не пролётом окна: они — сенсорные
    /// партнёры (латентная пара CS→CS′), информативны, и латентное торможение на них копиться
    /// НЕ должно. РЕГРЕСС НА БАГ: раньше каждый закрытый пачкой CS копил LI, множитель падал
    /// к полу, и даже регулярно подкрепляемый CS залипал на C≈0.27–0.30 (ниже γ) и никогда не
    /// становился активируемым — условный рефлекс сам себя хоронил.
    /// </summary>
    [Fact]
    public void ScenarioProfile_BurstCs_RareUs_ReinforcedCsReachesActivation()
    {
      ActivateSeedStyle();
      int us = Engine.SeedGeneticReflexId;

      // Пачки по три CS; подкрепляется всегда третий (последний перед US).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cs3 = NewPhraseImage();

      for (int i = 0; i < 8; i++)
      {
        int p = 1 + i * 10;
        Formation.RecordStimulus(p, cs1, 0, 0);       // закроется cs2 (сенсорная пара)
        Formation.RecordStimulus(p + 1, cs2, 0, 0);   // закроется cs3 (сенсорная пара)
        Formation.RecordStimulus(p + 2, cs3, 0, 0);
        Formation.RecordStimulus(p + 3, 0, 0, 0, us); // US в окне τ от cs3
        Formation.CheckTemporalCorrelations(p + 3);
      }

      var reinforced = Crs.GetAllConditionedReflexes().First(r => r.Level3 == cs3);
      Assert.True(reinforced.AssociationStrength >= Settings.ActivationThreshold,
          $"подкрепляемый CS обязан пробить γ за 8 циклов пачки+US: " +
          $"C={reinforced.AssociationStrength}, γ={Settings.ActivationThreshold}");
      Assert.True(reinforced.CanBeActivated(),
          $"подкрепляемый CS обязан стать активируемым: C={reinforced.AssociationStrength}");

      // «Шумовые» CS пачки не подкреплялись — у них не должно появиться УР (last-CS-wins),
      // но и латентное торможение на них не копилось (они закрывались партнёром, не пролётом).
      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs1);
      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs2);
    }

    /// <summary>
    /// Фильтр (b) п.1: CS, предъявленный ПЕРЕД другим CS (партнёром) и потому закрытый
    /// новым CS, а не пролётом окна τ, латентное торможение НЕ копит — он информативен
    /// (предсказывает партнёра). После серии таких «пар» он обучается полным темпом RW,
    /// как контрольный CS, никогда не видевший партнёра.
    /// </summary>
    [Fact]
    public void PairedCs_ClosedByAnotherCs_DoesNotAccumulateLatentInhibition()
    {
      ActivateSeedStyle();
      int us = Engine.SeedGeneticReflexId;
      int paired = NewPhraseImage();   // каждый раз закрывается партнёром partner
      int partner = NewPhraseImage();  // подкрепляется US (last-CS-wins)
      int control = NewPhraseImage();  // никогда не предъявляется с партнёром

      // Серия пар paired→partner→US: подкрепляется partner, paired лишь закрывается
      // новым CS. Старый код записывал бы paired как «CS без US» и копил LI.
      for (int i = 0; i < 8; i++)
      {
        int p = 1 + i * 10;
        Formation.RecordStimulus(p, paired, 0, 0);
        Formation.RecordStimulus(p + 1, partner, 0, 0);
        Formation.RecordStimulus(p + 2, 0, 0, 0, us);
        Formation.CheckTemporalCorrelations(p + 2);
      }

      // Теперь обучаем paired и control раздельными чистыми парами (без блокировки).
      for (int i = 0; i < 6; i++)
      {
        FeedPair(paired, us, 200 + i * 10);
        FeedPair(control, us, 500 + i * 10);
      }

      float cPaired = StrengthOf(paired);
      float cControl = StrengthOf(control);

      Assert.True(System.Math.Abs(cPaired - cControl) < 5e-4f,
          $"CS, закрытый партнёром, не должен копить LI и обязан идти полным RW: " +
          $"paired={cPaired}, control={cControl}");
      Assert.True(cPaired >= Settings.ActivationThreshold,
          $"CS без накопленного LI обязан пробить γ: C={cPaired}");
    }
  }
}