using ISIDA.Actions;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using ISIDA.Sensors;
using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Newtonsoft.Json;
using static ISIDA.Actions.AdaptiveActionsSystem;
using static ISIDA.Gomeostas.GomeostasSystem;
using static ISIDA.Reflexes.GeneticReflexesSystem;

namespace ISIDA.Common
{
  /// <summary>
  /// Активная система логирования - самостоятельно собирает и записывает данные состояния системы
  /// </summary>
  public sealed class ResearchLogger : IDisposable
  {
    #region Форматы логирования

    /// <summary>
    /// Форматы сохранения логов
    /// </summary>
    [Flags]
    public enum LogFormat
    {
      /// <summary>
      /// Без сохранения
      /// </summary>
      None = 0,

      /// <summary>
      /// JSON Lines формат (.jsonl)
      /// </summary>
      JsonL = 1,

      /// <summary>
      /// CSV формат (.csv)
      /// </summary>
      Csv = 2,

      /// <summary>
      /// Оба формата одновременно
      /// </summary>
      All = JsonL | Csv
    }

    #endregion

    #region Поля и свойства

    // Зависимости для сбора данных
    private readonly GomeostasSystem _gomeostas;
    private readonly PerceptionImagesSystem _perception;
    private readonly ReflexesActivator _reflexesActivator;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private readonly HomeostasisCalculator _homeostasisCalculator;

    // Состояние для отслеживания изменений
    private SystemState _lastState;
    private ParametersState _lastParametersState;
    private StylesState _lastStylesState;

    // Писатели в файлы
    private readonly string _logFilePath;
    private readonly StreamWriter _jsonlWriter;
    private readonly StreamWriter _csvWriter;
    private readonly StreamWriter _parametersJsonlWriter;
    private readonly StreamWriter _parametersCsvWriter;
    private readonly StreamWriter _stylesJsonlWriter;
    private readonly StreamWriter _stylesCsvWriter;
    private bool _enabled = false;
    private readonly object _lock = new object();
    private bool _disposed = false;
    private LogFormat _currentFormat;
    private readonly HashSet<string> _csvHeaders = new HashSet<string>();
    private bool _csvHeadersWritten = false;
    private bool _parametersHeadersWritten = false;
    private bool _stylesHeadersWritten = false;

    /// <summary>
    /// Флаг освобождения ресурсов
    /// </summary>
    public bool IsDisposed => _disposed;

    // Писатель в память (для UI)
    private static ILogWriter _memoryLogWriter;

    private Dictionary<string, object> _currentPulseLogEntry = null;
    private int _bufferedPulse = -1;

    // Логирование цепочек
    private readonly Dictionary<int, ActiveChainSession> _activeChains = new Dictionary<int, ActiveChainSession>();
    private readonly HashSet<string> _chainsLoggedInCycle = new HashSet<string>();

    private readonly Dictionary<int, (string ReflexChain, string AutomatizmChain)> _chainInfoByPulse
    = new Dictionary<int, (string, string)>();

    #endregion

    #region Внутренние классы

    /// <summary>
    /// Состояние системы для логирования
    /// </summary>
    private class SystemState
    {
      public int Pulse { get; set; }
      public DateTime Time { get; set; }
      public int? CurrentBaseID { get; set; }
      public int? CurrentBaseStyleID { get; set; }
      public int? CurrentTriggerStimulusID { get; set; }
      public int? CurrentGeneticReflexID { get; set; }
      public int? CurrentConditionReflexID { get; set; }
      public int? CurrentAutomatizmID { get; set; }
      public int? HasCriticalChanges { get; set; }
      public int? OrientationReflexType { get; set; }
      public int? OrientationReflexPulse { get; set; }
      // Поля для логирования цепочек
      public string LastReflexChainInfo { get; set; }  // "ChainId:ActionId"
      public string LastAutomatizmChainInfo { get; set; }  // "ChainId:ActionId"
    }

    /// <summary>
    /// Состояние параметров гомеостаза для логирования
    /// </summary>
    private class ParametersState
    {
      public int Pulse { get; set; }
      public DateTime Time { get; set; }
      public List<ParameterLogData> Parameters { get; set; } = new List<ParameterLogData>();
    }

    /// <summary>
    /// Данные параметра для логирования
    /// </summary>
    private class ParameterLogData
    {
      public int Id { get; set; }
      public string Name { get; set; }
      public int Weight { get; set; }
      public int NormaWell { get; set; }
      public int Speed { get; set; }
      public float Value { get; set; }
      public float UrgencyFunction { get; set; }
      public string ParameterState { get; set; }
      public string ActivationZone { get; set; }
    }

    /// <summary>
    /// Состояние стилей для логирования
    /// </summary>
    private class StylesState
    {
      public int Pulse { get; set; }
      public DateTime Time { get; set; }
      public List<StyleLogData> AfterInhibition { get; set; } = new List<StyleLogData>();
      public List<StyleActivationLog> Activations { get; set; } = new List<StyleActivationLog>();
      public List<StyleParameterActivation> ParameterActivations { get; set; } = new List<StyleParameterActivation>();
    }

    /// <summary>
    /// Данные стиля для логирования
    /// </summary>
    private class StyleLogData
    {
      public int Id { get; set; }
      public string Name { get; set; }
    }

    /// <summary>
    /// Данные активации стиля от параметра
    /// </summary>
    public class StyleParameterActivation
    {
      /// <summary>
      /// Номер пульса
      /// </summary>
      public int Pulse { get; set; }

      /// <summary>
      /// Время активации
      /// </summary>
      public DateTime Time { get; set; }

      /// <summary>
      /// Стадия процесса
      /// </summary>
      public string Stage { get; set; }

      /// <summary>
      /// ID параметра
      /// </summary>
      public int ParameterId { get; set; }

      /// <summary>
      /// Имя параметра
      /// </summary>
      public string ParameterName { get; set; }

      /// <summary>
      /// ID зоны активации (0-6)
      /// </summary>
      public int ZoneId { get; set; }

      /// <summary>
      /// Описание зоны
      /// </summary>
      public string ZoneDescription { get; set; }

      /// <summary>
      /// ID стиля
      /// </summary>
      public int StyleId { get; set; }

      /// <summary>
      /// Имя стиля
      /// </summary>
      public string StyleName { get; set; }

      /// <summary>
      /// Детали активации
      /// </summary>
      public string ActivationDetails { get; set; }
    }

