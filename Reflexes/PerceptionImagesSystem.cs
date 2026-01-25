using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Образы восприятия рефлексов
  /// </summary>
  public sealed class PerceptionImagesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly GeneticReflexesSystem _geneticReflexesSystem;
    private readonly GomeostasSystem _gomeostas;

    #region Инициализация

    private static PerceptionImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов восприятия. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static PerceptionImagesSystem Instance => _instance ??
        throw new InvalidOperationException("PerceptionImagesSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы образов восприятия
    /// </summary>
    /// <param name="gomeostasSystem">Система параметров гомеостаза</param>
    /// <param name="geneticReflexesSystem">Система генетических рефлексов</param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(GomeostasSystem gomeostasSystem, GeneticReflexesSystem geneticReflexesSystem)
    {
      if (_instance != null)
        throw new InvalidOperationException("PerceptionImagesSystem уже инициализирован.");

      _instance = new PerceptionImagesSystem(gomeostasSystem, geneticReflexesSystem);
    }

    private PerceptionImagesSystem(GomeostasSystem gomeostasSystem, GeneticReflexesSystem geneticReflexesSystem)
    {
      _geneticReflexesSystem = geneticReflexesSystem ?? throw new ArgumentNullException(nameof(geneticReflexesSystem));
      _gomeostas = gomeostasSystem ?? throw new ArgumentNullException(nameof(gomeostasSystem));

      try
      {
        EnsureDataDirectory();
        LoadPerceptionImages();
        LoadBehaviorStyleImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string PerceptionImagesFileName = "PerceptionImages";
    private const string BehaviorStyleImagesFileName = "BehaviorStyleImages";

    /// <summary>
    /// Образы восприятия рефлексов
    /// </summary>
    public class PerceptionImage
    {
      /// <summary>
      /// Уникальный идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Список ID воздействий с пульта
      /// </summary>
      public List<int> InfluenceActionsList { get; set; } = new List<int>();

      /// <summary>
      /// Список ID фраз
      /// </summary>
      public List<int> PhraseIdList { get; set; } = new List<int>();
    }

    /// <summary>
    /// Образы контекстов реагиварония
    /// </summary>
    public class BehaviorStyleImage
    {
      /// <summary>
      /// Уникальный идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Список ID стилей реагирования
      /// </summary>
      public List<int> BehaviorStylesList { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, PerceptionImage> _perceptionImages = new Dictionary<int, PerceptionImage>();
    private readonly Dictionary<int, BehaviorStyleImage> _behaviorStyleImages = new Dictionary<int, BehaviorStyleImage>();
    private int _lastBehaviorStyleImageId = 0;
    private int _lastPerceptionImageId = 0;

    #endregion

    #region Управление образами

    /// <summary>
    /// Возвращает список всех образов восприятия рефлексов
    /// </summary>
    /// <returns>Копия списка образов восприятия рефлексов</returns>
    public List<PerceptionImage> GetAllPerceptionImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _perceptionImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Возвращает список всех образов стилей реагирования
    /// </summary>
    /// <returns>Копия списка образов стилей реагирования</returns>
    public List<BehaviorStyleImage> GetAllBehaviorStyleImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _behaviorStyleImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Добавляет новый образ стилей реагирования или возвращает ID существующего
    /// </summary>
    /// <param name="behaviorStylesList">Список ID стилей реагирования</param>
    /// <returns>ID существующего или нового образа. 0 если ошибка</returns>
    public int AddBehaviorStyleImage(List<int> behaviorStylesList)
    {
      // образы нужны уже на стадии 0 - для привязки к дереву рефлексов
      if (behaviorStylesList == null || !behaviorStylesList.Any())
        return 0;

      var newBehaviorStyleImage = new BehaviorStyleImage
      {
        BehaviorStylesList = behaviorStylesList.OrderBy(x => x).ToList()
      };

      int resultId = 0;
      bool needSave = false;

      _lock.EnterWriteLock();
      try
      {
        var existingImage = _behaviorStyleImages.Values.FirstOrDefault(existing =>
            IsAreBehaviorStyleImage(existing, newBehaviorStyleImage));

        if (existingImage != null)
          resultId = existingImage.Id;
        else
        {
          int newId = ++_lastBehaviorStyleImageId;
          var styleImage = new BehaviorStyleImage
          {
            Id = newId,
            BehaviorStylesList = behaviorStylesList.OrderBy(x => x).ToList()
          };

          _behaviorStyleImages.Add(newId, styleImage);
          resultId = newId;
          needSave = true; // Помечаем, что нужно сохранить
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      // Сохраняем за пределами блокировок
      if (needSave)
      {
        try
        {
          var saveResult = SaveBehaviorStyleImages();
          if (!saveResult.Success)
            Logger.Warning($"Ошибка сохранения образа стилей ID {resultId}: {saveResult.ErrorMessage}");
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }

      return resultId;
    }

    private bool IsAreBehaviorStyleImage(BehaviorStyleImage existing, BehaviorStyleImage newImage)
    {
      if (existing == null || newImage == null) return false;

      return existing.BehaviorStylesList.OrderBy(x => x).SequenceEqual(
             newImage.BehaviorStylesList.OrderBy(x => x));
    }

    /// <summary>
    /// Добавляет новый образ восприятия рефлексов или возвращает ID существующего
    /// </summary>
    /// <param name="influenceActionList">Список ID воздействий с пульта</param>
    /// <param name="phraseIdList">Список ID фраз</param>
    /// <returns>ID существующего или нового образа. 0 если ошибка</returns>
    public int AddPerceptionImage(List<int> influenceActionList, List<int> phraseIdList)
    {
      // образы нужны уже на стадии 0 - для привязки к дереву рефлексов

      if ((influenceActionList == null || !influenceActionList.Any()) &&
          (phraseIdList == null || !phraseIdList.Any()))
        return 0;

      var newPerceptionImage = new PerceptionImage
      {
        InfluenceActionsList = influenceActionList?.OrderBy(x => x).ToList() ?? new List<int>(),
        PhraseIdList = phraseIdList?.OrderBy(x => x).ToList() ?? new List<int>()
      };

      int resultId = 0;
      bool needSave = false;

      _lock.EnterWriteLock();
      try
      {
        var existingImage = _perceptionImages.Values.FirstOrDefault(existing =>
            IsArePerceptionImage(existing, newPerceptionImage));

        if (existingImage != null)
        {
          resultId = existingImage.Id;
        }
        else
        {
          // Создаем новый образ
          int newId = ++_lastPerceptionImageId;
          var perceptionImage = new PerceptionImage
          {
            Id = newId,
            InfluenceActionsList = influenceActionList?.OrderBy(x => x).ToList() ?? new List<int>(),
            PhraseIdList = phraseIdList?.OrderBy(x => x).ToList() ?? new List<int>()
          };

          _perceptionImages.Add(newId, perceptionImage);
          resultId = newId;
          needSave = true; // Помечаем, что нужно сохранить
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      // Сохраняем за пределами блокировок
      if (needSave)
      {
        try
        {
          var saveResult = SavePerceptionImages();
          if (!saveResult.Success)
          {
            Logger.Warning($"Ошибка сохранения образа восприятия ID {resultId}: {saveResult.ErrorMessage}");
          }
        }
        catch (Exception ex)
        {
          Logger.Error(ex.Message);
        }
      }

      return resultId;
    }

    private bool IsArePerceptionImage(PerceptionImage existing, PerceptionImage newImage)
    {
      if (existing == null || newImage == null) return false;

      return existing.InfluenceActionsList.OrderBy(x => x).SequenceEqual(
             newImage.InfluenceActionsList.OrderBy(x => x)) &&
             existing.PhraseIdList.OrderBy(x => x).SequenceEqual(
             newImage.PhraseIdList.OrderBy(x => x));
    }

    /// <summary>
    /// Очищает все образы восприятия
    /// </summary>
    public void ClearAllPerceptionImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _perceptionImages.Clear();
        _lastPerceptionImageId = 0;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Очищает все образы стилей поведения
    /// </summary>
    public void ClearAllBehaviorStyleImages()
    {
      _lock.EnterWriteLock();
      try
      {
        _behaviorStyleImages.Clear();
        _lastBehaviorStyleImageId = 0;
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
      string directory = Path.GetDirectoryName(GetPerceptionImagesFilePath());
      if (!Directory.Exists(directory))
      {
        Directory.CreateDirectory(directory);
      }
    }

    private string GetPerceptionImagesFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{PerceptionImagesFileName}.dat");
    }

    private string GetBehaviorStyleImagesFilePath()
    {
      string reflexesPath = _geneticReflexesSystem.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{BehaviorStyleImagesFileName}.dat");
    }

    /// <summary>
    /// Проверяет валидность файла образов восприятия
    /// </summary>
    private bool IsValidPerceptionImagesFile(string filePath)
    {
      if (!File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidPerceptionImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов восприятия
    /// </summary>
    private bool IsValidPerceptionImagesFile(IEnumerable<string> lines)
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

      return true;
    }

    /// <summary>
    /// Проверяет валидность файла образов стилей поведения
    /// </summary>
    private bool IsValidBehaviorStyleImagesFile(string filePath)
    {
      if (!File.Exists(filePath))
        return false;

      try
      {
        var lines = File.ReadLines(filePath).ToList();
        return IsValidBehaviorStyleImagesFile(lines);
      }
      catch
      {
        return false;
      }
    }

    /// <summary>
    /// Проверяет валидность содержимого файла образов стилей поведения
    /// </summary>
    private bool IsValidBehaviorStyleImagesFile(IEnumerable<string> lines)
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
        if (parts.Length < 2)
          return false;

        if (!int.TryParse(parts[0], out _))
          return false;

        return true;
      }

      return true; // только шапка — допустимо
    }

    /// <summary>
    /// Загружает образы восприятия из файла
    /// </summary>
    private void LoadPerceptionImages()
    {
      string filePath = GetPerceptionImagesFilePath();

      if (!IsValidPerceptionImagesFile(filePath))
        return;

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _perceptionImages.Clear();
          _lastPerceptionImageId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 3)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var perceptionImage = new PerceptionImage
            {
              Id = id,
              InfluenceActionsList = AddUtils.ParseIntList(parts[1]),
              PhraseIdList = AddUtils.ParseIntList(parts[2])
            };

            _perceptionImages[id] = perceptionImage;
            if (id > _lastPerceptionImageId)
              _lastPerceptionImageId = id;
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
    /// Загружает образы стилей поведения из файла
    /// </summary>
    private void LoadBehaviorStyleImages()
    {
      string filePath = GetBehaviorStyleImagesFilePath();

      if (!IsValidBehaviorStyleImagesFile(filePath))
        return;

      try
      {
        _lock.EnterWriteLock();
        try
        {
          _behaviorStyleImages.Clear();
          _lastBehaviorStyleImageId = 0;

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

            var behaviorStyleImage = new BehaviorStyleImage
            {
              Id = id,
              BehaviorStylesList = AddUtils.ParseIntList(parts[1])
            };

            _behaviorStyleImages[id] = behaviorStyleImage;
            if (id > _lastBehaviorStyleImageId)
              _lastBehaviorStyleImageId = id;
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
    /// Сохраняет образы восприятия в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SavePerceptionImages()
    {
      try
      {
        var lines = new List<string>
                {
                  "# ID|InfluenceActionsList|PhraseIdList",
                  "# Формат списков: id1,id2,id3"
                };

        foreach (var image in _perceptionImages.Values.OrderBy(x => x.Id))
        {
          lines.Add($"{image.Id}|{AddUtils.IntListToString(image.InfluenceActionsList)}|{AddUtils.IntListToString(image.PhraseIdList)}");
        }

        var lineCount = 3;
        if (lines.Count == 2)
          lineCount = 2; // для случая очистки всего кроме шапки

        var result = FileValidator.SafeSaveFile(
            GetPerceptionImagesFilePath(),
            lines,
            content => IsValidPerceptionImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: lineCount,
            fileDescription: "образов восприятия");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    /// <summary>
    /// Сохраняет образы стилей поведения в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveBehaviorStyleImages()
    {
      try
      {
        var lines = new List<string>
                {
                  "# ID|BehaviorStylesList",
                  "# Формат списка: id1,id2,id3"
                };

        foreach (var image in _behaviorStyleImages.Values.OrderBy(x => x.Id))
        {
          lines.Add($"{image.Id}|{AddUtils.IntListToString(image.BehaviorStylesList)}");
        }

        var lineCount = 3;
        if (lines.Count == 2)
          lineCount = 2; // для случая очистки всего кроме шапки

        var result = FileValidator.SafeSaveFile(
            GetBehaviorStyleImagesFilePath(),
            lines,
            content => IsValidBehaviorStyleImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: lineCount,
            fileDescription: "образов стилей поведения");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    /// <summary>
    /// Сохраняет все данные образов
    /// </summary>
    internal void SaveAll()
    {
      try
      {
        SavePerceptionImages();
        SaveBehaviorStyleImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    #endregion

    #region Очистка фраз

    /// <summary>
    /// Очищает все PhraseIdList в образах восприятия
    /// </summary>
    public void ClearAllPhraseIds()
    {
      _lock.EnterWriteLock();
      try
      {
        foreach (var image in _perceptionImages.Values)
        {
          image.PhraseIdList.Clear();
        }

        // Сохраняем изменения
        SavePerceptionImages();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом PerceptionImagesSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveAll();
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