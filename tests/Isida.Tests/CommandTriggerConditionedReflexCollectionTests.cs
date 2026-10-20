using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс: триггер только командного канала (команда без actionIds) не активировал у-рефлекс.
  /// Причина — в <c>ReflexesActivator.CollectReflexesForExecution</c> при
  /// <c>_activeCurReflexTriggerStimulusID &gt; 0</c> в список безусловных добавлялся рефлекс
  /// «по умолчанию» (-1) даже при уже отобранных условных. <c>ExecuteReflexes</c> выполнял -1
  /// и выходил, не доходя до условных (ветка <c>_geneticReflexesToRun[0] == -1</c>).
  /// Фраза от этого не страдала: у образа без команды/действия/цвета
  /// <c>ActiveCurReflexTriggerStimulusID == 0</c>, и блок не срабатывал.
  /// </summary>
  public class CommandTriggerConditionedReflexCollectionTests : EngineIntegrationTestBase, System.IDisposable
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public CommandTriggerConditionedReflexCollectionTests(EngineFixture engine) : base(engine) { }

    public void Dispose()
    {
      if (ReflexesActivator.IsInitialized)
        ReflexesActivator.Instance.Dispose();
      if (ReflexExecutionService.IsInitialized)
        ReflexExecutionService.Instance.Dispose();
    }

    /// <summary>Образ «только команда + цвет сцены» (как шаг сценария с sw:N и без кнопок).</summary>
    private int NewCommandImage(int commandId, int color = 3) =>
        NewImage(commands: new[] { commandId }, color: color);

    /// <summary>Поднимает активатор на зависимостях фикстуры (как шаг 16 IsidaEngine).</summary>
    private static void EnsureActivator()
    {
      if (!ReflexExecutionService.IsInitialized)
      {
        ReflexExecutionService.InitializeInstance(
            AdaptiveActionsSystem.Instance,
            InfluenceActionSystem.Instance,
            GeneticReflexesSystem.Instance,
            ConditionedReflexesSystem.Instance);
      }

      if (!ReflexesActivator.IsInitialized)
      {
        ReflexesActivator.InitializeInstance(
            GomeostasSystem.Instance,
            GeneticReflexesSystem.Instance,
            ConditionedReflexesSystem.Instance,
            InfluenceActionSystem.Instance,
            ReflexTreeSystem.Instance,
            ReflexChainsSystem.Instance,
            ReflexExecutionService.Instance,
            AdaptiveActionsSystem.Instance,
            ConditionedReflexFormationService.Instance,
            PerceptionImagesSystem.Instance);
      }
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, PrivateInstance).SetValue(target, value);

    private static object GetField(object target, string name) =>
        target.GetType().GetField(name, PrivateInstance).GetValue(target);

    /// <summary>
    /// Воспроизводит состояние <c>ActiveFromPhrase</c> для пускового образа и запускает
    /// приватный <c>CollectReflexesForExecution</c>.
    /// </summary>
    private (List<int> Conditioned, List<int> Genetic) CollectFor(
        int triggerImageId, int reflexTriggerImageId, int styleImageId)
    {
      EnsureActivator();
      var activator = ReflexesActivator.Instance;

      SetField(activator, "_activeCurBaseID", 0);
      SetField(activator, "_activeCurBaseStyleID", styleImageId);
      SetField(activator, "_activeCurTriggerStimulusID", triggerImageId);
      SetField(activator, "_activeCurReflexTriggerStimulusID", reflexTriggerImageId);
      // Текущие активные стили — из эталонного стиля фикстуры: IsReflexConditionsMet(УР)
      // требует непустой список активных стилей.
      SetField(activator, "_activetStyleIds", new List<int> { Engine.SeedStyleId });

      ReflexTreeSystem.Instance.ConditionsDetection(new[] { 0, styleImageId, triggerImageId });

      typeof(ReflexesActivator)
          .GetMethod("CollectReflexesForExecution", PrivateInstance)
          .Invoke(activator, null);

      return ((List<int>)GetField(activator, "_conditionedReflexesToRun"),
              (List<int>)GetField(activator, "_geneticReflexesToRun"));
    }

    [Fact]
    public void CommandOnlyTrigger_CollectedConditionedReflex_NotMaskedByDefaultGeneticReflex()
    {
      ActivateSeedStyle();
      int styleImageId = Images.AddBehaviorStyleImage(new List<int> { Engine.SeedStyleId });
      int commandId = Engine.NextSeed();
      int cs = NewCommandImage(commandId);
      int reflexId = AddReflex(cs, authoritative: true, level2: new[] { Engine.SeedStyleId });

      var (conditioned, genetic) = CollectFor(cs, cs, styleImageId);

      Assert.Contains(reflexId, conditioned);
      Assert.DoesNotContain(-1, genetic);
    }

    [Fact]
    public void PhraseTrigger_StillCollectsConditionedReflex()
    {
      // Контроль: у фразового образа нет частичного (командного) образа,
      // поэтому -1 не добавлялся и до правки — поведение не должно измениться.
      ActivateSeedStyle();
      int styleImageId = Images.AddBehaviorStyleImage(new List<int> { Engine.SeedStyleId });
      int cs = NewPhraseImage();
      int reflexId = AddReflex(cs, authoritative: true, level2: new[] { Engine.SeedStyleId });

      var (conditioned, genetic) = CollectFor(cs, 0, styleImageId);

      Assert.Contains(reflexId, conditioned);
      Assert.DoesNotContain(-1, genetic);
    }
  }
}