    /// <summary>
    /// Событие выполнения цепочки рефлексов или автоматизмов
    /// </summary>
    private class ChainExecutionEvent
    {
      /// <summary>
      /// Номер пульса начала события
      /// </summary>
      public int Pulse { get; set; }

      /// <summary>
      /// Время события
      /// </summary>
      public DateTime Time { get; set; }

      /// <summary>
      /// Тип цепочки: "Reflex" или "Automatizm"
      /// </summary>
      public string ChainType { get; set; }

      /// <summary>
      /// ID цепочки
      /// </summary>
      public int ChainId { get; set; }

      /// <summary>
      /// Имя цепочки
      /// </summary>
      public string ChainName { get; set; }

      /// <summary>
      /// Тип события: "ChainStart", "LinkExecute", "Evaluation", "BranchDecision", "ChainComplete"
      /// </summary>
      public string EventType { get; set; }

      /// <summary>
      /// ID звена цепочки (для событий выполнения звена)
      /// </summary>
      public int? LinkId { get; set; }

      /// <summary>
      /// ID действия (для событий выполнения звена)
      /// </summary>
      public int? ActionId { get; set; }

      /// <summary>
      /// Результат выполнения действия (успех/неудача)
      /// </summary>
      public bool? ActionSuccess { get; set; }

      /// <summary>
      /// Оценка оператора (true=успех, false=неудача, null=ожидание)
      /// </summary>
      public bool? OperatorEvaluation { get; set; }

      /// <summary>
      /// ID следующего звена после ветвления
      /// </summary>
      public int? NextLinkId { get; set; }

      /// <summary>
      /// Тип выбранной ветви: "Success" или "Failure"
      /// </summary>
      public string BranchType { get; set; }

      /// <summary>
      /// Основная причина/описание события
      /// </summary>
      public string Details { get; set; }
    }

    /// <summary>
    /// Состояние выполняемой цепочки для отслеживания в сессии
    /// </summary>
    private class ActiveChainSession
    {
      /// <summary>
      /// ID цепочки
      /// </summary>
      public int ChainId { get; set; }

      /// <summary>
      /// Имя цепочки
      /// </summary>
      public string ChainName { get; set; }

      /// <summary>
      /// Тип цепочки
      /// </summary>
      public string ChainType { get; set; }

      /// <summary>
      /// Пульс начала цепочки
      /// </summary>
      public int StartPulse { get; set; }

      /// <summary>
      /// Время начала
      /// </summary>
      public DateTime StartTime { get; set; }

      /// <summary>
      /// Последний выполненный ID звена
      /// </summary>
      public int LastLinkId { get; set; }

      /// <summary>
      /// Последняя оценка оператора
      /// </summary>
      public bool? LastEvaluation { get; set; }

      /// <summary>
      /// Список выполненных звеньев в порядке выполнения
      /// </summary>
      public List<int> ExecutedLinks { get; set; } = new List<int>();

      /// <summary>
      /// История оценок оператора на каждом звене
      /// </summary>
      public Dictionary<int, bool?> EvaluationHistory { get; set; } = new Dictionary<int, bool?>();
    }

    /// <summary>
    /// Данные активации стиля поведения для логирования процесса определения активных стилей
    /// </summary>
    /// <remarks>
    /// Содержит информацию о том, как параметр гомеостаза активирует определенные стили поведения
    /// на основе своего текущего состояния и зоны активации.
    /// </remarks>
    public class StyleActivationLog
    {
      /// <summary>
      /// Уникальный идентификатор параметра гомеостаза
      /// </summary>
      public int ParameterId { get; set; }

      /// <summary>
      /// Наименование параметра гомеостаза
      /// </summary>
      public string ParameterName { get; set; }

      /// <summary>
      /// Идентификатор состояния параметра для активации стилей (0-6)
      /// </summary>
      /// <value>
      /// Целое число в диапазоне 0-6:
      /// <list type="bullet">
      /// <item><description>0 - Выход из нормы</description></item>
      /// <item><description>1 - Возврат в норму</description></item>
      /// <item><description>2 - Норма</description></item>
      /// <item><description>3 - Слабое отклонение</description></item>
      /// <item><description>4 - Умеренное отклонение</description></item>
      /// <item><description>5 - Значительное отклонение</description></item>
      /// <item><description>6 - Сильное отклонение</description></item>
      /// </list>
      /// </value>
      public int StateId { get; set; }

      /// <summary>
      /// Текстовое описание состояния параметра
      /// </summary>
      public string StateDescription { get; set; }

      /// <summary>
      /// Список идентификаторов стилей поведения, активируемых данным состоянием параметра
      /// </summary>
      public List<int> ActivatedStyles { get; set; } = new List<int>();

      /// <summary>
      /// Детальная информация о процессе активации стилей
      /// </summary>
      public string ActivationDetails { get; set; }
    }

    #endregion

    #region Конструктор

    /// <summary>
    /// Создает экземпляр активного логгера
    /// </summary>
    /// <param name="gomeostas">Система гомеостаза</param>
    /// <param name="perception">Система образов восприятия</param>
    /// <param name="reflexesActivator">Активатор рефлексов</param>
    /// <param name="adaptiveActions">Адаптивные действия</param> 
    /// <param name="logsDirectory">Каталог логов</param>  
    /// <param name="logFileName">Имя файла логов (без расширения)</param>
    /// <param name="format">Форматы сохранения логов</param>
    /// <param name="clearOnStart">Очищать файл при создании</param>
    /// <param name="enabled">Включить логирование</param>
    public ResearchLogger(
        GomeostasSystem gomeostas,
        PerceptionImagesSystem perception,
        ReflexesActivator reflexesActivator,
        AdaptiveActionsSystem adaptiveActions,
        string logsDirectory = null,
        string logFileName = "AgentLogs",
        LogFormat format = LogFormat.All,
        bool clearOnStart = true,
        bool enabled = true)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _perception = perception ?? throw new ArgumentNullException(nameof(perception));
      _reflexesActivator = reflexesActivator ?? throw new ArgumentNullException(nameof(reflexesActivator));
      _adaptiveActionsSystem = adaptiveActions ?? throw new ArgumentNullException(nameof(adaptiveActions));
      _homeostasisCalculator = gomeostas.Calculator;
      _currentFormat = format;
      _enabled = enabled;

      var logDir = logsDirectory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "ISIDA", "Logs");

      if (!Directory.Exists(logDir))
        Directory.CreateDirectory(logDir);

