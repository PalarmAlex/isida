using System.Collections.Generic;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Интеграция транзитивного обучения: боевой поток <see cref="ConditionedReflexFormationService"/>
  /// → <see cref="SensoryAssociationSystem"/>. Проверяет, что нейтральные CS-пары формируют цепь
  /// через <see cref="ConditionedReflexFormationService.RecordStimulus"/>, US между CS блокирует
  /// усиление звена, транзитивные настройки переживают цикл Save/Load, а иерархическая активация
  /// проходит по разномодальной цепи «командный стимул → речевой триггер».
  /// </summary>
  public class TransitiveLearningIntegrationTests : EngineIntegrationTestBase
  {
    public TransitiveLearningIntegrationTests(EngineFixture engine) : base(engine) { }

    /// <summary>RW-усиление звена A→B n раз напрямую (в обход сервиса формирования).</summary>
    private void Strengthen(int a, int b, int n)
    {
      for (int i = 0; i < n; i++)
        Sensory.StrengthenLink(a, b);
    }

    [Fact]
    public void RecordStimulus_NeutralPairs_FormTransitiveChain()
    {
      // Нейтральные CS-пары A→B и B→C, поданные через RecordStimulus в окне τ, формируют
      // звенья (TryStrengthenNeutralSequentialLink) и дают цепь A→B→C.
      // Регресс: «пары через RecordStimulus не формируют сенсорные звенья».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int c = NewPhraseImage();

      // A→B: пары разнесены на 10 пульсов, чтобы между парами звено не формировалось (шаг в паре 1 ≤ τ).
      for (int i = 0; i < 8; i++)
      {
        int p = 1 + i * 10;
        Formation.RecordStimulus(p, a, 0, 0, crFired: true);
        Formation.RecordStimulus(p + 1, b, 0, 0, crFired: true);
      }

      // B→C: новый временной интервал, тот же шаг в паре.
      for (int i = 0; i < 8; i++)
      {
        int p = 200 + i * 10;
        Formation.RecordStimulus(p, b, 0, 0, crFired: true);
        Formation.RecordStimulus(p + 1, c, 0, 0, crFired: true);
      }

      Assert.True(Sensory.TryGetStrength(a, b, out _), "пара A→B должна сформировать звено");
      Assert.True(Sensory.TryGetStrength(b, c, out _), "пара B→C должна сформировать звено");
      Assert.True(Sensory.TryGetChainStrength(a, c, out _, out int hops),
          "цепь A→B→C должна быть найдена после CS-пар A→B и B→C");
      Assert.Equal(2, hops);
    }

    [Fact]
    public void RecordStimulus_UsBetweenCs_BreaksChain()
    {
      // US между CS₁ и CS₂ блокирует TryStrengthenNeutralSequentialLink:
      // _lastUnconditionedStimulus.Pulse ∈ (prevPulse, currentPulse) → звено не усиливается.
      // Регресс: «звено формируется сквозь подкрепление».
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      int us = Engine.SeedGeneticReflexId;

      Formation.RecordStimulus(1, a, 0, 0);        // CS₁ = A
      Formation.RecordStimulus(2, 0, 0, 0, us);    // US между A и B
      Formation.RecordStimulus(3, b, 0, 0);        // CS₂ = B — звено должно быть заблокировано

      Assert.False(Sensory.TryGetStrength(a, b, out _),
          "US между CS должен заблокировать формирование звена A→B");
    }

    [Fact]
    public void RoundTrip_TransitiveSettings_SurviveFileReload()
    {
      // Все четыре транзитивные настройки обязаны переживать Save → LoadConditionedReflexSettings.
      // Регресс: «настройки транзитивного обучения не сохраняются в файл».
      Settings.EnableTransitiveLearning = false;
      Settings.TransitiveMaxDepth = 2;
      Settings.TransitiveDecayPerHop = 0.7f;
      Settings.TransitiveGammaCoefficient = 2.0f;

      var saveResult = Crs.SaveConditionedReflexSettings();
      Assert.True(saveResult.Success, saveResult.ErrorMessage);

      // Портим значения в памяти, затем реально перезагружаем из файла приватным методом.
      Settings.EnableTransitiveLearning = true;
      Settings.TransitiveMaxDepth = 99;
      Settings.TransitiveDecayPerHop = 0f;
      Settings.TransitiveGammaCoefficient = 0f;

      typeof(ConditionedReflexesSystem)
          .GetMethod("LoadConditionedReflexSettings",
              System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
          ?.Invoke(Crs, null);

      Assert.False(Settings.EnableTransitiveLearning,
          "EnableTransitiveLearning=false обязан восстановиться из файла");
      Assert.Equal(2, Settings.TransitiveMaxDepth);
      Assert.Equal(0.7f, Settings.TransitiveDecayPerHop, 5);
      Assert.Equal(2.0f, Settings.TransitiveGammaCoefficient, 5);
    }

    [Fact]
    public void CrossChannel_TransitiveChain_ActivatesReflex()
    {
      // Командный стимул A активирует рефлекс на богатом C (действие+фраза) через
      // разномодальную цепь A→B→C; прямого звена A→C нет.
      // Регресс: «транзитивная цепь не работает через разномодальные образы».
      int cmd = Engine.NextSeed();
      int phrase = Engine.NextSeed();
      int action = Engine.NextSeed();

      int a = NewImage(commands: new[] { cmd });                                  // командный канал
      int b = NewImage(phrases: new[] { phrase });                                // речевой канал
      int c = NewImage(actions: new[] { action }, phrases: new[] { phrase });     // богатый триггер
      var l2 = new List<int> { Engine.SeedStyleId };
      int id = AddReflex(c, authoritative: true, level2: l2);

      Settings.TransitiveGammaCoefficient = 1.0f;
      Settings.TransitiveDecayPerHop = 1.0f;
      Strengthen(a, b, 10);
      Strengthen(b, c, 10);

      Assert.False(Sensory.TryGetStrength(a, c, out _), "прямой связи A→C быть не должно");
      Assert.True(Sensory.IsChainActivatable(a, c), "цепь A→B→C должна быть активируема");

      var result = Crs.ResolveHierarchicalConditionedActivation(0, l2, a);
      Assert.Contains(result.ReflexesToActivate, r => r.Id == id);
    }
  }
}
