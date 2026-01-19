using ISIDA.Common;
using ISIDA.Psychic.Automatism;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Psychic.Automatism.ActionsImagesSystem;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система управления словесными образами агента
  /// </summary>
  public sealed class VerbalBrocaImagesSystem: IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static VerbalBrocaImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов действий. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static VerbalBrocaImagesSystem Instance => _instance ??
        throw new InvalidOperationException("VerbalBrocaImagesSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы вербальных сенсоров
    /// </summary>
    /// <param name="psychicDataPath">Путь к каталогу данных психики</param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("VerbalBrocaImagesSystem уже инициализирован.");

      _instance = new VerbalBrocaImagesSystem(psychicDataPath);
    }

    private VerbalBrocaImagesSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic")
          : Path.Combine(psychicDataPath);
      try
      {
        EnsureDataDirectory();
        LoadVerbalBrocaImages();
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string VerbalBrocaImageFileName = "verbal_broca_images";

    /// <summary>
    /// Словесный образ агента ИИ
    /// </summary>
    public class VerbalBrocaImage
    {
      /// <summary>
      /// Идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// ID первого символа фразы
      /// </summary>
      public int SimbolID { get; set; }

      /// <summary>
      /// Массив ID фраз
      /// </summary>
      public List<int> PhraseIdList { get; set; } = new List<int>();

      /// <summary>
      /// ID тона сообщения с Пульта или Ответного действия
      /// </summary>
      public int ToneId { get; set; }

      /// <summary>
      /// ID настроения при передаче фразы с Пульта или Ответного действия
      /// </summary>
      public int MoodId { get; set; }
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, VerbalBrocaImage> _verbalbrocaImages = new Dictionary<int, VerbalBrocaImage>();
    private int _lastverbalbrocaImageId = 0;

    #endregion

    #region Управление вербальными образами

    /// <summary>
    /// Возвращает список всех вербальных образов
    /// </summary>
    /// <returns>Копия списка вербальных образов</returns>
    public List<VerbalBrocaImage> GetAllVerbalBrocaImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _verbalbrocaImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить вербальный образ по ID
    /// </summary>
    /// <param name="id">ID вербального образа</param>
    /// <returns>вербальный образ или null, если не найден</returns>
    public VerbalBrocaImage GetVerbalBrocaImage(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _verbalbrocaImages.TryGetValue(id, out var image) ? image : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать новый вербальный образ или возвратить существующий
    /// </summary>
    /// <param name="simbolID">ID первого символа фразы</param>
    /// <param name="phraseIdList">Массив ID фраз</param>
    /// <param name="toneId">ID тона сообщения с Пульта или Ответного действия</param>
    /// <param name="moodId">ID настроения при передаче фразы с Пульта или Ответного действия</param>
    /// <param name="checkUnicum">Проверять уникальность</param>
    /// <returns>ID образа и сам образ</returns>
    public (int Id, VerbalBrocaImage Image) CreateNewVerbalBrocaImage(
        int simbolID,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      // Не создавать образ с пустым списками фраз
      if (phraseIdList == null)
        return (0, null);

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumVerbalBrocaImageNoLock(simbolID, phraseIdList, toneId, moodId);
          if (existing.Image != null)
          {
            Logger.Info($"Найден существующий образ ID={existing.Id}");
            return existing;
          }
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateVerbalBrocaImageCore(0, simbolID, phraseIdList, toneId, moodId, false);
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

    /// <summary>
    /// Создать новый вербальный образ с указанным ID (без блокировки - для внутреннего использования)
    /// </summary>
    internal (int Id, VerbalBrocaImage Image) CreateNewVerbalBrocaWithIdNoLock(
        int id,
        int simbolID,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      return CreateVerbalBrocaImageCore(id, simbolID, phraseIdList, toneId, moodId, checkUnicum);
    }

    /// <summary>
    /// Общая логика создания вербального образа (без блокировки)
    /// </summary>
    private (int Id, VerbalBrocaImage Image) CreateVerbalBrocaImageCore(
        int id,
        int simbolID,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      // Не создавать образ с пустым списками фраз
      if (phraseIdList == null)
        return (0, null);

      // Проверка уникальности (если нужно)
      if (checkUnicum)
      {
        var existing = CheckUnicumVerbalBrocaImageNoLock(simbolID, phraseIdList, toneId, moodId);
        if (existing.Image != null)
          return existing;
      }

      // Определение ID
      int newId = id;
      if (id == 0)
        newId = ++_lastverbalbrocaImageId;
      else if (_lastverbalbrocaImageId < id)
        _lastverbalbrocaImageId = id;

      // Создание объекта
      var image = new VerbalBrocaImage
      {
        Id = newId,
        SimbolID = simbolID,
        PhraseIdList = phraseIdList?.ToList() ?? new List<int>(),
        ToneId = toneId,
        MoodId = moodId
      };

      _verbalbrocaImages[newId] = image;
      if (checkUnicum)
        Logger.Info($"Создан новый образ ID={newId}");

      return (newId, image);
    }

    /// <summary>
    /// Проверить уникальность вербального образа (без блокировки - для внутреннего использования)
    /// </summary>
    private (int Id, VerbalBrocaImage Image) CheckUnicumVerbalBrocaImageNoLock(
      int simbolID,
      List<int> phraseIdList,
      int toneId,
      int moodId
      )
    {
      foreach (var kvp in _verbalbrocaImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (simbolID != v.SimbolID || toneId != v.ToneId || moodId != v.MoodId)
          continue;
        if (!AddUtils.AreListsEqual(phraseIdList, v.PhraseIdList))
          continue;

        return (kvp.Key, v);
      }

      return (0, null);
    }

    /// <summary>
    /// Очищает все вербальные образы
    /// </summary>
    public void ClearAllVerbalBrocaImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _verbalbrocaImages.Clear();
        _lastverbalbrocaImageId = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Работа с файлами

    /// <summary>
    /// Создает каталог данных, если его нет
    /// </summary>
    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_psychicDataPath))
        Directory.CreateDirectory(_psychicDataPath);
    }

    private string GetVerbalBrocaImagesFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{VerbalBrocaImageFileName}.dat");
    }

    /// <summary>
    /// Загружает вербальные образы из файла
    /// </summary>
    private void LoadVerbalBrocaImages()
    {
      string filePath = GetVerbalBrocaImagesFilePath();

      // Если файл не существует или невалиден, создаем новый с шапкой
      if (!File.Exists(filePath) || !FileValidator.IsValidVerbalBrocaImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.VerbalBrocaFileNameImagesFormat,
            FileValidator.FileHeaders.VerbalBrocaSimbolID,
            FileValidator.FileHeaders.VerbalBrocaPhraseIdList,
            FileValidator.FileHeaders.VerbalBrocaToneId,
            FileValidator.FileHeaders.VerbalBrocaMoodId
          };

          File.WriteAllLines(filePath, lines);
          _verbalbrocaImages.Clear();
          _lastverbalbrocaImageId = 0;
          return;
        }
        catch (Exception ex)
        {
          Logger.Error($"{ex.Message}");
          throw;
        }
      }

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _verbalbrocaImages.Clear();
          _lastverbalbrocaImageId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 5)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            int simbolID = 0;
            if (!string.IsNullOrWhiteSpace(parts[1]))
              int.TryParse(parts[1], out simbolID);

            var phraseIdList = AddUtils.ParseIntList(parts[2]);

            int toneId = 0;
            if (!string.IsNullOrWhiteSpace(parts[3]))
              int.TryParse(parts[3], out toneId);

            int moodId = 0;
            if (!string.IsNullOrWhiteSpace(parts[4]))
              int.TryParse(parts[4], out moodId);

            // При загрузке из файла НЕ проверяем уникальность - должны сохранить все записи как есть
            CreateVerbalBrocaImageCore(id, simbolID, phraseIdList, toneId, moodId, false);
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
    }

    /// <summary>
    /// Сохраняет  вербальные образы в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveVerbalBrocaImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveVerbalBrocaImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет вербальные образы в файл (без блокировки - для внутреннего использования)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveVerbalBrocaImagesNoLock()
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.VerbalBrocaFileNameImagesFormat,
          FileValidator.FileHeaders.VerbalBrocaSimbolID,
          FileValidator.FileHeaders.VerbalBrocaPhraseIdList,
          FileValidator.FileHeaders.VerbalBrocaToneId,
          FileValidator.FileHeaders.VerbalBrocaMoodId
        };

        foreach (var kvp in _verbalbrocaImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          var line = $"{v.Id}|";

          line += $"{v.SimbolID}|";
          line += AddUtils.IntListToString(v.PhraseIdList);
          line += $"{v.ToneId}|";
          line += $"{v.MoodId}|";

          lines.Add(line);
        }

        var minLinesCount = lines.Count == 5 ? 5 : 6;
        var result = FileValidator.SafeSaveFile(
            GetVerbalBrocaImagesFilePath(),
            lines,
            content => FileValidator.IsValidVerbalBrocaImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "вербальных образов");

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
    /// Освобождает ресурсы, используемые объектом
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveVerbalBrocaImages();
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
      }
    }

    #endregion

  }
}