      // Инициализация JSONL writers (только если выбран JsonL формат)
      if (format.HasFlag(LogFormat.JsonL))
      {
        _logFilePath = Path.Combine(logDir, $"{logFileName}.jsonl");
        _jsonlWriter = new StreamWriter(_logFilePath, append: !clearOnStart, Encoding.UTF8);
        _jsonlWriter.AutoFlush = true;

        var parametersJsonlPath = Path.Combine(logDir, $"{logFileName}_Parameters.jsonl");
        _parametersJsonlWriter = new StreamWriter(parametersJsonlPath, append: !clearOnStart, Encoding.UTF8);
        _parametersJsonlWriter.AutoFlush = true;

        var stylesJsonlPath = Path.Combine(logDir, $"{logFileName}_Styles.jsonl");
        _stylesJsonlWriter = new StreamWriter(stylesJsonlPath, append: !clearOnStart, Encoding.UTF8);
        _stylesJsonlWriter.AutoFlush = true;
      }

      // Инициализация CSV writers (только если выбран Csv формат)
      if (format.HasFlag(LogFormat.Csv))
      {
        var csvPath = Path.Combine(logDir, $"{logFileName}.csv");
        _csvWriter = new StreamWriter(csvPath, append: !clearOnStart, Encoding.UTF8);
        _csvWriter.AutoFlush = true;

        var parametersCsvPath = Path.Combine(logDir, $"{logFileName}_Parameters.csv");
        _parametersCsvWriter = new StreamWriter(parametersCsvPath, append: !clearOnStart, Encoding.UTF8);
        _parametersCsvWriter.AutoFlush = true;

        var stylesCsvPath = Path.Combine(logDir, $"{logFileName}_Styles.csv");
        _stylesCsvWriter = new StreamWriter(stylesCsvPath, append: !clearOnStart, Encoding.UTF8);
        _stylesCsvWriter.AutoFlush = true;
      }

