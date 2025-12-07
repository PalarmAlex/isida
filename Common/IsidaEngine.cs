using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.IO;
using static ISIDA.Common.ResearchLogger;

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
    /// Логгер исследований
    /// </summary>
    public ResearchLogger ResearchLogger { get; internal set; }

    /// <summary>
    /// Освобождает ресурсы, используемые контекстом ISIDA
    /// </summary>
    public void Dispose()
    {
      ResearchLogger?.Dispose();
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
        ReflexesActivator != null &&
        ReflexTree != null &&
        ReflexChains != null &&
        ReflexExecution != null &&
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
        GomeostasSystem.InitializeInstance(config.GomeostasFolder);
        context.Gomeostas = GomeostasSystem.Instance;

        // Шаг 3: Адаптивные действия
        initializationStep = 3;
        AdaptiveActionsSystem.InitializeInstance(context.Gomeostas, config.ActionsFolder);
        context.AdaptiveActions = AdaptiveActionsSystem.Instance;

        // Шаг 4: Внешние действия
        initializationStep = 4;
        InfluenceActionSystem.InitializeInstance(context.Gomeostas, config.ActionsFolder);
        context.InfluenceActions = InfluenceActionSystem.Instance;

        // Шаг 5: Сенсорная система
        initializationStep = 5;
        SensorySystem.InitializeInstance(context.Gomeostas, config.SensorsFolder);
        context.SensorySystem = SensorySystem.Instance;

        // Шаг 6: Безусловные рефлексы
        initializationStep = 6;
        GeneticReflexesSystem.InitializeInstance(context.Gomeostas, config.ReflexesFolder);
        context.GeneticReflexes = GeneticReflexesSystem.Instance;

        // Шаг 7: Система цепочек рефлексов
        initializationStep = 7;
        ReflexChainsSystem.InitializeInstance(context.GeneticReflexes, context.AdaptiveActions);
        context.ReflexChains = ReflexChainsSystem.Instance;

        // Шаг 8: Вторичная инициализация безусловных рефлексов с системой цепочек
        initializationStep = 8;
        GeneticReflexesSystem.InitializeWithChains(context.ReflexChains);

        // Шаг 9: Образы восприятия
        initializationStep = 9;
        PerceptionImagesSystem.InitializeInstance(context.Gomeostas, context.GeneticReflexes);
        context.PerceptionImages = PerceptionImagesSystem.Instance;

        context.Gomeostas.SetPerceptionImagesSystem(context.PerceptionImages);
        context.InfluenceActions.SetPerceptionImagesSystem(context.PerceptionImages);
        context.SensorySystem.SetDependentSystems(context.GeneticReflexes, context.PerceptionImages);

        // Шаг 10: Условные рефлексы
        initializationStep = 10;
        ConditionedReflexesSystem.InitializeInstance(
            context.Gomeostas,
            context.GeneticReflexes,
            context.PerceptionImages);
        context.ConditionedReflexes = ConditionedReflexesSystem.Instance;

        // Шаг 11: Дерево рефлексов
        initializationStep = 11;
        ReflexTreeSystem.InitializeInstance(
            context.GeneticReflexes,
            context.PerceptionImages,
            context.ReflexChains);
        context.ReflexTree = ReflexTreeSystem.Instance;

        // Шаг 12: Сервис выполнения рефлексов
        initializationStep = 12;
        ReflexExecutionService.InitializeInstance(
            context.AdaptiveActions,
            context.InfluenceActions,
            context.GeneticReflexes,
            context.ConditionedReflexes);
        context.ReflexExecution = ReflexExecutionService.Instance;

        // Шаг 13: Активатор рефлексов
        initializationStep = 13;
        ReflexesActivator.InitializeInstance(
            context.Gomeostas,
            context.GeneticReflexes,
            context.ConditionedReflexes,
            context.InfluenceActions,
            context.ReflexTree,
            context.ReflexChains,
            context.ReflexExecution,
            context.AdaptiveActions);
        context.ReflexesActivator = ReflexesActivator.Instance;

        // Шаг 14: Логирование и глобальный таймер
        initializationStep = 14;
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

        context.Gomeostas.SetResearchLogger(context.ResearchLogger);
        context.ReflexesActivator.SetResearchLogger(context.ResearchLogger);

        if (config.MemoryLogWriter != null)
          context.ResearchLogger.SetMemoryLogWriter(config.MemoryLogWriter);

        GlobalTimer.InitializeSystems(
            context.Gomeostas,
            context.AdaptiveActions,
            context.ReflexesActivator);

        // Шаг 15: Применение конфигурации
        initializationStep = 15;
        context.Gomeostas.DefaultStileId = config.DefaultStileId;
        context.Gomeostas.CompareLevel = config.CompareLevel;
        context.Gomeostas.DifSensorPar = config.DifSensorPar;
        context.Gomeostas.DynamicTime = config.DynamicTime;
        context.AdaptiveActions.ReflexActionDisplayDuration = config.ReflexActionDisplayDuration;
        context.AdaptiveActions.DefaultAdaptiveActionId = config.DefaultAdaptiveActionId;
        context.SensorySystem.VerbalRecognitionThreshold = config.RecognitionThreshold;
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