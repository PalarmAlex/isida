using System.Collections.Generic;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты цепочек у-рефлексов через реплей виртуальных стимулов.
  /// Проверяют минимальный срез: мотор→стимул ассоциация → TryGetNextChainStimulus.
  /// </summary>
  [Collection("EngineIntegration")]
  public class MotorStimulusChainReplayTests : EngineIntegrationTestBase
  {
    /// <summary>Создаёт тестовый экземпляр на общей фикстуре живого движка.</summary>
    /// <param name="engine">Общая фикстура движка.</param>
    public MotorStimulusChainReplayTests(EngineFixture engine) : base(engine) { }

    private static MotorStimulusAssociationSystem MotorAssoc =>
        MotorStimulusAssociationSystem.Instance;

    [Fact]
    public void TryGetNextChainStimulus_NoActivation_ReturnsFalse()
    {
      // Цепочка не активирована — TryGetNextChainStimulus должен вернуть false.
      bool result = Formation.TryGetNextChainStimulus(out int nextStimulusId);
      Assert.False(result);
      Assert.Equal(0, nextStimulusId);
    }

    [Fact]
    public void TryGetNextChainStimulus_AfterActivation_NoMotorLink_ReturnsFalse()
    {
      // У-рефлекс активирован, но мотор→стимул связь не обучена — цепочка не продолжается.
      int c1 = NewPhraseImage();
      int c5 = NewPhraseImage();
      int motor = Engine.NextSeed();

      int ur1 = AddReflex(c1);

      // Имитируем активацию у-рефлекса.
      Formation.SetLastMotorAction(motor, 10);
      Formation.RecordConditionedReflexActivation(ur1, c1);

      // Мотор→стимул связь не обучена — TryGetNextChainStimulus вернёт false.
      Assert.False(MotorAssoc.IsLinkActivatable(motor, c5));
      bool result = Formation.TryGetNextChainStimulus(out int nextStimulusId);
      Assert.False(result);
      Assert.Equal(0, nextStimulusId);
    }

    [Fact]
    public void TryGetNextChainStimulus_AfterActivation_TrainsLink_ReturnsTrue()
    {
      // У-рефлекс активирован, мотор→стимул связь обучена — TryGetNextChainStimulus вернёт CS2.
      int c1 = NewPhraseImage();
      int c5 = NewPhraseImage();
      int motor = Engine.NextSeed();

      int ur1 = AddReflex(c1);

      // Имитируем активацию у-рефлекса.
      Formation.SetLastMotorAction(motor, 10);
      Formation.RecordConditionedReflexActivation(ur1, c1);

      // Обучаем мотор→стимул связь.
      for (int i = 0; i < 10; i++)
        MotorAssoc.StrengthenLink(motor, c5);

      Assert.True(MotorAssoc.IsLinkActivatable(motor, c5),
          "мотор→стимул связь должна достичь порога γ");

      bool result = Formation.TryGetNextChainStimulus(out int nextStimulusId);
      Assert.True(result, "TryGetNextChainStimulus должен вернуть true при обученной связи");
      Assert.Equal(c5, nextStimulusId);
    }

    [Fact]
    public void ChainStep_Increments_OnEachActivation()
    {
      // Каждый вызов RecordConditionedReflexActivation устанавливает _chainStep = 1.
      // TryGetNextChainStimulus инкрементирует _chainStep.
      int c1 = NewPhraseImage();
      int c5 = NewPhraseImage();
      int c6 = NewPhraseImage();
      int motor1 = Engine.NextSeed();
      int motor2 = Engine.NextSeed();

      int ur1 = AddReflex(c1);
      int ur2 = AddReflex(c5);

      // Шаг 1: активация У1.
      Formation.SetLastMotorAction(motor1, 10);
      Formation.RecordConditionedReflexActivation(ur1, c1);
      for (int i = 0; i < 10; i++)
        MotorAssoc.StrengthenLink(motor1, c5);

      bool result1 = Formation.TryGetNextChainStimulus(out int step1Id);
      Assert.True(result1);
      Assert.Equal(c5, step1Id);

      // Шаг 2: активация У2 (имитация).
      Formation.SetLastMotorAction(motor2, 20);
      Formation.RecordConditionedReflexActivation(ur2, c5);
      for (int i = 0; i < 10; i++)
        MotorAssoc.StrengthenLink(motor2, c6);

      bool result2 = Formation.TryGetNextChainStimulus(out int step2Id);
      Assert.True(result2);
      Assert.Equal(c6, step2Id);
    }

    [Fact]
    public void ResetChainState_ClearsChainState()
    {
      // ResetChainState обнуляет состояние цепочки.
      int c1 = NewPhraseImage();
      int motor = Engine.NextSeed();

      int ur1 = AddReflex(c1);
      Formation.SetLastMotorAction(motor, 10);
      Formation.RecordConditionedReflexActivation(ur1, c1);

      // До сброса — TryGetNextChainStimulus может вернуть значение.
      Formation.TryGetNextChainStimulus(out _);

      // После сброса — должен вернуть false.
      Formation.ResetChainState();
      bool result = Formation.TryGetNextChainStimulus(out int nextId);
      Assert.False(result);
      Assert.Equal(0, nextId);
    }

    [Fact]
    public void FullCycle_TrainThenReplay_WorksTwoSteps()
    {
      // Полный цикл: обучение мотор→стимул → реплей виртуального стимула.
      // Эмуляция потока: С4→С1→У1(М1)→С5 (обучение) → С4→С1→У1(М1)→С5→У2(М2)→С6 (обучение)
      // Затем: С4 подан → цепочка должна реплейнуть С5 и С6.

      // Образы: С1 (триггер У1), С5 (мост между У1 и У2), С6 (стимул У2).
      int c1 = NewPhraseImage();
      int c5 = NewPhraseImage();
      int c6 = NewPhraseImage();

      // Моторные действия.
      int motor1 = Engine.NextSeed();
      int motor2 = Engine.NextSeed();

      // У-рефлексы.
      int ur1 = AddReflex(c1);
      int ur2 = AddReflex(c5);

      // === ШАГ 1: Обучение (эмуляция N повторов С4→С1→У1→М1→С5) ===
      // В реальном потоке: RecordStimulus(C1, 0) → ExecuteReflexes(У1, М1) → RecordStimulus(C5, 0)
      // Здесь эмулируем напрямую: SetLastMotorAction + RecordStimulus для усиления связи.
      for (int rep = 0; rep < 10; rep++)
      {
        Formation.SetLastMotorAction(motor1, 10 + rep * 10);
        Formation.RecordStimulus(11 + rep * 10, c5, 0, 0, crFired: true);
      }

      // === ШАГ 2: Обучение (эмуляция повторов С5→С2→У2→М2→С6) ===
      for (int rep = 0; rep < 10; rep++)
      {
        Formation.SetLastMotorAction(motor2, 20 + rep * 10);
        Formation.RecordStimulus(21 + rep * 10, c6, 0, 0, crFired: true);
      }

      // === ШАГ 3: Проверка обученных связей ===
      Assert.True(MotorAssoc.IsLinkActivatable(motor1, c5),
          "мотор1→стимул c5 должна быть обучена (C ≥ γ)");
      Assert.True(MotorAssoc.IsLinkActivatable(motor2, c6),
          "мотор2→стимул c6 должна быть обучена (C ≥ γ)");

      // === ШАГ 4: Реплей — первый шаг (С4/С1 → У1 → реплей С5) ===
      Formation.SetLastMotorAction(motor1, 100);
      Formation.RecordConditionedReflexActivation(ur1, c1);

      bool step1 = Formation.TryGetNextChainStimulus(out int stimulus1);
      Assert.True(step1, "Первый шаг реплея должен вернуть true");
      Assert.True(stimulus1 == c5, "Первый реплей должен вернуть стимул C5");

      // === ШАГ 5: Реплей — второй шаг (С5 → У2 → реплей С6) ===
      Formation.SetLastMotorAction(motor2, 110);
      Formation.RecordConditionedReflexActivation(ur2, c5);

      bool step2 = Formation.TryGetNextChainStimulus(out int stimulus2);
      Assert.True(step2, "Второй шаг реплея должен вернуть true");
      Assert.True(stimulus2 == c6, "Второй реплей должен вернуть стимул C6");

      // === ШАГ 6: Третий шаг — нет связей от мотора2→что-то, цепочка завершается ===
      bool step3 = Formation.TryGetNextChainStimulus(out int stimulus3);
      Assert.False(step3, "Третий шаг должен вернуть false (нет связей для мотора2)");
    }

    [Fact]
    public void TrainChain_MotorOfKnownReflex_DoesNotOverwriteCandidate_BothLinksGrow()
    {
      // Регресс Случая 23: «цепочка не учится поверх прокачанных у-рефлексов».
      // Реальный порядок ActiveFromPhrase в одном пульсе: ExecuteReflexes (SetLastMotorAction)
      // → RecordStimulus. Если на промежуточном CS уже есть прокачанный у-рефлекс, его
      // мотор-реакция приходила в том же пульсе, что и сам CS, и затирала кандидата
      // (мотор предыдущего звена) — из-за гейта currentPulse <= _lastMotorPulse первое
      // звено не усиливалось никогда. Цепь рвалась ровно на «известном» стимуле.
      int c1 = NewPhraseImage();   // триггер У1 (внешний пусковой стимул)
      int c5 = NewPhraseImage();   // триггер У2 — «известный» стимул (на нём есть прокачанный УР)
      int c6 = NewPhraseImage();   // стимул после мотора У2 (третий CS цепочки)
      int motor1 = Engine.NextSeed();
      int motor2 = Engine.NextSeed();

      int ur1 = AddReflex(c1);
      int ur2 = AddReflex(c5);     // прокачанный УР на c5 — его реакция и ломала обучение

      // Повторы полной цепочки: CS₁(+мотор У1) → CS₂(+мотор У2) → CS₃.
      // Раскладка в пределах окна τ: base, base+2, base+4 при τ = 5.
      for (int rep = 0; rep < 8; rep++)
      {
        int base1 = 1 + rep * 10;

        // Предъявление CS₁ и реакция У1 — в одном пульсе (порядок ActiveFromPhrase).
        Formation.SetLastMotorAction(motor1, base1);
        Formation.RecordStimulus(base1, c1, 0, 0, crFired: true);

        // Предъявление «известного» CS₂ и реакция У2 — снова в одном пульсе.
        Formation.SetLastMotorAction(motor2, base1 + 2);
        Formation.RecordStimulus(base1 + 2, c5, 0, 0, crFired: true);

        // Предъявление CS₃ — рефлекса на нём нет, мотор в этом пульсе не фиксируется.
        Formation.RecordStimulus(base1 + 4, c6, 0, 0);
      }

      Assert.True(MotorAssoc.IsLinkActivatable(motor1, c5),
          "звено мотор1→известный CS2 должно усилиться до γ: мотор-реакция У2 не имеет " +
          "затирать кандидата (мотор1) в пульсе предъявления CS2 (регресс Случая 23)");
      Assert.True(MotorAssoc.IsLinkActivatable(motor2, c6),
          "звено мотор2→CS3 должно усилиться до γ: после подкрепления кандидата его " +
          "реакция обязана стать новым кандидатом");

      Assert.True(ur1 > 0 && ur2 > 0);
    }

    [Fact]
    public void SetLastMotorAction_CandidateSamePulseAsTrigger_DoesNotStrengthenSelfLink()
    {
      // Защита от ложной связи «мотор → собственный триггер» сохранена после Случая 23:
      // CS того же пульса, что и SetLastMotorAction, связывать с этим мотором нельзя.
      int c1 = NewPhraseImage();
      int motor1 = Engine.NextSeed();

      Formation.SetLastMotorAction(motor1, 10);
      Formation.RecordStimulus(10, c1, 0, 0, crFired: true);

      Assert.False(MotorAssoc.TryGetStrength(motor1, c1, out _),
          "мотор не должен связываться со своим же триггером в одном пульсе");
    }

    [Fact]
    public void SetLastMotorAction_ConsumedCandidate_IsReplacedNotHeld()
    {
      // Когда кандидат уже подкреплён (потреблён), новый мотор обязан штатно его заменить,
      // а не уходить в удержание — иначе «удержанный» накопился бы вместо свежего кандидата.
      int c1 = NewPhraseImage();
      int c5 = NewPhraseImage();
      int c6 = NewPhraseImage();
      int motor1 = Engine.NextSeed();
      int motor2 = Engine.NextSeed();

      // Кандидат motor1 подкреплён на c5 (потребление).
      Formation.SetLastMotorAction(motor1, 10);
      Formation.RecordStimulus(12, c5, 0, 0, crFired: true);
      Assert.True(MotorAssoc.TryGetStrength(motor1, c5, out _), "связь motor1→c5 обязана появиться");

      // Новый мотор пришёл позже окна τ от потреблённого кандидата — штатная замена.
      Formation.SetLastMotorAction(motor2, 100);
      Formation.RecordStimulus(102, c6, 0, 0);

      Assert.True(MotorAssoc.IsLinkActivatable(motor1, c5) == false ||
                  MotorAssoc.TryGetStrength(motor2, c6, out _),
          "после потребления кандидата новый мотор обязан стать кандидатом, а не удерживаться");
      Assert.True(MotorAssoc.TryGetStrength(motor2, c6, out _),
          "связь motor2→c6 должна появиться: motor2 заменил потреблённого кандидата");

      Assert.Equal(c1, c1);
    }
  }
}
