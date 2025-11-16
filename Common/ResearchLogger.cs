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
using System.Text;
using System.Text.Json;
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

    // Писатель в память (для UI)
    private static ILogWriter _memoryLogWriter;

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
      public int? HasCriticalChanges { get; set; }
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
      public List<StyleLogData> BaseStyles { get; set; } = new List<StyleLogData>();
      public List<StyleLogData> AfterAntagonists { get; set; } = new List<StyleLogData>();
      public List<StyleLogData> AfterInhibition { get; set; } = new List<StyleLogData>();
      public List<StyleActivationLog> Activations { get; set; } = new List<StyleActivationLog>();
    }

    /// <summary>
    /// Данные стиля для логирования
    /// </summary>
    private class StyleLogData
    {
      public int Id { get; set; }
      public string Name { get; set; }
      public int Weight { get; set; }
      public float Activity { get; set; }
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
      /// <value>Целое число, соответствующее ID параметра в системе гомеостаза</value>
      public int ParameterId { get; set; }

      /// <summary>
      /// Наименование параметра гомеостаза
      /// </summary>
      /// <value>Строка с человеко-читаемым названием параметра</value>
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
      /// <value>Строка с описанием состояния (например, "Слабое отклонение")</value>
      public string StateDescription { get; set; }

      /// <summary>
      /// Список идентификаторов стилей поведения, активируемых данным состоянием параметра
      /// </summary>
      /// <value>
      /// Список целых чисел, где положительные значения означают активацию стиля,
      /// а отрицательные - деактивацию
      /// </value>
      /// <example>
      /// [1, 3, -2] - активирует стили с ID 1 и 3, деактивирует стиль с ID 2
      /// </example>
      public List<int> ActivatedStyles { get; set; } = new List<int>();

      /// <summary>
      /// Детальная информация о процессе активации стилей
      /// </summary>
      /// <value>
      /// Строка с техническими деталями активации в формате: 
      /// "ParamId|Deviation|Range|Percent|Zone"
      /// </value>
      /// <example>
      /// "1|5.25|50.0|10.5|4" - параметр ID 1, отклонение 5.25, диапазон 50.0, процент 10.5%, зона 4
      /// </example>
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
    public static void SetMemoryLogWriter(ILogWriter logWriter)
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

          if (!IsDuplicateState(currentState))
          {
            WriteLogEntry(currentState);
            _lastState = currentState;
          }

          if (!IsDuplicateParametersState(currentParametersState))
          {
            WriteParametersLogEntry(currentParametersState);
            _lastParametersState = currentParametersState;
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Ошибка логирования состояния системы: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирует процесс определения активных стилей ТОЛЬКО при изменениях
    /// </summary>
    public void LogStylesActivationProcess(
      int currentPulse,
      List<BehaviorStyle> baseStyles,
      List<BehaviorStyle> afterAntagonists,
      List<BehaviorStyle> afterInhibition,
      List<StyleActivationLog> activations)
    {
      if (!_enabled || _disposed) return;

      lock (_lock)
      {
        try
        {
          var currentFinalStyleIds = afterInhibition.Select(s => s.Id).OrderBy(id => id).ToList();
          var lastFinalStyleIds = _lastStylesState?.AfterInhibition.Select(s => s.Id).OrderBy(id => id).ToList() ?? new List<int>();

          // ЛОГИРУЕМ ТОЛЬКО ЕСЛИ ИЗМЕНИЛИСЬ ФИНАЛЬНЫЕ СТИЛИ
          if (!currentFinalStyleIds.SequenceEqual(lastFinalStyleIds))
          {
            var stylesState = new StylesState
            {
              Pulse = currentPulse,
              Time = DateTime.Now,
              BaseStyles = baseStyles.Select(s => new StyleLogData
              {
                Id = s.Id,
                Name = s.Name,
                Weight = s.Weight,
                Activity = s.Weight
              }).ToList(),
              AfterAntagonists = afterAntagonists.Select(s => new StyleLogData
              {
                Id = s.Id,
                Name = s.Name,
                Weight = s.Weight,
                Activity = s.Weight
              }).ToList(),
              AfterInhibition = afterInhibition.Select(s => new StyleLogData
              {
                Id = s.Id,
                Name = s.Name,
                Weight = s.Weight,
                Activity = s.Weight
              }).ToList(),
              Activations = activations
            };

            WriteStylesLogEntry(stylesState);
            _lastStylesState = stylesState;
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine($"Ошибка логирования процесса стилей: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Собирает текущее состояние системы
    /// </summary>
    private SystemState CollectSystemState(int pulse)
    {
      var state = new SystemState
      {
        Pulse = pulse,
        Time = DateTime.Now,
        CurrentBaseID = GetCurrentBaseState(),
        CurrentBaseStyleID = GetCurrentStyleImageID(),
        CurrentTriggerStimulusID = GetCurrentTriggerImageID(),
        CurrentGeneticReflexID = GetCurrentGeneticReflexID(),
        CurrentConditionReflexID = GetCurrentConditionedReflexID(),
        HasCriticalChanges = GetHasCriticalChanges()
      }; ;
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
        Debug.WriteLine($"Ошибка сбора состояния параметров: {ex.Message}");
      }

      return state;
    }

    /// <summary>
    /// Проверяет, является ли состояние дубликатом предыдущего
    /// </summary>
    private bool IsDuplicateState(SystemState current)
    {
      return _lastState.CurrentBaseID == current.CurrentBaseID &&
             _lastState.CurrentBaseStyleID == current.CurrentBaseStyleID &&
             _lastState.CurrentTriggerStimulusID == current.CurrentTriggerStimulusID &&
             _lastState.CurrentGeneticReflexID == current.CurrentGeneticReflexID &&
             _lastState.CurrentConditionReflexID == current.CurrentConditionReflexID &&
             _lastState.HasCriticalChanges == current.HasCriticalChanges;
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
        Debug.WriteLine($"Ошибка получения флага критического изменения гомеостаза: {ex.Message}");
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
        Debug.WriteLine($"Ошибка получения базового состояния: {ex.Message}");
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
          var currentStyles = _gomeostas.GetActiveStyles();
          var currentStyleIds = currentStyles.Select(s => s.Id).ToList();
          activeStyles = _perception.AddBehaviorStyleImage(currentStyleIds);
          return activeStyles;
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка получения образа стилей: {ex.Message}");
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
        Debug.WriteLine($"Ошибка получения образа триггеров: {ex.Message}");
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
        Debug.WriteLine($"Ошибка получения безусловного рефлекса: {ex.Message}");
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
        Debug.WriteLine($"Ошибка получения условного рефлекса: {ex.Message}");
        return null;
      }
    }

    #endregion

    #region Запись логов

    /// <summary>
    /// Записывает запись лога
    /// </summary>
    private void WriteLogEntry(SystemState state)
    {
      try
      {
        // Записываем в память (UI)
        _memoryLogWriter?.WriteLog(
            "ResearchLogger",
            "LogSystemState",
            state.Pulse,
            state.CurrentBaseID,
            NullIfZero(state.CurrentBaseStyleID),
            NullIfZero(state.CurrentTriggerStimulusID),
            NullIfZero(state.HasCriticalChanges),
            NullIfZero(state.CurrentGeneticReflexID),
            NullIfZero(state.CurrentConditionReflexID)
        );

        // Записываем в файлы
        var logEntry = new Dictionary<string, object>
        {
          ["Время"] = state.Time.ToString("yyyy-MM-dd HH:mm:ss"),
          ["Объект"] = "ResearchLogger",
          ["Метод"] = "LogSystemState",
          ["Пульс"] = state.Pulse.ToString(),
          ["Состояние"] = state.CurrentBaseID?.ToString() ?? "",
          ["Стили"] = state.CurrentBaseStyleID?.ToString() ?? "",
          ["Триггер"] = state.CurrentTriggerStimulusID?.ToString() ?? "",
          ["ОР1"] = state.HasCriticalChanges?.ToString() ?? "",
          ["Б/у рефлекс"] = state.CurrentGeneticReflexID?.ToString() ?? "",
          ["Усл. рефлекс"] = state.CurrentConditionReflexID?.ToString() ?? ""
        };

        // Записываем в JSONL
        if (_currentFormat.HasFlag(LogFormat.JsonL) && _jsonlWriter != null)
        {
          var jsonLine = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
          {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
          });
          _jsonlWriter.WriteLine(jsonLine);
        }

        // Записываем в CSV
        if (_currentFormat.HasFlag(LogFormat.Csv) && _csvWriter != null)
        {
          WriteCsvLine(logEntry, _csvWriter, ref _csvHeadersWritten, _csvHeaders);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка записи лога: {ex.Message}");
      }
    }

    /// <summary>
    /// Записывает лог параметров
    /// </summary>
    private void WriteParametersLogEntry(ParametersState state)
    {
      try
      {
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
            var jsonLine = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
            {
              WriteIndented = false,
              Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
        Debug.WriteLine($"Ошибка записи лога параметров: {ex.Message}");
      }
    }

    /// <summary>
    /// Записывает лог стилей
    /// </summary>
    private void WriteStylesLogEntry(StylesState state)
    {
      try
      {
        // Логируем базовые стили
        foreach (var style in state.BaseStyles)
        {
          WriteStyleEntry(state.Pulse, "Base", style);
        }

        // Логируем стили после антагонистов
        foreach (var style in state.AfterAntagonists)
        {
          WriteStyleEntry(state.Pulse, "AfterAntagonists", style);
        }

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
      }
      catch (Exception ex)
      {
        Debug.WriteLine($"Ошибка записи лога стилей: {ex.Message}");
      }
    }

    private void WriteStyleEntry(int pulse, string stage, StyleLogData style)
    {
      // Записываем в память (UI)
      _memoryLogWriter?.WriteStyleLog(
          pulse,
          stage,
          style.Id,
          style.Name,
          style.Weight,
          style.Activity
      );

      var logEntry = new Dictionary<string, object>
      {
        ["Pulse"] = pulse.ToString(),
        ["Time"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
        ["Stage"] = stage,
        ["StyleId"] = style.Id.ToString(),
        ["StyleName"] = style.Name,
        ["Weight"] = style.Weight.ToString(),
        ["Activity"] = style.Activity.ToString("F2")
      };

      // Записываем в JSONL (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.JsonL) && _stylesJsonlWriter != null)
      {
        var jsonLine = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
        {
          WriteIndented = false,
          Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
        ["ParamId"] = activation.ParameterId.ToString(),
        ["ParamName"] = activation.ParameterName,
        ["StateId"] = activation.StateId.ToString(),
        ["StateDescription"] = activation.StateDescription,
        ["ActivatedStyles"] = string.Join(",", activation.ActivatedStyles),
        ["ActivationDetails"] = activation.ActivationDetails
      };

      // Записываем в JSONL (если выбран формат)
      if (_currentFormat.HasFlag(LogFormat.JsonL) && _stylesJsonlWriter != null)
      {
        var jsonLine = JsonSerializer.Serialize(logEntry, new JsonSerializerOptions
        {
          WriteIndented = false,
          Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
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
        _disposed = true;
      }
    }

    #endregion
  }
}