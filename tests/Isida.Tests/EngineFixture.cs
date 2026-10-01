using System;
using System.IO;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Поднимает реальную цепочку движка на временном каталоге данных:
  /// InfoEnv → Gomeostas → Influence/Adaptive/Sensory/Genetic → PerceptionImages → ConditionedReflexes.
  /// Синглтоны движка статические, поэтому фикстура — общая на всю коллекцию
  /// <see cref="EngineIntegrationCollection"/> (последовательный прогон).
  /// </summary>
  public sealed class EngineFixture : IDisposable
  {
    /// <summary>Корень каталога <c>Data</c> для временного проекта движка.</summary>
    public string DataDir { get; }

    public EngineFixture()
    {
      DataDir = Path.Combine(Path.GetTempPath(), "isida_engine_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DataDir);

      InformationEnvironmentSystem.InitializeInstance();
      GomeostasSystem.InitializeInstance(InformationEnvironmentSystem.Instance, DataDir);
      InfluenceActionSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      AdaptiveActionsSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      SensorySystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      GeneticReflexesSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      PerceptionImagesSystem.InitializeInstance(GomeostasSystem.Instance, GeneticReflexesSystem.Instance);
      GomeostasSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);
      ConditionedReflexesSystem.InitializeInstance(
          GomeostasSystem.Instance, GeneticReflexesSystem.Instance, PerceptionImagesSystem.Instance);
    }

    public void Dispose()
    {
      try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true); } catch { }
    }
  }

  /// <summary>Коллекция интеграционных тестов на живом движке (без параллелизма).</summary>
  [CollectionDefinition("EngineIntegration", DisableParallelization = true)]
  public sealed class EngineIntegrationCollection : ICollectionFixture<EngineFixture> { }
}
