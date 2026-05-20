using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система CAD-образов агента (CadChannel PhraseTree)
  /// </summary>
  public sealed class CadBrocaImagesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static CadBrocaImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы CAD-образов. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static CadBrocaImagesSystem Instance => _instance ??
        throw new InvalidOperationException("CadBrocaImagesSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы CAD-образов
    /// </summary>
    /// <param name="psychicDataPath">Путь к каталогу данных психики</param>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("CadBrocaImagesSystem уже инициализирован.");

      _instance = new CadBrocaImagesSystem(psychicDataPath);
    }

    private CadBrocaImagesSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic")
          : Path.Combine(psychicDataPath);
      try
      {
        EnsureDataDirectory();
        LoadCadBrocaImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string CadBrocaImageFileName = "cad_broca_images";

    /// <summary>
    /// CAD-образ агента ИИ
    /// </summary>
    public class CadBrocaImage
    {
      /// <summary>
      /// Идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Массив ID паттернов из CadChannel PhraseTree
      /// </summary>
      public List<int> PatternIdList { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, CadBrocaImage> _cadBrocaImages = new Dictionary<int, CadBrocaImage>();
    private int _lastCadBrocaImageId = 0;

    private readonly Dictionary<string, int> _unicumCadBrocaKeyToId = new Dictionary<string, int>();

    private bool _suppressFoundExistingLog = false;

    /// <summary>
    /// Отключить логирование при нахождении существующего образа (для массовой загрузки из файла).
    /// </summary>
    public void SetSuppressFoundExistingLog(bool suppress)
    {
      _suppressFoundExistingLog = suppress;
    }

    #endregion

    #region Управление CAD-образами

    /// <summary>
    /// Возвращает список всех CAD-образов
    /// </summary>
    public List<CadBrocaImage> GetAllCadBrocaImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _cadBrocaImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить CAD-образ по ID
    /// </summary>
    public CadBrocaImage GetCadBrocaImage(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _cadBrocaImages.TryGetValue(id, out var image) ? image : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать новый CAD-образ или вернуть существующий
    /// </summary>
    public (int Id, CadBrocaImage Image) CreateNewCadBrocaImage(List<int> patternIdList, bool checkUnicum = true)
    {
      if (patternIdList == null)
        return (0, null);

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumCadBrocaImageNoLock(patternIdList);
          if (existing.Image != null)
          {
            if (!_suppressFoundExistingLog)
              Logger.Info($"Найден существующий CAD-образ ID={existing.Id}");
            return existing;
          }
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateCadBrocaImageCore(0, patternIdList, false);
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

    internal (int Id, CadBrocaImage Image) CreateNewCadBrocaWithIdNoLock(
        int id,
        List<int> patternIdList,
        bool checkUnicum)
    {
      return CreateCadBrocaImageCore(id, patternIdList, checkUnicum);
    }

    private (int Id, CadBrocaImage Image) CreateCadBrocaImageCore(
        int id,
        List<int> patternIdList,
        bool checkUnicum)
    {
      if (patternIdList == null)
        return (0, null);

      if (checkUnicum)
      {
        var existing = CheckUnicumCadBrocaImageNoLock(patternIdList);
        if (existing.Image != null)
          return existing;
      }

      int newId = id;
      if (id == 0)
        newId = ++_lastCadBrocaImageId;
      else if (_lastCadBrocaImageId < id)
        _lastCadBrocaImageId = id;

      var image = new CadBrocaImage
      {
        Id = newId,
        PatternIdList = patternIdList?.ToList() ?? new List<int>()
      };

      _cadBrocaImages[newId] = image;
      _unicumCadBrocaKeyToId[CadBrocaUnicumKey(patternIdList)] = newId;
      if (checkUnicum && !_suppressFoundExistingLog)
        Logger.Info($"Создан новый CAD-образ ID={newId}");

      return (newId, image);
    }

    private static string CadBrocaUnicumKey(List<int> patternIdList)
    {
      if (patternIdList == null || patternIdList.Count == 0)
        return "";
      return string.Join(",", patternIdList.OrderBy(x => x));
    }

    private (int Id, CadBrocaImage Image) CheckUnicumCadBrocaImageNoLock(List<int> patternIdList)
    {
      string key = CadBrocaUnicumKey(patternIdList);
      if (_unicumCadBrocaKeyToId.TryGetValue(key, out int existingId) &&
          _cadBrocaImages.TryGetValue(existingId, out var existingImg))
        return (existingId, existingImg);

      foreach (var kvp in _cadBrocaImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (!AddUtils.AreListsEqual(patternIdList, v.PatternIdList))
          continue;

        _unicumCadBrocaKeyToId[key] = kvp.Key;
        return (kvp.Key, v);
      }

      return (0, null);
    }

    /// <summary>
    /// Очищает все CAD-образы
    /// </summary>
    public void ClearAllCadBrocaImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _cadBrocaImages.Clear();
        _unicumCadBrocaKeyToId.Clear();
        _lastCadBrocaImageId = 0;
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

    private string GetCadBrocaImagesFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{CadBrocaImageFileName}.dat");
    }

    private void LoadCadBrocaImages()
    {
      string filePath = GetCadBrocaImagesFilePath();

      if (!File.Exists(filePath) || !FileValidator.IsValidCadBrocaImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.CadBrocaImagesFormat,
            FileValidator.FileHeaders.CadBrocaPatternIdList
          };

          File.WriteAllLines(filePath, lines);
          _cadBrocaImages.Clear();
          _unicumCadBrocaKeyToId.Clear();
          _lastCadBrocaImageId = 0;
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
          _cadBrocaImages.Clear();
          _unicumCadBrocaKeyToId.Clear();
          _lastCadBrocaImageId = 0;

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
            CreateCadBrocaImageCore(id, patternIdList, false);
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

    internal (bool Success, string ErrorMessage) SaveCadBrocaImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveCadBrocaImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    private (bool Success, string ErrorMessage) SaveCadBrocaImagesNoLock()
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.CadBrocaImagesFormat,
          FileValidator.FileHeaders.CadBrocaPatternIdList
        };

        foreach (var kvp in _cadBrocaImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          lines.Add($"{v.Id}|{AddUtils.IntListToString(v.PatternIdList)}|");
        }

        var minLinesCount = lines.Count == 2 ? 2 : 3;
        var result = FileValidator.SafeSaveFile(
            GetCadBrocaImagesFilePath(),
            lines,
            content => FileValidator.IsValidCadBrocaImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "CAD-образов");

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
    /// Освобождает ресурсы, используемые системой CAD-образов.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        var (success, error) = SaveCadBrocaImages();
        if (!success && !string.IsNullOrEmpty(error))
          Logger.Error($"CadBrocaImagesSystem: не удалось сохранить CAD-образы: {error}");
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
