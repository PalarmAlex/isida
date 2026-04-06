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
  /// Дискретные коды зрительного канала агента (фон сцены / поле зрения).
  /// 0 — белый по умолчанию; 1 — чёрный; 2–8 — семь спектральных оттенков (для протоколов компаундного CS).
  /// </summary>
  public static class AgentVisualColor
  {
    /// <summary>Код белого фона (по умолчанию)</summary>
    public const int White = 0;
    /// <summary>Код чёрного фона.</summary>
    public const int Black = 1;
    /// <summary>Минимальный допустимый код.</summary>
    public const int MinCode = 0;
    /// <summary>Максимальный допустимый код (семь спектральных оттенков после чёрного).</summary>
    public const int MaxCode = 8;

    /// <summary>Проверка, что код входит в диапазон зрительного канала.</summary>
    public static bool IsValidCode(int code) => code >= MinCode && code <= MaxCode;

    /// <summary>Краткое имя цвета для UI.</summary>
    public static string GetDisplayName(int code)
    {
      switch (code)
      {
        case 0: return "Белый";
        case 1: return "Чёрный";
        case 2: return "Красный";
        case 3: return "Оранжевый";
        case 4: return "Жёлтый";
        case 5: return "Зелёный";
        case 6: return "Голубой";
        case 7: return "Синий";
        case 8: return "Фиолетовый";
        default: return $"Код {code}";
      }
    }
  }

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

      /// <summary>
      /// Код зрительного канала (см. <see cref="AgentVisualColor"/>). Всегда задан; по умолчанию белый (0).
      /// </summary>
      public int VisualColorId { get; set; }
    }

    /// <summary>
    /// Степень специфичности пускового образа по числу задействованных модальностей:
    /// 3 — действия и фраза; 2 — только действия или только фраза (плюс учёт цвета в совпадении);
    /// 1 — ни действий, ни фраз (только цвет как опора; редко).
    /// </summary>
    public static int GetTriggerSpecificityTier(PerceptionImage img)
    {
      if (img == null) return 0;
      bool hasA = img.InfluenceActionsList?.Any() == true;
      bool hasP = img.PhraseIdList?.Any() == true;
      if (hasA && hasP) return 3;
      if (hasA || hasP) return 2;
      return 1;
    }

    /// <summary>
    /// Число модальностей, задающих «компаунд» (для суммации нескольких CS): действие, речь, ненулевой цвет.
    /// </summary>
    public static int CompoundModalityCount(PerceptionImage img)
    {
      if (img == null) return 0;
      int n = 0;
      if (img.InfluenceActionsList?.Any() == true) n++;
      if (img.PhraseIdList?.Any() == true) n++;
      if (img.VisualColorId != AgentVisualColor.White) n++;
      return n;
    }

    /// <summary>
    /// Множество list1 содержится в list2 (с учётом кратности через группировку минимальных Count).
    /// Пустой list1 не ограничивает.
    /// </summary>
    public static bool IsIntListSubset(List<int> small, List<int> large)
    {
      if (small == null || !small.Any()) return true;
      if (large == null) return false;
      var gSmall = small.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
      var gLarge = large.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
      foreach (var kv in gSmall)
      {
        if (!gLarge.TryGetValue(kv.Key, out int c) || c < kv.Value)
          return false;
      }
      return true;
    }

    /// <summary>
    /// Два пусковых образа совместимы для активации одного у-рефлекса по иерархии «часть — целое»:
    /// либо I ⊆ S (богаче стимул, беднее запись рефлекса), либо S ⊆ I (богаче запись, беднее стимул).
    /// Цвет участвует в проверке подмножества как модальность: White (отсутствие цвета)
    /// допускает любое значение у партнёра, а два различных ненулевых цвета конфликтуют.
    /// </summary>
    public static bool StimulusImagesHierarchyCompatible(PerceptionImage stimulus, PerceptionImage reflexTrigger)
    {
      if (stimulus == null || reflexTrigger == null) return false;

      int sColor = stimulus.VisualColorId;
      int tColor = reflexTrigger.VisualColorId;

      bool colorTriggerSubsetStimulus = tColor == AgentVisualColor.White || tColor == sColor;
      bool colorStimulusSubsetTrigger = sColor == AgentVisualColor.White || sColor == tColor;

      bool iSubsetS = colorTriggerSubsetStimulus &&
          IsIntListSubset(reflexTrigger.InfluenceActionsList, stimulus.InfluenceActionsList) &&
          IsIntListSubset(reflexTrigger.PhraseIdList, stimulus.PhraseIdList);
      bool sSubsetI = colorStimulusSubsetTrigger &&
          IsIntListSubset(stimulus.InfluenceActionsList, reflexTrigger.InfluenceActionsList) &&
          IsIntListSubset(stimulus.PhraseIdList, reflexTrigger.PhraseIdList);
      return iSubsetS || sSubsetI;
    }

    /// <summary>
    /// Строгое равенство содержимого образов (включая цвет).
    /// </summary>
    public static bool PerceptionImagesEqual(PerceptionImage a, PerceptionImage b)
    {
      if (a == null || b == null) return false;
      return a.VisualColorId == b.VisualColorId &&
             a.InfluenceActionsList.OrderBy(x => x).SequenceEqual(b.InfluenceActionsList.OrderBy(x => x)) &&
             a.PhraseIdList.OrderBy(x => x).SequenceEqual(b.PhraseIdList.OrderBy(x => x));
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
        }
      }
      finally
      {
        _lock.ExitWriteLock();
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
    /// <param name="visualColorId">Код зрительного канала (<see cref="AgentVisualColor"/>)</param>
    /// <returns>ID существующего или нового образа. 0 если ошибка</returns>
    public int AddPerceptionImage(List<int> influenceActionList, List<int> phraseIdList, int visualColorId = 0)
    {
      if (!AgentVisualColor.IsValidCode(visualColorId))
        visualColorId = AgentVisualColor.White;

      bool hasA = influenceActionList != null && influenceActionList.Any();
      bool hasP = phraseIdList != null && phraseIdList.Any();
      bool hasColorSignal = visualColorId != AgentVisualColor.White;
      if (!hasA && !hasP && !hasColorSignal)
        return 0;

      var newPerceptionImage = new PerceptionImage
      {
        InfluenceActionsList = influenceActionList?.OrderBy(x => x).ToList() ?? new List<int>(),
        PhraseIdList = phraseIdList?.OrderBy(x => x).ToList() ?? new List<int>(),
        VisualColorId = visualColorId
      };

      int resultId = 0;

      _lock.EnterWriteLock();
      try
      {
        var existingImage = _perceptionImages.Values.FirstOrDefault(existing =>
            IsArePerceptionImage(existing, newPerceptionImage));

        if (existingImage != null)
          resultId = existingImage.Id;
        else
        {
          int newId = ++_lastPerceptionImageId;
          var perceptionImage = new PerceptionImage
          {
            Id = newId,
            InfluenceActionsList = newPerceptionImage.InfluenceActionsList,
            PhraseIdList = newPerceptionImage.PhraseIdList,
            VisualColorId = visualColorId
          };

          _perceptionImages.Add(newId, perceptionImage);
          resultId = newId;
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      return resultId;
    }

    private bool IsArePerceptionImage(PerceptionImage existing, PerceptionImage newImage)
    {
      if (existing == null || newImage == null) return false;

      return existing.VisualColorId == newImage.VisualColorId &&
             existing.InfluenceActionsList.OrderBy(x => x).SequenceEqual(
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

      if (!FileValidator.IsValidPerceptionImagesFile(filePath))
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

            int colorId = AgentVisualColor.White;
            if (parts.Length > 3 && int.TryParse(parts[3].Trim(), out int parsedColor))
              colorId = AgentVisualColor.IsValidCode(parsedColor) ? parsedColor : AgentVisualColor.White;

            var perceptionImage = new PerceptionImage
            {
              Id = id,
              InfluenceActionsList = AddUtils.ParseIntList(parts[1]),
              PhraseIdList = AddUtils.ParseIntList(parts[2]),
              VisualColorId = colorId
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
                  FileValidator.FileHeaders.PerceptionImagesFormat,
                  FileValidator.FileHeaders.PerceptionImagesLists,
                  FileValidator.FileHeaders.PerceptionImagesVisualColor
                };

        foreach (var image in _perceptionImages.Values.OrderBy(x => x.Id))
        {
          lines.Add($"{image.Id}|{AddUtils.IntListToString(image.InfluenceActionsList)}|" +
                    $"{AddUtils.IntListToString(image.PhraseIdList)}|{image.VisualColorId}");
        }

        var lineCount = 3;
        if (lines.Count == 2)
          lineCount = 2; // для случая очистки всего кроме шапки

        var result = FileValidator.SafeSaveFile(
            GetPerceptionImagesFilePath(),
            lines,
            FileValidator.IsValidPerceptionImagesFile,
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