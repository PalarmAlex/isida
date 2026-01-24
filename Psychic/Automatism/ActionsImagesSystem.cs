using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Система образов действий агента или оператора
  /// </summary>
  public sealed class ActionsImagesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static ActionsImagesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы образов действий. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static ActionsImagesSystem Instance => _instance ??
        throw new InvalidOperationException("ActionsImagesSystem не инициализирован. Вызовите InitializeInstance().");

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
        throw new InvalidOperationException("ActionsImagesSystem уже инициализирован.");

      _instance = new ActionsImagesSystem(psychicDataPath);
    }

    private ActionsImagesSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Automatism")
          : Path.Combine(psychicDataPath, "Automatism");

      try
      {
        EnsureDataDirectory();
        LoadActionsImages();
      }
      catch (Exception ex)
      {
        Logger.Error($"{ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string ActionsImagesFileName = "action_images";
    private const int PrefixActionIdValue = 10000000; // если ID действия больше prefixActionIdValue, то это цепочка действий
    private static readonly Dictionary<int, (string Description, bool IsThreatening)> _toneDictionary =
      new Dictionary<int, (string, bool)>
      {
        {-1, ("Вялый", false)},
        {0, ("Нормальный", false)},
        {1, ("Повышенный", true)} // Угрожающий тон
      };
    private static readonly Dictionary<int, (string Description, bool IsThreatening)> _moodDictionary =
      new Dictionary<int, (string, bool)>
      {
        {0, ("Нормальное", false)},
        {1, ("Хорошее", false)},
        {2, ("Плохое", true)},        // Плохое настроение - может быть угрожающим
        {3, ("Игривое", false)},
        {4, ("Учитель", false)},
        {5, ("Агрессивное", true)},   // Агрессивное настроение - угрожающее
        {6, ("Защитное", false)},     // Защитное - не угроза, а реакция на угрозу
        {7, ("Протест", true)}        // Протест - может быть угрожающим
      };

    /// <summary>
    /// Образ действий оператора или агента ИИ
    /// </summary>
    public class ActionsImage
    {
      /// <summary>
      /// Идентификатор образа
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Тип образа: 0 - объективное действие, 1 - субъективное предположение
      /// </summary>
      /// <remarks>
      /// Метка о том, что действия является объективным Стимулом (реально воспринятым из Пульта)
      /// или реально выполненным действием (предположение, Правило из сновидения)
      /// </remarks>
      public int Kind { get; set; }

      /// <summary>
      /// Массив ID действий с Пульта или Ответного действия
      /// </summary>
      public List<int> ActIdList { get; set; } = new List<int>();

      /// <summary>
      /// Массив ID фраз (DetectedUnicumPhraseID)
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

    #region Методы для ToneId и MoodId

    /// <summary>
    /// Получает текстовое описание тона по его ID
    /// </summary>
    /// <param name="toneId">ID тона</param>
    /// <returns>Текстовое описание тона или пустую строку, если не найден</returns>
    public static string GetToneText(int toneId)
    {
      return _toneDictionary.TryGetValue(toneId, out var toneInfo)
          ? toneInfo.Description
          : string.Empty;
    }

    /// <summary>
    /// Получает текстовое описание настроения по его ID
    /// </summary>
    /// <param name="moodId">ID настроения</param>
    /// <returns>Текстовое описание настроения или пустую строку, если не найден</returns>
    public static string GetMoodText(int moodId)
    {
      return _moodDictionary.TryGetValue(moodId, out var moodInfo)
          ? moodInfo.Description
          : string.Empty;
    }

    /// <summary>
    /// Получает список всех доступных тонов в формате ключ-значение
    /// </summary>
    /// <returns>Словарь тонов (ID -> Описание)</returns>
    public static Dictionary<int, string> GetToneList()
    {
      return _toneDictionary.ToDictionary(
          kvp => kvp.Key,
          kvp => kvp.Value.Description);
    }

    /// <summary>
    /// Получает список всех доступных настроений в формате ключ-значение
    /// </summary>
    /// <returns>Словарь настроений (ID -> Описание)</returns>
    public static Dictionary<int, string> GetMoodList()
    {
      return _moodDictionary.ToDictionary(
          kvp => kvp.Key,
          kvp => kvp.Value.Description);
    }

    /// <summary>
    /// Проверяет, существует ли тон с указанным ID
    /// </summary>
    /// <param name="toneId">ID тона для проверки</param>
    /// <returns>True, если тон существует</returns>
    public static bool IsValidToneId(int toneId)
    {
      return _toneDictionary.ContainsKey(toneId);
    }

    /// <summary>
    /// Проверяет, существует ли настроение с указанным ID
    /// </summary>
    /// <param name="moodId">ID настроения для проверки</param>
    /// <returns>True, если настроение существует</returns>
    public static bool IsValidMoodId(int moodId)
    {
      return _moodDictionary.ContainsKey(moodId);
    }

    /// <summary>
    /// Получает полную информацию о тоне
    /// </summary>
    public static (string Description, bool IsThreatening) GetToneInfo(int toneId)
    {
      return _toneDictionary.TryGetValue(toneId, out var info)
          ? info
          : (string.Empty, false);
    }

    /// <summary>
    /// Получает полную информацию о настроении
    /// </summary>
    public static (string Description, bool IsThreatening) GetMoodInfo(int moodId)
    {
      return _moodDictionary.TryGetValue(moodId, out var info)
          ? info
          : (string.Empty, false);
    }

    #endregion

    #region Методы для проверки угрозы

    /// <summary>
    /// Проверяет, является ли тон угрожающим
    /// </summary>
    /// <param name="toneId">ID тона для проверки</param>
    /// <returns>True, если тон угрожающий</returns>
    public static bool IsToneThreatening(int toneId)
    {
      return _toneDictionary.TryGetValue(toneId, out var toneInfo)
          ? toneInfo.IsThreatening
          : false;
    }

    /// <summary>
    /// Проверяет, является ли настроение угрожающим
    /// </summary>
    /// <param name="moodId">ID настроения для проверки</param>
    /// <returns>True, если настроение угрожающее</returns>
    public static bool IsMoodThreatening(int moodId)
    {
      return _moodDictionary.TryGetValue(moodId, out var moodInfo)
          ? moodInfo.IsThreatening
          : false;
    }

    /// <summary>
    /// Проверяет, содержит ли комбинация тона и настроения угрозу
    /// </summary>
    /// <param name="toneId">ID тона</param>
    /// <param name="moodId">ID настроения</param>
    /// <returns>True, если есть угроза в тоне или настроении</returns>
    public static bool HasThreat(int toneId, int moodId)
    {
      return IsToneThreatening(toneId) || IsMoodThreatening(moodId);
    }

    /// <summary>
    /// Получает уровень угрозы на основе тона и настроения
    /// </summary>
    /// <param name="toneId">ID тона</param>
    /// <param name="moodId">ID настроения</param>
    /// <returns>Уровень угрозы: 0 - нет угрозы, 1 - низкая, 2 - высокая</returns>
    public static int GetThreatLevel(int toneId, int moodId)
    {
      bool toneThreat = IsToneThreatening(toneId);
      bool moodThreat = IsMoodThreatening(moodId);

      if (toneThreat && moodThreat) return 2; // Высокая угроза
      if (toneThreat || moodThreat) return 1; // Низкая угроза
      return 0; // Нет угрозы
    }

    /// <summary>
    /// Получает рекомендацию по реакции на комбинацию тона и настроения
    /// </summary>
    public static string GetThreatReactionAdvice(int toneId, int moodId)
    {
      int threatLevel = GetThreatLevel(toneId, moodId);
      string result = "";

      switch (threatLevel)
      {
        case 1:
          result = "Обнаружена потенциальная угроза. Рекомендуется осторожность.";
          break;
        case 2:
          result = "Высокая угроза! Рекомендуется немедленная защитная реакция.";
          break;
        default:
          result = "Угрозы не обнаружено. Безопасное взаимодействие.";
          break;
      }

      return result;
    }

    #endregion


    #region Поля и свойства

    private readonly Dictionary<int, ActionsImage> _actionsImages = new Dictionary<int, ActionsImage>();
    private int _lastActionsImageId = 0;

    /// <summary>
    /// Флаг распознавания фразы из активации дерева автоматизмов
    /// </summary>
    private bool _isUnrecognizedPhraseFromAtmtzmTreeActivation = false;

    #endregion

    #region Управление образами действий

    /// <summary>
    /// Возвращает список всех образов действий
    /// </summary>
    /// <returns>Копия списка образов действий</returns>
    public List<ActionsImage> GetAllActionsImagesList()
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
    /// Получить образ действия по ID
    /// </summary>
    /// <param name="id">ID образа действия</param>
    /// <returns>Образ действия или null, если не найден</returns>
    public ActionsImage GetActionsImage(int id)
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

    private (int Id, ActionsImage Image) CreateActionsImageCore(
    int id, // 0 для автоинкремента
    int kind,
    List<int> actIdList,
    List<int> phraseIdList,
    int toneId,
    int moodId,
    bool checkUnicum)
    {
      if (actIdList == null && (phraseIdList == null || _isUnrecognizedPhraseFromAtmtzmTreeActivation))
        return (0, null);

      if (checkUnicum)
      {
        var existing = CheckUnicumActionsImageNoLock(kind, actIdList, phraseIdList, toneId, moodId);
        if (existing.Image != null)
          return existing;
      }

      int newId = id;
      if (id == 0)
        newId = ++_lastActionsImageId;
      else if (_lastActionsImageId < id)
        _lastActionsImageId = id;

      // Создание объекта
      var image = new ActionsImage
      {
        Id = newId,
        Kind = kind,
        ActIdList = actIdList?.ToList() ?? new List<int>(),
        PhraseIdList = phraseIdList?.ToList() ?? new List<int>(),
        ToneId = toneId,
        MoodId = moodId
      };

      _actionsImages[newId] = image;
      if (checkUnicum)
        Logger.Info($"Создан новый образ ID={newId}");

      return (newId, image);
    }

    /// <summary>
    /// Создать новый образ действий или возвратить существующий
    /// </summary>
    /// <param name="kind">Тип: 0 - объективное действие, 1 - субъективное предположение</param>
    /// <param name="actIdList">Массив ID действий</param>
    /// <param name="phraseIdList">Массив ID фраз</param>
    /// <param name="toneId">ID тона</param>
    /// <param name="moodId">ID настроения</param>
    /// <param name="checkUnicum">Проверять уникальность</param>
    /// <returns>ID образа и сам образ</returns>
    internal (int Id, ActionsImage Image) CreateNewActionsImage(
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      _lock.EnterUpgradeableReadLock();
      try
      {
        if (checkUnicum)
        {
          var existing = CheckUnicumActionsImageNoLock(kind, actIdList, phraseIdList, toneId, moodId);
          if (existing.Image != null)
            return existing;
        }

        _lock.EnterWriteLock();
        try
        {
          return CreateActionsImageCore(0, kind, actIdList, phraseIdList, toneId, moodId, false);
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

    internal (int Id, ActionsImage Image) CreateNewActionsImageWithIdNoLock(
        int id,
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      return CreateActionsImageCore(id, kind, actIdList, phraseIdList, toneId, moodId, checkUnicum);
    }

    /// <summary>
    /// Проверить уникальность образа действий (без блокировки - для внутреннего использования)
    /// </summary>
    private (int Id, ActionsImage Image) CheckUnicumActionsImageNoLock(
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId)
    {
      foreach (var kvp in _actionsImages)
      {
        var v = kvp.Value;
        if (v == null)
          continue;

        if (kind != v.Kind)
          continue;

        if (!AddUtils.AreListsEqual(actIdList, v.ActIdList))
          continue;

        if (!AddUtils.AreListsEqual(phraseIdList, v.PhraseIdList))
          continue;

        if (toneId != v.ToneId || moodId != v.MoodId)
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
      return Path.Combine(_psychicDataPath, $"{ActionsImagesFileName}.dat");
    }

    /// <summary>
    /// Загружает образы действий из файла
    /// </summary>
    private void LoadActionsImages()
    {
      string filePath = GetActionsImagesFilePath();

      // Если файл не существует или невалиден, создаем новый с шапкой
      if (!File.Exists(filePath) || !FileValidator.IsValidActionsImagesFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.ActionsImagesFormat,
            FileValidator.FileHeaders.ActionsImagesActIdList,
            FileValidator.FileHeaders.ActionsImagesPhraseIdList,
            FileValidator.FileHeaders.ActionsImagesToneId,
            FileValidator.FileHeaders.ActionsImagesMoodId,
            FileValidator.FileHeaders.ActionsImagesKind
          };

          File.WriteAllLines(filePath, lines);
          _actionsImages.Clear();
          _lastActionsImageId = 0;
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
          _actionsImages.Clear();
          _lastActionsImageId = 0;

          foreach (var line in File.ReadLines(filePath))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 6)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var actIdList = AddUtils.ParseIntList(parts[1]);
            var phraseIdList = AddUtils.ParseIntList(parts[2]);

            int toneId = 0;
            if (!string.IsNullOrWhiteSpace(parts[3]))
              int.TryParse(parts[3], out toneId);

            int moodId = 0;
            if (!string.IsNullOrWhiteSpace(parts[4]))
              int.TryParse(parts[4], out moodId);

            int kind = 0;
            if (!string.IsNullOrWhiteSpace(parts[5]))
              int.TryParse(parts[5], out kind);

            // При загрузке из файла НЕ проверяем уникальность - должны сохранить все записи как есть
            CreateActionsImageCore(id, kind, actIdList, phraseIdList, toneId, moodId, false);
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
    /// Сохраняет образы действий в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveActionsImages()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveActionsImagesNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет образы действий в файл (без блокировки - для внутреннего использования)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveActionsImagesNoLock()
    {
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.ActionsImagesFormat,
          FileValidator.FileHeaders.ActionsImagesActIdList,
          FileValidator.FileHeaders.ActionsImagesPhraseIdList,
          FileValidator.FileHeaders.ActionsImagesToneId,
          FileValidator.FileHeaders.ActionsImagesMoodId,
          FileValidator.FileHeaders.ActionsImagesKind
        };

        foreach (var kvp in _actionsImages.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          if (v == null)
            continue;

          var line = $"{v.Id}|";

          line += AddUtils.IntListToString(v.ActIdList);
          line += "|";
          line += AddUtils.IntListToString(v.PhraseIdList);
          line += "|";
          line += $"{v.ToneId}|";
          line += $"{v.MoodId}|";
          line += $"{v.Kind}";

          lines.Add(line);
        }

        var minLinesCount = lines.Count == 6 ? 6 : 7;
        var result = FileValidator.SafeSaveFile(
            GetActionsImagesFilePath(),
            lines,
            content => FileValidator.IsValidActionsImagesFile(string.Join(Environment.NewLine, content)),
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

    #region Вспомогательные методы

    /// <summary>
    /// Проверяет, является ли ID цепочкой действий
    /// </summary>
    /// <param name="actionId">ID действия</param>
    /// <returns>True, если это цепочка действий</returns>
    public bool IsActionChain(int actionId)
    {
      return actionId >= PrefixActionIdValue;
    }

    /// <summary>
    /// Получает ID цепочки из общего ID действия
    /// </summary>
    /// <param name="actionId">ID действия (может быть одиночным или цепочкой)</param>
    /// <returns>ID цепочки или 0, если не цепочка</returns>
    public int GetChainIdFromActionId(int actionId)
    {
      return IsActionChain(actionId) ? actionId - PrefixActionIdValue : 0;
    }

    /// <summary>
    /// Создает ID для цепочки действий
    /// </summary>
    /// <param name="chainId">ID цепочки</param>
    /// <returns>ID действия для цепочки</returns>
    public int CreateActionIdForChain(int chainId)
    {
      return chainId + PrefixActionIdValue;
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом ActionsImagesSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveActionsImages();
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