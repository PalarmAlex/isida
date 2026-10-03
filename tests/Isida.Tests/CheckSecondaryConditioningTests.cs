using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Вторичное обусловливание: <c>CheckSecondaryConditioning</c> /
  /// <c>ProcessSecondaryConditionedAssociation</c> (CS₂ → CS₁ → CR₁ ⇒ CR₂ на CS₂).
  /// Это путь бага «CR₂ не активируется на пульсе 90» из Scenario 2 (AIStudio):
  /// тесты фиксируют, что CR₂ создаётся, имеет порядок 2, наследует безусловный источник
  /// и действительно активируется по CS₂ без CS₁.
  /// </summary>
  public class CheckSecondaryConditioningTests : EngineIntegrationTestBase
  {
    public CheckSecondaryConditioningTests(EngineFixture engine) : base(engine) { }

    /// <summary>
    /// Воспроизводит сценарий «CS₂ предшествует CS₁, который активирует CR₁» и возвращает
    /// id CR₂ (0 — не создан). Пары подаются так же, как это делает ReflexesActivator.ActiveFromPhrase:
    /// сначала CheckSecondaryConditioning для подкрепляющего CS₁, затем RecordStimulus.
    /// CS₁ записывается как условный стимул (geneticReflexId = 0) — точное соответствие тому,
    /// что делает активатор при срабатывании у-рефлекса.
    /// </summary>
    private int RunPair(int cs2Image, int cs1Image, int cr1Id, int pulse, bool authoritative = false)
    {
      Formation.RecordStimulus(pulse, cs2Image, 0, 0);           // CS₂ (нейтральный)
      Formation.CheckSecondaryConditioning(
          currentPulse: pulse + 1,
          reinforcingStimulusImageId: cs1Image,
          baseState: 0,
          behaviorStyleImageId: 0,
          activatedConditionedReflexId: cr1Id,
          authoritativeMode: authoritative,
          reinforcingToneId: 0,
          reinforcingMoodId: 0);
      Formation.RecordStimulus(pulse + 1, cs1Image, 0, 0, crFired: true); // CS₁ подтверждён CR₁
      return Crs.GetAllConditionedReflexes()
          .Where(r => r.Level3 == cs2Image && r.SourceConditionedReflexId == cr1Id)
          .Select(r => r.Id)
          .DefaultIfEmpty(0)
          .Single();
    }

    [Fact]
    public void CheckSecondaryConditioning_Cs2ThenCs1_CreatesCr2OfOrder2()
    {
      // Ядро бага Scenario 2: CR₂ обязан появиться с Order = 2 и наследовать UR-источник CR₁.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true); // CR₁ на CS₁

      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10);

      Assert.True(cr2 > 0, "CR₂ не создан");
      var r2 = Find(cr2);
      Assert.Equal(2, r2.Order);
      Assert.Equal(cr1, r2.SourceConditionedReflexId);
      Assert.Equal(Find(cr1).SourceGeneticReflexId, r2.SourceGeneticReflexId);
      Assert.Equal(cs2, r2.Level3);
    }

    [Fact]
    public void CheckSecondaryConditioning_RepeatedPairs_GrowsCr2ToThreshold()
    {
      // 5+ подтверждённых пар CS₂→CS₁ по RW с понижением α/K ⇒ C(CR₂) монотонно растёт
      // и догоняет γ. Пары НЕ авторитарные: CR₂ рождается с C₀ = (C_min+0.1)/K(2) < γ,
      // поэтому рост обеспечивается именно повторными подкреплениями (StrengthenAssociation),
      // а не «авторитарным» стартом C = 0.95/K(2) ≥ γ (иначе тест был бы тавтологией).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true); // родитель обязан быть активируемым

      int cr2 = 0;
      float prev = -1f;
      float c = 0f;
      for (int i = 0; i < 8; i++)
      {
        int pulse = 10 + i * 2;
        cr2 = RunPair(cs2, cs1, cr1, pulse); // authoritative = false
        Assert.True(cr2 > 0, $"CR₂ не создан после пары №{i + 1}");
        c = Find(cr2).AssociationStrength;
        Assert.True(c > prev, $"C(CR₂) обязано расти от пары к паре: {prev} → {c}");
        prev = c;
      }

      Assert.True(c >= Settings.ActivationThreshold,
          $"после 8 подкреплений C(CR₂)={c:F3} должен быть ≥ γ={Settings.ActivationThreshold}");
      Assert.True(Find(cr2).CanBeActivated(), "CR₂ обязан активироваться при C ≥ γ");
    }

    [Fact]
    public void CheckSecondaryConditioning_Cr2WithoutCs1_CanBeActivated()
    {
      // «CR₂ на CS₂ без CS₁ активируется при C ≥ γ» — сценарий проверки с пульта.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);
      // Авторитарная пара ⇒ C(CR₂) = 0.95/K(2) ≥ γ ⇒ CR₂ сразу активируем.
      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10, authoritative: true);
      Assert.True(cr2 > 0);
      Assert.True(Find(cr2).CanBeActivated());

      // Сам предикат активации не зависит от «присутствия CS₁» — только C ≥ γ и TTL:
      // продублировали CS₂ (без CS₁) — CR₂ по-прежнему активируем.
      Formation.RecordStimulus(pulse: 20, stimulusImageId: cs2, baseState: 0, behaviorStyleImageId: 0);
      Assert.True(Find(cr2).CanBeActivated());
    }

    [Fact]
    public void CheckSecondaryConditioning_ThirdOrderParent_NotCreated()
    {
      // Цепочка обрывается на третьем порядке: CR₃ создаётся, CR₄ — нет (parentReflex.Order >= 3 → return).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cs3 = NewPhraseImage();
      int cs4 = NewPhraseImage();

      int cr1 = AddReflex(cs1, authoritative: true);
      // Каждая пара авторитарная: родитель обязан быть активируемым (CanBeActivated),
      // иначе CheckSecondaryConditioning выходит до построения следующего порядка.
      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10, authoritative: true);
      Assert.True(cr2 > 0);
      int cr3 = RunPair(cs3, cs2, cr2, pulse: 20, authoritative: true);
      Assert.True(cr3 > 0);
      Assert.Equal(3, Find(cr3).Order);

      int cr4 = RunPair(cs4, cs3, cr3, pulse: 30, authoritative: true);
      Assert.Equal(0, cr4);
      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs4);
    }

    [Fact]
    public void CheckSecondaryConditioning_PoorRichPair_NotCreated()
    {
      // Пара «бедный CSₐ ⊂ богатый CSᵦ» — зона сенсорной прекондиции, не вторичного CR.
      int action = Engine.NextSeed();
      int phrase = Engine.NextSeed();
      int poor = NewImage(actions: new[] { action });            // только действие
      int rich = NewImage(actions: new[] { action }, phrases: new[] { phrase }); // действие + фраза

      Assert.True(Crs.IsSensoryPreconditioningPair(poor, rich));

      int cr1 = AddReflex(rich, authoritative: true);
      int cr2 = RunPair(poor, rich, cr1, pulse: 10);

      Assert.Equal(0, cr2);
      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == poor);
    }

    [Fact]
    public void IsSensoryPreconditioningPair_WhiteEarlier_IsPair()
    {
      // Цвет — часть предиката «беднее»: белый (White=0) у более раннего образа — wildcard,
      // поэтому пара бедного действия остаётся прекондицией независимо от цвета богатого.
      int action = Engine.NextSeed();
      int poor = NewImage(actions: new[] { action }, color: ISIDA.Reflexes.AgentVisualColor.White);
      int rich = NewImage(actions: new[] { action }, phrases: new[] { Engine.NextSeed() }, color: 3);

      Assert.True(Crs.IsSensoryPreconditioningPair(poor, rich));
    }

    [Fact]
    public void IsSensoryPreconditioningPair_DifferentNonWhiteColors_NotPair()
    {
      // Разные НЕбелые цвета → цвет не подмножество → прекондиции нет (пара не сенсорная).
      int action = Engine.NextSeed();
      int poor = NewImage(actions: new[] { action }, color: 1);
      int rich = NewImage(actions: new[] { action }, phrases: new[] { Engine.NextSeed() }, color: 2);

      Assert.False(Crs.IsSensoryPreconditioningPair(poor, rich));
    }

    [Fact]
    public void IsSensoryPreconditioningPair_SameNonWhiteColor_IsPair()
    {
      // Совпадающий ненулевой цвет + подмножество модальностей → прекондиция есть.
      int action = Engine.NextSeed();
      int poor = NewImage(actions: new[] { action }, color: 3);
      int rich = NewImage(actions: new[] { action }, phrases: new[] { Engine.NextSeed() }, color: 3);

      Assert.True(Crs.IsSensoryPreconditioningPair(poor, rich));
    }

    [Fact]
    public void IsSensoryPreconditioningPair_IdenticalImages_NotPair()
    {
      // Требование строгой бедности: равные образы (не подмножество, а равенство) — не пара.
      int action = Engine.NextSeed();
      int phrase = Engine.NextSeed();
      int a = NewImage(actions: new[] { action }, phrases: new[] { phrase });
      int b = NewImage(actions: new[] { action }, phrases: new[] { phrase });

      Assert.False(Crs.IsSensoryPreconditioningPair(a, b));
    }

    [Fact]
    public void CheckSecondaryConditioning_WeakParent_NotCreated()
    {
      // Родительский CR слаб (C < γ ⇒ CanBeActivated == false) — вторичный не создаётся.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: false); // C = 0.2 < γ
      Assert.False(Find(cr1).CanBeActivated());

      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10);

      Assert.Equal(0, cr2);
    }

    [Fact]
    public void CheckSecondaryConditioning_ParentExpired_NotCreated()
    {
      // TTL родителя истёк — активация невозможна, CR₂ не создаётся
      // (регрессия «оживление» мусорных CR₂ от протухших родителей).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      Settings.InitialLifetimePulses = 100;
      int cr1 = AddReflex(cs1, authoritative: true);
      Assert.True(Find(cr1).CanBeActivated());

      Engine.AdvancePulses(100); // now == ExpiresAt

      // UpdateAgentLifetime() в фикстуре вызывает RemoveExpiredReflexes(): протухший
      // родитель удалён из реестра. CheckSecondaryConditioning обязан тихо выйти
      // (GetConditionedReflexById == null) и не породить CR₂ от «мёртвого» родителя.
      Assert.Null(Find(cr1));
      int cr2 = RunPair(cs2, cs1, cr1, pulse: Engine.CurrentPulse);
      Assert.Equal(0, cr2);
    }

    [Fact]
    public void CheckSecondaryConditioning_PreviousCsOutsideWindow_NotCreated()
    {
      // CS₂ слишком далеко от CS₁ (вне τ) — ассоциация не образуется.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);

      Formation.RecordStimulus(pulse: 1, cs2, 0, 0);
      int pulse = 1 + Settings.TimeWindowPulses + 1;
      Formation.CheckSecondaryConditioning(pulse, cs1, 0, 0, cr1, false, 0, 0);

      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs2);
    }

    [Fact]
    public void CheckSecondaryConditioning_SameStimulus_NotCreated()
    {
      // CS₂ == CS₁ (тот же образ) — вторичный рефлекс не создаётся.
      int cs1 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);

      Formation.RecordStimulus(pulse: 10, cs1, 0, 0);
      Formation.CheckSecondaryConditioning(11, cs1, 0, 0, cr1, false, 0, 0);

      Assert.Equal(1, Crs.GetAllConditionedReflexes().Count(r => r.Level3 == cs1));
    }

    [Fact]
    public void CheckSecondaryConditioning_ExistingCr2_StrengthenedNotDuplicated()
    {
      // Повторная пара усиливает существующий CR₂, а не плодит дубликаты
      // (дубликаты — источник «мусора» и непредсказуемой активации).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);

      int cr2a = RunPair(cs2, cs1, cr1, pulse: 10);
      float c1 = Find(cr2a).AssociationStrength;
      int cr2b = RunPair(cs2, cs1, cr1, pulse: 20);

      Assert.Equal(cr2a, cr2b);
      Assert.Equal(1, Crs.GetAllConditionedReflexes().Count(r => r.Level3 == cs2));
      Assert.True(Find(cr2a).AssociationStrength > c1, "повторная пара обязана усилить CR₂");
    }

    [Fact]
    public void CheckSecondaryConditioning_UnknownParentId_NotCreated()
    {
      // Несуществующий родительский id — тихий выход без исключения и без CR₂.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();

      Formation.RecordStimulus(pulse: 10, cs2, 0, 0);
      Formation.CheckSecondaryConditioning(11, cs1, 0, 0, activatedConditionedReflexId: 999999,
          authoritativeMode: false, reinforcingToneId: 0, reinforcingMoodId: 0);

      Assert.DoesNotContain(Crs.GetAllConditionedReflexes(), r => r.Level3 == cs2);
    }

    [Fact]
    public void CheckSecondaryConditioning_CascadeStrengthen_PropagatesToChild()
    {
      // Усиление родителя каскадно усиливает дочерний CR₂ (CascadeStrengthenChildren).
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);
      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10);
      float before = Find(cr2).AssociationStrength;

      Crs.StrengthenAssociation(cr1);

      Assert.True(Find(cr2).AssociationStrength > before,
          "дочерний CR₂ обязан каскадно усиливаться при усилении CR₁");
    }
    [Fact]

    public void CheckSecondaryConditioning_GetReflexOrder_TracksChain()
    {
      // GetReflexOrder обходит цепочку родителей: 1 → 2 → 3, глубже — −1.
      int cs1 = NewPhraseImage();
      int cs2 = NewPhraseImage();
      int cs3 = NewPhraseImage();
      int cr1 = AddReflex(cs1, authoritative: true);
      int cr2 = RunPair(cs2, cs1, cr1, pulse: 10, authoritative: true);
      int cr3 = RunPair(cs3, cs2, cr2, pulse: 20, authoritative: true);

      Assert.Equal(1, Crs.GetReflexOrder(cr1));
      Assert.Equal(2, Crs.GetReflexOrder(cr2));
      Assert.True(cr3 > 0, "CR₃ не создан");
      Assert.Equal(3, Crs.GetReflexOrder(cr3));
      Assert.Equal(0, Crs.GetReflexOrder(999999)); // несуществующий
    }
  }
}