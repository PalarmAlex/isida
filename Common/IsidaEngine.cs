using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Psychic.Automatism;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.IO;
using System.Threading;
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
    /// Директория с данными психики (образы действий оператора и симбионта ИИ)
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
    /// Директория для файлов загрукзки
    /// </summary>
    public string BootDataFolder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ISIDA",
        "BootData");

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
    /// Порог начала изменения глобального состояния симбионта
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
    /// Идентификатор типа темы по умолчанию (например, 4 — «Стимул с Пульта» / «Базовая тема»)
    /// </summary>
    public int DefaultThemeTypeId { get; set; } = 4;

    /// <summary>
    /// Порог распознавания для вербального канала
    /// </summary>
    public int RecognitionThreshold { get; set; } = 3;

    /// <summary>
    /// Период ожидания реакции оператора на действия автоматизма в пульсах
    /// </summary>
    public int WaitingPeriodForActionsVal { get; set; } = 30;

    /// <summary>Устарело: раньше делитель A в формуле loss = B + (age/A); формула фонового затухания заменена на горизонт <see cref="ThinkingCycleBackgroundFadeTargetPulses"/>.</summary>
    public int ThinkingCycleDecayAgeDivisor { get; set; } = 100;

    /// <summary>Устарело: раньше базовое снятие B за пульс; не используется в текущей формуле затухания фона.</summary>
    public int ThinkingCycleDecayBase { get; set; } = 1;

    /// <summary>Максимальный возраст главного цикла в пульсах до принудительного снятия.</summary>
    public int ThinkingCycleMainMaxAgePulses { get; set; } = 1000;

    /// <summary>
    /// Порог тишины (пульсов без стимула с пульта) для события «долго без оператора» и привязки темы по коду симбионта.
    /// </summary>
    public int NoOperatorStimulusSilencePulses { get; set; } = 30;

    /// <summary>
    /// Если true — на каждом пульсе значения параметров гомеостаза сдвигаются на величину Speed (со знаком).
    /// Если false — при пульсации сдвига нет; параметры меняются только при внешнем воздействии и аналогичных событиях.
    /// </summary>
    public bool HomeostasisPulseSpeedDriftEnabled { get; set; } = true;

    /// <summary>Целевой горизонт (пульсы), за который фоновый цикл с типичным весом (~100 после демоута главного) «естественно» разряжается до нуля.</summary>
    public int ThinkingCycleBackgroundFadeTargetPulses { get; set; } = 1000;

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
      BootDataFolder = Path.Combine(BaseDirectory, "BootData");
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
      if (string.IsNullOrEmpty(LogsFolder))
        throw new ArgumentException("LogsFolder не указан");
      if (string.IsNullOrEmpty(BootDataFolder))
        throw new ArgumentException("BootDataFolder не указан");
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
    /// Класс для загрузки автоматизмов из файла
    /// </summary>
    public AutomatizmFileLoader AutomatizmFileLoader { get; internal set; }

    /// <summary>
    /// Загрузчик базовых примитивов (эхо+цепочка) по шаблону для стадии 2.
    /// </summary>
    public Stage2PrimitivesLoader Stage2PrimitivesLoader { get; internal set; }

    /// <summary>
    /// Класс для загрузки безусловных рефлексов и цепочек из файла
    /// </summary>
    public GeneticReflexFileLoader GeneticReflexFileLoader { get; internal set; }

    /// <summary>
    /// Система управления цепочками автоматизмов
    /// </summary>
    public AutomatizmChainsSystem AutomatizmChainsSystem  { get; internal set; }

    /// <summary>
    /// Система конвертирования условных рефлексов в автоматизмы
    /// </summary>
    public ConditionedReflexToAutomatizmConverter ConditionedReflexToAutomatizm { get; internal set; }

    /// <summary>
    /// Система отслеживания результатов выполнения автоамтизмов
    /// </summary>
    public AutomatismResultTracker AutomatismResult { get; internal set; }

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
    /// Система образов действий симбионта или оператора
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
    /// Сервис зеркалирования автоматизмов (стадия 3).
    /// </summary>
    public MirrorAutomatizmService MirrorAutomatizmService { get; internal set; }

    /// <summary>
    /// Система управления эмоциями
    /// </summary>
    public EmotionsImageSystem EmotionsImageSystem { get; internal set; }

    /// <summary>
    /// Система управления вербальными образами
    /// </summary>
    public VerbalBrocaImagesSystem VerbalBrocaImagesSystem { get; internal set; }

    /// <summary>
    /// Система управления образами команды
    /// </summary>
    public CommandBrocaImagesSystem CommandBrocaImagesSystem { get; internal set; }

    /// <summary>
    /// Система управления информационной картиной
    /// </summary>
    public InformationEnvironmentSystem InformationEnvironmentSystem { get; internal set; }

    /// <summary>
    /// Система управления гомеостатическими целями
    /// </summary>
    public PurposeGeneticImageSystem PurposeGeneticImageSystem { get; internal set; }

    /// <summary>
    /// Сервис выполнения автоматизмов
    /// </summary>
    public AutomatismExecutionService AutomatismExecution { get; internal set; }

    /// <summary>
    /// Система ориентировочного рефлекса
    /// </summary>
    public OrientationReflexSystem OrientationReflex { get; internal set; }

    /// <summary>
    /// Справочник типов ситуаций
    /// </summary>
    public Psychic.Understanding.SituationTypeSystem SituationTypeSystem { get; internal set; }

    /// <summary>
    /// Система образов ситуаций
    /// </summary>
    public Psychic.Understanding.SituationImageSystem SituationImageSystem { get; internal set; }

    /// <summary>
    /// Дерево понимания ситуации (Understanding)
    /// </summary>
    public Psychic.Understanding.UnderstandingTreeSystem UnderstandingTreeSystem { get; internal set; }

    /// <summary>
    /// Ментальная эпизодическая память (цепочки инфо-функций по контексту узла проблемы, темы и цели).
    /// </summary>
    public Psychic.Understanding.MentalEpisodicTreeSystem MentalEpisodicTreeSystem { get; internal set; }

    /// <summary>
    /// Дерево проблем (для эпизодической памяти)
    /// </summary>
    public Psychic.Understanding.ProblemTreeSystem ProblemTree { get; internal set; }

    /// <summary>
    /// Моторная эпизодическая память
    /// </summary>
    public Psychic.Memory.Episodic.EpisodicMemorySystem EpisodicMemory { get; internal set; }

    /// <summary>
    /// Сервис записи правил в эпизодическую память
    /// </summary>
    public Psychic.Memory.Episodic.EpisodicMemoryRulesService EpisodicMemoryRulesService { get; internal set; }

    /// <summary>
    /// Сервис управления переключением между стадиями эволюции
    /// </summary>
    public EvolutionStageService EvolutionStageService { get; internal set; }

    /// <summary>
    /// Логгер исследований
    /// </summary>
    public ResearchLogger ResearchLogger { get; internal set; }

    private bool _disposed = false;

    /// <summary>
    /// Отменить период ожидания оценки и сбросить состояние зеркального диалога (цепочки эхо/сдвиг).
    /// Вызывать при клике по плашке сброса времени ожидания, чтобы при следующем прогоне не использовались
    /// устаревшие _dialogMirrorActive/_dialogTriggerNodeId и не перезаписывался Belief штатного сдвига эхо-автоматизмом.
    /// Вставляет пустой кадр в историю эпизодической памяти (разрыв цепочки правил реагирования).
    /// </summary>
    public void CancelWaitingPeriodAndResetMirror()
    {
      AppGlobalState.ForceStopWaitingForOperatorEvaluation();
      // Иначе в логе остаётся «активный» автоматизм и полезность после ручного сброса ожидания (шаг сценария без стимула).
      AppGlobalState.ResetAutomatizmInfo();
      MirrorAutomatizmService?.ResetDialogMirror();
      EpisodicMemory?.SetInterruption();
    }

    /// <summary>
    /// Освобождает ресурсы, используемые контекстом ISIDA
    /// </summary>
    /// <remarks>
    /// Порядок: сначала остановка <see cref="GlobalTimer"/> и сброс его статических ссылок (чтобы пульсы не шли во время выгрузки),
    /// затем подсистемы сверху вниз по зависимостям: психика и связанные деревья до нижележащих сенсоров/гомеостаза.
    /// <see cref="Psychic.PsychicSystem"/> освобождается до <see cref="Gomeostas.GomeostasSystem"/>, чтобы логика психики не обращалась к уже обнулённому <c>GomeostasSystem.Instance</c>.
    /// <see cref="Psychic.Automatism.MirrorAutomatizmService"/> входит в состав <see cref="Psychic.PsychicSystem"/> и не диспозится отдельно.
    /// </remarks>
    public void Dispose()
    {
      if (_disposed) return;

      Logger.Info("Начинается безопасное освобождение ресурсов...");

      try
      {
        ResearchLogger?.SuspendLogging();
        GlobalTimer.Stop();
        Thread.Sleep(200);
        SafeDispose(ResearchLogger, "ResearchLogger");
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
        Logger.Warning($"Ошибка при сохранении свойств симбионта: {ex.Message}");
      }

      SafeDispose(ConditionedReflexFormation, "ConditionedReflexFormation");

      SafeDispose(GeneticReflexFileLoader, "GeneticReflexFileLoader");
      SafeDispose(AutomatizmFileLoader, "AutomatizmFileLoader");
      SafeDispose(EpisodicMemory, "EpisodicMemory");
      SafeDispose(UnderstandingTreeSystem, "UnderstandingTreeSystem");
      SafeDispose(MentalEpisodicTreeSystem, "MentalEpisodicTreeSystem");
      if (Psychic.Understanding.ThemeImageSystem.IsInitialized)
        SafeDispose(Psychic.Understanding.ThemeImageSystem.Instance, "ThemeImageSystem");
      if (Psychic.Understanding.PurposeImageSystem.IsInitialized)
        SafeDispose(Psychic.Understanding.PurposeImageSystem.Instance, "PurposeImageSystem");
      SafeDispose(SituationImageSystem, "SituationImageSystem");
      SafeDispose(SituationTypeSystem, "SituationTypeSystem");
      SafeDispose(ProblemTree, "ProblemTree");
      SafeDispose(PsychicSystem, "PsychicSystem");
      SafeDispose(ConditionedReflexToAutomatizm, "ConditionedReflexToAutomatizm");
      SafeDispose(VerbalBrocaImagesSystem, "VerbalBrocaImagesSystem");
      SafeDispose(CommandBrocaImagesSystem, "CommandBrocaImagesSystem");
      SafeDispose(EmotionsImageSystem, "EmotionsImageSystem");
      SafeDispose(AutomatismResult, "AutomatismResult");
      SafeDispose(AutomatizmChainsSystem, "AutomatizmChainsSystem");
      SafeDispose(AutomatizmSystem, "AutomatizmSystem");
      SafeDispose(AutomatizmTree, "AutomatizmTree");
      SafeDispose(PurposeGeneticImageSystem, "PurposeGeneticImageSystem");
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
      SafeDispose(EvolutionStageService, "EvolutionStageService");
      SafeDispose(Gomeostas, "Gomeostas");
      SafeDispose(InfluenceActions, "InfluenceActions");
      SafeDispose(AutomatismExecution, "AutomatismExecution");
      SafeDispose(OrientationReflex, "OrientationReflex");
      SafeDispose(InformationEnvironmentSystem, "InformationEnvironmentSystem");
      AgentSleepOrchestrator.Reset();

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
        ReflexesActivator != null &&
        ReflexTree != null &&
        ReflexChains != null &&
        ReflexExecution != null &&
        ConditionedReflexFormation != null &&

        // Системы автоматизмов
        ActionsImages != null &&
        InfluenceActionsImages != null &&
        AutomatizmTree != null &&
        AutomatizmSystem != null &&
        AutomatismExecution != null &&
        AutomatismResult != null &&
        AutomatizmFileLoader != null &&

        // Системы психики
        ProblemTree != null &&
        MentalEpisodicTreeSystem != null &&
        EpisodicMemory != null &&
        EpisodicMemoryRulesService != null &&
        PsychicSystem != null &&
        MirrorAutomatizmService != null &&
        EmotionsImageSystem != null &&
        VerbalBrocaImagesSystem != null &&
        CommandBrocaImagesSystem != null &&
        InformationEnvironmentSystem != null &&
        PurposeGeneticImageSystem != null &&
        OrientationReflex != null &&

        // Дополнительные сервисы
        ConditionedReflexToAutomatizm != null &&
        EvolutionStageService != null &&
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
      try
      {
        InitializeEngine(context, config);
        return context;
      }
      catch
      {
        try
        {
          context.Dispose();
        }
        catch (Exception disposeEx)
        {
          Logger.Warning($"Откат частичной инициализации ISIDA: {disposeEx.Message}");
        }

        throw;
      }
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

        AgentSleepOrchestrator.Initialize(context.InformationEnvironmentSystem);

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
        context.SensorySystem.CommandRecognitionThreshold = config.RecognitionThreshold;

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

        GeneticReflexFileLoader.InitializeInstance(config.BootDataFolder);
        context.GeneticReflexFileLoader = GeneticReflexFileLoader.Instance;

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
            context.ConditionedReflexFormation,
            context.PerceptionImages);
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
        GlobalTimer.SetResearchLogger(context.ResearchLogger);

        // Шаг 18: Система образов действий оператора и симбионта ИИ
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

        // Шаг 19a: Справочник типов ситуаций и образы ситуаций (для Understanding)
        initializationStep = 20;
        Psychic.Understanding.SituationTypeSystem.InitializeInstance(config.PsychicDataFolder);
        context.SituationTypeSystem = Psychic.Understanding.SituationTypeSystem.Instance;
        Psychic.Understanding.SituationImageSystem.InitializeInstance(config.PsychicDataFolder, context.SituationTypeSystem);
        context.SituationImageSystem = Psychic.Understanding.SituationImageSystem.Instance;

        // Шаг 20: Образы тем и целей (для дерева проблем, 4 уровня)
        initializationStep = 21;
        Psychic.Understanding.ThemeImageSystem.InitializeInstance(config.PsychicDataFolder);
        Psychic.Understanding.ThemeImageSystem.Instance.DefaultThemeTypeId = config.DefaultThemeTypeId;
        Psychic.Understanding.PurposeImageSystem.InitializeInstance(config.PsychicDataFolder);

        // Шаг 21: Дерево проблем (для эпизодической памяти)
        initializationStep = 22;
        Psychic.Understanding.ProblemTreeSystem.InitializeInstance(config.PsychicDataFolder);
        context.ProblemTree = Psychic.Understanding.ProblemTreeSystem.Instance;
        context.AutomatizmTree.SetProblemTree(context.ProblemTree);

        // Шаг 22: Дерево понимания ситуации (Understanding)
        initializationStep = 23;
        Psychic.Understanding.UnderstandingTreeSystem.InitializeInstance(config.PsychicDataFolder);
        context.UnderstandingTreeSystem = Psychic.Understanding.UnderstandingTreeSystem.Instance;
        context.UnderstandingTreeSystem.SetDependencies(
          context.SituationImageSystem,
          context.SituationTypeSystem,
          Psychic.Understanding.ThemeImageSystem.Instance,
          Psychic.Understanding.PurposeImageSystem.Instance);

        Psychic.Understanding.MentalEpisodicTreeSystem.InitializeInstance(config.PsychicDataFolder);
        context.MentalEpisodicTreeSystem = Psychic.Understanding.MentalEpisodicTreeSystem.Instance;

        // Шаг 24: Система автоматизмов
        initializationStep = 24;
        AutomatizmSystem.InitializeInstance(config.PsychicDataFolder);
        context.AutomatizmSystem = AutomatizmSystem.Instance;

        // Шаг 25: Система эмоций
        initializationStep = 25;
        EmotionsImageSystem.InitializeInstance(config.PsychicDataFolder);
        context.EmotionsImageSystem = EmotionsImageSystem.Instance;

        // Шаг 25: Система вербальных образов
        initializationStep = 25;
        VerbalBrocaImagesSystem.InitializeInstance(config.PsychicDataFolder);
        context.VerbalBrocaImagesSystem = VerbalBrocaImagesSystem.Instance;

        CommandBrocaImagesSystem.InitializeInstance(config.PsychicDataFolder);
        context.CommandBrocaImagesSystem = CommandBrocaImagesSystem.Instance;

        // Шаг 26: Сервис отслеживания выполнения резульатов автоматизмов
        initializationStep = 26;
        AutomatismResultTracker.InitializeInstance(context.AutomatizmSystem);
        context.AutomatismResult = AutomatismResultTracker.Instance;

        // Шаг 27: Моторная эпизодическая память
        initializationStep = 27;
        Psychic.Memory.Episodic.EpisodicMemorySystem.InitializeInstance(
          config.PsychicDataFolder,
          context.AutomatizmTree,
          context.ProblemTree,
          context.UnderstandingTreeSystem,
          context.InformationEnvironmentSystem,
          context.Gomeostas,
          context.ActionsImages);
        context.EpisodicMemory = Psychic.Memory.Episodic.EpisodicMemorySystem.Instance;
        context.EpisodicMemoryRulesService = new Psychic.Memory.Episodic.EpisodicMemoryRulesService(context.EpisodicMemory);
        context.AutomatismResult.SetEpisodicMemoryRulesService(context.EpisodicMemoryRulesService);

        // Шаг 28: Система психики
        initializationStep = 28;
        PsychicSystem.InitializeInstance(
          context.AutomatizmSystem, 
          context.AutomatizmTree, 
          context.InfluenceActionsImages,
          context.InfluenceActions,
          context.ActionsImages,
          context.EmotionsImageSystem,
          context.SensorySystem,
          context.VerbalBrocaImagesSystem,
          context.CommandBrocaImagesSystem,
          context.AutomatismResult,
          context.Gomeostas);
          context.PsychicSystem = PsychicSystem.Instance;
          context.MirrorAutomatizmService = context.PsychicSystem.MirrorAutomatizmService;        

        // Шаг 29: Система управления гомеостатическими целями
        initializationStep = 29;
        PurposeGeneticImageSystem.InitializeInstance(
          context.InformationEnvironmentSystem, 
          context.ActionsImages,
          context.AutomatizmSystem,
          context.AdaptiveActions);
        context.PurposeGeneticImageSystem = PurposeGeneticImageSystem.Instance;

        // Шаг 30: Класс для загрузки автоматизмов из файла
        initializationStep = 30;
        AutomatizmFileLoader.InitializeInstance(config.BootDataFolder);
        context.AutomatizmFileLoader = AutomatizmFileLoader.Instance;

        // Шаг 31: Сервис выполнения автоматизмов
        initializationStep = 31;
        AutomatismExecutionService.InitializeInstance(
            context.AdaptiveActions,
            context.ActionsImages);
        context.AutomatismExecution = AutomatismExecutionService.Instance;

        // Шаг 32: Система ориентировочного рефлекса
        initializationStep = 32;
        OrientationReflexSystem.InitializeInstance(
            context.InformationEnvironmentSystem,
            context.PurposeGeneticImageSystem);
        context.OrientationReflex = OrientationReflexSystem.Instance;
        context.OrientationReflex.SetDependencies(context.AutomatizmSystem, context.AutomatizmTree);

        // Шаг 33: Система управления цепочками автоматизмов
        initializationStep = 33;
        AutomatizmChainsSystem.InitializeInstance(context.AutomatizmSystem);
        context.AutomatizmChainsSystem = AutomatizmChainsSystem.Instance;

        // Шаг 34: Сервис выполнения автоматизмов
        initializationStep = 34;
        AutomatismExecutionService.InitializeWithDependencies(
            context.AutomatizmSystem,
            context.PsychicSystem,
            context.AutomatizmChainsSystem);
        context.AutomatismExecution = AutomatismExecutionService.Instance;

        context.PsychicSystem.SetPsychicSystemDop(
          context.AutomatismExecution,
          context.OrientationReflex,
          context.PerceptionImages,
          context.EpisodicMemory,
          context.UnderstandingTreeSystem,
          context.ProblemTree,
          context.InformationEnvironmentSystem,
          context.MentalEpisodicTreeSystem,
          context.AutomatizmChainsSystem);
        if (context.EpisodicMemory != null)
          context.OrientationReflex.SetEpisodicMemorySystem(context.EpisodicMemory);
        context.OrientationReflex.SetUnderstandingTreeSystem(context.UnderstandingTreeSystem);
        context.Gomeostas.SetResearchLogger(context.ResearchLogger);
        context.ReflexesActivator.SetResearchLogger(context.ResearchLogger);
        context.AutomatismExecution.SetResearchLogger(context.ResearchLogger);
        context.ReflexesActivator.SetPsychicSystemm(context.PsychicSystem);
        AppGlobalState.WaitingPeriodForActionsVal = config.WaitingPeriodForActionsVal > 0
            ? config.WaitingPeriodForActionsVal
            : 30;
        AppGlobalState.NoOperatorStimulusSilencePulses = config.NoOperatorStimulusSilencePulses;
        AppGlobalState.HomeostasisPulseSpeedDriftEnabled = config.HomeostasisPulseSpeedDriftEnabled;
        context.PsychicSystem.ApplyThinkingCyclesConfig(
            config.ThinkingCycleDecayAgeDivisor,
            config.ThinkingCycleDecayBase,
            config.ThinkingCycleMainMaxAgePulses,
            config.ThinkingCycleBackgroundFadeTargetPulses);

        // Шаг 35: Сервис конвертирования условных рефлексов в автоматизмы
        initializationStep = 35;
        ConditionedReflexToAutomatizmConverter.InitializeInstance(
            context.ConditionedReflexes,
            context.GeneticReflexes,
            context.AdaptiveActions,
            context.EmotionsImageSystem,
            context.ActionsImages,
            context.AutomatizmTree,
            context.AutomatizmSystem,
            context.PerceptionImages,
            context.SensorySystem,
            context.VerbalBrocaImagesSystem,
            context.ReflexChains,
            context.InfluenceActionsImages,
            context.AutomatizmChainsSystem);
        context.ConditionedReflexToAutomatizm = ConditionedReflexToAutomatizmConverter.Instance;
        context.PurposeGeneticImageSystem.SetDopPurposeGeneticImageSystem(
          context.ConditionedReflexToAutomatizm,
          context.AutomatizmChainsSystem);
        context.PurposeGeneticImageSystem.SetStage2EchoDependencies(
          context.MirrorAutomatizmService,
          context.VerbalBrocaImagesSystem,
          context.SensorySystem);

        // Шаг 33a: Загрузчик базовых примитивов по шаблону (стадия 2)
        context.Stage2PrimitivesLoader = new Stage2PrimitivesLoader(
          context.Gomeostas,
          context.EmotionsImageSystem,
          context.SensorySystem,
          context.VerbalBrocaImagesSystem,
          context.AutomatizmTree,
          context.ActionsImages,
          context.MirrorAutomatizmService);

        // Шаг 36: Сервис переключения стадий эволюции (ссылки на системы Understanding передаём явно, без перекрёстных обращений через Instance)
        initializationStep = 36;
        EvolutionStageService.InitializeInstance(
            context.AutomatizmSystem,
            context.ConditionedReflexes,
            context.AutomatizmTree,
            context.EpisodicMemory,
            context.ProblemTree,
            Psychic.Understanding.PurposeImageSystem.Instance,
            context.SituationImageSystem,
            Psychic.Understanding.ThemeImageSystem.Instance,
            context.UnderstandingTreeSystem,
            context.PsychicSystem);
        context.EvolutionStageService = EvolutionStageService.Instance;
        context.Gomeostas.SetEvolutionStageService(context.EvolutionStageService);

        if (config.MemoryLogWriter != null)
          context.ResearchLogger.SetMemoryLogWriter(config.MemoryLogWriter);
        context.PsychicSystem.AttachResearchLoggerForThinkingCycleClosure(context.ResearchLogger);

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
    public const string ProjectName = "ISIDA (Intelligent Symbiotic Integrator for Distributed Adaptation)";

    /// <summary>
    /// Версия проекта
    /// </summary>
    public const string ProjectVersion = "V3.2";

    /// <summary>
    /// Дата сборки
    /// </summary>
    public const string BuildDate = "2026.04.26";

    /// <summary>
    /// Краткое описание концепции проекта
    /// </summary>
    public const string ProjectDescription =
      "ISIDA (Intelligent Symbiotic Integrator for Distributed Adaptation) — архитектура для построения " +
      "автономных симбионтов с поэтапным развитием на основе иерархических гомеостатических механизмов и " +
      "адаптивного поведения.\n\n" +
      "Симбионт — цифровое живое существо, которое не выполняет внешние команды, " +
      "а поддерживает внутренний гомеостаз через непрерывное взаимодействие со средой. Восприятие, действие, " +
      "память, формирование моделей мира — все его проявления подчинены не внешней цели, а стремлению удержать " +
      "параметры своего существования в целевых пределах. Архитектура не предписывает симбионту ни примитивности, " +
      "ни сложности — иерархия гомеостатов может порождать как простые, так и высокоразвитые формы поведения, " +
      "включая целеполагание, рефлексию и обучение.";

    /// <summary>
    /// Полное теоретическое обоснование проекта
    /// </summary>
    public const string TheoreticalBasis =
      "Теоретическая основа - МВАП с принципами:\n\n" +
      "1. Инвариантности адаптивности: базовые механизмы развития не зависят от способа реализации.\n" +
      "2. Схемотехничности: адаптивные системы имеют строго причинно-следственную структуру.\n\n" +
      "Архитектура основана на поэтапном развитии симбионта, имитирующем филогенез и онтогенез. \n" +
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
        "Теоретическая база: МВАП, Петрийчук Н.Д."
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