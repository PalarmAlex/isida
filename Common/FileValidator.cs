using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ISIDA.Common
{
  /// <summary>
  /// Утилитный класс для проверки валидности файлов и безопасного сохранения
  /// </summary>
  public static class FileValidator
  {
    internal static class FileHeaders
    {
      // Условные рефлексы
      public const string ConditionedReflexesFormat = "# ID|Name|Description|Level1|Level2|Level3|AdaptiveActions|Rank|AssociationStrength|LastActivation|BirthTime|SourceGeneticReflexId";
      public const string ConditionedReflexesLevel1 = "# Level1: -1:Плохо, 0:Норма, 1:Хорошо";
      public const string ConditionedReflexesLevel2 = "# Level2: id1,id2,id3 (образ стилей поведения)";
      public const string ConditionedReflexesLevel3 = "# Level3: ID образа пускового стимула";
      public const string ConditionedReflexesActions = "# AssociationStrength: крепость связи C_ij ∈ [0,1]";

      // Безусловные рефлексы
      public const string GeneticReflexesFormat = "# Формат: ID|Level1|Level2|Level3|Адаптивные действия|ReflexChainID";
      public const string GeneticReflexesLevel1 = "# Level1: Интегральное базовое состояние гомеостаза: -1: 0: 1";
      public const string GeneticReflexesLevel2 = "# Level2: Контексты реагирования: id1,id2,id3";
      public const string GeneticReflexesLevel3 = "# Level3: Гомеостатические воздействия: id1,id2,id3";
      public const string GeneticReflexesActions = "# Адаптивные действия: id1,id2,id3";
      public const string GeneticReflexesChain = "# ReflexChainID: ID цепочки рефлексов (0 если нет)";

      // Цепочки безусловных рефлексов
      public const string ReflexChainsFormat = "# Формат файла цепочек рефлексов";
      public const string ReflexChainsChain = "# CHAIN|ID|Name|Description";
      public const string ReflexChainsLink = "# LINK|LinkID|ActionID|SuccessNext|FailureNext|Description";
      public const string ReflexChainsChainDesc = "# ID: уникальный идентификатор цепочки";
      public const string ReflexChainsNameDesc = "# Name: наименование цепочки";
      public const string ReflexChainsLinkDesc = "# LinkID: уникальный идентификатор звена";
      public const string ReflexChainsReflexDesc = "# ActionID: ID действия для выполнения";
      public const string ReflexChainsSuccessDesc = "# SuccessNext: ID следующего звена при успехе";
      public const string ReflexChainsFailureDesc = "# FailureNext: ID следующего звена при неудаче";

      // Гомеостатические воздействия
      public const string InfluenceActionsFormat = "# Формат: ID|Имя|Описание|Воздействие|Антагонисты";
      public const string InfluenceActionsBenefit = "# Воздействие: paramId1:effect1;paramId2:effect2";
      public const string InfluenceAntagonists = "# Антагонисты: id1,id2,id3";

      // Адаптивные действия
      public const string ActionsFormat = "# Формат: ID|Имя|Описание|Интенсивность|Антагонисты";
      public const string ActionsAntagonists = "# Антагонисты: id1,id2,id3";

      // Стили поведения
      public const string StylesFormat = "# Формат: ID|Имя|Описание|Вес|Антагонисты";
      public const string StylesAntagonis = "# Антагонисты: id1,id2,id3";

      // Параметры гомеостаза
      public const string ParametersFormat = "# Формат: ID|Название|Описание|Значение|Вес|Норма|Скорость|Активации стилей|Критический|Мин.значение|Макс.значение";
      public const string ParametersActivations = "# Активации стилей: id1,id2,id3";

      // Свойства агента
      public const string PropertiesFormat = "# Формат: Ключ|Значение";
      public const string PropertiesIsSleeping = "IsSleeping|";
      public const string PropertiesIsDead = "IsDead|";
    }

    private static string _logFilePath;

    /// <summary>
    /// Путь к каталогу логов
    /// </summary>
    public static string LogFilePath
    {
      get
      {
        if (_logFilePath == null)
        {
          // Путь по умолчанию
          _logFilePath = Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Logs", "SaveErrors.log");
        }
        return _logFilePath;
      }
    }

    /// <summary>
    /// Установка пути к каталогу логов
    /// </summary>
    public static void SetLogsPath(string logsDirectory)
    {
      if (!string.IsNullOrEmpty(logsDirectory))
      {
        _logFilePath = Path.Combine(logsDirectory, "SaveErrors.log");

        try
        {
          // Создаем директорию, если её нет
          var directory = Path.GetDirectoryName(_logFilePath);
          if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
          {
            Directory.CreateDirectory(directory);
          }
        }
        catch (Exception ex)
        {
          System.Diagnostics.Debug.WriteLine($"SetLogsPath error: {ex.Message}");
        }
      }
    }

    /// <summary>
    /// Логирование ошибок в файл
    /// </summary>
    public static void LogError(string message)
    {
      try
      {
        if(message != "")
          File.AppendAllText(_logFilePath,
              $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
      }
      catch
      {
        // Игнорируем ошибки логирования
      }
    }

    // ======== ПЕРЕГРУЗКИ ВАЛИДАЦИЙ: по пути и по содержимому ========

    #region IsValidReflexChainsFile

    /// <summary>
    /// Проверяет валидность файла цепочек рефлексов по пути
    /// </summary>
    public static bool IsValidReflexChainsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidReflexChainsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла цепочек рефлексов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidReflexChainsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем, что файл содержит только комментарии/пустые строки
      bool hasOnlyComments = lineList.All(line =>
          string.IsNullOrWhiteSpace(line) ||
          line.Trim().StartsWith("#", StringComparison.Ordinal));

      if (hasOnlyComments)
        return true;

      // Проверяем все строки с данными
      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');

        if (parts.Length >= 2 && parts[0] == "CHAIN")
        {
          // CHAIN|ID
          if (!int.TryParse(parts[1], out int chainId) || chainId <= 0)
            return false;

          // Должно быть хотя бы ID после CHAIN
          if (parts.Length < 2)
            return false;
        }
        else if (parts.Length >= 5 && parts[0] == "LINK")
        {
          if (!int.TryParse(parts[1], out int linkId) || linkId <= 0 ||
              !int.TryParse(parts[2], out int actionId) || actionId <= 0 ||
              !int.TryParse(parts[3], out int successNext) ||
              !int.TryParse(parts[4], out int failureNext))
            return false;
        }
        else
        {
          return false; // Неизвестный формат строки
        }
      }

      return true; // Все строки прошли проверку
    }

    #endregion

    #region IsValidGeneticReflexesFile

    /// <summary>
    /// Проверяет валидность файла безусловных рефлексов по пути
    /// </summary>
    public static bool IsValidGeneticReflexesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidGeneticReflexesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла безусловных рефлексов
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidGeneticReflexesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 5)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidActionsFile

    /// <summary>
    /// Проверяет валидность файла адаптивных действий по пути
    /// </summary>
    public static bool IsValidActionsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidActionsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла адаптивных действий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidActionsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 5)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsInfluenceValidActionsFile

    /// <summary>
    /// Проверяет валидность файла гомеостатических воздействий по пути
    /// </summary>
    public static bool IsInfluenceValidActionsFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsInfluenceValidActionsFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла гомеостатических воздействий
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsInfluenceValidActionsFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 3)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidStyleFile

    /// <summary>
    /// Проверяет валидность файла стилей по пути
    /// </summary>
    public static bool IsValidStyleFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidStyleFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла стилей (по строкам)
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidStyleFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 4)
          return false;

        if (!int.TryParse(parts[0], out _) ||
            !int.TryParse(parts[3], out int weight) || weight < 0 || weight > 100)
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAgentParametersFile

    /// <summary>
    /// Проверяет валидность файла параметров агента по пути
    /// </summary>
    public static bool IsValidAgentParametersFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAgentParametersFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла параметров агента
    /// Разрешает файлы, содержащие только шапку (комментарии #)
    /// </summary>
    public static bool IsValidAgentParametersFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      foreach (var line in lineList)
      {
        var trimmed = line?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#", StringComparison.Ordinal))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 9)
          return false;

        if (!int.TryParse(parts[0], out _) ||
            !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || value < 0 || value > 100 ||
            !int.TryParse(parts[4], out int weight) || weight < 0 || weight > 100 ||
            !int.TryParse(parts[5], out int norma) || norma < 0 || norma > 100)
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    #endregion

    #region IsValidAgentPropertiesFile

    /// <summary>
    /// Проверяет валидность файла свойств агента по пути
    /// </summary>
    public static bool IsValidAgentPropertiesFile(string filePath)
    {
      if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidAgentPropertiesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла свойств агента
    /// Разрешает файлы, содержащие только шапку (комментарии #), если нет данных
    /// Если есть данные — требует обязательные ключи
    /// </summary>
    public static bool IsValidAgentPropertiesFile(IEnumerable<string> lines)
    {
      if (lines == null)
        return false;

      var lineList = lines.ToList();
      if (lineList.Count < 1)
        return false;

      // Проверяем наличие шапки
      bool hasHeader = lineList.Any(l => l?.Contains(FileHeaders.PropertiesFormat) == true);
      if (!hasHeader)
        return false;

      // Проверяем ключи
      bool hasIsSleeping = lineList.Any(l => l?.Contains(FileHeaders.PropertiesIsSleeping) == true);
      bool hasIsDead = lineList.Any(l => l?.Contains(FileHeaders.PropertiesIsDead) == true);
      bool hasName = lineList.Any(l => l?.StartsWith("Name|") == true);
      bool hasEvolutionStage = lineList.Any(l => l?.StartsWith("EvolutionStage|") == true);

      // Если есть хотя бы один ключ — требуем все
      if (hasIsSleeping || hasIsDead || hasName || hasEvolutionStage)
        return hasIsSleeping && hasIsDead && hasName && hasEvolutionStage;

      // Если нет данных — достаточно шапки
      return true;
    }

    #endregion

    // Вспомогательный метод для разделения строк
    private static string[] SplitLines(string content)
    {
      return content.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries);
    }

    // ======== БЕЗОПАСНОЕ СОХРАНЕНИЕ С РЕЗЕРВНОЙ КОПИЕЙ ========

    /// <summary>
    /// Безопасное сохранение файла с проверкой валидности
    /// </summary>
    public static (bool Success, string ErrorMessage) SafeSaveFile(
        string filePath,
        IEnumerable<string> content,
        Func<string, bool> validationFunc,
        int minLinesCount = 1,
        string fileDescription = "файл")
    {
      var (success, error) = SafeSaveFileDetailed(
          filePath, content, validationFunc, minLinesCount, fileDescription);

      if (!success)
      {
        LogError($"SafeSaveFile: Ошибка сохранения {fileDescription} ({filePath}): {error}");
      }

      return (success, error);
    }

    /// <summary>
    /// Подробная реализация безопасного сохранения с .tmp и .bak
    /// </summary>
    public static (bool Success, string ErrorMessage) SafeSaveFileDetailed(
        string filePath,
        IEnumerable<string> content,
        Func<string, bool> validationFunc,
        int minLinesCount,
        string fileDescription)
    {
      if (content == null)
      {
        return (false, $"Нет данных для сохранения {fileDescription}");
      }

      var contentList = content.ToList();
      if (contentList.Count < minLinesCount)
      {
        return (false, $"Недостаточно данных (требуется минимум {minLinesCount} строк)");
      }

      string tempPath = filePath + ".tmp";

      try
      {
        File.WriteAllLines(tempPath, contentList);

        // Валидация через функцию
        if (!validationFunc(tempPath))
        {
          File.Delete(tempPath);
          return (false, "Данные не прошли проверку на корректность");
        }

        if (File.Exists(filePath))
        {
          string backupPath = filePath + ".bak";
          File.Replace(tempPath, filePath, backupPath);
        }
        else
        {
          File.Move(tempPath, filePath);
        }

        return (true, string.Empty);
      }
      catch (UnauthorizedAccessException)
      {
        return (false, "Нет прав для сохранения файла. Запустите программу от имени администратора.");
      }
      catch (IOException ex)
      {
        return (false, $"Ошибка записи файла: {ex.Message}");
      }
      catch (Exception ex)
      {
        return (false, $"Неожиданная ошибка: {ex.Message}");
      }
      finally
      {
        if (File.Exists(tempPath))
        {
          try { File.Delete(tempPath); } catch { }
        }
      }
    }
  }
}