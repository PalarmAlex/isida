using System;
using System.IO;
using System.Linq;
using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регрессия: предзапуск сценария с флагом очистки данных должен чистить состояние так же,
  /// как переход на стадию 0, — в том числе <c>SensoryAssociations.dat</c>.
  /// <para>
  /// Раньше <see cref="EvolutionStageService.ClearStageDataOnlyForScenarioPreRun"/> чистил только
  /// стадии 1..N через <c>ClearStageData</c> и не вызывал очистку сенсорных ассоциаций (и образов
  /// действий), поэтому связи CS→CS переживали старт сценария, хотя при переключении на стадию 0
  /// на пульте они очищались.
  /// </para>
  /// <para>
  /// <see cref="EvolutionStageService"/> и Automatism-подсистемы не входят в базовую
  /// <see cref="EngineFixture"/>, поэтому поднимаются здесь идемпотентно поверх того же временного
  /// каталога данных. Синглтоны статические, коллекция прогоняется последовательно
  /// (<see cref="EngineIntegrationCollection"/>).
  /// </para>
  /// </summary>
  [Collection("EngineIntegration")]
  public sealed class EvolutionStageScenarioPreRunClearTests : EngineIntegrationTestBase, IDisposable
  {
    /// <summary>
    /// Поднимает Automatism-подсистемы (нужны <see cref="EvolutionStageService"/> как обязательные
    /// зависимости) и сам сервис стадий.
    /// </summary>
    /// <param name="engine">Общая фикстура движка коллекции.</param>
    public EvolutionStageScenarioPreRunClearTests(EngineFixture engine) : base(engine)
    {
      if (!ActionsImagesSystem.IsInitialized)
        ActionsImagesSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmTreeSystem.IsInitialized)
        AutomatizmTreeSystem.InitializeInstance(Engine.DataDir);
      if (!AutomatizmSystem.IsInitialized)
        AutomatizmSystem.InitializeInstance(Engine.DataDir);

      if (!EvolutionStageService.IsInitialized)
        EvolutionStageService.InitializeInstance(
            AutomatizmSystem.Instance,
            Crs,
            AutomatizmTreeSystem.Instance);
    }

    /// <summary>Освобождает статический экземпляр сервиса стадий после теста.</summary>
    public void Dispose()
    {
      if (EvolutionStageService.IsInitialized)
        EvolutionStageService.Instance.Dispose();
    }

    /// <summary>
    /// Связь CS₁→CS₂, созданная до предзапуска сценария, должна исчезнуть и из памяти, и из файла.
    /// </summary>
    [Fact]
    public void ScenarioPreRun_ClearsSensoryAssociations_InMemoryAndOnDisk()
    {
      int a = NewPhraseImage();
      int b = NewPhraseImage();
      Sensory.StrengthenLink(a, b);
      Assert.True(Sensory.TryGetStrength(a, b, out _), "связь должна существовать до очистки");

      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(AppGlobalState.EvolutionStage);

      Assert.False(Sensory.TryGetStrength(a, b, out _),
          "после предзапуска сценария связь должна быть очищена в памяти");
      Assert.Empty(ReadSensoryLinkLines());
    }

    /// <summary>
    /// Читает строки-связи из <c>SensoryAssociations.dat</c> (без шапки и пустых строк).
    /// </summary>
    private static string[] ReadSensoryLinkLines()
    {
      string reflexesPath = GeneticReflexesSystem.Instance.GetGeneticReflexesFilePath();
      string file = Path.Combine(Path.GetDirectoryName(reflexesPath), "SensoryAssociations.dat");
      if (!File.Exists(file))
        return new string[0];

      return File.ReadAllLines(file)
          .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
          .ToArray();
    }
  }
}
