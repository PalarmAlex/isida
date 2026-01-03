using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace isida.Psychic.Automatism
{
  /// <summary>
  /// Система образов действий оператора и агента ИИ
  /// </summary>
  /// <remarks>
  /// Образ действий оператора - это Стимул (образ восприятия)
  /// Образ действий Beast - это Акция (образ действия)
  /// 
  /// При каждой стимуляции с Пульта Дерева автоматизмов возникает образ восприятия curActiveActionsID, curActiveActions
  /// Фактически структура повторяет TriggerStimuls из рефлексов и позволяет сохранять
  /// как образы действий в автоматизмах, так и образы действий оператора, отражаемые в дереве мот.автомтаизмов.
  /// Используется для формирования пар стимул (действия оператора) - действия (ответ beast)
  /// для эпизодической памяти и структуры rules - Правил примитивного опыта.
  /// 
  /// Обоснование:
  /// Обощенные образы восприятия, возникающие в теменной ассоциативной коре полностью соотвествуют воспринимаемому
  /// и не могут меняться.
  /// Но в лобной коре есть отражение этих образов,
  /// с возможностью произвольно создавать любые новые из известных элементов старого.
  /// Поэтому для области рефлексов используется TriggerStimuls,
  /// а для области психики - ActionsImage (с меткой Kind int // 0 - объектиное действие, 1 - субъективное предположение).
  /// Эти два вида структур локализуются по обе стороны двигательных программ,
  /// образуя основу "зеркальной" системы подражания.
  /// </remarks>
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
          : psychicDataPath;

      try
      {
        EnsureDataDirectory();
        LoadActionsImages();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации ActionsImagesSystem: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string ActionsImagesFileName = "action_images";
    private const int PrefixActionIdValue = 10000000; // если ID действия больше prefixActionIdValue, то это цепочка действий

    /// <summary>
    /// Образ действий оператора или агента ИИ
    /// </summary>
    public class ActionsImage
    {
      /// <summary>
      /// Идентификатор данного сочетания пусковых стимулов
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Тип образа: 0 - объективное действие, 1 - субъективное предположение
      /// </summary>
      /// <remarks>
      /// Метка о том, что действия не является объективным Стимулом (реально воспринятым из Пульта)
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

    #region Поля и свойства

    private readonly Dictionary<int, ActionsImage> _actionsImages = new Dictionary<int, ActionsImage>();
    private int _lastActionsImageId = 0;

    /// <summary>
    /// Флаг записи в файл
    /// </summary>
    private bool _doWritingFile = true;

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
    public (int Id, ActionsImage Image) CreateNewActionsImage(
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      // Не создавать образ с пустым действием и вербальным сенсором
      if (actIdList == null && (phraseIdList == null || _isUnrecognizedPhraseFromAtmtzmTreeActivation))
        return (0, null);

      if (checkUnicum)
      {
        var existing = CheckUnicumActionsImage(kind, actIdList, phraseIdList, toneId, moodId);
        if (existing.Image != null)
          return existing;
      }

      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastActionsImageId;
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

        if (_doWritingFile)
          SaveActionsImages();

        return (newId, image);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Создать новый образ действий с указанным ID
    /// </summary>
    internal (int Id, ActionsImage Image) CreateNewActionsImageWithId(
        int id,
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId,
        bool checkUnicum)
    {
      if (id == 0)
        return CreateNewActionsImage(kind, actIdList, phraseIdList, toneId, moodId, checkUnicum);

      if (checkUnicum)
      {
        var existing = CheckUnicumActionsImage(kind, actIdList, phraseIdList, toneId, moodId);
        if (existing.Image != null)
          return existing;
      }

      _lock.EnterWriteLock();
      try
      {
        if (_lastActionsImageId < id)
          _lastActionsImageId = id;

        var image = new ActionsImage
        {
          Id = id,
          Kind = kind,
          ActIdList = actIdList?.ToList() ?? new List<int>(),
          PhraseIdList = phraseIdList?.ToList() ?? new List<int>(),
          ToneId = toneId,
          MoodId = moodId
        };

        _actionsImages[id] = image;

        if (_doWritingFile)
          SaveActionsImages();

        return (id, image);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверить уникальность образа действий
    /// </summary>
    private (int Id, ActionsImage Image) CheckUnicumActionsImage(
        int kind,
        List<int> actIdList,
        List<int> phraseIdList,
        int toneId,
        int moodId)
    {
      _lock.EnterReadLock();
      try
      {
        foreach (var kvp in _actionsImages)
        {
          var v = kvp.Value;
          if (v == null || kind != v.Kind)
            continue;

          if (!AreListsEqual(actIdList, v.ActIdList))
            continue;

          if (!AreListsEqual(phraseIdList, v.PhraseIdList))
            continue;

          if (toneId != v.ToneId || moodId != v.MoodId)
            continue;

          return (kvp.Key, v);
        }

        return (0, null);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сравнение двух списков на равенство
    /// </summary>
    private bool AreListsEqual(List<int> list1, List<int> list2)
    {
      if (list1 == null && list2 == null)
        return true;
      if (list1 == null || list2 == null)
        return false;
      if (list1.Count != list2.Count)
        return false;

      return list1.OrderBy(x => x).SequenceEqual(list2.OrderBy(x => x));
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
      return Path.Combine(_psychicDataPath, $"{ActionsImagesFileName}.txt");
    }

    /// <summary>
    /// Загружает образы действий из файла
    /// </summary>
    private void LoadActionsImages()
    {
      string filePath = GetActionsImagesFilePath();
      if (!FileValidator.IsValidActionsImagesFile(filePath))
        return;

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
            if (string.IsNullOrWhiteSpace(trimmedLine))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 6)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            var actIdList = ParseIntList(parts[1]);
            var phraseIdList = ParseIntList(parts[2]);

            int toneId = 0;
            if (!string.IsNullOrWhiteSpace(parts[3]))
              int.TryParse(parts[3], out toneId);

            int moodId = 0;
            if (!string.IsNullOrWhiteSpace(parts[4]))
              int.TryParse(parts[4], out moodId);

            int kind = 0;
            if (!string.IsNullOrWhiteSpace(parts[5]))
              int.TryParse(parts[5], out kind);

            var saveDoWritingFile = _doWritingFile;
            _doWritingFile = false;
            CreateNewActionsImageWithId(id, kind, actIdList, phraseIdList, toneId, moodId, false);
            _doWritingFile = saveDoWritingFile;
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }
      catch (Exception ex)
      {
        LogError($"Ошибка загрузки файла образов действий: {ex.Message}");
      }
    }

    /// <summary>
    /// Сохраняет образы действий в файл
    /// </summary>
    internal (bool Success, string ErrorMessage) SaveActionsImages()
    {
      try
      {
        var lines = new List<string>();

        _lock.EnterReadLock();
        try
        {
          foreach (var kvp in _actionsImages.OrderBy(x => x.Key))
          {
            var v = kvp.Value;
            if (v == null)
              continue;

            var line = $"{v.Id}|";

            line += IntListToString(v.ActIdList);
            line += "|";
            line += IntListToString(v.PhraseIdList);
            line += "|";
            line += $"{v.ToneId}|";
            line += $"{v.MoodId}|";
            line += $"{v.Kind}";

            lines.Add(line);
          }
        }
        finally
        {
          _lock.ExitReadLock();
        }

        if (lines.Count == 0)
          lines.Add("# ID|ActID через ,|PhraseID через ,|ToneID|MoodID|Kind");

        var result = FileValidator.SafeSaveFile(
            GetActionsImagesFilePath(),
            lines,
            content => FileValidator.IsValidActionsImagesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: 1,
            fileDescription: "образов действий");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
    }

    /// <summary>
    /// Парсит строку со списком целых чисел
    /// </summary>
    private List<int> ParseIntList(string listStr)
    {
      if (string.IsNullOrWhiteSpace(listStr))
        return new List<int>();

      return listStr.Split(',', (char)StringSplitOptions.RemoveEmptyEntries)
          .Select(s => int.TryParse(s.Trim(), out int result) ? result : 0)
          .Where(x => x != 0)
          .ToList();
    }

    /// <summary>
    /// Преобразует список целых чисел в строку
    /// </summary>
    private string IntListToString(List<int> list)
    {
      return list != null && list.Count > 0 ? string.Join(",", list) : string.Empty;
    }

    /// <summary>
    /// Логирование ошибок
    /// </summary>
    private static void LogError(string message)
    {
      FileValidator.LogError(message);
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
        LogError($"Error during disposal: {ex.Message}");
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
