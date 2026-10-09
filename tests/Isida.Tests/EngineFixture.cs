using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
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
  /// InfoEnv → Gomeostas → Influence/Adaptive/Sensory/Genetic → PerceptionImages → ConditionedReflexes
  /// → SensoryAssociation → ConditionedReflexFormationService.
  /// Синглтоны движка статические, поэтому фикстура — общая на всю коллекцию
  /// <see cref="EngineIntegrationCollection"/> (последовательный прогон).
  /// Изоляция тестов обеспечивается вызовом <see cref="ResetToCleanState"/> в конструкторе каждого
  /// интеграционного теста: состояние движка НЕ сбрасывается между тестами автоматически.
  /// </summary>
  public sealed class EngineFixture : IDisposable
  {
    /// <summary>Корень каталога <c>Data</c> для временного проекта движка.</summary>
    public string DataDir { get; }

    /// <summary>
    /// Детерминированный счётчик пульсов. Тесты передвигают время движка
    /// (<see cref="AppGlobalState.Lifetime"/> + <c>UpdateAgentLifetime()</c>) только через этот счётчик —
    /// без <c>DateTime.Now</c>, <c>Environment.TickCount</c> и <c>Thread.Sleep</c>.
    /// </summary>
    private int _pulse;

    /// <summary>
    /// Нынешний «пульс движка», синхронизированный с <see cref="AppGlobalState.Lifetime"/>.
    /// </summary>
    public int CurrentPulse => _pulse;

    public EngineFixture()
    {
      DataDir = Path.Combine(Path.GetTempPath(), "isida_engine_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(DataDir);

      InformationEnvironmentSystem.InitializeInstance();
      GomeostasSystem.InitializeInstance(InformationEnvironmentSystem.Instance, DataDir);
      InfluenceActionSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      AdaptiveActionsSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      SensorySystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      // AddAction/AddBehaviorStyle/AddGeneticReflex разрешены только в стадии 0,
      // поэтому эталонные данные создаём до перехода на стадию 1 (ResetToCleanState).
      AppGlobalState.EvolutionStage = 0;
      GeneticReflexesSystem.InitializeInstance(GomeostasSystem.Instance, DataDir);
      SeedReferenceGeneticReflex();
      PerceptionImagesSystem.InitializeInstance(GomeostasSystem.Instance, GeneticReflexesSystem.Instance);
      GomeostasSystem.Instance.SetPerceptionImagesSystem(PerceptionImagesSystem.Instance);
      ConditionedReflexesSystem.InitializeInstance(
          GomeostasSystem.Instance, GeneticReflexesSystem.Instance, PerceptionImagesSystem.Instance);

      // Тесты двигают время счётчиком пульсов (десятки пульсов), поэтому период
      // пассивного угасания ставим заведомо больше любых тестовых перемоток:
      // иначе ApplyDecay() (вызывается из UpdateAgentLifetime) «точит» C под
      // тестами математики и вторичного обусловливания, и ассерты плывут.
      ConditionedReflexesSystem.Instance.Settings.PassiveDecayPeriodPulses = 100_000;

      // Эталонный слепок настроек: снимаем ПОСЛЕ тюнинга выше и ВОССТАНАВЛИВАЕМ в
      // ResetToCleanState, иначе тесты, меняющие InitialLifetimePulses / PassiveDecayPeriodPulses /
      // TieBreakPreferSmallerReflexId и т.п., «отравляют» все последующие тесты коллекции.
      CaptureBaselineSettings();

      // Формируем тот же граф зависимостей, что и IsidaEngine (шаги 9–15):
      // без ReflexTreeSystem событие ConditionedReflexCreated («Обработчик создания условного
      // рефлекса в дереве рефлексов») не подписан и AddConditionedReflex возвращает 0 с warning;
      // без SensoryAssociationSystem и FormationService не работают вторичное обусловливание,
      // сенсорная прекондиция и активное угасание по таймеру.
      ReflexChainsSystem.InitializeInstance(
          GeneticReflexesSystem.Instance, AdaptiveActionsSystem.Instance);
      GeneticReflexesSystem.InitializeWithChains(ReflexChainsSystem.Instance);
      SensoryAssociationSystem.InitializeInstance(
          GeneticReflexesSystem.Instance, ConditionedReflexesSystem.Instance);
      ReflexTreeSystem.InitializeInstance(
          GeneticReflexesSystem.Instance,
          ConditionedReflexesSystem.Instance,
          PerceptionImagesSystem.Instance,
          ReflexChainsSystem.Instance);
      ConditionedReflexFormationService.InitializeInstance(
          GomeostasSystem.Instance, GeneticReflexesSystem.Instance, ConditionedReflexesSystem.Instance);

      ResetToCleanState();
    }

    /// <summary>
    /// Сбрасывает состояние движка к «чистому листу» перед тестом.
    /// Порядок важен:FormationService (истимулов) →CRS (рефлексы) →SAS (связи CS→CS, через перезагрузку с диска) → образы.
    /// </summary>
    public void ResetToCleanState()
    {
      AppGlobalState.EvolutionStage = 1;

      // 0. Настройки движка — к эталонному слепку (см. CaptureBaselineSettings):
      //    страховка от тестов, меняющих Settings.* без try/finally.
      RestoreBaselineSettings();

      // 1. История стимулов сервиса формирования (иначе pending-CS «течёт» между тестами).
      if (ConditionedReflexFormationService.IsInitialized)
        ConditionedReflexFormationService.Instance.ResetHistory();

      // 2. Все условные рефлексы. RemoveAll проверяет EvolutionStage >= 1;
      //    на случай изменения логики дополнительно ставим internal-флаг через рефлексию.
      var crs = ConditionedReflexesSystem.Instance;
      typeof(ConditionedReflexesSystem)
          .GetField("removeAllConditionedReflexes", BindingFlags.Instance | BindingFlags.NonPublic)
          ?.SetValue(crs, true);
      crs.RemoveAllConditionedReflexes();
      typeof(ConditionedReflexesSystem)
          .GetField("removeAllConditionedReflexes", BindingFlags.Instance | BindingFlags.NonPublic)
          ?.SetValue(crs, false);

      // 3. Сенсорные связи CS→CS: публичного Clear нет — очищаем файл и перезагружаем
      //    систему из пустого состояния. Дополнительно чистим _links через рефлексию,
      //    т.к. Load() при отсутствии файла делает return ДО _links.Clear(), и связи
      //    от предыдущего теста остаются в словаре.
      TryDeleteSensoryAssociationsFile();
      if (SensoryAssociationSystem.IsInitialized)
      {
        SensoryAssociationSystem.Instance.Load();
        // Форсированная очистка словаря _links на случай, если Load() не прочитал файл:
        // Clear() через необобщённый IDictionary — тип ключа (ValueTuple) не нужен.
        (typeof(SensoryAssociationSystem)
            .GetField("_links", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(SensoryAssociationSystem.Instance) as System.Collections.IDictionary)
            ?.Clear();
        // Индекс исходящих рёбер (_outLinks) — новое состояние транзитивного обучения.
        // Load() при отсутствии файла делает return ДО перестройки индекса, поэтому
        // без явной очистки рёбра предыдущего теста «текут» в обход цепочек.
        (typeof(SensoryAssociationSystem)
            .GetField("_outLinks", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.GetValue(SensoryAssociationSystem.Instance) as System.Collections.IDictionary)
            ?.Clear();
      }

      // 4. Пульс времени: 0 — детерминированная стартовая точка (в т.ч. для
      //    ApplyDecay() сенсорных связей, срабатывающего на кратных 100 пульсов).
      _pulse = 0;
      AppGlobalState.Lifetime = 0;
      crs.UpdateAgentLifetime();

      // 5. Образы восприятия и образы стилей (после рефлексов — дерево реагирует на события).
      PerceptionImagesSystem.Instance.ClearAllPerceptionImages();
      PerceptionImagesSystem.Instance.ClearAllBehaviorStyleImages();
      GomeostasSystem.Instance.ActiveBehaviorStyleImageId = 0;

      // Активные стили — пустые (internal-сеттер AppGlobalState доступен через InternalsVisibleTo):
      // иначе стили, активированные предыдущим тестом, попадают в Level2 новых УР.
      AppGlobalState.UpdateActiveStyles(System.Linq.Enumerable.Empty<GomeostasSystem.BehaviorStyle>());

      // 6. Генетические рефлексы — эталонные из комплекта данных, не удаляем:
      //    без них не работает дерево рефлексов и активация у-рефлексов.
    }

    /// <summary>
    /// ID эталонного безусловного рефлекса, созданного при поднятии фикстуры
    /// (в пустом временном каталоге данных UR отсутствуют, а без UR не работает
    /// ни дерево рефлексов, ни у-рефлексы с SourceGeneticReflexId).
    /// </summary>
    public int SeedGeneticReflexId { get; private set; }

    /// <summary>
    /// Level2 («контексты реагирования») эталонного UR — корректный контекст для
    /// интеграционных тестов; совпадает с <see cref="SeedStyleId"/>.
    /// </summary>
    public int SeedStyleId { get; private set; }

    /// <summary>
    /// Создаёт минимальный эталонный UR: стиль гомеостаза + моторное действие + рефлекс
    /// (только в стадии 0). Идемпотентен: повторный вызов ничего не добавляет.
    /// </summary>
    private void SeedReferenceGeneticReflex()
    {
      if (SeedGeneticReflexId > 0)
        return;

      var (styleId, _) = GomeostasSystem.Instance.AddBehaviorStyle("Тестовый стиль", "");
      var (actionId, _) = AdaptiveActionsSystem.Instance.AddAction("Тестовое действие", "", Vigor: 5);
      var (reflexId, _) = GeneticReflexesSystem.Instance.AddGeneticReflex(
          1, new System.Collections.Generic.List<int> { styleId }, null, null,
          new System.Collections.Generic.List<int> { actionId });
      SeedStyleId = styleId;
      SeedGeneticReflexId = reflexId;
    }

    /// <summary>
    /// Перематывает пульс движка в <paramref name="pulse"/> (присваивает
    /// <see cref="AppGlobalState.Lifetime"/> и вызывает <c>UpdateAgentLifetime()</c>,
    /// что обрабатывает TTL-удаления и pending-угасание).
    /// </summary>
    public void AdvanceToPulse(int pulse)
    {
      if (pulse < _pulse)
        throw new ArgumentOutOfRangeException(nameof(pulse), "пульс можно только перематывать вперёд");
      _pulse = pulse;
      AppGlobalState.Lifetime = pulse;
      ConditionedReflexesSystem.Instance.UpdateAgentLifetime();
    }

    /// <summary>Шаг на <paramref name="pulses"/> пульсов вперёд.</summary>
    public void AdvancePulses(int pulses) => AdvanceToPulse(_pulse + pulses);

    /// <summary>
    /// Детерминированный ID для списков действий/фраз/стилей (без TickCount/Guid):
    /// монотонный счётчик со случайным стартом, чтобы не сталкиваться с эталонными данными.
    /// </summary>
    public int NextSeed() => ++_seed;
    private int _seed = 100000;

    // Слепок настроек ConditionedReflexSettings (значения value-свойств-примитивов),
    // чтобы ResetToCleanState мог вернуть движок к «заводскому» состоянию после тестов,
    // меняющих Settings.* напрямую.
    private Dictionary<string, object> _baselineSettings;

    private void CaptureBaselineSettings()
    {
      _baselineSettings = ReadSettingsSnapshot();
    }

    private void RestoreBaselineSettings()
    {
      if (_baselineSettings == null)
        return;
      var settings = ConditionedReflexesSystem.Instance.Settings;
      foreach (var prop in settings.GetType()
          .GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!prop.CanRead || !prop.CanWrite)
          continue;
        if (_baselineSettings.TryGetValue(prop.Name, out object value))
          prop.SetValue(settings, value);
      }
    }

    private Dictionary<string, object> ReadSettingsSnapshot()
    {
      var snapshot = new Dictionary<string, object>();
      var settings = ConditionedReflexesSystem.Instance.Settings;
      foreach (var prop in settings.GetType()
          .GetProperties(BindingFlags.Public | BindingFlags.Instance))
      {
        if (!prop.CanRead || !prop.CanWrite)
          continue;
        Type t = prop.PropertyType;
        // Снимаем только простые значения (числа, bool, string) — их безопасно копировать
        // и восстанавливать между тестами. Ссылочные/сложные типы пропускаем.
        if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal))
          snapshot[prop.Name] = prop.GetValue(settings);
      }
      return snapshot;
    }

    private void TryDeleteSensoryAssociationsFile()
    {
      try
      {
        string reflexesPath = GeneticReflexesSystem.Instance.GetGeneticReflexesFilePath();
        string file = Path.Combine(Path.GetDirectoryName(reflexesPath), "SensoryAssociations.dat");
        if (File.Exists(file))
          File.Delete(file);
      }
      catch { }
    }

    public void Dispose()
    {
      // Порядок как в IsidaEngine.Shutdown: сначала сервисы поверх CRS, затем сам CRS.
      try { if (ConditionedReflexFormationService.IsInitialized) ConditionedReflexFormationService.Instance.Dispose(); } catch { }
      try { if (ReflexTreeSystem.IsInitialized) ReflexTreeSystem.Instance.Dispose(); } catch { }
      try { if (SensoryAssociationSystem.IsInitialized) SensoryAssociationSystem.Instance.Dispose(); } catch { }
      try { if (ConditionedReflexesSystem.IsInitialized) ConditionedReflexesSystem.Instance.Dispose(); } catch { }
      try { if (ReflexChainsSystem.IsInitialized) ReflexChainsSystem.Instance.Dispose(); } catch { }
      try { if (Directory.Exists(DataDir)) Directory.Delete(DataDir, recursive: true); } catch { }
    }
  }

  /// <summary>Коллекция интеграционных тестов на живом движке (без параллелизма).</summary>
  [CollectionDefinition("EngineIntegration", DisableParallelization = true)]
  public sealed class EngineIntegrationCollection : ICollectionFixture<EngineFixture> { }
}
