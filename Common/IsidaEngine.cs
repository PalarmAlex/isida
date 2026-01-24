using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Xml.Linq;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Common.ResearchLogger;
using static ISIDA.Psychic.Automatism.AutomatizmSystem;
using static ISIDA.Psychic.Automatism.AutomatizmTreeSystem;
using static ISIDA.Psychic.Automatism.InfluenceActionsImagesSystem;
using static ISIDA.Reflexes.ConditionedReflexesSystem;
using static ISIDA.Reflexes.GeneticReflexesSystem;
using static ISIDA.Reflexes.PerceptionImagesSystem;
using static ISIDA.Reflexes.ReflexChainsSystem;

namespace ISIDA.Common
{
  /// <summary>
  /// Конфигурация для инициализации движка ISIDA
  /// </summary>
  /// <remarks>
  /// Предоставляет гибкую настройку всех параметров инициализации библиотеки ISIDA.
  /// Поддерживает как ручную настройку всех параметров, так и использование значений по умолчанию.
  /// </remarks>
  public class IsidaConfig
  {
    /// <summary>
    /// Директория с данными психики (образы действий оператора и агента ИИ)
    /// </summary>
    public string PsychicDataFolder { get; set; }

    /// <summary>
    /// Базовая директория для всех файлов системы ISIDA
    /// </summary>
    /// <value>По умолчанию: %ProgramData%\ISIDA</value>
    public string BaseDirectory { get; set; }

    /// <summary>
    /// Директория с данными системы гомеостаза
    /// </summary>
    public string GomeostasFolder { get; set; }

    /// <summary>
    /// Директория с данными адаптивных действий
    /// </summary>
    public string ActionsFolder { get; set; }

    /// <summary>
    /// Директория с данными сенсорной системы
    /// </summary>
    public string SensorsFolder { get; set; }

    /// <summary>
    /// Директория с данными рефлексов
    /// </summary>
    public string ReflexesFolder { get; set; }

    /// <summary>
    /// Директория для сохранения логов
    /// </summary>
    public string LogsFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ISIDA",
        "Logs");

    /// <summary>
    /// Формат файлов логов
    /// </summary>
    /// <value>По умолчанию: LogFormat.All (оба формата)</value>
    public LogFormat LogFormat { get; set; } = LogFormat.All;

    /// <summary>
    /// Включено ли логирование
    /// </summary>
    /// <value>По умолчанию: true</value>
    public bool LogEnabled { get; set; } = true;

    /// <summary>
    /// Очищать ли файлы логов при запуске
    /// </summary>
    /// <value>По умолчанию: false</value>
    public bool ClearLogsOnStart { get; set; } = false;

    /// <summary>
    /// Идентификатор стиля поведения по умолчанию
    /// </summary>
    public int DefaultStileId { get; set; } = 0;

    /// <summary>
    /// Порог начала изменения глобального состояния агента
    /// </summary>
    public int CompareLevel { get; set; } = 100;

    /// <summary>
    /// Минимальная величина изменения параметров для детектирования
    /// </summary>
    public float DifSensorPar { get; set; } = 0.5f;

    /// <summary>
    /// Время в пульсах удержания состояния для возврата в норму после активации состояния ХОРОШО
    /// </summary>
    public int DynamicTime { get; set; } = 50;

    /// <summary>
    /// Время удержания рефлекторных действий для визуализации
    /// </summary>
    public int ReflexActionDisplayDuration { get; set; } = 2;

    /// <summary>
    /// Идентификатор адаптивного действия по умолчанию
    /// </summary>
    public int DefaultAdaptiveActionId { get; set; } = 0;

    /// <summary>
    /// Порог распознавания для вербального канала
    /// </summary>
    public int RecognitionThreshold { get; set; } = 3;

