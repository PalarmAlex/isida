using ISIDA.Psychic.Automatism;
using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic
{
  /// <summary>
  /// Система управления эмоциями агента
  /// </summary>
  public sealed class EmotionsImageSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static EmotionsImageSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов действий. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static EmotionsImageSystem Instance => _instance ??
        throw new InvalidOperationException("EmotionsSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы образов действий
    /// </summary>
    /// <param name="psychicDataPath">Путь к каталогу данных психики</param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("EmotionsSystem уже инициализирован.");

      _instance = new EmotionsImageSystem(psychicDataPath);
    }

    private EmotionsImageSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic")
          : Path.Combine(psychicDataPath);
      try
      {
        EnsureDataDirectory();
        LoadEmotionsImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string EmotionsImageFileName = "emotions_images";

    /// <summary>
    /// Образ эмоций агента ИИ
    /// </summary>
    public class EmotionsImage
    {
      /// <summary>
      /// Идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Массив ID стилей реагирования
      /// </summary>
      public List<int> BaseStylesList { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, EmotionsImage> _emotionsImages = new Dictionary<int, EmotionsImage>();
    private int _lastEmotionsImageId = 0;

    /// <summary>
    /// Быстрый поиск по ключу списка стилей для проверки уникальности (O(1)).
    /// </summary>
    private readonly Dictionary<string, int> _unicumEmotionsImageKeyToId = new Dictionary<string, int>();

    /// <summary>
    /// Не логировать «Найден существующий образ» (для массовой загрузки).
    /// </summary>
    private bool _suppressFoundExistingLog = false;

    /// <summary>
    /// Отключить логирование при нахождении существующего образа (для массовой загрузки из файла).
    /// </summary>
    public void SetSuppressFoundExistingLog(bool suppress)
    {
      _suppressFoundExistingLog = suppress;
    }

    #endregion

    #region Управление образами действий

    /// <summary>
    /// Получить список ID эмоций образа
    /// </summary>
    public IReadOnlyList<int> GetBaseEmotionIds(int emotionsImageId)
    {
      try
      {
        var emotionImg = GetEmotionsImage(emotionsImageId);
        return emotionImg?.BaseStylesList?.AsReadOnly() ?? new List<int>().AsReadOnly();
      }
      catch(Exception ex)
      {
        Logger.Error(ex.Message);
        return new List<int>().AsReadOnly();
      }
    }

    /// <summary>
    /// Возвращает список всех образов эмоций
    /// </summary>
    /// <returns>Копия списка образов действий</returns>
    public List<EmotionsImage> GetAllEmotionsImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _emotionsImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить образ эмоции по ID
    /// </summary>
    /// <param name="id">ID образа эмоции</param>
    /// <returns>Образ эмоции или null, если не найден</returns>
    public EmotionsImage GetEmotionsImage(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _emotionsImages.TryGetValue(id, out var image) ? image : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать новый образ эмоций или возвратить существующий
    /// </summary>
    /// <param name="baseStylesList">Массив ID стилей</param>
    /// <param name="checkUnicum">Проверять уникальность</param>
    /// <returns>ID образа и сам образ</returns>
    public (int Id, EmotionsImage Image) CreateNewEmotionsImage(
        List<int> baseStylesList,
        bool checkUnicum)
    {
      // Не создавать образ с пустым стилями
      if (baseStylesList == null)
        return (0, null);

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumEmotionsImageNoLock(baseStylesList);
          if (existing.Image != null)
          {
            if (!_suppressFoundExistingLog)
              Logger.Info($"Найден существующий образ ID={existing.Id}");
            return existing;
          }
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateEmotionsImageCore(0, baseStylesList, false);
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
    /// Создать новый образ эмоций с указанным ID (без блокировки - для внутреннего использования)
    /// </summary>
    internal (int Id, EmotionsImage Image) CreateNewEmotionsImageWithIdNoLock(
        int id,
        List<int> baseStylesList,
        bool checkUnicum)
    {
      return CreateEmotionsImageCore(id, baseStylesList, checkUnicum);
    }

    /// <summary>
    /// Общая логика создания образа эмоций (без блокировки)
    /// </summary>
    private (int Id, EmotionsImage Image) CreateEmotionsImageCore(
        int id,
        List<int> baseStylesList,
        bool checkUnicum)
    {
      // Не создавать образ с пустыми стилями
      if (baseStylesList == null)
        return (0, null);

      // Проверка уникальности (если нужно)
      if (checkUnicum)
      {
        var existing = CheckUnicumEmotionsImageNoLock(baseStylesList);
        if (existing.Image != null)
          return existing;
      }

      // Определение ID
      int newId = id;
      if (id == 0)
        newId = ++_lastEmotionsImageId;
      else if (_lastEmotionsImageId < id)
        _lastEmotionsImageId = id;

      // Создание объекта
      var image = new EmotionsImage
      {
        Id = newId,
        BaseStylesList = baseStylesList?.ToList() ?? new List<int>()
      };

      _emotionsImages[newId] = image;
      _unicumEmotionsImageKeyToId[EmotionsImageUnicumKey(baseStylesList)] = newId;
      if (checkUnicum)
        Logger.Info($"Создан новый образ ID={newId}");

      return (newId, image);
    }

    private static string EmotionsImageUnicumKey(List<int> baseStylesList)
    {
      if (baseStylesList == null || baseStylesList.Count == 0)
        return "";
      return string.Join(",", baseStylesList.OrderBy(x => x));
    }

    /// <summary>
    /// Проверить уникальность образа действий (O(1) по индексу, иначе перебор).
    /// </summary>
    private (int Id, EmotionsImage Image) CheckUnicumEmotionsImageNoLock(List<int> baseStylesList)
    {
      string key = EmotionsImageUnicumKey(baseStylesList);
      if (_unicumEmotionsImageKeyToId.TryGetValue(key, out int existingId) &&
          _emotionsImages.TryGetValue(existingId, out var existingImg))
        return (existingId, existingImg);

      foreach (var kvp in _emotionsImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (!AddUtils.AreListsEqual(baseStylesList, v.BaseStylesList))
          continue;

        _unicumEmotionsImageKeyToId[key] = kvp.Key;
        return (kvp.Key, v);
      }

      return (0, null);
    }

    /// <summary>
    /// Очищает все образы действий
    /// </summary>
    public void ClearAllActionsImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _emotionsImages.Clear();
        _unicumEmotionsImageKeyToId.Clear();
        _lastEmotionsImageId = 0;
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

    private string GetEmotionsImagesFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{EmotionsImageFileName}.dat");
    }

    /// <summary>
    /// Загружает образы действий из файла
    /// </summary>
    private void LoadEmotionsImages()
    {
      string filePath = GetEmotionsImagesFilePath();

      // Если файл не существует или невалиден, создаем новый с шапкой
      if (!File.Exists(filePath) || !FileValidator.IsValidEmotionsImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.EmotionsImagesFormat,
            FileValidator.FileHeaders.EmotionsImagesBaseIdList
          };

          File.WriteAllLines(filePath, lines);
          _emotionsImages.Clear();
          _unicumEmotionsImageKeyToId.Clear();
          _lastEmotionsImageId = 0;
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
          _emotionsImages.Clear();
          _unicumEmotionsImageKeyToId.Clear();
          _lastEmotionsImageId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 1)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var baseStylesList = AddUtils.ParseIntList(parts[1]);

            // При загрузке из файла НЕ проверяем уникальность - должны сохранить все записи как есть
            CreateEmotionsImageCore(id, baseStylesList, false);
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

    /// <summary>
    /// Сохраняет образы эмоций в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveEmotionsImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveEmotionsImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет образы эмоций в файл (без блокировки - для внутреннего использования)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveEmotionsImagesNoLock()
    {
      try
      {
        var lines = new List<string>
          {
            FileValidator.FileHeaders.EmotionsImagesFormat,
            FileValidator.FileHeaders.EmotionsImagesBaseIdList
          };

        foreach (var kvp in _emotionsImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          var line = $"{v.Id}|";
          line += AddUtils.IntListToString(v.BaseStylesList);
          lines.Add(line);
        }

        var minLinesCount = lines.Count == 2 ? 2 : 3;
        var result = FileValidator.SafeSaveFile(
            GetEmotionsImagesFilePath(),
            lines,
            content => FileValidator.IsValidEmotionsImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "образов эмоций");

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
    /// Освобождает ресурсы, используемые объектом PsychicSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveEmotionsImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
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
