using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Common;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Общая база интеграционных тестов на живом движке.
  /// Конструктор вызывает <see cref="EngineFixture.ResetToCleanState"/>, поэтому каждый тест
  /// стартует с пустого набора УР, сенсорных связей и образов — тесты не зависят от порядка.
  /// Все времена в тестах берутся из детерминированного счётчика <see cref="EngineFixture.CurrentPulse"/>.
  /// </summary>
  [Collection("EngineIntegration")]
  public abstract class EngineIntegrationTestBase
  {
    protected EngineIntegrationTestBase(EngineFixture engine)
    {
      Engine = engine;
      Engine.ResetToCleanState();
    }

    protected EngineFixture Engine { get; }

    protected static ConditionedReflexesSystem Crs => ConditionedReflexesSystem.Instance;
    protected static PerceptionImagesSystem Images => PerceptionImagesSystem.Instance;
    protected static SensoryAssociationSystem Sensory => SensoryAssociationSystem.Instance;
    protected static ConditionedReflexFormationService Formation => ConditionedReflexFormationService.Instance;

    protected ConditionedReflexesSystem.ConditionedReflexSettings Settings => Crs.Settings;

    /// <summary>
    /// Level2 («контексты реагирования»), используемый в тестах по умолчанию.
    /// Держим его пустым: ProcessConditionedAssociation сравнивает Level2 с
    /// AppGlobalState.ActiveStyles (пусто после ResetToCleanState), а пустые списки
    /// совпадают всегда. Непустой Level2 требовал бы подделки активных стилей гомеостаза.
    /// </summary>
    protected static readonly int[] DefaultLevel2 = new int[0];

    /// <summary>Создаёт образ восприятия по списку ID (детерминированный seed из фикстуры).</summary>
    protected int NewImage(int[] actions = null, int[] phrases = null, int color = 0)
    {
      int id = Images.AddPerceptionImage(
          (actions ?? new int[0]).ToList(),
          (phrases ?? new int[0]).ToList(),
          color);
      Assert.True(id > 0, "не удалось создать образ восприятия");
      return id;
    }

    /// <summary>Создаёт образ с одним действием-«кнопкой» (уникальный id).</summary>
    protected int NewActionImage() => NewImage(actions: new[] { Engine.NextSeed() });

    /// <summary>Создаёт образ с одной фразой (уникальный id).</summary>
    protected int NewPhraseImage() => NewImage(phrases: new[] { Engine.NextSeed() });

    /// <summary>Рефлекс по ID из живого реестра (null, если удалён).</summary>
    protected static ConditionedReflexesSystem.ConditionedReflex Find(int reflexId) =>
        Crs.GetAllConditionedReflexes().FirstOrDefault(r => r.Id == reflexId);

    /// <summary>
    /// Добавляет УР на указанный пусковой образ (по умолчанию «авторитарный», C = 0.95).
    /// Level2 по умолчанию — <see cref="DefaultLevel2"/> (пусто, см. комментарий к полю).
    /// </summary>
    protected static int AddReflex(
        int trigger,
        bool authoritative = true,
        int level1 = 0,
        int sourceGeneticReflexId = 1,
        int toneId = 0,
        int moodId = 0,
        int sourceConditionedReflexId = 0,
        IEnumerable<int> level2 = null)
    {
      var (id, warnings) = Crs.AddConditionedReflex(
          level1,
          (level2 ?? DefaultLevel2).ToList(),
          trigger,
          sourceGeneticReflexId,
          authoritative,
          toneId,
          moodId,
          sourceConditionedReflexId);
      Assert.True(id > 0, "AddConditionedReflex вернул 0: " + string.Join("; ", warnings ?? new string[0]));
      return id;
    }

    /// <summary>
    /// Крепость после n шагов RW-усиления: Cₙ = β − (β − C₀)·(1 − α_eff)ⁿ,
    /// α_eff = α / K(order) — как в <c>StrengthenReflexInternal</c>.
    /// </summary>
    protected float ExpectedRwStrength(float c0, int steps, int order = 1, float beta = 1f)
    {
      float alphaEff = Settings.LearningRate / Crs.GetReductionCoefficientForOrder(order);
      float c = c0;
      for (int i = 0; i < steps; i++)
        c = c + alphaEff * (beta - c);
      return c;
    }
  }
}