    /// <summary>
    /// Реализация интерфейса ILogWriter для записи логов в память
    /// </summary>
    /// <remarks>
    /// Если указан, будет использоваться для записи логов в память в дополнение к файловому логированию
    /// </remarks>
    public ILogWriter MemoryLogWriter { get; set; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="IsidaConfig"/> с базовой директорией по умолчанию
    /// </summary>
    public IsidaConfig()
    {
      BaseDirectory = Path.Combine(
          Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
          "ISIDA");
    }

    /// <summary>
    /// Настраивает стандартные пути к директориям относительно базовой директории
    /// </summary>
    /// <returns>Текущий экземпляр конфигурации для цепочки вызовов</returns>
    public IsidaConfig WithDefaultFolders()
    {
      GomeostasFolder = Path.Combine(BaseDirectory, "DataGomeostas");
      ActionsFolder = Path.Combine(BaseDirectory, "DataActions");
      SensorsFolder = Path.Combine(BaseDirectory, "Sensors");
      ReflexesFolder = Path.Combine(BaseDirectory, "Reflexes");
      PsychicDataFolder = Path.Combine(BaseDirectory, "Data", "Psychic");
      LogsFolder = Path.Combine(BaseDirectory, "Logs");
      return this;
    }

    /// <summary>
    /// Проверяет корректность заполнения обязательных параметров конфигурации
    /// </summary>
    /// <exception cref="ArgumentException">Выбрасывается при отсутствии обязательных параметров</exception>
    public void Validate()
    {
      if (string.IsNullOrEmpty(GomeostasFolder))
        throw new ArgumentException("GomeostasFolder не указан");
      if (string.IsNullOrEmpty(ActionsFolder))
        throw new ArgumentException("ActionsFolder не указан");
      if (string.IsNullOrEmpty(SensorsFolder))
        throw new ArgumentException("SensorsFolder не указан");
      if (string.IsNullOrEmpty(ReflexesFolder))
        throw new ArgumentException("ReflexesFolder не указан");
      if (string.IsNullOrEmpty(PsychicDataFolder))
        throw new ArgumentException("PsychicDataFolder не указан");
    }
  }

  /// <summary>
  /// Контекст движка ISIDA со всеми инициализированными системами
  /// </summary>
  /// <remarks>
  /// Предоставляет доступ ко всем компонентам библиотеки ISIDA после успешной инициализации.
  /// Реализует интерфейс <see cref="IDisposable"/> для освобождения ресурсов.
  /// </remarks>
  public class IsidaContext : IDisposable
  {
    /// <summary>
    /// Система гомеостаза
    /// </summary>
    public GomeostasSystem Gomeostas { get; internal set; }

    /// <summary>
    /// Сенсорная система
    /// </summary>
    public SensorySystem SensorySystem { get; internal set; }

    /// <summary>
    /// Система адаптивных действий
    /// </summary>
    public AdaptiveActionsSystem AdaptiveActions { get; internal set; }

    /// <summary>
    /// Система внешних воздействий
    /// </summary>
    public InfluenceActionSystem InfluenceActions { get; internal set; }

    /// <summary>
    /// Система безусловных рефлексов
    /// </summary>
    public GeneticReflexesSystem GeneticReflexes { get; internal set; }

    /// <summary>
    /// Система условных рефлексов
    /// </summary>
    public ConditionedReflexesSystem ConditionedReflexes { get; internal set; }

    /// <summary>
    /// Система образов восприятия
    /// </summary>
    public PerceptionImagesSystem PerceptionImages { get; internal set; }

    /// <summary>
    /// Активатор рефлексов
    /// </summary>
    public ReflexesActivator ReflexesActivator { get; internal set; }

    /// <summary>
    /// Система дерева рефлексов
    /// </summary>
    public ReflexTreeSystem ReflexTree { get; internal set; }

    /// <summary>
    /// Система цепочек рефлексов
    /// </summary>
    public ReflexChainsSystem ReflexChains { get; internal set; }

    /// <summary>
    /// Сервис выполнения рефлексов
    /// </summary>
    public ReflexExecutionService ReflexExecution { get; internal set; }

    /// <summary>
    /// Сервис формирования условных рефлексов на основе временных корреляций
    /// </summary>
    public ConditionedReflexFormationService ConditionedReflexFormation { get; internal set; }

    /// <summary>
    /// Система образов действий агента или оператора
    /// </summary>
    public ActionsImagesSystem ActionsImages { get; internal set; }

    /// <summary>
    /// Система образов действий Оператора
    /// </summary>
    public InfluenceActionsImagesSystem InfluenceActionsImages { get; internal set; }

