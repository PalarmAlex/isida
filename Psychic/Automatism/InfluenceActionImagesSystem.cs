using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Система образов сочетаний действий с пульта (для дерева автоматизмов)
  /// </summary>
  public sealed class InfluenceActionsImagesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static InfluenceActionsImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов действий. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static InfluenceActionsImagesSystem Instance => _instance ??
        throw new InvalidOperationException("InfluenceActionsImagesSystem не инициализирован. Вызовите InitializeInstance().");

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
        throw new InvalidOperationException("InfluenceActionsImagesSystem уже инициализирован.");

      _instance = new InfluenceActionsImagesSystem(psychicDataPath);
    }

    private InfluenceActionsImagesSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Automatism")
          : Path.Combine(psychicDataPath, "Automatism");
      try
      {
        EnsureDataDirectory();
        LoadInfluenceActionsImages();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string InfluenceActionsImagesFileName = "influence_action_images";

    /// <summary>
    /// Образ действий оператора или симбионта ИИ
    /// </summary>
    public class InfluenceActionsImage
    {
      /// <summary>
      /// Идентификатор данного сочетания пусковых стимулов
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Массив ID действий с Пульта
      /// </summary>
      public List<int> ActIdList { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, InfluenceActionsImage> _actionsImages = new Dictionary<int, InfluenceActionsImage>();
    private int _lastActionsImageId = 0;

    #endregion

    #region Управление образами действий

    /// <summary>
    /// Возвращает список всех образов действий
    /// </summary>
    /// <returns>Копия списка образов действий</returns>
    public List<InfluenceActionsImage> GetAllInfluenceActionsImagesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _actionsImages.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получить список ID действий образа
    /// </summary>
    public IReadOnlyList<int> GetInfluenceActionIds(int id)
    {
      try
      {
        var actImg = GetInfluenceActionsImage(id);
        return actImg?.ActIdList?.AsReadOnly() ?? new List<int>().AsReadOnly();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return new List<int>().AsReadOnly();
      }
    }

    /// <summary>
    /// Получить образ действия по ID
    /// </summary>
    /// <param name="id">ID образа действия</param>
    /// <returns>Образ действия или null, если не найден</returns>
    public InfluenceActionsImage GetInfluenceActionsImage(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _actionsImages.TryGetValue(id, out var image) ? image : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создать новый образ действий или возвратить существующий
    /// </summary>
    /// <param name="actIdList">Массив ID действий</param>
    /// <param name="checkUnicum">Проверять уникальность</param>
    /// <returns>ID образа и сам образ</returns>
    public (int Id, InfluenceActionsImage Image) CreateNewInfluenceActionsImage(
        List<int> actIdList,
        bool checkUnicum)
    {
      // Не создавать образ с пустым действием
      if (actIdList == null || actIdList.Count == 0)
        return (0, null);

      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumInfluenceActionsImageNoLock(actIdList);
          if (existing.Image != null)
          {
            Logger.Info($"Найден существующий образ ID={existing.Id}");
            return existing;
          }
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateInfluenceActionsImageCore(0, actIdList, false);
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
    /// Создать новый образ действий с указанным ID (без блокировки - для внутреннего использования)
    /// </summary>
    internal (int Id, InfluenceActionsImage Image) CreateNewInfluenceActionsImageWithIdNoLock(
        int id,
        List<int> actIdList,
        bool checkUnicum)
    {
      return CreateInfluenceActionsImageCore(id, actIdList, checkUnicum);
    }

    /// <summary>
    /// Общая логика создания образа действий (без блокировки)
    /// </summary>
    private (int Id, InfluenceActionsImage Image) CreateInfluenceActionsImageCore(
        int id,
        List<int> actIdList,
        bool checkUnicum)
    {
      // Не создавать образ с пустым действием
      if (actIdList == null)
        return (0, null);

      // Проверка уникальности (если нужно)
      if (checkUnicum)
      {
        var existing = CheckUnicumInfluenceActionsImageNoLock(actIdList);
        if (existing.Image != null)
          return existing;
      }

      // Определение ID
      int newId = id;
      if (id == 0)
        newId = ++_lastActionsImageId;
      else if (_lastActionsImageId < id)
        _lastActionsImageId = id;

      // Создание объекта
      var image = new InfluenceActionsImage
      {
        Id = newId,
        ActIdList = actIdList?.ToList() ?? new List<int>()
      };

      _actionsImages[newId] = image;
      if (checkUnicum)
        Logger.Info($"Создан новый образ ID={newId}");

      return (newId, image);
    }

    /// <summary>
    /// Проверить уникальность образа действий
    /// </summary>
    private (int Id, InfluenceActionsImage Image) CheckUnicumInfluenceActionsImage(List<int> actIdList)
    {
      _lock.EnterReadLock();
      try
      {
        return CheckUnicumInfluenceActionsImageNoLock(actIdList);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверить уникальность образа действий (без блокировки - для внутреннего использования)
    /// </summary>
    private (int Id, InfluenceActionsImage Image) CheckUnicumInfluenceActionsImageNoLock(List<int> actIdList)
    {
      foreach (var kvp in _actionsImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (!AddUtils.AreListsEqual(actIdList, v.ActIdList))
          continue;

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
        _actionsImages.Clear();
        _lastActionsImageId = 0;
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

    private string GetActionsImagesFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{InfluenceActionsImagesFileName}.dat");
    }

    /// <summary>
    /// Загружает образы действий из файла
    /// </summary>
    private void LoadInfluenceActionsImages()
    {
      string filePath = GetActionsImagesFilePath();

      // Если файл не существует или невалиден, создаем новый с шапкой
      if (!File.Exists(filePath) || !FileValidator.IsValidInfluenceActionsImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.InfluenceActionsImagesFormat,
            FileValidator.FileHeaders.InfluenceActionsImagesActIdList
          };

          File.WriteAllLines(filePath, lines);
          _actionsImages.Clear();
          _lastActionsImageId = 0;
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
          _actionsImages.Clear();
          _lastActionsImageId = 0;

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

            var actIdList = AddUtils.ParseIntList(parts[1]);

            // При загрузке из файла НЕ проверяем уникальность - должны сохранить все записи как есть
            CreateInfluenceActionsImageCore(id, actIdList, false);
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
    /// Сохраняет образы действий в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveInfluenceActionsImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveInfluenceActionsImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет образы действий в файл (без блокировки - для внутреннего использования)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveInfluenceActionsImagesNoLock()
    {
      try
      {
        var lines = new List<string>
          {
            FileValidator.FileHeaders.InfluenceActionsImagesFormat,
            FileValidator.FileHeaders.InfluenceActionsImagesActIdList
          };

        foreach (var kvp in _actionsImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          var line = $"{v.Id}|";
          line += AddUtils.IntListToString(v.ActIdList);
          lines.Add(line);
        }

        var minLinesCount = lines.Count == 2 ? 2 : 3;
        var result = FileValidator.SafeSaveFile(
            GetActionsImagesFilePath(),
            lines,
            content => FileValidator.IsValidInfluenceActionsImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "образов действий");

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
    /// Освобождает ресурсы, используемые объектом InfluenceActionsImagesSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveInfluenceActionsImages();
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