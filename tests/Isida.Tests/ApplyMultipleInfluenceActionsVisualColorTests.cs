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
  /// Проверка зрительного канала образов, создаваемых
  /// <see cref="InfluenceActionSystem.ApplyMultipleInfluenceActions"/>.
  ///
  /// На каждый стимул создаются образы восприятия:
  /// - CS-образ (<see cref="InfluenceActionSystem.ActiveCurTriggerStimulusID"/>) — с текущим
  ///   <c>visualColorId</c>: цвет различает «деталь / сборка / чертёж» в условном рефлексе;
  /// - б/у-триггер (<see cref="InfluenceActionSystem.ActiveCurReflexTriggerStimulusID"/>) — с тем же
  ///   <c>visualColorId</c>. Б/у-рефлекс теперь несёт собственный код зрительного канала
  ///   (GeneticReflex.VisualColorId), и при привязке узла дерева триггерный образ заводится тем же
  ///   цветом, поэтому в ReflexTree.dat на уровне триггера стоит ID цветного образа нужного типа
  ///   документа и матчинг по ID образа сходится. При одинаковом наборе EA+Command+цвет CS-образ и
  ///   б/у-триггер схлопываются в ОДИН образ (дедупликация), дубля нет.
  ///
  /// Исторически (случай 32) б/у-триггер был жёстко белым, потому что б/у-рефлексы не имели своего
  /// цвета и узел дерева ссылался на белый образ. После добавления GeneticReflex.VisualColorId это
  /// ограничение снято: цвет б/у-триггера = цвет рефлекса.
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class ApplyMultipleInfluenceActionsVisualColorTests
  {
    private readonly EngineFixture _engine;

    public ApplyMultipleInfluenceActionsVisualColorTests(EngineFixture engine)
    {
      _engine = engine;
    }

    /// <summary>
    /// При не-белом <c>visualColorId</c> CS-образ и б/у-триггер получают ОДИН И ТОТ ЖЕ цвет.
    /// Поскольку набор EA+Command+цвет совпадает, оба схлопываются в один образ восприятия
    /// (дедупликация <c>CreatePerceptionImage</c>), дубля нет.
    /// </summary>
    [Fact]
    public void ApplyMultipleInfluenceActions_WithCommandPattern_CsAndReflexTriggerBothColored()
    {
      int actionId = PrepareOperatorAction();
      int commandPatternId = _engine.NextSeed();
      int visualColor = AgentVisualColor.Black; // не белый цвет

      var (success, errorMsg) = ApplyStimulus(actionId, commandPatternId, visualColor);
      Assert.True(success, "ApplyMultipleInfluenceActions failed: " + errorMsg);

      var csImage = PerceptionImagesSystem.Instance
          .GetPerceptionImageById(InfluenceActionSystem.Instance.ActiveCurTriggerStimulusID);
      var reflexImage = PerceptionImagesSystem.Instance
          .GetPerceptionImageById(InfluenceActionSystem.Instance.ActiveCurReflexTriggerStimulusID);

      Assert.NotNull(csImage);
      Assert.NotNull(reflexImage);

      // Оба образа наследуют цвет сцены: б/у-триггер больше не жёстко белый.
      Assert.Equal(visualColor, csImage.VisualColorId);
      Assert.Equal(visualColor, reflexImage.VisualColorId);

      // Одинаковый набор EA+Command+цвет → один и тот же образ (дедупликация).
      Assert.Equal(
          InfluenceActionSystem.Instance.ActiveCurTriggerStimulusID,
          InfluenceActionSystem.Instance.ActiveCurReflexTriggerStimulusID);

      // Образ содержит command-паттерн.
      Assert.Contains(commandPatternId, csImage.CommandPatternIdList);
      Assert.Contains(commandPatternId, reflexImage.CommandPatternIdList);
    }

    /// <summary>
    /// При белом <c>visualColorId</c> CS-образ и б/у-триггер схлопываются в ОДИН образ
    /// (одинаковый набор EA+Command+белый) — дедупликация работает, дубля нет.
    /// Этот тест гарантирует, что нейтральный сценарий не порождает лишний образ.
    /// </summary>
    [Fact]
    public void ApplyMultipleInfluenceActions_WithWhiteColor_SingleImage()
    {
      int actionId = PrepareOperatorAction();
      int commandPatternId = _engine.NextSeed();
      int visualColor = AgentVisualColor.White; // белый — нейтральный сценарий

      var (success, errorMsg) = ApplyStimulus(actionId, commandPatternId, visualColor);
      Assert.True(success, "ApplyMultipleInfluenceActions failed: " + errorMsg);

      Assert.Equal(
          InfluenceActionSystem.Instance.ActiveCurTriggerStimulusID,
          InfluenceActionSystem.Instance.ActiveCurReflexTriggerStimulusID);

      var perceptionImages = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList();
      var imagesWithCommand = perceptionImages
          .Where(img => img.CommandPatternIdList != null
                        && img.CommandPatternIdList.Contains(commandPatternId))
          .ToList();

      Assert.Single(imagesWithCommand);
      Assert.Equal(AgentVisualColor.White, imagesWithCommand[0].VisualColorId);
    }

    #region Подготовка

    /// <summary>
    /// Регистрирует воздействие оператора (пустой ProbeKey → попадает в образ пускового стимула)
    /// и поднимает окружение, нужное для прямого вызова <c>ApplyMultipleInfluenceActions</c>.
    /// Возвращает ID воздействия.
    /// </summary>
    private int PrepareOperatorAction()
    {
      int actionId = _engine.NextSeed();
      var action = new InfluenceActionSystem.GomeostasisInfluenceAction
      {
        Id = actionId,
        Name = "Test action",
        Description = "",
        Influences = new Dictionary<int, int>()
      };

      var influenceActions = typeof(InfluenceActionSystem)
          .GetField("_influenceActions", BindingFlags.NonPublic | BindingFlags.Instance)
          ?.GetValue(InfluenceActionSystem.Instance) as Dictionary<int, InfluenceActionSystem.GomeostasisInfluenceAction>;
      Assert.NotNull(influenceActions);
      influenceActions[actionId] = action;

      // Пульсация (IsPulsationRunning имеет private set — ставим через backing field).
      typeof(GlobalTimer)
          .GetField("<IsPulsationRunning>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
          ?.SetValue(null, true);

      // Обходим проверку TryEnsureAgentState (IsDead/IsFirstPulse — свойства).
      var gomeostasSystem = GomeostasSystem.Instance;
      var agentStateField = gomeostasSystem.GetType().GetField("_agentState", BindingFlags.NonPublic | BindingFlags.Instance);
      var agentState = agentStateField?.GetValue(gomeostasSystem);
      agentState?.GetType().GetProperty("IsDead", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);
      agentState?.GetType().GetProperty("IsFirstPulse", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);

      // Связываем InfluenceActionSystem с PerceptionImagesSystem (фикстура не делает это автоматически).
      InfluenceActionSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);

      return actionId;
    }

    /// <summary>Прямой вызов ApplyMultipleInfluenceActions с одним воздействием и command-паттерном.</summary>
    private (bool Success, string Error) ApplyStimulus(int actionId, int commandPatternId, int visualColor)
    {
      return InfluenceActionSystem.Instance.ApplyMultipleInfluenceActions(
          new List<int> { actionId },
          new List<int>(),
          new List<int> { commandPatternId },
          false,
          0,
          0,
          visualColor,
          false);
    }

    #endregion
  }
}