    /// <summary>
    /// Система дерева автоматизмов
    /// </summary>
    public AutomatizmTreeSystem AutomatizmTree { get; internal set; }

    /// <summary>
    /// Система автоматизмов
    /// </summary>
    public AutomatizmSystem AutomatizmSystem { get; internal set; }

    /// <summary>
    /// Система психики
    /// </summary>
    public PsychicSystem PsychicSystem { get; internal set; }

    /// <summary>
    /// Система управления эмоциями
    /// </summary>
    public EmotionsImageSystem EmotionsImageSystem { get; internal set; }

    /// <summary>
    /// Система управления вербальными образами
    /// </summary>
    public VerbalBrocaImagesSystem VerbalBrocaImagesSystem { get; internal set; }

    /// <summary>
    /// Система управления информационной картиной
    /// </summary>
    public InformationEnvironmentSystem InformationEnvironmentSystem { get; internal set; }

    /// <summary>
    /// Система управления гомеостатическими целями
    /// </summary>
    public PurposeGeneticImageSystem PurposeGeneticImageSystem { get; internal set; }

    /// <summary>
    /// Логгер исследований
    /// </summary>
    public ResearchLogger ResearchLogger { get; internal set; }

    private bool _disposed = false;

    /// <summary>
    /// Освобождает ресурсы, используемые контекстом ISIDA
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      Logger.Info("[IsidaContext] Начинается безопасное освобождение ресурсов...");

      try
      {
        GlobalTimer.Stop();
        Thread.Sleep(200);
        GlobalTimer.ClearSystems();
      }
      catch (Exception ex)
      {
        Logger.Warning($"Ошибка при остановке GlobalTimer: {ex.Message}");
      }

      try
      {
        Gomeostas?.SaveAgentProperties();
      }
      catch (Exception ex)
      {
        Logger.Warning($"Ошибка при сохранении свойств агента: {ex.Message}");
      }

      //SafeDispose(ConditionedReflexFormation, "ConditionedReflexFormation");
      Logger.Info($"ConditionedReflexFormation успешно освобожден");

      SafeDispose(ResearchLogger, "ResearchLogger");
      SafeDispose(PsychicSystem, "PsychicSystem");
      SafeDispose(VerbalBrocaImagesSystem, "VerbalBrocaImagesSystem");
      SafeDispose(EmotionsImageSystem, "EmotionsImageSystem");
      SafeDispose(AutomatizmSystem, "AutomatizmSystem");
      SafeDispose(AutomatizmTree, "AutomatizmTree");
      SafeDispose(AutomatizmTree, "PurposeGeneticImageSystem");
      SafeDispose(ActionsImages, "ActionsImages");
      SafeDispose(InfluenceActionsImages, "InfluenceActionsImages");
      SafeDispose(ReflexesActivator, "ReflexesActivator");
      SafeDispose(ReflexExecution, "ReflexExecution");
      SafeDispose(ReflexTree, "ReflexTree");
      SafeDispose(ReflexChains, "ReflexChains");
      SafeDispose(ConditionedReflexes, "ConditionedReflexes");
      SafeDispose(GeneticReflexes, "GeneticReflexes");
      SafeDispose(PerceptionImages, "PerceptionImages");
      SafeDispose(SensorySystem, "SensorySystem");
      SafeDispose(AdaptiveActions, "AdaptiveActions");
      SafeDispose(Gomeostas, "Gomeostas");
      SafeDispose(InfluenceActions, "InfluenceActions");
      SafeDispose(InformationEnvironmentSystem, "InformationEnvironmentSystem");

      _disposed = true;
      Logger.Info($"Освобождение завершено");
    }

    private static void SafeDispose(IDisposable disposable, string name)
    {
      if (disposable == null)
      {
        Logger.Warning($"{name} равен null, пропускаем");
        return;
      }

      try
      {
        disposable.Dispose();
        Logger.Info($"{name} успешно освобожден");
      }
      catch (ObjectDisposedException)
      {
        Logger.Warning($"{name} уже освобожден (ObjectDisposedException). Тип: {disposable.GetType().Name}");
      }
      catch (InvalidOperationException ex)
      {
        Logger.Error($"{name}: InvalidOperationException: {ex.Message}\n{ex.StackTrace}");
      }
      catch (Exception ex)
      {
        Logger.Error($"Критическая ошибка при освобождении {name}: {ex.Message}");
      }
    }

