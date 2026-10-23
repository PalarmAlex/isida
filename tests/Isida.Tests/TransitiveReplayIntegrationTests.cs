using System;
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
  /// Интеграция транзитивного реплея цепочки условных рефлексов через штатный контур
  /// <see cref="ReflexesActivator"/>.<c>ProcessActiveChain</c> / <c>ExecuteTransitiveChainLink</c>
  /// (Случай 1 из DEBUG_CASEBOOK_1.md: полностью выученная цепь не проигрывалась в рантайме).
  ///
  /// Поток теста повторяет рантаймовый:
  /// 1. Формируются два УР на разные пусковые образы (общий эталонный UR → общий мотор).
  /// 2. Мотор первого УР связывается со вторым пусковым образом (крепость ≥ γ).
  /// 3. Эмулируется <c>ActiveFromPhrase</c>: <c>ConditionsDetection</c> +
  ///    <c>CollectReflexesForExecution</c> + <c>ExecuteReflexes</c> — после активации УР1
  ///    должна построиться виртуальная цепочка.
  /// 4. Эмулируется конец пульса (<c>ResetStates</c>), затем шагами <c>ProcessActiveChain</c>
  ///    цепочка прогоняется до активации УР2 и завершения.
  /// </summary>
  public class TransitiveReplayIntegrationTests : EngineIntegrationTestBase
  {
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public TransitiveReplayIntegrationTests(EngineFixture engine) : base(engine)
    {
      EnsureActivator();
    }

    /// <summary>
    /// Восстанавливает активатор и сервис выполнения, если предыдущий тест коллекции
    /// disposal-ом обнулил общие синглтоны (так делает
    /// <see cref="CommandTriggerConditionedReflexCollectionTests.Dispose"/>). Без этого
    /// <c>ReflexExecutionService.Instance</c> бросает InvalidOperationException.
    /// Порядок зависимостей — как в <c>IsidaEngine</c> (шаг 14 до 16), мотор→стимул связи
    /// передаются повторно, иначе <c>BuildTransitiveChain</c> нечего строить.
    /// </summary>
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
        ReflexesActivator.Instance.SetMotorStimulusAssociations(
            MotorStimulusAssociationSystem.Instance);
      }
    }

    private static void SetField(object target, string name, object value)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      if (field != null)
        field.SetValue(target, value);
    }

    private static object GetField(object target, string name)
    {
      var field = target.GetType().GetField(name, PrivateInstance);
      return field?.GetValue(target);
    }

    // IsTransitive / TransitiveReflexIds — публичные автосвойства приватного вложенного
    // класса ActiveReflexChain: сам класс приватен, но его члены публичны.
    private const BindingFlags ChainMembers =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static T GetChainField<T>(object chain, string name)
    {
      Assert.NotNull(chain);
      var property = chain.GetType().GetProperty(name, ChainMembers);
      Assert.NotNull(property);
      return (T)property.GetValue(chain);
    }

    /// <summary>Стиль-образ активных стилей (его же строит дерево в OnConditionedReflexCreated).</summary>
    private int SeedStyleImage() =>
        Images.AddBehaviorStyleImage(new List<int> { Engine.SeedStyleId });

    /// <summary>
    /// Эмулирует состояние активатора в момент <c>ActiveFromPhrase</c> для заданного пускового
    /// образа и запускает приватный <c>CollectReflexesForExecution</c>.
    /// Согласовано с <see cref="EngineIntegrationTestBase.AddReflex"/> при
    /// <c>level2 = [SeedStyleId]</c>: Level1=0, стиль-образ = <see cref="SeedStyleImage"/>.
    /// </summary>
    private void EmulatePhraseTrigger(int triggerImageId, int styleImageId)
    {
      var activator = ReflexesActivator.Instance;

      SetField(activator, "_activeCurBaseID", 0);
      SetField(activator, "_activetStyleIds", new List<int> { Engine.SeedStyleId });
      SetField(activator, "_activeCurBaseStyleID", styleImageId);
      SetField(activator, "_activeCurTriggerStimulusID", triggerImageId);
      SetField(activator, "_activeCurReflexTriggerStimulusID", 0);
      SetField(activator, "_activatedPulsCount", Engine.CurrentPulse);
      SetField(activator, "_weitPulceCount", Engine.CurrentPulse);
      // Страховка от «протёкшего» состояния предыдущего теста:
      AppGlobalState.IsReflexChainActive = false;
      AppGlobalState.IsNewConditions = false;

      ReflexTreeSystem.Instance.ConditionsDetection(new[] { 0, styleImageId, triggerImageId });

      typeof(ReflexesActivator)
          .GetMethod("CollectReflexesForExecution", PrivateInstance)
          .Invoke(activator, null);
    }

    private static void InvokePrivate(string name, int pulseCount) =>
        typeof(ReflexesActivator)
            .GetMethod(name, PrivateInstance)
            .Invoke(ReflexesActivator.Instance, new object[] { pulseCount });

    /// <summary>
    /// Воспроизводит конец пульса движка: <c>GlobalTimer.ProcessAgentPulse</c> в <c>finally</c>
    /// сбрасывает флаг новых условий и вызывает <c>ReflexesActivator.ResetStates</c>, обнуляющий
    /// пусковые стимулы и списки «к выполнению». Активная цепочка при этом сохраняется.
    /// </summary>
    private void EndPulse(int pulseCount)
    {
      AppGlobalState.IsNewConditions = false;
      ReflexesActivator.Instance.ResetStates(pulseCount);
    }

    /// <summary>
    /// Прогоняет <paramref name="pulses"/> пульсов как движок: перематывает время и на каждом
    /// вызывает <c>ProcessActiveChain</c> + <c>ResetStates</c>, ничего не проверяя по ходу.
    /// </summary>
    private void AdvanceAndProcess(int pulses)
    {
      for (int i = 0; i < pulses; i++)
      {
        Engine.AdvancePulses(1);
        int pulse = Engine.CurrentPulse;
        InvokePrivate("ProcessActiveChain", pulse);
        EndPulse(pulse);
      }
    }

    /// <summary>
    /// Прогоняет пульсы как движок, пока активная цепочка не погаснет сама. Возвращает
    /// <c>true</c>, если она завершилась в пределах <paramref name="maxPulses"/>, иначе <c>false</c>.
    /// </summary>
    private bool RunPulsesUntilChainStops(int maxPulses)
    {
      for (int i = 0; i < maxPulses; i++)
      {
        Engine.AdvancePulses(1);
        int pulse = Engine.CurrentPulse;
        InvokePrivate("ProcessActiveChain", pulse);
        EndPulse(pulse);
        if (!ReflexesActivator.Instance.IsChainActive)
          return true;
      }
      return false;
    }

    /// <summary>
    /// Прогон звена выполняется только по истечении <c>_reflexActionDuration</c> пульсов
    /// (в фикстуре = <see cref="AdaptiveActionsSystem.ReflexActionDisplayDuration"/> = 2).
    /// Возвращает это значение, чтобы тесты не зависели от конкретной константы.
    /// </summary>
    private static int ReflexActionDuration =>
        AdaptiveActionsSystem.Instance.ReflexActionDisplayDuration;

    /// <summary>
    /// Основной сценарий: фраза запускает УР1, строится транзитивная цепочка,
    /// штатный контур проигрывает её и активирует УР2.
    /// </summary>
    [Fact]
    public void TransitiveChain_ActiveFromPhrase_BuildsChainAndReplays()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      // Пусковые образы фраз A и B.
      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      // УР1 и УР2 на один эталонный UR: мотор общий, поэтому цепочку даёт именно
      // выученная мотор→стимул связь, а не совпадение разных моторов.
      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();

      // Мотор первого УР «предсказывает» пусковой образ второго (крепость ≥ γ).
      float gamma = Settings.MotorAssociationActivationThreshold;
      do
      {
        MotorStimulusAssociationSystem.Instance.StrengthenLink(motor1, triggerB);
      }
      while (!MotorStimulusAssociationSystem.Instance.TryGetStrength(motor1, triggerB, out float c) || c < gamma);

      Assert.Contains(triggerB, MotorStimulusAssociationSystem.Instance.GetActivatableStimuli(motor1));

      // === Пульс активации: фраза A → УР1 → построение цепочки ===
      EmulatePhraseTrigger(triggerA, styleImageId);

      var condToRun = (List<int>)GetField(ReflexesActivator.Instance, "_conditionedReflexesToRun");
      Assert.Contains(reflex1Id, condToRun);

      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.Equal(reflex1Id, (int)GetField(ReflexesActivator.Instance, "_activeConditionReflexID"));
      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "транзитивная цепочка не построена после активации УР1");

      var chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<bool>(chain, "IsTransitive"));
      Assert.Equal(new[] { reflex2Id }, GetChainField<List<int>>(chain, "TransitiveReflexIds"));

      // Конец пульса: ResetStates обнуляет пусковой стимул, цепочка обязана выжить.
      // Именно на этом месте в рантайме она и гасла (Случай 1).
      EndPulse(Engine.CurrentPulse);
      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "цепочка погашена в конце пульса активации");

      // === Прогон: ждём завершения мотора и разворачиваем звено ===
      // Для одного звена: пульс 1 — ExecuteChainLink ставит IsWaitingForResult,
      // пульс 2 — ProcessActiveChain оценивает и останавливает цепочку.
      AdvanceAndProcess(ReflexActionDuration);

      // УР2 должен активироваться в ExecuteChainLink, а ResetStates обнуляет
      // _activeConditionReflexID — проверяем через AppGlobalState.CurrentConditionedReflexID
      Assert.True(AppGlobalState.CurrentConditionedReflexID == reflex2Id,
          $"УР2 не активирован в процессе прогона цепочки (CurrentConditionedReflexID={AppGlobalState.CurrentConditionedReflexID})");

      // === Завершение: оценка не приходила → успех по умолчанию, звеньев больше нет ===
      // Цепочка должна погаснуть сама в пределах нескольких пульсов.
      Assert.True(RunPulsesUntilChainStops(5),
          "цепочка не завершилась после активации УР2");

      Assert.False(ReflexesActivator.Instance.IsChainActive);
    }

    /// <summary>
    /// Регресс Случая 17: <c>ProcessActiveChain</c> в том же пульсе, что и <c>ExecuteReflexes</c>,
    /// гасил только что построенную цепочку — <c>CanContinueChain</c> требовал нулевого пускового
    /// стимула, а обнуляет его только <c>ResetStates</c> в конце пульса.
    /// Дословный симптом в логах: «Построена виртуальная транзитивная цепочка» → «Цепочка рефлексов -1
    /// остановлена» → «завершена» в пределах одной миллисекунды.
    /// </summary>
    [Fact]
    public void TransitiveChain_ProcessActiveChainInActivationPulse_DoesNotStopChain()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();

      float gamma = Settings.MotorAssociationActivationThreshold;
      do
      {
        MotorStimulusAssociationSystem.Instance.StrengthenLink(motor1, triggerB);
      }
      while (!MotorStimulusAssociationSystem.Instance.TryGetStrength(motor1, triggerB, out float c) || c < gamma);

      EmulatePhraseTrigger(triggerA, styleImageId);
      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "цепочка не построена в пульсе активации");

      // Пусковой стимул ещё ненулевой — его снимет только ResetStates в конце пульса.
      Assert.True((int)GetField(ReflexesActivator.Instance, "_activeCurTriggerStimulusID") > 0,
          "ожидался ненулевой пусковой стимул до конца пульса");

      // Тот же пульс, что и в рантайме: ProcessActiveChain вызывается раньше ResetStates.
      InvokePrivate("ProcessActiveChain", Engine.CurrentPulse);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "ProcessActiveChain в пульсе построения погасил транзитивную цепочку (регресс Случая 17)");
    }

    /// <summary>
    /// Контроль: без мотор→стимул связи цепочка не строится, но УР отрабатывает штатно.
    /// </summary>
    [Fact]
    public void TransitiveChain_NoMotorStimulusLink_NoChainBuilt()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();
      Assert.Empty(MotorStimulusAssociationSystem.Instance.GetActivatableStimuli(motor1));

      EmulatePhraseTrigger(triggerA, styleImageId);
      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.Equal(reflex1Id, (int)GetField(ReflexesActivator.Instance, "_activeConditionReflexID"));
      Assert.False(ReflexesActivator.Instance.IsChainActive);
    }

    /// <summary>
    /// Контроль: связь слабее порога γ не запускает прогон (торможение нерелевантных моторов).
    /// </summary>
    [Fact]
    public void TransitiveChain_WeakLink_NoChainBuilt()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();

      // Одно усиление даёт C = α = 0.2 — заведомо ниже γ.
      MotorStimulusAssociationSystem.Instance.StrengthenLink(motor1, triggerB);
      Assert.True(MotorStimulusAssociationSystem.Instance.TryGetStrength(motor1, triggerB, out float weak));
      Assert.True(weak < Settings.MotorAssociationActivationThreshold,
          $"ожидалась связь слабее γ, получена {weak}");

      EmulatePhraseTrigger(triggerA, styleImageId);
      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.Equal(reflex1Id, (int)GetField(ReflexesActivator.Instance, "_activeConditionReflexID"));
      Assert.False(ReflexesActivator.Instance.IsChainActive);
    }
  }
}