using System.Collections.Generic;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессионные тесты для явных учительских воздействий (поощрить / наказать).
  /// <para>
  /// Если оба ID = 0 (не настроены) — автоматизмы создаются по всем триггерам.<br/>
  /// Если хотя бы один ID &gt; 0 — автоматизм НЕ создаётся по этому триггеру
  /// (путь M: TryCreateMirrorFromInfluenceActions, путь A: CreateAutomatizmByGeneticPurpose).
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class TeacherInfluenceIdTests : EngineIntegrationTestBase
  {
    // ID воздействия-триггера с пульта (то, что кладётся в CurrentStimulusActionIdList / образ стимула).
    private readonly int _teacherInfluenceId;
    private readonly int _normalInfluenceId;
    private readonly int _paramId;
    // Реальный AdaptiveAction, на который конвертируется _normalInfluenceId (для пути M).
    private readonly int _normalAdaptiveId;
    // Реальный AdaptiveAction с recipe-слотом (для клонирования по пути A).
    private readonly int _cloneActionId;
    private readonly MirrorAutomatizmService _mirrorService;
    private readonly AutomatizmSystem _autom;

    public TeacherInfluenceIdTests(EngineFixture engine) : base(engine)
    {
      EnsureAutomatismSystems();
      _autom = AutomatizmSystem.Instance;
      _mirrorService = new MirrorAutomatizmService(_autom);
      EnsurePurposeSystem();

      // Действия создаём на стадии 0 (создание G_AD вне 2-й стадии).
      int stage = AppGlobalState.EvolutionStage;
      AppGlobalState.EvolutionStage = 0;
      try
      {
        var (paramId, _) = GomeostasSystem.Instance.AddParameter(
            "Тест-параметр-учитель-" + Engine.NextSeed(), "", 50f, 50, 50, 1);
        _paramId = paramId;

        // Обычное воздействие: реальное действие с recipe + связь InfluenceActionId (путь M зеркалит его).
        var (normalAdaptive, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Обычное действие", "", targetGomeoParamIdArr: new List<int> { _paramId }, Vigor: 5);
        _normalAdaptiveId = normalAdaptive;
        _normalInfluenceId = Engine.NextSeed();
        AdaptiveActionsSystem.Instance.GetAdaptiveAction(_normalAdaptiveId).InfluenceActionId = _normalInfluenceId;

        // Действие-клон с recipe (мотор клона пути A).
        var (cloneAction, _) = AdaptiveActionsSystem.Instance.AddAction(
            "Действие-клон", "", targetGomeoParamIdArr: new List<int> { _paramId }, Vigor: 5);
        _cloneActionId = cloneAction;
      }
      finally
      {
        AppGlobalState.EvolutionStage = stage;
      }

      // Учительское воздействие — триггер с пульта (действовать будет только как id в списке).
      _teacherInfluenceId = Engine.NextSeed();

      ResetState(0, 0); // по умолчанию оба 0 — всё создаётся
    }

    #region Поднятие подсистем и подготовка

    private void EnsureAutomatismSystems()
    {
      if (!ActionsImagesSystem.IsInitialized)
        ActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!InfluenceActionsImagesSystem.IsInitialized)
        InfluenceActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmTreeSystem.IsInitialized)
        AutomatizmTreeSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmSystem.IsInitialized)
        AutomatizmSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.InitializeInstance(AutomatizmSystem.Instance);
      if (AutomatizmTreeSystem.Instance.Tree.Children.Count == 0)
        AutomatizmTreeSystem.Instance.CreateBasicAutomatizmTree();
    }

    private void EnsurePurposeSystem()
    {
      if (!PurposeGeneticImageSystem.IsInitialized)
      {
        PurposeGeneticImageSystem.InitializeInstance(
            InformationEnvironmentSystem.Instance,
            ActionsImagesSystem.Instance,
            AutomatizmSystem.Instance,
            AdaptiveActionsSystem.Instance);
      }
      PurposeGeneticImageSystem.Instance.SetStage2EchoDependencies(
          _mirrorService,
          VerbalBrocaImagesSystem.IsInitialized ? VerbalBrocaImagesSystem.Instance : null,
          null);
    }

    private void ResetState(int rewardId, int punishId)
    {
      AppGlobalState.EvolutionStage = 2;
      if (AutomatizmSystem.IsInitialized)
        AutomatizmSystem.Instance.DeleteAllAutomatizm();
      if (AutomatismResultTracker.IsInitialized)
        AutomatismResultTracker.Instance.ClearHistory();

      AppGlobalState.AutomatizmNodeId = Engine.NextSeed();
      AppGlobalState.CurrentOverallState = AppGlobalState.HomeostasisState.Normal;
      AppGlobalState.CurActiveVerbalId = Engine.NextSeed(); // > 0, чтобы не сработал путь A через вербальную ветку
      AppGlobalState.FlgConditionReflexes = false;
      AppGlobalState.UpdateGlobalConditionedReflexesActions(new List<int>());
      AppGlobalState.UpdateGlobalGeneticReflexesActions(new List<int>());
      AppGlobalState.UpdateTeachingModeFromStimulusMood(ActionsImagesSystem.TeacherMoodId);
      InformationEnvironmentSystem.Instance.SetVeryActualSituation(false);
      GlobalTimer.Reset();

      AppGlobalState.SetTeacherInfluenceIds(rewardId, punishId);
    }

    #endregion

    #region AppGlobalState — IsTeacherInfluenceId

    [Fact]
    public void IsTeacherInfluenceId_BothZero_ReturnsFalse()
    {
      AppGlobalState.SetTeacherInfluenceIds(0, 0);
      Assert.False(AppGlobalState.IsTeacherInfluenceId(_teacherInfluenceId));
      Assert.False(AppGlobalState.IsTeacherInfluenceId(_normalInfluenceId));
    }

    [Fact]
    public void IsTeacherInfluenceId_RewardSet_ReturnsTrueForReward()
    {
      AppGlobalState.SetTeacherInfluenceIds(_teacherInfluenceId, 0);
      Assert.True(AppGlobalState.IsTeacherInfluenceId(_teacherInfluenceId));
      Assert.False(AppGlobalState.IsTeacherInfluenceId(_normalInfluenceId));
    }

    [Fact]
    public void IsTeacherInfluenceId_PunishSet_ReturnsTrueForPunish()
    {
      AppGlobalState.SetTeacherInfluenceIds(0, _teacherInfluenceId);
      Assert.True(AppGlobalState.IsTeacherInfluenceId(_teacherInfluenceId));
      Assert.False(AppGlobalState.IsTeacherInfluenceId(_normalInfluenceId));
    }

    [Fact]
    public void IsTeacherInfluenceId_BothSet_ReturnsTrueForEither()
    {
      int rewardId = Engine.NextSeed();
      int punishId = Engine.NextSeed();
      AppGlobalState.SetTeacherInfluenceIds(rewardId, punishId);
      Assert.True(AppGlobalState.IsTeacherInfluenceId(rewardId));
      Assert.True(AppGlobalState.IsTeacherInfluenceId(punishId));
      Assert.False(AppGlobalState.IsTeacherInfluenceId(_normalInfluenceId));
    }

    [Fact]
    public void IsTeacherInfluenceId_ZeroId_ReturnsFalse()
    {
      AppGlobalState.SetTeacherInfluenceIds(5, 10);
      Assert.False(AppGlobalState.IsTeacherInfluenceId(0));
    }

    #endregion

    #region Путь M — TryCreateMirrorFromInfluenceActions

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_NoTeacherIds_CreatesAutomatizm()
    {
      AppGlobalState.SetTeacherInfluenceIds(0, 0);

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _normalInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);
      Assert.True(result > 0);
    }

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_TeacherIdSet_ReturnsZero()
    {
      ResetState(_teacherInfluenceId, 0);

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _teacherInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);
      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateMirrorFromInfluenceActions_NormalIdAfterTeacherSet_CreatesAutomatizm()
    {
      ResetState(_teacherInfluenceId, 0);

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _normalInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateMirrorFromInfluenceActions(
          AppGlobalState.AutomatizmNodeId, stimulusImageId);
      Assert.True(result > 0);
    }

    #endregion

    #region Путь M в GetAutomatizmByGeneticPurpose

    [Fact]
    public void GetAutomatizmByGeneticPurpose_NoTeacherIds_PathM_CreatesAutomatizm()
    {
      AppGlobalState.SetTeacherInfluenceIds(0, 0);
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _normalInfluenceId };

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _normalInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();
      Assert.NotNull(atmz);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_TeacherIdSet_PathM_ReturnsNull()
    {
      ResetState(_teacherInfluenceId, 0);
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _teacherInfluenceId };

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _teacherInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();
      Assert.Null(atmz);
    }

    [Fact]
    public void GetAutomatizmByGeneticPurpose_NormalIdAfterTeacherSet_CreatesAutomatizm()
    {
      ResetState(_teacherInfluenceId, 0);
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _normalInfluenceId };

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _normalInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);
      AppGlobalState.CurrentStimulusActionsImageId = stimulusImageId;

      var atmz = PurposeGeneticImageSystem.Instance.GetAutomatizmByGeneticPurpose();
      Assert.NotNull(atmz);
    }

    #endregion

    #region Путь A — CreateAutomatizmByGeneticPurpose

    [Fact]
    public void CreateAutomatizmByGeneticPurpose_NoTeacherIds_CreatesAutomatizm()
    {
      AppGlobalState.SetTeacherInfluenceIds(0, 0);
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _normalInfluenceId };

      // Мотор клона — реальное действие с recipe-слотом.
      var (cloneImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _cloneActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      var image = new PurposeGeneticImageSystem.PurposeGeneticImage
      {
        ActionImage = ActionsImagesSystem.Instance.GetActionsImage(cloneImageId),
        VeryActual = false
      };

      var atmz = PurposeGeneticImageSystem.Instance.CreateAutomatizmByGeneticPurpose(image);
      Assert.NotNull(atmz);
    }

    [Fact]
    public void CreateAutomatizmByGeneticPurpose_TeacherIdSet_ReturnsNull()
    {
      // Тот же мотор с recipe, но текущий триггер с пульта = учительское воздействие.
      ResetState(_teacherInfluenceId, 0);
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _teacherInfluenceId };

      var (cloneImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _cloneActionId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      var image = new PurposeGeneticImageSystem.PurposeGeneticImage
      {
        ActionImage = ActionsImagesSystem.Instance.GetActionsImage(cloneImageId),
        VeryActual = false
      };

      var atmz = PurposeGeneticImageSystem.Instance.CreateAutomatizmByGeneticPurpose(image);
      Assert.Null(atmz);
    }

    #endregion

    #region Стадия 3 — TryCreateInitialParrotAutomatizm (диалоговое зеркало)

    [Fact]
    public void TryCreateInitialParrotAutomatizm_Stage3_TeacherIdSet_ReturnsZero()
    {
      // Стадия 3: диалоговое зеркало создаёт эхо-автоматизм через CreateNewAutomatizm.
      // До бага: по учительскому воздействию эхо создавалось. ResetState ставит стадию 2,
      // поэтому стадию 3 выставляем ПОСЛЕ него.
      ResetState(_teacherInfluenceId, 0);
      AppGlobalState.EvolutionStage = 3;
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _teacherInfluenceId };

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _teacherInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateInitialParrotAutomatizm(
          AppGlobalState.AutomatizmNodeId, stimulusImageId,
          hasVerbalPart: false, hasCommandPart: false, hasNonVerbalPart: true);

      Assert.Equal(0, result);
    }

    [Fact]
    public void TryCreateInitialParrotAutomatizm_Stage3_NormalId_CreatesAutomatizm()
    {
      // Тот же путь, но триггер не учительский — эхо создаётся штатно.
      ResetState(0, 0);
      AppGlobalState.EvolutionStage = 3;
      AppGlobalState.CurrentStimulusActionIdList = new List<int> { _normalInfluenceId };

      var (stimulusImageId, _) = ActionsImagesSystem.Instance.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int> { _normalInfluenceId },
          phraseIdList: null,
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      int result = _mirrorService.TryCreateInitialParrotAutomatizm(
          AppGlobalState.AutomatizmNodeId, stimulusImageId,
          hasVerbalPart: false, hasCommandPart: false, hasNonVerbalPart: true);

      Assert.True(result > 0);
    }

    #endregion
  }
}