    /// <summary>
    /// Интерфейс для объектов с флагом освобождения
    /// </summary>
    public interface IDisposableState
    {
      /// <summary>
      /// Флаг освобождения объекта
      /// </summary>
      bool IsDisposed { get; }
    }

    /// <summary>
    /// Проверяет, все ли системы инициализированы
    /// </summary>
    public bool IsFullyInitialized =>
        Gomeostas != null &&
        SensorySystem != null &&
        AdaptiveActions != null &&
        InfluenceActions != null &&
        GeneticReflexes != null &&
        ConditionedReflexes != null &&
        PerceptionImages != null &&
        AutomatizmTree != null &&
        AutomatizmSystem != null &&
        PurposeGeneticImageSystem != null &&
        ActionsImages != null &&
        InfluenceActionsImages != null &&
        ReflexesActivator != null &&
        ReflexTree != null &&
        ReflexChains != null &&
        ReflexExecution != null &&
        ConditionedReflexFormation != null &&
        ResearchLogger != null;
  }

  /// <summary>
  /// Фабрика для создания и инициализации движка ISIDA
  /// </summary>
  /// <remarks>
  /// Предоставляет статические методы для инициализации всех компонентов библиотеки ISIDA
  /// в правильной последовательности с минимальной конфигурацией.
  /// </remarks>
  public static class IsidaEngine
  {
    /// <summary>
    /// Создает и инициализирует движок ISIDA с использованием указанной конфигурации
    /// </summary>
    /// <param name="config">Конфигурация инициализации</param>
    /// <returns>Готовый к работе контекст ISIDA со всеми инициализированными системами</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если config равен null</exception>
    /// <exception cref="IsidaInitializationException">Выбрасывается при ошибках инициализации</exception>
    public static IsidaContext Create(IsidaConfig config)
    {
      if (config == null)
        throw new ArgumentNullException(nameof(config));

      config.Validate();

      var context = new IsidaContext();
      InitializeEngine(context, config);
      return context;
    }

    /// <summary>
    /// Создает и инициализирует движок ISIDA с настройками по умолчанию
    /// </summary>
    /// <param name="baseDirectory">Базовая директория для файлов системы. Если не указана, используется директория по умолчанию</param>
    /// <returns>Готовый к работе контекст ISIDA со всеми инициализированными системами</returns>
    public static IsidaContext CreateWithDefaults(string baseDirectory = null)
    {
      var config = new IsidaConfig();

      if (!string.IsNullOrEmpty(baseDirectory))
        config.BaseDirectory = baseDirectory;

      config.WithDefaultFolders();
      return Create(config);
    }

    private static void InitializeEngine(IsidaContext context, IsidaConfig config)
    {
      int initializationStep = 0;

      try
      {
        // Шаг 1: Инициализация логов и директорий
        initializationStep = 1;
        InitializeFileValidator(config.LogsFolder);

        // Шаг 2: Гомеостаз
        initializationStep = 2;
        InformationEnvironmentSystem.InitializeInstance();
        context.InformationEnvironmentSystem = InformationEnvironmentSystem.Instance;

        // Шаг 3: Гомеостаз
        initializationStep = 3;
        GomeostasSystem.InitializeInstance(context.InformationEnvironmentSystem, config.GomeostasFolder);
        context.Gomeostas = GomeostasSystem.Instance;
        context.Gomeostas.DefaultStileId = config.DefaultStileId;
        context.Gomeostas.CompareLevel = config.CompareLevel;
        context.Gomeostas.DifSensorPar = config.DifSensorPar;
        context.Gomeostas.DynamicTime = config.DynamicTime;

        // Шаг 4: Адаптивные действия
        initializationStep = 4;
        AdaptiveActionsSystem.InitializeInstance(context.Gomeostas, config.ActionsFolder);
        context.AdaptiveActions = AdaptiveActionsSystem.Instance;
        context.AdaptiveActions.ReflexActionDisplayDuration = config.ReflexActionDisplayDuration;
        context.AdaptiveActions.DefaultAdaptiveActionId = config.DefaultAdaptiveActionId;

        // Шаг 5: Внешние действия
        initializationStep = 5;
        InfluenceActionSystem.InitializeInstance(context.Gomeostas, config.ActionsFolder);
        context.InfluenceActions = InfluenceActionSystem.Instance;

        // Шаг 6: Образы внешних действий
        initializationStep = 6;
        InfluenceActionsImagesSystem.InitializeInstance(config.PsychicDataFolder);
        context.InfluenceActionsImages = InfluenceActionsImagesSystem.Instance;

        // Шаг 7: Сенсорная система
        initializationStep = 7;
        SensorySystem.InitializeInstance(context.Gomeostas, config.SensorsFolder);
        context.SensorySystem = SensorySystem.Instance;
        context.SensorySystem.VerbalRecognitionThreshold = config.RecognitionThreshold;

        // Шаг 8: Безусловные рефлексы
        initializationStep = 8;
        GeneticReflexesSystem.InitializeInstance(context.Gomeostas, config.ReflexesFolder);
        context.GeneticReflexes = GeneticReflexesSystem.Instance;

        // Шаг 9: Система цепочек рефлексов
        initializationStep = 9;
        ReflexChainsSystem.InitializeInstance(context.GeneticReflexes, context.AdaptiveActions);
        context.ReflexChains = ReflexChainsSystem.Instance;

        // Шаг 10: Вторичная инициализация безусловных рефлексов с системой цепочек
        initializationStep = 10;
        GeneticReflexesSystem.InitializeWithChains(context.ReflexChains);

        // Шаг 11: Образы восприятия
        initializationStep = 11;
        PerceptionImagesSystem.InitializeInstance(context.Gomeostas, context.GeneticReflexes);
        context.PerceptionImages = PerceptionImagesSystem.Instance;

        context.Gomeostas.SetPerceptionImagesSystem(context.PerceptionImages);
        context.InfluenceActions.SetPerceptionImagesSystem(context.PerceptionImages);
        context.SensorySystem.SetDependentSystems(context.GeneticReflexes, context.PerceptionImages);

        // Шаг 12: Условные рефлексы
        initializationStep = 12;
        ConditionedReflexesSystem.InitializeInstance(
            context.Gomeostas,
            context.GeneticReflexes,
            context.PerceptionImages);
        context.ConditionedReflexes = ConditionedReflexesSystem.Instance;

        // Шаг 13: Дерево рефлексов
        initializationStep = 13;
        ReflexTreeSystem.InitializeInstance(
            context.GeneticReflexes,
            context.ConditionedReflexes,
            context.PerceptionImages,
            context.ReflexChains);
        context.ReflexTree = ReflexTreeSystem.Instance;

        // Шаг 14: Сервис выполнения рефлексов
        initializationStep = 14;
        ReflexExecutionService.InitializeInstance(
            context.AdaptiveActions,
            context.InfluenceActions,
            context.GeneticReflexes,
            context.ConditionedReflexes);
        context.ReflexExecution = ReflexExecutionService.Instance;

        // Шаг 15: Сервис формирования условных рефлексов на основе временных корреляций
        initializationStep = 15;
        ConditionedReflexFormationService.InitializeInstance(
            context.Gomeostas,
            context.GeneticReflexes,
            context.ConditionedReflexes);
        context.ConditionedReflexFormation = ConditionedReflexFormationService.Instance;

        // Шаг 16: Активатор рефлексов
        initializationStep = 16;
        ReflexesActivator.InitializeInstance(
            context.Gomeostas,
            context.GeneticReflexes,
            context.ConditionedReflexes,
            context.InfluenceActions,
            context.ReflexTree,
            context.ReflexChains,
            context.ReflexExecution,
            context.AdaptiveActions,
            context.ConditionedReflexFormation);
        context.ReflexesActivator = ReflexesActivator.Instance;

        // Шаг 17: Логирование и глобальный таймер
        initializationStep = 17;
        context.ResearchLogger = new ResearchLogger(
            context.Gomeostas,
            context.PerceptionImages,
            context.ReflexesActivator,
            context.AdaptiveActions,
            logsDirectory: config.LogsFolder,
            logFileName: "AgentLogs",
            format: config.LogFormat,
            clearOnStart: config.ClearLogsOnStart,
            enabled: config.LogEnabled
        );

        // Шаг 18: Система образов действий оператора и агента ИИ
        initializationStep = 18;
        ActionsImagesSystem.InitializeInstance(config.PsychicDataFolder);
        context.ActionsImages = ActionsImagesSystem.Instance;

        // Шаг 19: Система дерева автоматизмов
        initializationStep = 19;
        AutomatizmTreeSystem.InitializeInstance(config.PsychicDataFolder);
        context.AutomatizmTree = AutomatizmTreeSystem.Instance;

        // Создать базовую структуру дерева, если она пустая
        if (context.AutomatizmTree.Tree.Children.Count == 0)
          context.AutomatizmTree.CreateBasicAutomatizmTree();

        // Шаг 20: Система автоматизмов
        initializationStep = 20;
        AutomatizmSystem.InitializeInstance(config.PsychicDataFolder);
        context.AutomatizmSystem = AutomatizmSystem.Instance;

        // Шаг 21: Система эмоций
        initializationStep = 21;
        EmotionsImageSystem.InitializeInstance(config.PsychicDataFolder);
        context.EmotionsImageSystem = EmotionsImageSystem.Instance;

        // Шаг 22: Система вербальных образов
        initializationStep = 22;
        VerbalBrocaImagesSystem.InitializeInstance(config.PsychicDataFolder);
        context.VerbalBrocaImagesSystem = VerbalBrocaImagesSystem.Instance;

        // Шаг 23: Система психики
        initializationStep = 23;
        PsychicSystem.InitializeInstance(
          context.AutomatizmSystem, 
          context.AutomatizmTree, 
          context.InfluenceActionsImages,
          context.ActionsImages,
          context.EmotionsImageSystem,
          context.SensorySystem,
          context.VerbalBrocaImagesSystem,
          context.Gomeostas);
          context.PsychicSystem = PsychicSystem.Instance;

        // Шаг 24: Система управления гомеостатическими целями
        initializationStep = 24;
        PurposeGeneticImageSystem.InitializeInstance(
          context.InformationEnvironmentSystem, 
          context.ActionsImages,
          context.AutomatizmSystem);
        context.PurposeGeneticImageSystem = PurposeGeneticImageSystem.Instance;

        // Шаг 25: Система ориентировочного рефлпекса
        initializationStep = 25;
        OrientationReflexSystem.InitializeInstance(
          context.InformationEnvironmentSystem,
          context.PurposeGeneticImageSystem);
        var orientationReflex = OrientationReflexSystem.Instance;
        orientationReflex.SetDependencies(context.AutomatizmSystem, context.AutomatizmTree);
        // Связывание систем
        PsychicSystem.Instance.SetOrientationReflexSystem(orientationReflex);

        context.Gomeostas.SetResearchLogger(context.ResearchLogger);
        context.ReflexesActivator.SetResearchLogger(context.ResearchLogger);
        context.ReflexesActivator.SetPsychicSystemm(context.PsychicSystem);

        if (config.MemoryLogWriter != null)
          context.ResearchLogger.SetMemoryLogWriter(config.MemoryLogWriter);

        GlobalTimer.InitializeSystems(
            context.Gomeostas,
            context.AdaptiveActions,
            context.ReflexesActivator,
            context.PsychicSystem);

        GlobalTimer.SetConditionedReflexesSystem(context.ConditionedReflexes);
        GlobalTimer.SetReflexFormationService(context.ConditionedReflexFormation);
      }
      catch (Exception ex)
      {
        throw new IsidaInitializationException(
            $"Ошибка инициализации ISIDA на шаге {initializationStep}: {ex.Message}",
            initializationStep,
            ex);
      }
    }

    private static void InitializeFileValidator(string logsPath)
    {
      if (!Directory.Exists(logsPath))
        Directory.CreateDirectory(logsPath);

      FileValidator.SetLogsPath(logsPath);
    }

    /// <summary>
    /// Название проекта
    /// </summary>
    public const string ProjectName = "ISIDA (Incremental System for Intelligent Development of Agents)";

    /// <summary>
    /// Версия проекта
    /// </summary>
    public const string ProjectVersion = "V1.2";

    /// <summary>
    /// Дата сборки
    /// </summary>
    public const string BuildDate = "2024.01.10";

    /// <summary>
    /// Краткое описание концепции проекта
    /// </summary>
    public const string ProjectDescription =
        "ISIDA (Incremental System for Intelligent Development of Agents) - архитектура для построения интеллектуальных агентов с поэтапным развитием " +
        "на основе иерархических гомеостатических механизмов и адаптивного поведения.";

    /// <summary>
    /// Полное теоретическое обоснование проекта
    /// </summary>
    public const string TheoreticalBasis =
      "Теоретическая основа - МВАП с принципами:\n\n" +
      "1. Инвариантности адаптивности: базовые механизмы развития не зависят от способа реализации.\n" +
      "2. Схемотехничности: адаптивные системы имеют строго причинно-следственную структуру.\n\n" +
      "Архитектура основана на поэтапном развитии агента, имитирующем филогенез и онтогенез. \n" +
      "Развитие начинается с нулевой стадии, затем последовательно формируются более сложные \n" +
      "навыки под управлением оператора и через взаимодействие со средой.";

    /// <summary>
    /// Ссылка на документацию проекта
    /// </summary>
    public const string DocumentationUrl = "https://scorcher.ru/isida/iadaptive_agents_guide.php";

    /// <summary>
    /// Авторы проекта
    /// </summary>
    public static readonly string[] ProjectAuthors = new string[]
    {
        "Основной разработчик: Парусников А.В.",
        "Концепция: Beast Project Team",
        "Теоретическая база: МВАП исследовательская группа"
    };

    /// <summary>
    /// Получает краткую информацию о проекте для отображения в диалоге "О программе"
    /// </summary>
    public static string GetAboutInfo()
    {
      return $"{ProjectName} {ProjectVersion}\n" +
             $"Сборка от {BuildDate}\n\n" +
             $"{ProjectDescription}\n\n" +
             $"Документация: {DocumentationUrl}";
    }

    /// <summary>
    /// Получает полную информацию о проекте для детального просмотра
    /// </summary>
    public static string GetDetailedInfo()
    {
      string authors = string.Join("\n", ProjectAuthors);

      return $"{ProjectName}\n" +
             $"Версия: {ProjectVersion}\n" +
             $"Дата сборки: {BuildDate}\n\n" +
             $"ОПИСАНИЕ ПРОЕКТА:\n{ProjectDescription}\n\n" +
             $"ТЕОРЕТИЧЕСКАЯ ОСНОВА:\n{TheoreticalBasis}\n\n" +
             $"АВТОРЫ:\n{authors}\n\n" +
             $"ДОКУМЕНТАЦИЯ: {DocumentationUrl}";
    }
  }

  /// <summary>
  /// Исключение, возникающее при ошибках инициализации библиотеки ISIDA
  /// </summary>
  public class IsidaInitializationException : Exception
  {
    /// <summary>
    /// Шаг инициализации, на котором произошла ошибка
    /// </summary>
    public int InitializationStep { get; }

    /// <summary>
    /// Инициализирует новый экземпляр исключения с указанием шага инициализации
    /// </summary>
    /// <param name="message">Сообщение об ошибке</param>
    /// <param name="step">Шаг инициализации, на котором произошла ошибка</param>
    /// <param name="innerException">Внутреннее исключение</param>
    public IsidaInitializationException(string message, int step, Exception innerException)
        : base(message, innerException)
    {
      InitializationStep = step;
    }

    /// <summary>
    /// Инициализирует новый экземпляр исключения с указанием шага инициализации
    /// </summary>
    /// <param name="message">Сообщение об ошибке</param>
    /// <param name="step">Шаг инициализации, на котором произошла ошибка</param>
    public IsidaInitializationException(string message, int step)
        : base(message)
    {
      InitializationStep = step;
    }
  }
}