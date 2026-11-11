using System;
using System.Collections.Generic;
using System.Linq;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессия: удаление триггера-воздействия должно КАСКАДНО вычищать его ID из образов
  /// восприятия (<see cref="PerceptionImagesSystem"/> / InfluenceActionsList) и образов
  /// действий (<see cref="InfluenceActionsImagesSystem"/> / ActIdList), а не блокировать
  /// удаление исключением. Связь отзеркаливания в <see cref="AdaptiveActionsSystem"/>
  /// блокирует удаление по-прежнему.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class InfluenceActionCascadeCleanupTests
  {
    private readonly EngineFixture _engine;

    public InfluenceActionCascadeCleanupTests(EngineFixture engine)
    {
      _engine = engine;
      AppGlobalState.EvolutionStage = 0;

      // Фикстура поднимает InfluenceActionSystem раньше PerceptionImagesSystem и не связывает
      // их (это делает IsidaEngine). Для каскада нужны обе связи — ставим явно.
      InfluenceActionSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);

      if (!InfluenceActionsImagesSystem.IsInitialized)
        InfluenceActionsImagesSystem.InitializeInstance(_engine.DataDir);
      InfluenceActionSystem.Instance.SetInfluenceActionsImagesSystem(InfluenceActionsImagesSystem.Instance);
    }

    [Fact]
    public void RemoveAction_ClearsIdFromPerceptionImages_AndDoesNotThrow()
    {
      int actionId = AddInfluenceAction("Каскад-в-образах");
      int otherActionId = AddInfluenceAction("Сосед-воздействие");

      int imageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { actionId, otherActionId }, new List<int>());
      Assert.True(imageId > 0);

      Assert.True(InfluenceActionSystem.Instance.RemoveAction(actionId));

      var image = PerceptionImagesSystem.Instance.GetPerceptionImageById(imageId);
      Assert.NotNull(image);
      Assert.DoesNotContain(actionId, image.InfluenceActionsList);
      Assert.Contains(otherActionId, image.InfluenceActionsList);

      // Удалённый ID не должен остаться ни в одном образе.
      Assert.All(PerceptionImagesSystem.Instance.GetAllPerceptionImagesList(),
          img => Assert.DoesNotContain(actionId, img.InfluenceActionsList));
    }

    [Fact]
    public void RemoveAction_ClearsIdFromActionsImages_AndDoesNotThrow()
    {
      int actionId = AddInfluenceAction("Каскад-в-образах-действий");
      int otherActionId = AddInfluenceAction("Сосед-воздействие-2");

      var (actionsImageId, _) = InfluenceActionsImagesSystem.Instance.CreateNewInfluenceActionsImage(
          new List<int> { actionId, otherActionId }, false);
      Assert.True(actionsImageId > 0);

      Assert.True(InfluenceActionSystem.Instance.RemoveAction(actionId));

      var actionsImage = InfluenceActionsImagesSystem.Instance.GetInfluenceActionsImage(actionsImageId);
      Assert.NotNull(actionsImage);
      Assert.DoesNotContain(actionId, actionsImage.ActIdList);
      Assert.Contains(otherActionId, actionsImage.ActIdList);
    }

    /// <summary>
    /// Раньше этот сценарий падал с «используется в образах восприятия и не может быть удалено».
    /// Теперь удаление разрешено, а следы в образах вычищаются.
    /// </summary>
    [Fact]
    public void RemoveAction_UsedInImages_RemovesSuccessfully()
    {
      int actionId = AddInfluenceAction("Занятый триггер");

      PerceptionImagesSystem.Instance.AddPerceptionImage(new List<int> { actionId }, new List<int>());
      InfluenceActionsImagesSystem.Instance.CreateNewInfluenceActionsImage(new List<int> { actionId }, false);

      var exception = Record.Exception(() => InfluenceActionSystem.Instance.RemoveAction(actionId));
      Assert.Null(exception);
      Assert.Null(InfluenceActionSystem.Instance.GetInfluenceAction(actionId));
    }

    [Fact]
    public void RemoveAction_UsedForMirroring_StillThrows()
    {
      int actionId = AddInfluenceAction("Зеркальный триггер");
      var (adaptiveId, _) = AdaptiveActionsSystem.Instance.AddAction(
          "Зеркальное действие", "", influenceActionId: actionId);
      Assert.True(adaptiveId > 0);

      try
      {
        Assert.Throws<InvalidOperationException>(
            () => InfluenceActionSystem.Instance.RemoveAction(actionId));
        Assert.NotNull(InfluenceActionSystem.Instance.GetInfluenceAction(actionId));
      }
      finally
      {
        AdaptiveActionsSystem.Instance.RemoveAction(adaptiveId);
      }
    }

    /// <summary>
    /// Воздействие, стоящее триггером в безусловном рефлексе, удалить нельзя: коды триггеров
    /// в рефлексах каскадно НЕ вычищаются (в отличие от образов).
    /// </summary>
    [Fact]
    public void RemoveAction_UsedAsGeneticReflexTrigger_Throws_AndKeepsReflexIntact()
    {
      int actionId = AddInfluenceAction("Триггер б/у рефлекса");
      var (reflexId, _) = GeneticReflexesSystem.Instance.AddGeneticReflex(
          1, new List<int> { _engine.SeedStyleId }, new List<int> { actionId }, null,
          new List<int> { _engine.SeedAdaptiveActionId });
      Assert.True(reflexId > 0);

      try
      {
        Assert.Throws<InvalidOperationException>(
            () => InfluenceActionSystem.Instance.RemoveAction(actionId));

        // Ни воздействие, ни его код в триггере рефлекса не должны пострадать.
        Assert.NotNull(InfluenceActionSystem.Instance.GetInfluenceAction(actionId));
        var reflex = GeneticReflexesSystem.Instance.GetGeneticReflex(reflexId);
        Assert.NotNull(reflex);
        Assert.Contains(actionId, reflex.InfluenceActionIds);
      }
      finally
      {
        GeneticReflexesSystem.Instance.RemoveGeneticReflex(reflexId);
      }

      // После снятия триггера с рефлекса удаление проходит.
      Assert.True(InfluenceActionSystem.Instance.RemoveAction(actionId));
    }

    [Fact]
    public void RemoveAction_ResetsCachedTriggerStimulusImages()
    {
      int actionId = AddInfluenceAction("Триггер стимула");
      PerceptionImagesSystem.Instance.AddPerceptionImage(new List<int> { actionId }, new List<int>());

      InfluenceActionSystem.Instance.ActiveCurTriggerStimulusID = 424242;
      InfluenceActionSystem.Instance.ActiveCurReflexTriggerStimulusID = 424243;

      Assert.True(InfluenceActionSystem.Instance.RemoveAction(actionId));

      // Образ-стимул пересобран, кэш прежних пусковых образов недействителен.
      Assert.Equal(0, InfluenceActionSystem.Instance.ActiveCurTriggerStimulusID);
      Assert.Equal(0, InfluenceActionSystem.Instance.ActiveCurReflexTriggerStimulusID);
    }

    private int AddInfluenceAction(string name) =>
        InfluenceActionSystem.Instance
            .AddInfluenceAction(name, "", new Dictionary<int, int>()).ActionId;
  }
}
