using System.Collections.Generic;
using System.Linq;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Иерархическая активация у-рефлексов на частичный стимул через SensoryAssociation.
  /// Это путь, по которому «раздражающие» рефлексы срабатывают на неполный CS: полный
  /// образ сформировал рефлекс, бедный CS активирует его через выученную связь CS₁→CS₂.
  /// Гейт SensoryAssociation (C ≥ γ) — ключевая защита от таких срабатываний.
  /// </summary>
  public class ResolveHierarchicalConditionedActivationTests : EngineIntegrationTestBase
  {
    public ResolveHierarchicalConditionedActivationTests(EngineFixture engine) : base(engine) { }

    private List<int> L2 => new List<int> { Engine.SeedStyleId };

    private ConditionedReflexesSystem.CompoundActivationResult Resolve(int stimulus) =>
        Crs.ResolveHierarchicalConditionedActivation(0, L2, stimulus);

    [Fact]
    public void EmptyLevel2_ReturnsEmpty()
    {
      // Гейт: иерархия не работает без контекста реагирования (Level2).
      int rich = NewImage(actions: new[] { Engine.NextSeed() }, phrases: new[] { Engine.NextSeed() });
      AddReflex(rich, level2: L2);

      var result = Crs.ResolveHierarchicalConditionedActivation(0, new List<int>(), rich);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void MissingStimulusImage_ReturnsEmpty()
    {
      int rich = NewImage(actions: new[] { Engine.NextSeed() }, phrases: new[] { Engine.NextSeed() });
      AddReflex(rich, level2: L2);

      var result = Crs.ResolveHierarchicalConditionedActivation(0, L2, 999999);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void ExactMatch_NotHandledByHierarchy()
    {
      // Иерархия обрабатывает только ЧАСТИЧНЫЙ стимул: точное совпадение исключено
      // (его обрабатывает дерево рефлексов в CollectConditionedReflexes).
      int full = NewImage(actions: new[] { Engine.NextSeed() }, phrases: new[] { Engine.NextSeed() });
      AddReflex(full, authoritative: true, level2: L2);

      var result = Resolve(full);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void PartialStimulus_WithoutSensoryLink_NotActivated()
    {
      // Бедный стимул (только фраза), рефлекс на богатый (фраза+действие),
      // SensoryAssociation НЕ создана — иерархия не активирует. Защита от «раздражающих форм».
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: true, level2: L2);

      var result = Resolve(poor);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void PartialStimulus_WithWeakSensoryLink_NotActivated()
    {
      // Связь есть, но C < γ — иерархия не активирует.
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: true, level2: L2);

      Sensory.StrengthenLink(poor, rich); // один раз — C = 0.2 < γ
      Assert.False(Sensory.IsLinkActivatable(poor, rich));

      var result = Resolve(poor);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void PartialStimulus_WithStrongSensoryLink_ActivatesRichReflex()
    {
      // Тот же случай, но SensoryAssociation poor→rich ≥ γ — иерархия активирует богатый рефлекс.
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      int id = AddReflex(rich, authoritative: true, level2: L2);

      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(poor, rich); // C ≥ γ
      Assert.True(Sensory.IsLinkActivatable(poor, rich));

      var result = Resolve(poor);
      Assert.Contains(result.ReflexesToActivate, r => r.Id == id);
    }

    [Fact]
    public void StrongSensoryLink_ButWeakReflex_NotActivated()
    {
      // Гейт SensoryAssociation пройден, но сам рефлекс слаб (C < γ) — не активируется.
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int poor = NewImage(phrases: new[] { phrase });
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: false, level2: L2); // C = 0.2 < γ

      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(poor, rich);

      var result = Resolve(poor);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void UnrelatedStimulus_NotActivated()
    {
      // Стимул с другой фразой (не подмножество) — иерархия не срабатывает даже при связи.
      int phrase = Engine.NextSeed();
      int otherPhrase = Engine.NextSeed();
      int action = Engine.NextSeed();
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      AddReflex(rich, authoritative: true, level2: L2);

      int unrelated = NewImage(phrases: new[] { otherPhrase });
      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(unrelated, rich);

      var result = Resolve(unrelated);
      Assert.Empty(result.ReflexesToActivate);
    }

    [Fact]
    public void CrossChannel_StimulusCommand_TriggerPhrase_ActivatesRichReflex()
    {
      // Регрессия случая 11: стимул через командный канал, триггер через речевой.
      // При строгой проверке подмножества (команды ⊆ [] = false) рефлекс отсекался
      // до гейта сенсорной прекондиции. Добавлена кросс-канальная совместимость:
      // команда-стимул + фраза-триггер считаются совместимыми.
      int cmd = Engine.NextSeed();
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();

      // Триггер рефлекса: фраза + действие (богатый образ)
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      int reflexId = AddReflex(rich, authoritative: true, level2: L2);

      // Стимул: только командный канал (пустая фраза)
      int cmdStimulus = NewImage(commands: new[] { cmd });
      Assert.False(cmdStimulus == rich); // точно разные образы

      // Сенсорная связь cmd → rich ≥ γ
      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(cmdStimulus, rich);
      Assert.True(Sensory.IsLinkActivatable(cmdStimulus, rich));

      var result = Resolve(cmdStimulus);
      Assert.Contains(result.ReflexesToActivate, r => r.Id == reflexId);
    }

    [Fact]
    public void CrossChannel_StimulusPhrase_TriggerCommand_NotActivated_NoLink()
    {
      // Стимул с фразой, триггер с командой: IsPoorStimulusRichReflex вернёт false
      // (phrase ⊄ []), поэтому ветка сенсорной прекондиции не срабатывает.
      // Это ожидаемо — кросс-канальная совместимость введена только для
      // StimulusImagesHierarchyCompatible, а IsPoorStimulusRichReflex игнорирует команду.
      int phrase = Engine.NextSeed();
      int cmd = Engine.NextSeed();
      int action = Engine.NextSeed();

      int rich = NewImage(actions: new[] { action }, commands: new[] { cmd });
      AddReflex(rich, authoritative: true, level2: L2);

      int phraseStimulus = NewImage(phrases: new[] { phrase });
      for (int i = 0; i < 10; i++)
        Sensory.StrengthenLink(phraseStimulus, rich);

      var result = Resolve(phraseStimulus);
      Assert.Empty(result.ReflexesToActivate);
    }
  }
}