      // Инициализация начального состояния
      _lastState = new SystemState { Pulse = 0 };
      _lastParametersState = new ParametersState { Pulse = 0 };
      _lastStylesState = new StylesState { Pulse = 0 };
    }

    #endregion

    #region Управление логированием

    /// <summary>
    /// Включить/выключить логирование
    /// </summary>
    public void EnableLogging(bool enable = true)
    {
      _enabled = enable;
    }

    /// <summary>
    /// Установить формат логирования
    /// </summary>
    public void SetLogFormat(LogFormat format)
    {
      lock (_lock)
      {
        _currentFormat = format;
      }
    }

    /// <summary>
    /// Очистить файл логов
    /// </summary>
    public void ClearLogs()
    {
      lock (_lock)
      {
        if (_disposed) return;

        // Очищаем общие логи
        _jsonlWriter?.BaseStream?.SetLength(0);
        _jsonlWriter?.Flush();
        _csvWriter?.BaseStream?.SetLength(0);
        _csvWriter?.Flush();

        // Очищаем логи параметров
        _parametersJsonlWriter?.BaseStream?.SetLength(0);
        _parametersJsonlWriter?.Flush();
        _parametersCsvWriter?.BaseStream?.SetLength(0);
        _parametersCsvWriter?.Flush();

        // Очищаем логи стилей
        _stylesJsonlWriter?.BaseStream?.SetLength(0);
        _stylesJsonlWriter?.Flush();
        _stylesCsvWriter?.BaseStream?.SetLength(0);
        _stylesCsvWriter?.Flush();

        // Сбрасываем флаги заголовков
        _csvHeadersWritten = false;
        _csvHeaders.Clear();
        _parametersHeadersWritten = false;
        _stylesHeadersWritten = false;

        // Очищаем активные цепочки и логированные в цикле
        _activeChains.Clear();
        _chainsLoggedInCycle.Clear();

        // Сбрасываем состояние
        _lastState = new SystemState { Pulse = 0 };
        _lastParametersState = new ParametersState { Pulse = 0 };
        _lastStylesState = new StylesState { Pulse = 0 };
      }
    }

    /// <summary>
    /// Устанавливает писатель логов для записи в память
    /// </summary>
    /// <param name="logWriter">Писатель логов</param>
    public void SetMemoryLogWriter(ILogWriter logWriter)
    {
      _memoryLogWriter = logWriter;
    }

    #endregion

    #region Основной метод логирования

    /// <summary>
    /// Снимает слепок состояния системы и логирует если есть изменения
    /// </summary>
    /// <param name="currentPulse">Текущий номер пульса</param>
    public void LogSystemState(int currentPulse)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          var currentState = CollectSystemState(currentPulse);
          var currentParametersState = CollectParametersState(currentPulse);

          bool hasChainInfoForPulse = _chainInfoByPulse.ContainsKey(currentPulse);
          bool hasStateChanges = IsDuplicateState(currentState);

          if (hasChainInfoForPulse)
          {
            var chainInfo = _chainInfoByPulse[currentPulse];
            Debug.WriteLine($"[RESEARCH LOGGER] LogSystemState: pulse={currentPulse}, hasChainInfo={hasChainInfoForPulse}, reflex={chainInfo.ReflexChain}, automatizm={chainInfo.AutomatizmChain}");
          }

          if (hasStateChanges || hasChainInfoForPulse)
          {
            int correctPulse = currentState.Pulse;
            if (_lastState.CurrentBaseID == -1 || _lastState.CurrentBaseID == 2)
              correctPulse++;

            var logEntry = CreateLogEntry(currentState, correctPulse);

            // ИСПРАВЛЕНИЕ: Если есть информация о цепочке для текущего пульса,
            // принудительно записываем буфер и создаем новую запись
            if (hasChainInfoForPulse)
            {
              // Записываем предыдущий буфер, если есть
              if (_currentPulseLogEntry != null)
                WriteBufferedLogEntry();

              // Создаем новую запись для текущего пульса
              _currentPulseLogEntry = logEntry;
              _bufferedPulse = correctPulse;

              // НЕМЕДЛЕННО записываем в память
              WriteBufferedLogEntry();
              _currentPulseLogEntry = null;
              _bufferedPulse = -1;
            }
            else if (_bufferedPulse != correctPulse)
            {
              if (_currentPulseLogEntry != null)
                WriteBufferedLogEntry();

              _currentPulseLogEntry = logEntry;
              _bufferedPulse = correctPulse;
            }
            else
            {
              _currentPulseLogEntry = logEntry;
            }

            _lastState = currentState;

            if (currentState.OrientationReflexType.HasValue && currentState.OrientationReflexType.Value > 0)
              AppGlobalState.ResetOrientationReflexInfo();
          }

          if (!IsDuplicateParametersState(currentParametersState))
          {
            WriteParametersLogEntry(currentParametersState);
            _lastParametersState = currentParametersState;
          }
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }
    }

    /// <summary>
    /// Создает запись лога из состояния
    /// </summary>
    private Dictionary<string, object> CreateLogEntry(SystemState state, int correctPulse)
    {
      string orTypeString = "";
      if (state.OrientationReflexType.HasValue && state.OrientationReflexType.Value > 0)
      {
        orTypeString = state.OrientationReflexType.Value == 1 ? "ОР1" :
                      state.OrientationReflexType.Value == 2 ? "ОР2" : "";
      }

      // Получаем информацию о цепочках для этого пульса
      string reflexChainInfo = string.Empty;
      string automatizmChainInfo = string.Empty;

      if (_chainInfoByPulse.TryGetValue(correctPulse, out var chainInfo))
      {
        reflexChainInfo = chainInfo.ReflexChain;
        automatizmChainInfo = chainInfo.AutomatizmChain;
      }

      return new Dictionary<string, object>
      {
        ["Время"] = state.Time.ToString("yyyy-MM-dd HH:mm:ss"),
        ["Объект"] = "ResearchLogger",
        ["Метод"] = "LogSystemState",
        ["Пульс"] = correctPulse.ToString(),
        ["Состояние"] = state.CurrentBaseID?.ToString() ?? "",
        ["Стили"] = state.CurrentBaseStyleID?.ToString() ?? "",
        ["Триггер"] = state.CurrentTriggerStimulusID?.ToString() ?? "",
        ["ОР"] = orTypeString,
        ["Б/у рефлекс"] = state.CurrentGeneticReflexID?.ToString() ?? "",
        ["Усл. рефлекс"] = state.CurrentConditionReflexID?.ToString() ?? "",
        ["Автоматизм"] = state.CurrentAutomatizmID?.ToString() ?? "",
        ["Цепочка РФ"] = reflexChainInfo,
        ["Цепочка АВ"] = automatizmChainInfo
      };
    }

    /// <summary>
    /// Записывает буферизованную запись лога
    /// </summary>
    private void WriteBufferedLogEntry()
    {
      if (_currentPulseLogEntry == null || _disposed) return;

      if ((_currentFormat.HasFlag(LogFormat.JsonL) && (_jsonlWriter == null)) ||
          (_currentFormat.HasFlag(LogFormat.Csv) && (_csvWriter == null)))
        return;

      try
      {
        if (_currentFormat.HasFlag(LogFormat.JsonL) && _jsonlWriter != null)
        {
          var jsonLine = JsonConvert.SerializeObject(_currentPulseLogEntry, new JsonSerializerSettings
          {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
          });
          _jsonlWriter.WriteLine(jsonLine);
        }

        if (_currentFormat.HasFlag(LogFormat.Csv) && _csvWriter != null)
        {
          WriteCsvLine(_currentPulseLogEntry, _csvWriter, ref _csvHeadersWritten, _csvHeaders);
        }

        string reflexChainInfo = string.Empty;
        string automatizmChainInfo = string.Empty;

        if (_chainInfoByPulse.TryGetValue(_bufferedPulse, out var chainInfo))
        {
          reflexChainInfo = chainInfo.ReflexChain;
          automatizmChainInfo = chainInfo.AutomatizmChain;

          Debug.WriteLine($"[RESEARCH LOGGER] Writing to memory: pulse={_bufferedPulse}, reflex={reflexChainInfo}, automatizm={automatizmChainInfo}");
        }

        WriteToMemoryLog(_currentPulseLogEntry, _bufferedPulse, reflexChainInfo, automatizmChainInfo);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Записывает в память (UI)
    /// </summary>
    private void WriteToMemoryLog(Dictionary<string, object> logEntry, int currentPulse,
                                  string reflexChainInfo = "", string automatizmChainInfo = "")
    {
      if (_memoryLogWriter == null || _disposed) return;

      int? orType = null;
      if (logEntry.ContainsKey("ОР") && !string.IsNullOrEmpty(logEntry["ОР"].ToString()))
      {
        string orValue = logEntry["ОР"].ToString();
        if (orValue == "ОР1")
          orType = 1;
        else if (orValue == "ОР2")
          orType = 2;
      }

      _memoryLogWriter.WriteLog(
          "ResearchLogger",
          "LogSystemState",
          int.Parse(logEntry["Пульс"].ToString()),
          logEntry.ContainsKey("Состояние") && !string.IsNullOrEmpty(logEntry["Состояние"].ToString()) ?
              int.Parse(logEntry["Состояние"].ToString()) : (int?)null,
          logEntry.ContainsKey("Стили") && !string.IsNullOrEmpty(logEntry["Стили"].ToString()) ?
              int.Parse(logEntry["Стили"].ToString()) : (int?)null,
          logEntry.ContainsKey("Триггер") && !string.IsNullOrEmpty(logEntry["Триггер"].ToString()) ?
              int.Parse(logEntry["Триггер"].ToString()) : (int?)null,
          orType,
          logEntry.ContainsKey("Б/у рефлекс") && !string.IsNullOrEmpty(logEntry["Б/у рефлекс"].ToString()) ?
              int.Parse(logEntry["Б/у рефлекс"].ToString()) : (int?)null,
          logEntry.ContainsKey("Усл. рефлекс") && !string.IsNullOrEmpty(logEntry["Усл. рефлекс"].ToString()) ?
              int.Parse(logEntry["Усл. рефлекс"].ToString()) : (int?)null,
          logEntry.ContainsKey("Автоматизм") && !string.IsNullOrEmpty(logEntry["Автоматизм"].ToString()) ?
              int.Parse(logEntry["Автоматизм"].ToString()) : (int?)null,
          reflexChainInfo,
          automatizmChainInfo
      );
    }

    /// <summary>
    /// Записывает все буферизованные данные при завершении
    /// </summary>
    public void Flush()
    {
      lock (_lock)
      {
        if (_disposed) return;

        WriteBufferedLogEntry();
        _currentPulseLogEntry = null;
        _bufferedPulse = -1;
        _chainInfoByPulse.Clear();
      }
    }

    /// <summary>
    /// Временное отключение логирования
    /// </summary>
    public void SuspendLogging()
    {
      lock (_lock)
      {
        _enabled = false;
        Flush();
      }
    }

    /// <summary>
    /// Логирует процесс определения активных стилей ТОЛЬКО при изменениях
    /// </summary>
    public void LogStylesActivationProcess(
        int currentPulse,
        List<BehaviorStyle> finalStyles,
        List<StyleActivationLog> activations,
        List<StyleParameterActivation> parameterActivations)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          var currentFinalStyleIds = finalStyles.Select(s => s.Id).OrderBy(id => id).ToList();
          var lastFinalStyleIds = _lastStylesState?.AfterInhibition.Select(s => s.Id).OrderBy(id => id).ToList() ?? new List<int>();

          // ЛОГИРУЕМ ТОЛЬКО ЕСЛИ ИЗМЕНИЛИСЬ ФИНАЛЬНЫЕ СТИЛИ
          if (!currentFinalStyleIds.SequenceEqual(lastFinalStyleIds))
          {
            var stylesState = new StylesState
            {
              Pulse = currentPulse,
              Time = DateTime.Now,
              AfterInhibition = finalStyles.Select(s => new StyleLogData
              {
                Id = s.Id,
                Name = s.Name,
              }).ToList(),
              Activations = activations,
              ParameterActivations = parameterActivations.Select(pa =>
              {
                pa.Pulse = currentPulse; // заполняем пульс
                return pa;
              }).ToList()
            };

            WriteStylesLogEntry(stylesState);
            _lastStylesState = stylesState;
          }
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }
    }

    /// <summary>
    /// Собирает текущее состояние системы
    /// </summary>
    private SystemState CollectSystemState(int pulse)
    {
      var orInfo = AppGlobalState.GetOrientationReflexInfo();
      var atmInfo = AppGlobalState.GetAutomatizmInfo();

      // Получаем ID рефлексов
      int? geneticReflexId = GetCurrentGeneticReflexID();
      int? conditionedReflexId = GetCurrentConditionedReflexID();

      // Приоритет: условный рефлекс имеет приоритет над безусловным
      // Если есть оба, то выводим только условный
      int? finalGeneticReflexId = null;
      int? finalConditionedReflexId = conditionedReflexId;

      if (conditionedReflexId.HasValue && conditionedReflexId.Value > 0)
        // Есть условный рефлекс - игнорируем безусловный
        finalGeneticReflexId = null;
      else if (geneticReflexId.HasValue && geneticReflexId.Value > 0)
        // Нет условного рефлекса, но есть безусловный
        finalGeneticReflexId = geneticReflexId;

      var state = new SystemState
      {
        Pulse = pulse,
        Time = DateTime.Now,
        CurrentBaseID = GetCurrentBaseState(),
        CurrentBaseStyleID = GetCurrentStyleImageID(),
        CurrentTriggerStimulusID = GetCurrentTriggerImageID(),
        CurrentGeneticReflexID = finalGeneticReflexId,
        CurrentConditionReflexID = finalConditionedReflexId,
        CurrentAutomatizmID = atmInfo.Id != 0 ? (int?)atmInfo.Id : null,
        HasCriticalChanges = GetHasCriticalChanges(),
        OrientationReflexType = orInfo.Type != 0 ? (int?)orInfo.Type : null,
        OrientationReflexPulse = orInfo.Pulse != 0 ? (int?)orInfo.Pulse : null
      };

      // Если автоматизм был активирован на предыдущем пульсе, сбрасываем его
      if (atmInfo.Pulse == pulse - 1)
        AppGlobalState.ResetAutomatizmInfo();

      return state;
    }

    /// <summary>
    /// Собирает состояние параметров гомеостаза
    /// </summary>
    private ParametersState CollectParametersState(int pulse)
    {
      var state = new ParametersState
      {
        Pulse = pulse,
        Time = DateTime.Now
      };

      try
      {
        var parameters = _gomeostas.GetAllParameters();
        foreach (var param in parameters)
        {
          var urgencyFunction = _homeostasisCalculator.CalculateUrgencyFunction(param);
          var (activationZone, activationDetails) = _homeostasisCalculator.GetStateForStyleActivation(param, param.CurrentState);
          if (!string.IsNullOrEmpty(activationDetails))
          {
            int pipeIndex = activationDetails.IndexOf('|');
            if (pipeIndex >= 0)
              activationDetails = activationDetails.Substring(pipeIndex + 1);
          }

          state.Parameters.Add(new ParameterLogData
          {
            Id = param.Id,
            Name = param.Name,
            Weight = param.Weight,
            NormaWell = param.NormaWell,
            Speed = param.Speed,
            Value = param.Value,
            UrgencyFunction = urgencyFunction,
            ParameterState = param.CurrentState.ToString(),
            ActivationZone = $"{activationZone} ({activationDetails})"
          });
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }

      return state;
    }

    /// <summary>
    /// Проверяет, является ли состояние дубликатом предыдущего
    /// </summary>
    private bool IsDuplicateState(SystemState current)
    {
      return _lastState.CurrentBaseID != current.CurrentBaseID ||
             _lastState.CurrentBaseStyleID != current.CurrentBaseStyleID ||
             _lastState.CurrentTriggerStimulusID != current.CurrentTriggerStimulusID ||

             // Используем логику приоритета при сравнении
             !AreReflexesEqual(_lastState.CurrentGeneticReflexID, _lastState.CurrentConditionReflexID,
                             current.CurrentGeneticReflexID, current.CurrentConditionReflexID) ||

             _lastState.CurrentAutomatizmID != current.CurrentAutomatizmID ||
             _lastState.HasCriticalChanges != current.HasCriticalChanges ||
             _lastState.OrientationReflexType != current.OrientationReflexType ||
             _lastState.OrientationReflexPulse != current.OrientationReflexPulse;
    }

    /// <summary>
    /// Сравнивает рефлексы с учетом приоритета условного рефлекса
    /// </summary>
    private bool AreReflexesEqual(int? lastGenetic, int? lastConditioned,
                                  int? currentGenetic, int? currentConditioned)
    {
      // Приоритет: если есть условный рефлекс, игнорируем безусловный
      var lastEffectiveConditioned = lastConditioned.HasValue && lastConditioned.Value > 0 ? lastConditioned : null;
      var lastEffectiveGenetic = (lastEffectiveConditioned == null && lastGenetic.HasValue && lastGenetic.Value > 0) ? lastGenetic : null;

      var currentEffectiveConditioned = currentConditioned.HasValue && currentConditioned.Value > 0 ? currentConditioned : null;
      var currentEffectiveGenetic = (currentEffectiveConditioned == null && currentGenetic.HasValue && currentGenetic.Value > 0) ? currentGenetic : null;

      return lastEffectiveConditioned == currentEffectiveConditioned &&
             lastEffectiveGenetic == currentEffectiveGenetic;
    }

    /// <summary>
    /// Проверяет, является ли состояние параметров дубликатом предыдущего
    /// </summary>
    private bool IsDuplicateParametersState(ParametersState current)
    {
      if (_lastParametersState.Parameters.Count != current.Parameters.Count)
        return false;

      for (int i = 0; i < current.Parameters.Count; i++)
      {
        var currentParam = current.Parameters[i];
        var lastParam = _lastParametersState.Parameters[i];

        if (currentParam.ParameterState != lastParam.ParameterState &&
          currentParam.ActivationZone != lastParam.ActivationZone)
         
          return false;
      }
      return true;
    }

    #endregion

    #region Методы сбора данных

    /// <summary>
    /// Получает флаг критических изменений гомеостаза
    /// </summary>
    private int? GetHasCriticalChanges()
    {
      try
      {
        return _gomeostas.HasCriticalChanges ? 1 : 0;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Получает текущее базовое состояние гомеостаза
    /// </summary>
    private int? GetCurrentBaseState()
    {
      try
      {
        // Базовое состояние гомеостаза
        var homeostasisState = _gomeostas.GetHomeostasisState();
        return (int)homeostasisState.OverallState;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Получает ID образа стилей поведения
    /// </summary>
    private int? GetCurrentStyleImageID()
    {
      try
      {
        var activeStyles = _gomeostas.ActiveBehaviorStyleImageId;
        if (activeStyles != 0)
          return activeStyles;
        else
        {
          var currentStyles = AppGlobalState.ActiveStyles;
          var currentStyleIds = currentStyles.Select(s => s.Id).ToList();
          activeStyles = _perception.AddBehaviorStyleImage(currentStyleIds);
          return activeStyles;
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Получает ID образа триггеров
    /// </summary>
    private int? GetCurrentTriggerImageID()
    {
      try
      {
        return _reflexesActivator.ActiveGlobalCurTriggerStimulusID;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Получает ID активного безусловного рефлекса
    /// </summary>
    private int? GetCurrentGeneticReflexID()
    {
      try
      {
        return _reflexesActivator.ActiveGeneticReflexID;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    /// <summary>
    /// Получает ID активного условного рефлекса
    /// </summary>
    private int? GetCurrentConditionedReflexID()
    {
      try
      {
        return _reflexesActivator.ActiveConditionReflexID;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return null;
      }
    }

    #endregion

    #region Запись логов

    /// <summary>
    /// Записывает лог параметров
    /// </summary>
    private void WriteParametersLogEntry(ParametersState state)
    {
      try
      {
        if (_lastParametersState != null && _lastParametersState.Pulse == state.Pulse)
          return;

        foreach (var param in state.Parameters)
        {
          // Записываем в память (UI)
          _memoryLogWriter?.WriteParameterLog(
              state.Pulse,
              param.Id,
              param.Name,
              param.Weight,
              param.NormaWell,
              param.Speed,
              param.Value,
              param.UrgencyFunction,
              param.ParameterState,
              param.ActivationZone
          );

          var logEntry = new Dictionary<string, object>
          {
            ["Pulse"] = state.Pulse.ToString(),
            ["Time"] = state.Time.ToString("yyyy-MM-dd HH:mm:ss"),
            ["ParamId"] = param.Id.ToString(),
            ["ParamName"] = param.Name,
            ["Weight"] = param.Weight.ToString(),
            ["NormaWell"] = param.NormaWell.ToString(),
            ["Speed"] = param.Speed.ToString(),
            ["Value"] = param.Value.ToString("F3"),
            ["UrgencyFunction"] = param.UrgencyFunction.ToString("F3"),
            ["ParameterState"] = param.ParameterState,
            ["ActivationZone"] = param.ActivationZone
          };

          // Записываем в JSONL (если выбран формат)
          if (_currentFormat.HasFlag(LogFormat.JsonL) && _parametersJsonlWriter != null)
          {
            var jsonLine = JsonConvert.SerializeObject(logEntry, new JsonSerializerSettings
            {
              Formatting = Formatting.None,
              NullValueHandling = NullValueHandling.Ignore
            });
            _parametersJsonlWriter.WriteLine(jsonLine);
          }

          // Записываем в CSV (если выбран формат)
          if (_currentFormat.HasFlag(LogFormat.Csv) && _parametersCsvWriter != null)
          {
            WriteCsvLine(logEntry, _parametersCsvWriter, ref _parametersHeadersWritten, new HashSet<string>());
          }
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    private void WriteParameterActivationEntry(StyleParameterActivation activation)
    {
      _memoryLogWriter?.WriteStyleParameterActivation(
        activation.Pulse,
        activation.Stage,
        activation.ParameterId,
        activation.ParameterName,
        activation.ZoneId,
        activation.ZoneDescription,
        activation.StyleId,
        activation.StyleName,
        activation.ActivationDetails
      );

      var logEntry = new Dictionary<string, object>
      {
        ["Pulse"] = activation.Pulse.ToString(),
        ["Time"] = activation.Time.ToString("yyyy-MM-dd HH:mm:ss"),
        ["Stage"] = activation.Stage,
        ["StyleId"] = activation.StyleId.ToString(),
        ["StyleName"] = activation.StyleName,
        ["ParameterId"] = activation.ParameterId.ToString(),
        ["ParameterName"] = activation.ParameterName,
        ["ZoneId"] = activation.ZoneId.ToString(),
        ["ZoneDescription"] = activation.ZoneDescription,
        ["ActivationDetails"] = activation.ActivationDetails
      };

      // Записываем в JSONL (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.JsonL) && _stylesJsonlWriter != null)
      {
        var jsonLine = JsonConvert.SerializeObject(logEntry, new JsonSerializerSettings
        {
          Formatting = Formatting.None,
          NullValueHandling = NullValueHandling.Ignore
        });
        _stylesJsonlWriter.WriteLine(jsonLine);
      }

      // Записываем в CSV (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.Csv) && _stylesCsvWriter != null)
      {
        WriteCsvLine(logEntry, _stylesCsvWriter, ref _stylesHeadersWritten, new HashSet<string>());
      }
    }

    private void WriteStylesLogEntry(StylesState state)
    {
      try
      {
        // Логируем финальные стили
        foreach (var style in state.AfterInhibition)
        {
          WriteStyleEntry(state.Pulse, "Final", style);
        }

        // Логируем активации
        foreach (var activation in state.Activations)
        {
          WriteActivationEntry(state.Pulse, activation);
        }

        // Логируем связи параметров и стилей
        foreach (var paramActivation in state.ParameterActivations)
        {
          WriteParameterActivationEntry(paramActivation);
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    private void WriteStyleEntry(int pulse, string stage, StyleLogData style)
    {
      // Записываем в память (UI)
      _memoryLogWriter?.WriteStyleLog(
          pulse,
          stage,
          style.Id,
          style.Name
      );

      var logEntry = new Dictionary<string, object>
      {
        ["Pulse"] = pulse.ToString(),
        ["Time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["Stage"] = stage,
        ["StyleId"] = style.Id.ToString(),
        ["StyleName"] = style.Name,
        ["ParameterId"] = "",
        ["ParameterName"] = "",
        ["ZoneId"] = "",
        ["ZoneDescription"] = "",
        ["ActivationDetails"] = ""
      };

      // Записываем в JSONL (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.JsonL) && _stylesJsonlWriter != null)
      {
        var jsonLine = JsonConvert.SerializeObject(logEntry, new JsonSerializerSettings
        {
          Formatting = Formatting.None,
          NullValueHandling = NullValueHandling.Ignore
        });
        _stylesJsonlWriter.WriteLine(jsonLine);
      }

      // Записываем в CSV (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.Csv) && _stylesCsvWriter != null)
      {
        WriteCsvLine(logEntry, _stylesCsvWriter, ref _stylesHeadersWritten, new HashSet<string>());
      }
    }

    private void WriteActivationEntry(int pulse, StyleActivationLog activation)
    {
      var logEntry = new Dictionary<string, object>
      {
        ["Pulse"] = pulse.ToString(),
        ["Time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["Stage"] = "Activation",
        ["StyleId"] = "",
        ["StyleName"] = "",
        ["Weight"] = "",
        ["ParameterId"] = activation.ParameterId.ToString(),
        ["ParameterName"] = activation.ParameterName,
        ["ZoneId"] = activation.StateId.ToString(),
        ["ZoneDescription"] = activation.StateDescription,
        ["ActivationDetails"] = activation.ActivationDetails
      };

      // Записываем в JSONL (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.JsonL) && _stylesJsonlWriter != null)
      {
        var jsonLine = JsonConvert.SerializeObject(logEntry, new JsonSerializerSettings
        {
          Formatting = Formatting.None,
          NullValueHandling = NullValueHandling.Ignore
        });
        _stylesJsonlWriter.WriteLine(jsonLine);
      }

      // Записываем в CSV (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.Csv) && _stylesCsvWriter != null)
      {
        WriteCsvLine(logEntry, _stylesCsvWriter, ref _stylesHeadersWritten, new HashSet<string>());
      }
    }

    /// <summary>
    /// Преобразует 0 в null для всех полей кроме BaseID
    /// </summary>
    private int? NullIfZero(int? value)
    {
      return value == 0 ? null : value;
    }

    /// <summary>
    /// Запись строки в CSV
    /// </summary>
    private void WriteCsvLine(Dictionary<string, object> logEntry, StreamWriter writer,
                            ref bool headersWritten, HashSet<string> headers)
    {
      // Обновляем заголовки
      foreach (var key in logEntry.Keys)
      {
        headers.Add(key);
      }
      var headersList = new List<string>(headers);

      // Записываем заголовки если еще не записаны
      if (!headersWritten)
      {
        headersList = new List<string>(headers);
        headersList.Sort();
        writer.WriteLine(string.Join(";", headersList));
        headersWritten = true;
      }

      // Записываем данные
      headersList = new List<string>(headers);
      headersList.Sort();
      var values = headersList.Select(header =>
          logEntry.ContainsKey(header) ? EscapeCsvValue(logEntry[header]?.ToString() ?? "") : "");

      writer.WriteLine(string.Join(";", values));
    }

    private string EscapeCsvValue(string value)
    {
      if (string.IsNullOrEmpty(value)) return "";

      if (value.Contains(";") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
      {
        return $"\"{value.Replace("\"", "\"\"")}\"";
      }
      return value;
    }

    #endregion

    #region Логирование цепочек

    private void SetChainInfoForCurrentPulse(int pulse, string chainType, string chainInfo)
    {
      if (!_chainInfoByPulse.ContainsKey(pulse))
        _chainInfoByPulse[pulse] = (string.Empty, string.Empty);

      var current = _chainInfoByPulse[pulse];
      if (chainType == "Reflex")
        _chainInfoByPulse[pulse] = (chainInfo, current.AutomatizmChain);
      else if (chainType == "Automatizm")
        _chainInfoByPulse[pulse] = (current.ReflexChain, chainInfo);

      Debug.WriteLine($"[RESEARCH LOGGER] SetChainInfoForCurrentPulse: pulse={pulse}, type={chainType}, info={chainInfo}");
    }

    /// <summary>
    /// Регистрирует цепочку в активных цепочках БЕЗ логирования события
    /// Нужна для инициализации цепочки, чтобы последующие вызовы LogChainLinkExecution работали
    /// </summary>
    public void RegisterActiveChain(int chainId, string chainName, string chainType)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          // Просто регистрируем цепочку в словаре, но не логируем событие
          var session = new ActiveChainSession
          {
            ChainId = chainId,
            ChainName = chainName,
            ChainType = chainType,
            StartPulse = GlobalTimer.GlobalPulsCount,
            StartTime = DateTime.Now,
            LastLinkId = 0
          };
          _activeChains[chainId] = session;
          Logger.Info($"Цепочка {chainType} {chainId} зарегистрирована для логирования");
        }
        catch (Exception ex)
        {
          Logger.Error($"Error registering active chain: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирует начало выполнения цепочки (рефлекса или автоматизма)
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="chainName">Имя цепочки</param>
    /// <param name="chainType">Тип: "Reflex" или "Automatizm"</param>
    /// <param name="startPulse">Номер пульса начала</param>
    public void LogChainStart(int chainId, string chainName, string chainType, int startPulse)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          // Создаём новую сессию активной цепочки
          var session = new ActiveChainSession
          {
            ChainId = chainId,
            ChainName = chainName,
            ChainType = chainType,
            StartPulse = startPulse,
            StartTime = DateTime.Now,
            LastLinkId = 0
          };
          _activeChains[chainId] = session;

          // Логируем начало цепочки в основные логи (просто информационное сообщение)
          Logger.Info($"[CHAIN_START|{chainType}|{chainId}] Цепочка {chainName} ({chainType}) активирована");
        }
        catch (Exception ex)
        {
          Logger.Error($"Error logging chain start: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирует выполнение звена цепочки - пишет ПРЯМО в основной AgentLogs файл
    /// </summary>
    public void LogChainLinkExecution(int chainId, int linkId, int actionId, int pulse)
    {
      if (!_enabled || _disposed) return;

      Debug.WriteLine($"[RESEARCH LOGGER] LogChainLinkExecution START: chainId={chainId}, linkId={linkId}, actionId={actionId}, pulse={pulse}");

      lock (_lock)
      {
        try
        {
          if (_activeChains.TryGetValue(chainId, out var session))
          {
            session.LastLinkId = linkId;
            if (!session.ExecutedLinks.Contains(linkId))
              session.ExecutedLinks.Add(linkId);

            string chainInfo = $"{chainId}:{actionId}";

            // Пишем прямо в основные AgentLogs файлы
            var logEntry = new Dictionary<string, object>
            {
              ["Время"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
              ["Объект"] = "ChainExecution",
              ["Метод"] = "LogChainLinkExecution",
              ["Пульс"] = pulse.ToString(),
              ["Состояние"] = "",
              ["Стили"] = "",
              ["Триггер"] = "",
              ["ОР"] = "",
              ["Б/у рефлекс"] = session.ChainType == "Reflex" ? chainId.ToString() : "",
              ["Усл. рефлекс"] = "",
              ["Автоматизм"] = session.ChainType == "Automatizm" ? chainId.ToString() : "",
              ["Цепочка РФ"] = session.ChainType == "Reflex" ? chainInfo : "",
              ["Цепочка АВ"] = session.ChainType == "Automatizm" ? chainInfo : ""
            };

            // Записываем в JSONL
            if (_currentFormat.HasFlag(LogFormat.JsonL) && _jsonlWriter != null)
            {
              var jsonLine = JsonConvert.SerializeObject(logEntry, new JsonSerializerSettings
              {
                Formatting = Formatting.None,
                NullValueHandling = NullValueHandling.Ignore
              });
              _jsonlWriter.WriteLine(jsonLine);
            }

            // Записываем в CSV
            if (_currentFormat.HasFlag(LogFormat.Csv) && _csvWriter != null)
            {
              WriteCsvLine(logEntry, _csvWriter, ref _csvHeadersWritten, _csvHeaders);
            }

            SetChainInfoForCurrentPulse(pulse, session.ChainType, chainInfo);
          }
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }
    }

    /// <summary>
    /// Логирует оценку оператора для звена цепочки
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="linkId">ID звена</param>
    /// <param name="evaluation">Оценка (true=успех, false=неудача, null=ожидание)</param>
    /// <param name="pulse">Номер пульса оценки</param>
    public void LogChainEvaluation(int chainId, int linkId, bool? evaluation, int pulse)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          if (_activeChains.TryGetValue(chainId, out var session))
          {
            session.LastEvaluation = evaluation;
            session.EvaluationHistory[linkId] = evaluation;

            var evalStr = evaluation == null ? "ожидание" : (evaluation.Value ? "успех" : "неудача");
            // Логируем в основные логи с маркером [CHAIN_EVAL|...]
            Logger.Info($"[CHAIN_EVAL|{session.ChainType}|{chainId}|{linkId}|{evalStr}] Оценка звена {linkId} цепочки {session.ChainName}: {evalStr}");
          }
        }
        catch (Exception ex)
        {
          Logger.Error($"Error logging chain evaluation: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирует решение о ветвлении цепочки на основе оценки
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="currentLinkId">ID текущего звена</param>
    /// <param name="evaluation">Значение оценки, определившее ветвление</param>
    /// <param name="nextLinkId">ID следующего звена</param>
    /// <param name="branchType">Тип ветви: "Success" (true) или "Failure" (false)</param>
    /// <param name="pulse">Номер пульса решения</param>
    public void LogChainBranchDecision(int chainId, int currentLinkId, bool? evaluation, 
                                       int nextLinkId, string branchType, int pulse)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          if (_activeChains.TryGetValue(chainId, out var session))
          {
            var evalStr = evaluation == null ? "null" : evaluation.Value.ToString();
            // Логируем в основные логи с маркером [CHAIN_BRANCH|...]
            Logger.Info($"[CHAIN_BRANCH|{session.ChainType}|{chainId}|{currentLinkId}|{nextLinkId}|{branchType}] Ветвление: от звена {currentLinkId} к {nextLinkId} ({branchType})");
          }
        }
        catch (Exception ex)
        {
          Logger.Error($"Error logging branch decision: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирует завершение цепочки
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <param name="pulse">Номер пульса завершения</param>
    /// <param name="totalLinksExecuted">Количество выполненных звеньев</param>
    /// <param name="finalEvaluation">Финальная оценка цепочки</param>
    public void LogChainCompletion(int chainId, int pulse, int totalLinksExecuted, bool? finalEvaluation = null)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          if (_activeChains.TryGetValue(chainId, out var session))
          {
            var evalStr = finalEvaluation == null ? "нет" : (finalEvaluation.Value ? "успех" : "неудача");
            Logger.Info($"[CHAIN_COMPLETE|{session.ChainType}|{chainId}] Цепочка {session.ChainName} завершена: звеньев={totalLinksExecuted}, итог={evalStr}");

            // ИСПРАВЛЕНИЕ: Принудительно записываем буфер при завершении цепочки
            if (_bufferedPulse == pulse)
            {
              WriteBufferedLogEntry();
              _currentPulseLogEntry = null;
              _bufferedPulse = -1;
            }

            _activeChains.Remove(chainId);
          }
        }
        catch (Exception ex)
        {
          Logger.Error($"Error logging chain completion: {ex.Message}");
        }
      }
    }
    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;

      lock (_lock)
      {
        _jsonlWriter?.Dispose();
        _csvWriter?.Dispose();
        _parametersJsonlWriter?.Dispose();
        _parametersCsvWriter?.Dispose();
        _stylesJsonlWriter?.Dispose();
        _stylesCsvWriter?.Dispose();
        Flush();
        _disposed = true;
      }
    }

    #endregion
  }
}