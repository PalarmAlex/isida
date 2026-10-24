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
            AutomatizmTreeSystem.Instance,
            reflexTreeSystem: ReflexTreeSystem.Instance);
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
    /// Перестройка образов восприятия при предзапуске сценария (как при переходе на стадию 0):
    /// мусорный образ (не принадлежащий ни одному б/у рефлексу) удаляется из памяти и файла,
    /// а образ стиля эталонного б/у рефлекса сохраняется.
    /// </summary>
    [Fact]
    public void ScenarioPreRun_RebuildsPerceptionImages_KeepsGenetic_DropsGarbage()
    {
      // Мусорный образ: фраза, не связанная ни с одним б/у рефлексом.
      int garbage = NewPhraseImage();
      Assert.Contains(Images.GetAllPerceptionImagesList(), x => x.Id == garbage);

      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(AppGlobalState.EvolutionStage);

      // 1. Мусорный образ исчез из памяти.
      Assert.DoesNotContain(Images.GetAllPerceptionImagesList(), x => x.Id == garbage);

      // 2. Мусорного образа нет в файле PerceptionImages.dat (строки вида "ID|...").
      string garbagePrefix = garbage + "|";
      Assert.DoesNotContain(ReadPerceptionImageLines(),
          line => line.StartsWith(garbagePrefix, StringComparison.Ordinal));

      // 3. Образ стиля эталонного б/у рефлекса (Level2) пересоздан и присутствует.
      var seedReflex = GeneticReflexesSystem.Instance.GetGeneticReflex(Engine.SeedGeneticReflexId);
      Assert.NotNull(seedReflex);
      Assert.Contains(Images.GetAllBehaviorStyleImagesList(),
          s => s.BehaviorStylesList.OrderBy(x => x)
              .SequenceEqual((seedReflex.Level2 ?? new System.Collections.Generic.List<int>()).OrderBy(x => x)));
    }

    /// <summary>
    /// Повторная перестройка идемпотентна: набор ID образов не «дрейфует» между прогонами.
    /// </summary>
    [Fact]
    public void ScenarioPreRun_PerceptionImageRebuild_IsIdempotent()
    {
      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(AppGlobalState.EvolutionStage);
      var firstIds = Images.GetAllPerceptionImagesList().Select(x => x.Id).OrderBy(x => x).ToList();
      var firstStyles = Images.GetAllBehaviorStyleImagesList().Select(x => x.Id).OrderBy(x => x).ToList();

      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(AppGlobalState.EvolutionStage);
      var secondIds = Images.GetAllPerceptionImagesList().Select(x => x.Id).OrderBy(x => x).ToList();
      var secondStyles = Images.GetAllBehaviorStyleImagesList().Select(x => x.Id).OrderBy(x => x).ToList();

      Assert.Equal(firstIds, secondIds);
      Assert.Equal(firstStyles, secondStyles);
    }

    /// <summary>
    /// Регрессия (Случай 19): предзапуск сценария НЕ должен трогать образы стилей
    /// (<c>BehaviorStyleImages.dat</c>). Колонка «Стили» в логе — это ID образа стилей из
    /// глобального счётчика; сброс счётчика и удаление session-learned образов стилей гомеостаза
    /// ломали сверку ожиданий по стилю в группе (первый сценарий: «ожид. 21, факт 23»,
    /// «ожид. 23/25/27, факт -»). При этом образы восприятия чистятся по-прежнему.
    /// </summary>
    [Fact]
    public void ScenarioPreRun_KeepsBehaviorStyleImages_DropsGarbagePerceptionImages()
    {
      // Session-learned образ стиля: не принадлежит ни одному б/у рефлексу (произвольный набор).
      int learnedStyle = Images.AddBehaviorStyleImage(new System.Collections.Generic.List<int> { 901, 902 });
      Assert.True(learnedStyle > 0);
      // Мусорный образ восприятия: фраза, не связанная ни с одним б/у рефлексом.
      int garbage = NewPhraseImage();

      EvolutionStageService.Instance.ClearStageDataOnlyForScenarioPreRun(AppGlobalState.EvolutionStage);

      // 1. Session-learned образ стиля сохранён в памяти и на диске.
      Assert.Contains(Images.GetAllBehaviorStyleImagesList(),
          s => s.BehaviorStylesList.OrderBy(x => x).SequenceEqual(new[] { 901, 902 }));
      // 2. Его ID не изменился (счётчик образов стилей не сбрасывался).
      Assert.Contains(Images.GetAllBehaviorStyleImagesList(), s => s.Id == learnedStyle);
      // 3. Мусорный образ восприятия удалён.
      Assert.DoesNotContain(Images.GetAllPerceptionImagesList(), x => x.Id == garbage);
    }

    /// <summary>Строки данных (без шапки/пустых) из PerceptionImages.dat.</summary>
    private static string[] ReadPerceptionImageLines()
    {
      string reflexesPath = GeneticReflexesSystem.Instance.GetGeneticReflexesFilePath();
      string file = Path.Combine(Path.GetDirectoryName(reflexesPath), "PerceptionImages.dat");
      if (!File.Exists(file))
        return new string[0];

      return File.ReadAllLines(file)
          .Where(l => !string.IsNullOrWhiteSpace(l) && !l.StartsWith("#"))
          .ToArray();
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
