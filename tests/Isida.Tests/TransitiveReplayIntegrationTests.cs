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

      if (!MotorStimulusAssociationSystem.IsInitialized)
      {
        MotorStimulusAssociationSystem.InitializeInstance(
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

      // Всегда обновляем ссылку: предыдущий тест мог обнулить MotorStimulusAssociationSystem
      // через Dispose, а ссылка в активаторе осталась «мёртвой».
      ReflexesActivator.Instance.SetMotorStimulusAssociations(
          MotorStimulusAssociationSystem.Instance);
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

      // Полная очистка «протёкшего» состояния предыдущего теста.
      AppGlobalState.IsReflexChainActive = false;
      AppGlobalState.IsNewConditions = false;
      SetField(activator, "_activeReflexChain", null);
      SetField(activator, "_chainAlreadyActivatedInThisContext", false);
      SetField(activator, "_isChainActive", false);
      SetField(activator, "_chainBaseID", 0);
      SetField(activator, "_chainStyleID", 0);

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
    /// Создаёт активную транзитивную цепочку напрямую, минуя <c>BuildTransitiveChain</c>:
    /// тот строит звенья только по реальным мотор→стимул связям, а для проверки
    /// синхронизации по <c>SetChainStepResult</c> нужна цепочка из двух и более звеньев.
    /// </summary>
    private void SeedActiveTransitiveChain(int seedReflexId, params int[] linkReflexIds)
    {
      var activator = ReflexesActivator.Instance;

      var chainType = typeof(ReflexesActivator)
          .GetNestedType("ActiveReflexChain", BindingFlags.NonPublic);
      Assert.NotNull(chainType);

      object chain = Activator.CreateInstance(chainType, nonPublic: true);
      Assert.NotNull(chain);

      chainType.GetProperty("ChainId", ChainMembers).SetValue(chain, -seedReflexId);
      chainType.GetProperty("CurrentLinkId", ChainMembers).SetValue(chain, 1);
      chainType.GetProperty("StartPulse", ChainMembers).SetValue(chain, Engine.CurrentPulse);
      chainType.GetProperty("LastStepPulse", ChainMembers).SetValue(chain, Engine.CurrentPulse);
      chainType.GetProperty("IsWaitingForResult", ChainMembers).SetValue(chain, false);
      chainType.GetProperty("IsTransitive", ChainMembers).SetValue(chain, true);
      chainType.GetProperty("TransitiveReflexIds", ChainMembers)
          .SetValue(chain, linkReflexIds.ToList());

      SetField(activator, "_activeReflexChain", chain);
      SetField(activator, "_activeChainId", -seedReflexId);
      // Условия «заморожены» теми же значениями, что и в активаторе: иначе CanContinueChain
      // погасит цепочку по смене base/style.
      SetField(activator, "_chainBaseID", (int)GetField(activator, "_activeCurBaseID"));
      SetField(activator, "_chainStyleID", (int)GetField(activator, "_activeCurBaseStyleID"));
      AppGlobalState.IsReflexChainActive = true;
    }

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

      // Стартовый мотор УР1 ещё не подтверждён хостом: цепочка рождается в ожидании,
      // CurrentLinkId = 0 («звенья ещё не запускались»). Иначе первое звено пошло бы на
      // следующем пульсе, пока мотор стартового УР не завершён (баг прогона Scenario_8).
      Assert.True(GetChainField<bool>(chain, "IsWaitingForResult"),
          "сразу после построения цепочка обязана ждать подтверждения стартового мотора");
      Assert.Equal(0, GetChainField<int>(chain, "CurrentLinkId"));
      Assert.True(ReflexesActivator.Instance.IsChainWaitingForResult(),
          "IsChainWaitingForResult должен быть истинен до подтверждения стартового мотора");

      // Конец пульса: ResetStates обнуляет пусковой стимул, цепочка обязана выжить.
      // Именно на этом месте в рантайме она и гасла (Случай 1).
      EndPulse(Engine.CurrentPulse);
      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "цепочка погашена в конце пульса активации");

      // === Хост подтверждает завершение стартового мотора (эмуляция SetChainStepResult из Velum) ===
      // Только после этого подтверждения штатный контур развернёт первое звено.
      ReflexesActivator.Instance.SetChainStepResult(true);

      // === Следующий пульс: переход 0 → 1 и запуск первого звена (активация УР2) ===
      AdvanceAndProcess(1);

      // УР2 должен активироваться в ExecuteTransitiveChainLink, а ResetStates обнуляет
      // _activeConditionReflexID — проверяем через AppGlobalState.CurrentConditionedReflexID
      Assert.True(AppGlobalState.CurrentConditionedReflexID == reflex2Id,
          $"УР2 не активирован в процессе прогона цепочки (CurrentConditionedReflexID={AppGlobalState.CurrentConditionedReflexID})");

      chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.Equal(1, GetChainField<int>(chain, "CurrentLinkId"));
      Assert.True(GetChainField<bool>(chain, "IsWaitingForResult"),
          "после запуска первого звена цепочка снова ждёт оценку хоста");

      // === Завершение: хост подтверждает выполненное первое звено ===
      // Транзитивная цепочка не идёт «по таймеру» — она ждёт оценку от хоста.
      // Оценка, пришедшая в том же пульсе, что запуск звена, не обрабатывается (см.
      // TransitiveChain_SamePulseEvaluation_SkipsImmediateAdvance), поэтому погасание
      // происходит на следующем пульсе — в пределах 1–2 итераций.
      ReflexesActivator.Instance.SetChainStepResult(true);
      Assert.True(RunPulsesUntilChainStops(2),
          "цепочка не завершилась после подтверждения звена хостом");

      Assert.False(ReflexesActivator.Instance.IsChainActive);
    }

    /// <summary>
    /// Регресс бага прогона Scenario_8 (25.10): первое звено цепочки стартовало на следующем
    /// пульсе после построения, не дождавшись подтверждения завершения мотора стартового УР.
    /// Мотор стартового у-рефлекса ещё выполнялся, а звено уже шло на выполнение.
    /// Теперь цепочка рождается в ожидании подтверждения (как и любое звено): без сигнала
    /// хоста первое звено не запускается даже по истечении <c>_reflexActionDuration</c> пульсов.
    /// </summary>
    [Fact]
    public void TransitiveChain_StartMotorNotConfirmed_FirstLinkDoesNotStart()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

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

      var chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.True(GetChainField<bool>(chain, "IsWaitingForResult"),
          "цепочка обязана родиться в ожидании подтверждения стартового мотора");

      EndPulse(Engine.CurrentPulse);

      // Прогоняем столько пульсов, сколько раньше хватило бы для старта звена по таймеру.
      // Без подтверждения стартового мотора хостом первое звено запущено быть не должно.
      AdvanceAndProcess(ReflexActionDuration);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "цепочка не должна гаснуть раньше TransitiveChainStepEvaluationTimeout");
      Assert.True(ReflexesActivator.Instance.IsChainWaitingForResult(),
          "цепочка обязана оставаться в ожидании подтверждения стартового мотора");
      Assert.NotEqual(reflex2Id, AppGlobalState.CurrentConditionedReflexID);

      chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.Equal(0, GetChainField<int>(chain, "CurrentLinkId"));
    }

    /// <summary>
    /// Регресс бага прогона Scenario_8 (25.10, второй): звено транзитивной цепи запускалось
    /// «как мотор», без сверки с контекстом, в котором оно выучено. Мотор брался из исходного
    /// б/у рефлекса и исполнялся через <c>ExecuteConditionedReflex</c> даже если гомеостаз
    /// сменился (метрика отпустила → «Плохо» сменилось на «Хорошо», стиль другой). Сенсорные и
    /// мотор→стимул пары задают карту условий каждого звена — запуск вне этой карты недопустим.
    /// Теперь <c>ExecuteTransitiveChainLink</c> сверяет Level1/Level2 звена с актуальным
    /// гомеостазом и обрывает цепь при несовпадении. Level3 не сверяется (виртуальный стимул).
    /// </summary>
    [Fact]
    public void TransitiveChain_ContextChangedBeforeLink_ChainStopsAndLinkNotExecuted()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      // Оба звена выучены в контексте эталонного стиля (Level2 = [SeedStyleId]).
      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

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
      EndPulse(Engine.CurrentPulse);

      // Хост подтверждает завершение стартового мотора: штатно это разворачивает первое звено.
      ReflexesActivator.Instance.SetChainStepResult(true);

      // Но прежде чем контур развернёт звено, гомеостаз меняет контекст: активируется ДРУГОЙ
      // стиль реагирования (не тот, в котором выучено звено). Воспроизводит прогон Scenario_8.
      var (otherStyleId, _) = GomeostasSystem.Instance.AddBehaviorStyle("Чужой стиль", "");
      var otherStyle = GomeostasSystem.Instance.GetAllBehaviorStyles()[otherStyleId];
      AppGlobalState.UpdateActiveStyles(new[] { otherStyle });

      // Следующий пульс: ExecuteTransitiveChainLink сверяет контекст звена и обязан отказать.
      AdvanceAndProcess(1);

      Assert.False(ReflexesActivator.Instance.IsChainActive,
          "цепь обязана оборваться при смене контекста гомеостаза до запуска звена");
      Assert.NotEqual(reflex2Id, AppGlobalState.CurrentConditionedReflexID);
    }

    /// <summary>
    /// Контроль к предыдущему тесту: если контекст НЕ менялся, звено штатно разворачивается
    /// и УР2 активируется — фикс не должен тормозить корректный прогон.
    /// </summary>
    [Fact]
    public void TransitiveChain_ContextUnchanged_LinkExecutes()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });

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
      EndPulse(Engine.CurrentPulse);

      // Контекст (эталонный стиль) не меняем — только подтверждаем стартовый мотор.
      ReflexesActivator.Instance.SetChainStepResult(true);
      AdvanceAndProcess(1);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "при неизменном контексте цепь обязана остаться активной");
      Assert.Equal(reflex2Id, AppGlobalState.CurrentConditionedReflexID);
    }

    /// <summary>
    /// Регресс Случая 22: транзитивная цепь, где звено выучено в ДРУГОМ контексте (стиле),
    /// чем стартовый рефлекс. До фикса `CanContinueChain` (проверка `_chainBaseID/_chainStyleID`
    /// без учёта `IsTransitive`) цепь мерла на любой смене контекста, не допуская второго звена.
    /// Теперь `CanContinueChain` пропускает базовую проверку base/style для транзитивных цепей —
    /// контроль контекста передан на уровень звена (<see cref="IsChainLinkContextMet"/>), и звено
    /// запускается, если его контекст совпадает с текущим гомеостазом.
    /// </summary>
    [Fact]
    public void TransitiveChain_LinksInDifferentContexts_SecondLinkExecutes()
    {
      // === Шаг 1: создаём стиль B и выучиваем reflex2 в нём ===
      ActivateSeedStyle();
      int styleImageIdA = SeedStyleImage();

      // Добавляем стиль B и активируем его
      var (styleBId, _) = GomeostasSystem.Instance.AddBehaviorStyle("Стиль B", "");
      var styleB = GomeostasSystem.Instance.GetAllBehaviorStyles()[styleBId];
      AppGlobalState.UpdateActiveStyles(new[] { styleB });

      // Стиль B должен стать активным образом стилей
      int styleImageIdB = Images.AddBehaviorStyleImage(new List<int> { styleBId });

      // Пусковые образы
      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();

      // reflex1 выучен в стиле A, reflex2 — в стиле B
      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { styleBId });

      // Мотор reflex1 связывается со стимулом triggerB
      int motor1 = ReflexExecutionService.Instance
          .GetActionsForGeneticReflex(Find(reflex1Id).SourceGeneticReflexId).First();

      float gamma = Settings.MotorAssociationActivationThreshold;
      do
      {
        MotorStimulusAssociationSystem.Instance.StrengthenLink(motor1, triggerB);
      }
      while (!MotorStimulusAssociationSystem.Instance.TryGetStrength(motor1, triggerB, out float c) || c < gamma);

      // === Шаг 2: переключаемся на стиль A для активации reflex1 ===
      ActivateSeedStyle();

      // Эмуляция фразы A → reflex1 → построение цепи
      EmulatePhraseTrigger(triggerA, styleImageIdA);
      InvokePrivate("ExecuteReflexes", Engine.CurrentPulse);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "транзитивная цепочка не построена после активации УР1");

      var chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<bool>(chain, "IsTransitive"));
      Assert.Equal(new[] { reflex2Id }, GetChainField<List<int>>(chain, "TransitiveReflexIds"));

      EndPulse(Engine.CurrentPulse);

      // === Шаг 3: переключаем контекст на стиль B ===
      AppGlobalState.UpdateActiveStyles(new[] { styleB });

      // Эмуляция UpdateCurrentStates реального пульса: активатор пересчитывает замороженный
      // образ активных стилей (в рантайме — из GomeostasSystem.ActiveBehaviorStyleImageId).
      // Именно это расхождение _chainStyleID (стиль A) != _activeCurBaseStyleID (стиль B)
      // на пульсе 139 прогона Scenario_8 и гасило цепь до запуска второго звена.
      SetField(ReflexesActivator.Instance, "_activeCurBaseStyleID", styleImageIdB);

      // Подтверждение стартового мотора → цепь должна перейти к звену 1
      ReflexesActivator.Instance.SetChainStepResult(true);

      // Прогон пульса: ExecuteTransitiveChainLink сверяет контекст reflex2 (Level2=[BId])
      // с текущим гомеостазом (стиль B) — должно совпасть, звено запускается
      AdvanceAndProcess(1);

      Assert.True(ReflexesActivator.Instance.IsChainActive,
          "цепь не должна быть остановлена при совпадении контекста звена с текущим");
      Assert.True(AppGlobalState.CurrentConditionedReflexID == reflex2Id,
          $"звено 2 (УР2) должно активироваться при совпадении контекста " +
          $"(CurrentConditionedReflexID={AppGlobalState.CurrentConditionedReflexID})");
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

    /// <summary>
    /// Оценка из хоста (Velum) через SetChainStepResult: цепочка переходит к следующему звену
    /// немедленно, не дожидаясь _reflexActionDuration пульсов.
    /// </summary>
    [Fact]
    public void TransitiveChain_SetChainStepResult_AdvancesImmediately()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();
      int triggerC = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });
      int reflex3Id = AddReflex(triggerC, level2: new[] { Engine.SeedStyleId });

      // Цепочка из двух звеньев: УР2 → УР3. Первое звено (УР1) уже отработало.
      EmulatePhraseTrigger(triggerA, styleImageId);
      SeedActiveTransitiveChain(reflex1Id, reflex2Id, reflex3Id);

      var chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<bool>(chain, "IsTransitive"), "не транзитивная цепочка");
      Assert.True(GetChainField<List<int>>(chain, "TransitiveReflexIds").Count == 2,
          "ожидается 2 звена в цепочке");

      EndPulse(Engine.CurrentPulse);

      // === Прогон первого звена: нужно дождаться _reflexActionDuration пульсов ===
      AdvanceAndProcess(ReflexActionDuration);

      Assert.True(ReflexesActivator.Instance.IsChainWaitingForResult(),
          "цепочка должна ждать результат после запуска звена");

      // === Эмуляция ответа хоста: SetChainStepResult(true) ===
      // Оценка приходит в том же пульсе, что и запуск звена, поэтому переход
      // произойдёт только на следующем пульсе.
      ReflexesActivator.Instance.SetChainStepResult(true);

      // === Следующий пульс: немедленный переход ко второму звену ===
      Engine.AdvancePulses(1);
      InvokePrivate("ProcessActiveChain", Engine.CurrentPulse);

      chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<int>(chain, "CurrentLinkId") == 2,
          "ожидается переход ко второму звену (CurrentLinkId=2)");

      // === Подтверждаем второе звено — цепочка завершается ===
      ReflexesActivator.Instance.SetChainStepResult(true);
      Engine.AdvancePulses(1);
      InvokePrivate("ProcessActiveChain", Engine.CurrentPulse);

      Assert.False(ReflexesActivator.Instance.IsChainActive,
          "цепочка должна завершиться после подтверждения последнего звена");
    }

    /// <summary>
    /// Оценка не пришла за TransitiveChainStepEvaluationTimeout — цепочка завершается.
    /// </summary>
    [Fact]
    public void TransitiveChain_EvaluationTimeout_ChainStops()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();
      int triggerC = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });
      int reflex3Id = AddReflex(triggerC, level2: new[] { Engine.SeedStyleId });

      EmulatePhraseTrigger(triggerA, styleImageId);
      SeedActiveTransitiveChain(reflex1Id, reflex2Id, reflex3Id);
      EndPulse(Engine.CurrentPulse);

      // Прогон первого звена — теперь цепочка ждёт оценку
      AdvanceAndProcess(ReflexActionDuration);
      Assert.True(ReflexesActivator.Instance.IsChainWaitingForResult());

      int originalTimeout = Settings.TransitiveChainStepEvaluationTimeout;
      Settings.TransitiveChainStepEvaluationTimeout = 2;

      try
      {
        // Оценка не приходит — цепочка обязана погаснуть по таймауту.
        Assert.True(RunPulsesUntilChainStops(10),
            "цепочка должна завершиться по таймауту ожидания оценки");
        Assert.False(ReflexesActivator.Instance.IsChainActive);
      }
      finally
      {
        Settings.TransitiveChainStepEvaluationTimeout = originalTimeout;
      }
    }

    /// <summary>
    /// Оценка пришла в том же пульсе, что и запуск звена — немедленный переход блокируется.
    /// Цепочка переходит к следующему звену только на следующий пульс.
    /// </summary>
    [Fact]
    public void TransitiveChain_SamePulseEvaluation_SkipsImmediateAdvance()
    {
      ActivateSeedStyle();
      int styleImageId = SeedStyleImage();

      int triggerA = NewPhraseImage();
      int triggerB = NewPhraseImage();
      int triggerC = NewPhraseImage();

      int reflex1Id = AddReflex(triggerA, level2: new[] { Engine.SeedStyleId });
      int reflex2Id = AddReflex(triggerB, level2: new[] { Engine.SeedStyleId });
      int reflex3Id = AddReflex(triggerC, level2: new[] { Engine.SeedStyleId });

      EmulatePhraseTrigger(triggerA, styleImageId);
      SeedActiveTransitiveChain(reflex1Id, reflex2Id, reflex3Id);
      EndPulse(Engine.CurrentPulse);

      // === Прогон первого звена ===
      AdvanceAndProcess(ReflexActionDuration);

      Assert.True(ReflexesActivator.Instance.IsChainWaitingForResult());
      int lastStepPulse = GetChainField<int>(GetField(ReflexesActivator.Instance, "_activeReflexChain"), "LastStepPulse");
      Assert.Equal(Engine.CurrentPulse, lastStepPulse);

      // Оценка приходит в том же пульсе, что и запуск звена.
      // Это же моделирует синхронный рецепт адаптера (RunCommand/вызов метода): он
      // вызывает SetChainStepResult ещё внутри ExecuteConditionedReflex, и оценка
      // не должна теряться при выставлении состояния ожидания.
      var waitingChain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.Null(GetChainField<bool?>(waitingChain, "LastEvaluation"));

      ReflexesActivator.Instance.SetChainStepResult(true);

      Assert.True(GetChainField<bool?>(waitingChain, "LastEvaluation") == true,
          "оценка синхронного рецепта потеряна при запуске звена");

      // Тот же пульс: перехода быть не должно
      InvokePrivate("ProcessActiveChain", Engine.CurrentPulse);

      var chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<int>(chain, "CurrentLinkId") == 1,
          "в том же пульсе цепочка не должна переходить к следующему звену");
      Assert.True(GetChainField<bool>(chain, "IsWaitingForResult"),
          "цепочка должна остаться в ожидании");

      // === Следующий пульс: переход произойдёт ===
      Engine.AdvancePulses(1);
      InvokePrivate("ProcessActiveChain", Engine.CurrentPulse);

      chain = GetField(ReflexesActivator.Instance, "_activeReflexChain");
      Assert.NotNull(chain);
      Assert.True(GetChainField<int>(chain, "CurrentLinkId") == 2,
          "на следующий пульс цепочка должна перейти к следующему звену");
    }
  }
}