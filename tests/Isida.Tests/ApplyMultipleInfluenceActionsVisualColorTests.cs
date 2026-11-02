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
  /// Проверка, что <see cref="InfluenceActionSystem.ApplyMultipleInfluenceActions"/> не создаёт
  /// «белый» дубликат образа восприятия при не-белом коде зрительного канала.
  ///
  /// Случай: при активном документе SolidWorks (цвет 1/2/3) на каждый стимул создавалась пара
  /// PerceptionImage: цветной CS-образ и «нейтральный» белый (код 0). Белый дубль появился
  /// из-за жёстко заданного AgentVisualColor.White во втором вызове CreatePerceptionImage для
  /// б/у-триггера.
  ///
  /// Исправление: второй вызов теперь передаёт visualColorId, а не белый код.
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
    /// ApplyMultipleInfluenceActions создаёт ровно один PerceptionImage с указанным цветом
    /// и не создаёт белый дубль.
    /// </summary>
    [Fact]
    public void ApplyMultipleInfluenceActions_WithCommandPattern_NoWhiteDuplicate()
    {
      // --- Подготавливаем action и command-pattern ---
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

      int commandPatternId = _engine.NextSeed();
      int visualColor = AgentVisualColor.Black; // не белый цвет

      // --- Включаем пульсацию (IsPulsationRunning имеет private set, ставим через backing field) ---
      typeof(GlobalTimer)
          .GetField("<IsPulsationRunning>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
          ?.SetValue(null, true);

      // --- Обходим проверку TryEnsureAgentState (IsDead/IsFirstPulse — свойства) ---
      var gomeostasSystem = GomeostasSystem.Instance;
      var agentStateField = gomeostasSystem.GetType().GetField("_agentState", BindingFlags.NonPublic | BindingFlags.Instance);
      var agentState = agentStateField?.GetValue(gomeostasSystem);
      agentState?.GetType().GetProperty("IsDead", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);
      agentState?.GetType().GetProperty("IsFirstPulse", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);

      // --- Связываем InfluenceActionSystem с PerceptionImagesSystem (фикстура не делает это автоматически) ---
      InfluenceActionSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);

      // --- Вызываем ApplyMultipleInfluenceActions напрямую ---
      var (success, errorMsg) = InfluenceActionSystem.Instance.ApplyMultipleInfluenceActions(
          new List<int> { actionId },
          new List<int>(),
          new List<int> { commandPatternId },
          false,
          0,
          0,
          visualColor,
          false);
      Assert.True(success, "ApplyMultipleInfluenceActions failed: " + errorMsg);

      // --- Проверяем образы восприятия ---
      var perceptionImages = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList();

      // Должен быть создан ровно один образ, содержащий указанный command-паттерн
      var imagesWithCommand = perceptionImages
          .Where(img => img.CommandPatternIdList != null
                        && img.CommandPatternIdList.Contains(commandPatternId))
          .ToList();

      Assert.Single(imagesWithCommand);
      Assert.Equal(visualColor, imagesWithCommand[0].VisualColorId);

      // Не должно быть отдельного образа с тем же command-паттерном и белым цветом (код 0)
      var whiteDuplicate = perceptionImages
          .Where(img => img.CommandPatternIdList != null
                        && img.CommandPatternIdList.Contains(commandPatternId)
                        && img.VisualColorId == AgentVisualColor.White)
          .ToList();

      Assert.False(whiteDuplicate.Any(),
          "Не должен создаваться белый дубликат образа с тем же command-паттерном (код цвета 0)");
    }

    /// <summary>
    /// При белом цвете (visualColorId == White) поведение не меняется: создаётся один образ.
    /// Этот тест гарантирует, что исправление не сломало нейтральный сценарий.
    /// </summary>
    [Fact]
    public void ApplyMultipleInfluenceActions_WithWhiteColor_SingleImage()
    {
      // --- Подготавливаем action и command-pattern ---
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

      int commandPatternId = _engine.NextSeed();
      int visualColor = AgentVisualColor.White; // белый — нейтральный сценарий

      // --- Обходим проверки ---
      typeof(GlobalTimer)
          .GetField("<IsPulsationRunning>k__BackingField", BindingFlags.NonPublic | BindingFlags.Static)
          ?.SetValue(null, true);

      var gomeostasSystem = GomeostasSystem.Instance;
      var agentStateField = gomeostasSystem.GetType().GetField("_agentState", BindingFlags.NonPublic | BindingFlags.Instance);
      var agentState = agentStateField?.GetValue(gomeostasSystem);
      agentState?.GetType().GetProperty("IsDead", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);
      agentState?.GetType().GetProperty("IsFirstPulse", BindingFlags.Public | BindingFlags.Instance)?.SetValue(agentState, false);

      // --- Связываем InfluenceActionSystem с PerceptionImagesSystem (фикстура не делает это автоматически) ---
      InfluenceActionSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);

      // --- Вызываем ---
      var (success, errorMsg) = InfluenceActionSystem.Instance.ApplyMultipleInfluenceActions(
          new List<int> { actionId },
          new List<int>(),
          new List<int> { commandPatternId },
          false,
          0,
          0,
          visualColor,
          false);
      Assert.True(success, "ApplyMultipleInfluenceActions failed: " + errorMsg);

      // --- Проверяем ---
      var perceptionImages = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList();
      var imagesWithCommand = perceptionImages
          .Where(img => img.CommandPatternIdList != null
                        && img.CommandPatternIdList.Contains(commandPatternId))
          .ToList();

      Assert.Single(imagesWithCommand);
      Assert.Equal(AgentVisualColor.White, imagesWithCommand[0].VisualColorId);
    }
  }
}
