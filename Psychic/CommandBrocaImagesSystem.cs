using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система образов команды симбионта (CommandChannel PhraseTree)
  /// </summary>
  public sealed class CommandBrocaImagesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static CommandBrocaImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов команды. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static CommandBrocaImagesSystem Instance => _instance ??
        throw new InvalidOperationException("CommandBrocaImagesSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы образов команды
    /// </summary>
    /// <param name="psychicDataPath">Путь к каталогу данных психики</param>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("CommandBrocaImagesSystem уже инициализирован.");

      _instance = new CommandBrocaImagesSystem(psychicDataPath);
    }

    private CommandBrocaImagesSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic")
          : Path.Combine(psychicDataPath);
      try
      {
        EnsureDataDirectory();
        LoadCommandBrocaImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string CommandBrocaImageFileName = "command_broca_images";
    private const string LegacyCadBrocaImageFileName = "cad_broca_images";

    /// <summary>
    /// Образ команды симбионта ИИ
    /// </summary>
    public class CommandBrocaImage
    {
      /// <summary>
      /// Идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Массив ID паттернов из CommandChannel PhraseTree
      /// </summary>
      public List<int> PatternIdList { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, CommandBrocaImage> _commandBrocaImages = new Dictionary<int, CommandBrocaImage>();
    private int _lastCommandBrocaImageId = 0;

    private readonly Dictionary<string, int> _unicumCommandBrocaKeyToId = new Dictionary<string, int>();

    private bool _suppressFoundExistingLog = false;

    /// <summary>
    /// Отключить логирование при нахождении существующего образа (для массовой загрузки из файла).
    /// </summary>
    public void SetSuppressFoundExistingLog(bool suppress)
    {
      _suppressFoundExistingLog = suppress;
    }

    #endregion

    #region Управление образами команды

    /// <summary>
    /// Возвращает список всех образов команды
    /// </summary>
    public List<CommandBrocaImage> GetAllCommandBrocaImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _commandBrocaImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить образ команды по ID
    /// </summary>
    public CommandBrocaImage GetCommandBrocaImage(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _commandBrocaImages.TryGetValue(id, out var image) ? image : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать новый образ команды или вернуть существующий
    /// </summary>
    public (int Id, CommandBrocaImage Image) CreateNewCommandBrocaImage(List<int> patternIdList, bool checkUnicum = true)
    {
      if (patternIdList == null)
        return (0, null);

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumCommandBrocaImageNoLock(patternIdList);
          if (existing.Image != null)
          {
            if (!_suppressFoundExistingLog)
              Logger.Info($"Найден существующий образ команды ID={existing.Id}");
            return existing;
          }
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateCommandBrocaImageCore(0, patternIdList, false);
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      finally
      {
        _lock.ExitUpgradeableReadLock();
      }
    }

    internal (int Id, CommandBrocaImage Image) CreateNewCommandBrocaWithIdNoLock(
        int id,
        List<int> patternIdList,
        bool checkUnicum)
    {
      return CreateCommandBrocaImageCore(id, patternIdList, checkUnicum);
    }

    private (int Id, CommandBrocaImage Image) CreateCommandBrocaImageCore(
        int id,
        List<int> patternIdList,
        bool checkUnicum)
    {
      if (patternIdList == null)
        return (0, null);

      if (checkUnicum)
      {
        var existing = CheckUnicumCommandBrocaImageNoLock(patternIdList);
        if (existing.Image != null)
          return existing;
      }

      int newId = id;
      if (id == 0)
        newId = ++_lastCommandBrocaImageId;
      else if (_lastCommandBrocaImageId < id)
        _lastCommandBrocaImageId = id;

      var image = new CommandBrocaImage
      {
        Id = newId,
        PatternIdList = patternIdList?.ToList() ?? new List<int>()
      };

      _commandBrocaImages[newId] = image;
      _unicumCommandBrocaKeyToId[CommandBrocaUnicumKey(patternIdList)] = newId;
      if (checkUnicum && !_suppressFoundExistingLog)
        Logger.Info($"Создан новый образ команды ID={newId}");

      return (newId, image);
    }

    private static string CommandBrocaUnicumKey(List<int> patternIdList)
    {
      if (patternIdList == null || patternIdList.Count == 0)
        return "";
      return string.Join(",", patternIdList.OrderBy(x => x));
    }

    private (int Id, CommandBrocaImage Image) CheckUnicumCommandBrocaImageNoLock(List<int> patternIdList)
    {
      string key = CommandBrocaUnicumKey(patternIdList);
      if (_unicumCommandBrocaKeyToId.TryGetValue(key, out int existingId) &&
          _commandBrocaImages.TryGetValue(existingId, out var existingImg))
        return (existingId, existingImg);

      foreach (var kvp in _commandBrocaImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (!AddUtils.AreListsEqual(patternIdList, v.PatternIdList))
          continue;

        _unicumCommandBrocaKeyToId[key] = kvp.Key;
        return (kvp.Key, v);
      }

      return (0, null);
    }

    /// <summary>
    /// Очищает все образы команды
    /// </summary>
    public void ClearAllCommandBrocaImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _commandBrocaImages.Clear();
        _unicumCommandBrocaKeyToId.Clear();
        _lastCommandBrocaImageId = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Работа с файлами

    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_psychicDataPath))
        Directory.CreateDirectory(_psychicDataPath);
    }

    private string GetCommandBrocaImagesFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{CommandBrocaImageFileName}.dat");
    }

    private string ResolveCommandBrocaImagesLoadPath()
    {
      string newPath = GetCommandBrocaImagesFilePath();
      if (File.Exists(newPath))
        return newPath;

      string legacyPath = Path.Combine(_psychicDataPath, $"{LegacyCadBrocaImageFileName}.dat");
      if (File.Exists(legacyPath))
        return legacyPath;

      return newPath;
    }

    private void LoadCommandBrocaImages()
    {
      string filePath = ResolveCommandBrocaImagesLoadPath();

      if (!File.Exists(filePath) || !FileValidator.IsValidCommandBrocaImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.CommandBrocaImagesFormat,
            FileValidator.FileHeaders.CommandBrocaPatternIdList
          };

          File.WriteAllLines(GetCommandBrocaImagesFilePath(), lines);
          _commandBrocaImages.Clear();
          _unicumCommandBrocaKeyToId.Clear();
          _lastCommandBrocaImageId = 0;
          return;
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
          throw;
        }
      }

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _commandBrocaImages.Clear();
          _unicumCommandBrocaKeyToId.Clear();
          _lastCommandBrocaImageId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 2)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var patternIdList = AddUtils.ParseIntList(parts[1]);
            CreateCommandBrocaImageCore(id, patternIdList, false);
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    internal (bool Success, string ErrorMessage) SaveCommandBrocaImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveCommandBrocaImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    private (bool Success, string ErrorMessage) SaveCommandBrocaImagesNoLock()
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.CommandBrocaImagesFormat,
          FileValidator.FileHeaders.CommandBrocaPatternIdList
        };

        foreach (var kvp in _commandBrocaImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          lines.Add($"{v.Id}|{AddUtils.IntListToString(v.PatternIdList)}|");
        }

        var minLinesCount = lines.Count == 2 ? 2 : 3;
        var result = FileValidator.SafeSaveFile(
            GetCommandBrocaImagesFilePath(),
            lines,
            content => FileValidator.IsValidCommandBrocaImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "образов команды");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые системой образов команды.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (success, error) = SaveCommandBrocaImages();
        if (!success && !string.IsNullOrEmpty(error))
          Logger.Error($"CommandBrocaImagesSystem: не удалось сохранить образы команды: {error}");
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}
