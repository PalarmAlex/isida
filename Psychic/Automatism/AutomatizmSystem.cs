using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Автоматизм - может совершать внешние действия или внутренние произвольные действия
  /// </summary>
  public class Automatizm
  {
    /// <summary>
    /// ID автоматизма
    /// </summary>
    public int ID { get; set; }

    /// <summary>
    /// ID объекта, к которому привязан автоматизм
    /// (может быть привязан к узлу дерева, к фразе или действиям)
    /// </summary>
    public int BranchID { get; set; }

    /// <summary>
    /// (БЕС)ПОЛЕЗНОСТЬ: -10 вред 0 +10 +n польза
    /// </summary>
    public int Usefulness { get; set; }

    /// <summary>
    /// ID образа действий
    /// </summary>
    public int ActionsImageID { get; set; }

    /// <summary>
    /// ID следующей цепочки действий
    /// </summary>
    public int NextID { get; set; }

    /// <summary>
    /// Энергичность действия или фразы (от 1 до 10, по умолчанию = 5)
    /// </summary>
    public int Energy { get; set; } = 5;

    /// <summary>
    /// Уверенность: 0 - предположение, 1 - чужие сведения, 2 - проверенное собственное знание
    /// </summary>
    public int Belief { get; set; }

    /// <summary>
    /// Надежность: число использований с подтверждением (бес)полезности
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    /// Какие ID гомео-параметров фактически улучшило действие этого автоматизма
    /// </summary>
    public List<int> GomeoIdSuccesArr { get; set; } = new List<int>();
  }

  /// <summary>
  /// Система управления автоматизмами
  /// </summary>
  public sealed class AutomatizmSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;
    private readonly string _psychicDataPath;

    #region Инициализация

    private static AutomatizmSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы автоматизмов
    /// </summary>
    public static AutomatizmSystem Instance => _instance ??
        throw new InvalidOperationException("AutomatizmSystem не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы автоматизмов
    /// </summary>
    public static void InitializeInstance(string psychicDataPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatizmSystem уже инициализирован.");

      _instance = new AutomatizmSystem(psychicDataPath);
    }

    private AutomatizmSystem(string psychicDataPath = null)
    {
      _psychicDataPath = string.IsNullOrWhiteSpace(psychicDataPath)
          ? Path.Combine(
              Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
              "ISIDA", "Data", "Psychic", "Automatism")
          : Path.Combine(psychicDataPath, "Automatism");

      try
      {
        EnsureDataDirectory();
        LoadAutomatizm();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Константы и поля

    private const string AutomatizmFileName = "automatizm";

    /// <summary>
    /// Все автоматизмы по ID
    /// </summary>
    private readonly Dictionary<int, Automatizm> _automatizmsById = new Dictionary<int, Automatizm>();

    /// <summary>
    /// Штатные автоматизмы, прикрепленные к ID узла Дерева с Belief == 2
    /// </summary>
    private readonly Dictionary<int, Automatizm> _automatizmBelief2FromTreeNodeId = new Dictionary<int, Automatizm>();

    /// <summary>
    /// Автоматизмы, привязанные к ID образа действий с пульта
    /// (BranchID начинается с 1000000)
    /// </summary>
    private readonly Dictionary<int, List<Automatizm>> _automatizmFromActionId = new Dictionary<int, List<Automatizm>>();

    /// <summary>
    /// Автоматизмы, привязанные к ID фразы
    /// (BranchID начинается с 2000000)
    /// </summary>
    private readonly Dictionary<int, List<Automatizm>> _automatizmFromPhraseId = new Dictionary<int, List<Automatizm>>();

    /// <summary>
    /// Список удачных автоматизмов (Usefulness > 0)
    /// </summary>
    private readonly Dictionary<int, Automatizm> _automatizmSuccessFromId = new Dictionary<int, Automatizm>();

    /// <summary>
    /// ID последнего созданного автоматизма
    /// </summary>
    private int _lastAutomatizmId = 0;

    /// <summary>
    /// Не выдавать сообщение о новом автоматизме
    /// </summary>
    private bool _noWarningCreateShow = false;

    #endregion

    #region Управление автоматизмами

    /// <summary>
    /// Получает автоматизм по ID
    /// </summary>
    public Automatizm GetAutomatizmById(int id)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmsById.TryGetValue(id, out var automatizm) ? automatizm : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает все автоматизмы
    /// </summary>
    public List<Automatizm> GetAllAutomatizms()
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmsById.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Создает новый автоматизм или возвращает его, если такой уже есть
    /// </summary>
    public (int Id, Automatizm Automatizm) CreateNewAutomatizm(
        int branchId,
        int actionsImageId,
        bool checkUnicum = true)
    {
      if (actionsImageId == 0)
        return (0, null);

      if (checkUnicum)
      {
        var existing = CheckUnicumMotorsAutomatizm(branchId, actionsImageId);
        if (existing.Automatizm != null)
          return existing;
      }

      _lock.EnterWriteLock();
      try
      {
        _lastAutomatizmId++;
        var id = _lastAutomatizmId;

        var automatizm = new Automatizm
        {
          ID = id,
          BranchID = branchId,
          ActionsImageID = actionsImageId,
          Energy = 5,
          Usefulness = 0,
          Belief = 0,
          Count = 0
        };

        _automatizmsById[id] = automatizm;

        // Добавляем в соответствующие коллекции
        if (branchId > 1000000 && branchId < 2000000)
        {
          var imgId = branchId - 1000000;
          if (!_automatizmFromActionId.ContainsKey(imgId))
            _automatizmFromActionId[imgId] = new List<Automatizm>();
          _automatizmFromActionId[imgId].Add(automatizm);
        }
        else if (branchId > 2000000)
        {
          var imgId = branchId - 2000000;
          if (!_automatizmFromPhraseId.ContainsKey(imgId))
            _automatizmFromPhraseId[imgId] = new List<Automatizm>();
          _automatizmFromPhraseId[imgId].Add(automatizm);
        }

        if (!_noWarningCreateShow)
          Logger.Info($"Создан новый автоматизм Id={automatizm.ID}");

        return (id, automatizm);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Создает дубликат автоматизма
    /// </summary>
    public (int Id, Automatizm Automatizm) CreateDuplicateAutomatizm(int branchId, Automatizm source)
    {
      if (source == null)
        return (0, null);

      return CreateNewAutomatizm(branchId, source.ActionsImageID, false);
    }

    /// <summary>
    /// Проверяет уникальность автоматизма по сочетанию BranchID и ActionsImageID
    /// </summary>
    private (int Id, Automatizm Automatizm) CheckUnicumMotorsAutomatizm(int branchId, int actionsImageId)
    {
      _lock.EnterReadLock();
      try
      {
        foreach (var kvp in _automatizmsById)
        {
          if (kvp.Value.BranchID == branchId && kvp.Value.ActionsImageID == actionsImageId)
          {
            return (kvp.Key, kvp.Value);
          }
        }
        return (0, null);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Устанавливает уверенность (Belief) для автоматизма
    /// </summary>
    public void SetAutomatizmBelief(Automatizm automatizm, int belief)
    {
      if (automatizm == null)
        return;

      try
      {
        if (belief == 2)
        {
          // Если устанавливаем Belief=2, сбрасываем другие автоматизмы с Belief=2 для этого BranchID
          foreach (var kvp in _automatizmsById)
          {
            if (kvp.Value.BranchID == automatizm.BranchID && kvp.Value.Belief == 2 && kvp.Value.ID != automatizm.ID)
              kvp.Value.Belief = 0;
          }

          // Обновляем карту штатных автоматизмов
          _automatizmBelief2FromTreeNodeId[automatizm.BranchID] = automatizm;
        }
        else if (automatizm.Belief == 2 && belief != 2)
          // Убираем из штатных
          _automatizmBelief2FromTreeNodeId.Remove(automatizm.BranchID);

        automatizm.Belief = belief;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Обновляет полезность автоматизма
    /// </summary>
    public void UpdateAutomatizmUsefulness(int automatizmId, int usefulness, List<int> gomeoIdSuccesArr = null)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_automatizmsById.TryGetValue(automatizmId, out var automatizm))
          return;

        automatizm.Usefulness = usefulness;
        automatizm.Count++;

        if (gomeoIdSuccesArr != null)
          automatizm.GomeoIdSuccesArr = gomeoIdSuccesArr.ToList();

        // Обновляем списки успешных/неуспешных
        if (usefulness > 0)
          _automatizmSuccessFromId[automatizmId] = automatizm;
        else if (_automatizmSuccessFromId.ContainsKey(automatizmId))
          _automatizmSuccessFromId.Remove(automatizmId);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет автоматизм
    /// </summary>
    public void DeleteAutomatizm(int id)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_automatizmsById.TryGetValue(id, out var automatizm))
          return;

        _automatizmsById.Remove(id);
        _automatizmSuccessFromId.Remove(id);
        _automatizmBelief2FromTreeNodeId.Remove(automatizm.BranchID);

        if (automatizm.BranchID > 1000000 && automatizm.BranchID < 2000000)
        {
          var imgId = automatizm.BranchID - 1000000;
          if (_automatizmFromActionId.ContainsKey(imgId))
          {
            _automatizmFromActionId[imgId].Remove(automatizm);
            if (_automatizmFromActionId[imgId].Count == 0)
              _automatizmFromActionId.Remove(imgId);
          }
        }
        else if (automatizm.BranchID > 2000000)
        {
          var imgId = automatizm.BranchID - 2000000;
          if (_automatizmFromPhraseId.ContainsKey(imgId))
          {
            _automatizmFromPhraseId[imgId].Remove(automatizm);
            if (_automatizmFromPhraseId[imgId].Count == 0)
              _automatizmFromPhraseId.Remove(imgId);
          }
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удалить все автоматизмы
    /// </summary>
    public bool DeleteAllAutomatizm()
    {
      _lock.EnterWriteLock();
      try
      {
        if (AppGlobalState.EvolutionStage < 2)
          throw new InvalidOperationException("Автоматзмы доступны только начиная со стадии 2");

        var deletedAutomatizmIds = _automatizmsById.Keys.ToList();

        bool removed = true;
        foreach (var atmzId in deletedAutomatizmIds)
        {
          removed = _automatizmsById.Remove(atmzId);
          if (!removed)
            break;
        }

        if (removed)
        {
          _lastAutomatizmId = 0;
          _automatizmSuccessFromId.Clear();
          _automatizmBelief2FromTreeNodeId.Clear();
          _automatizmFromActionId.Clear();
          _automatizmFromPhraseId.Clear();
        }

        return removed;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        return false;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Поиск и получение автоматизмов

    /// <summary>
    /// Получает список всех автоматизмов для ID узла дерева
    /// </summary>
    public List<Automatizm> GetMotorsAutomatizmListFromTreeId(int nodeId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmsById.Values
            .Where(a => a.BranchID == nodeId)
            .ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Проверяет, есть ли штатный автоматизм для узла дерева
    /// </summary>
    public bool ExistsAutomatizmForThisNodeId(int nodeId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmBelief2FromTreeNodeId.ContainsKey(nodeId);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает штатный автоматизм для узла дерева
    /// </summary>
    public Automatizm GetBelief2AutomatizmFromTreeId(int nodeId)
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmBelief2FromTreeNodeId.TryGetValue(nodeId, out var automatizm) ? automatizm : null;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает штатный автоматизм для образа действий
    /// </summary>
    public Automatizm GetAutomatizmBeliefFromActionId(int actionId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_automatizmFromActionId.TryGetValue(actionId, out var automatizms))
          return null;

        return automatizms.FirstOrDefault(a => a.Belief == 2);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает штатный автоматизм для фразы
    /// </summary>
    public Automatizm GetAutomatizmBeliefFromPhraseId(int phraseId)
    {
      _lock.EnterReadLock();
      try
      {
        if (!_automatizmFromPhraseId.TryGetValue(phraseId, out var automatizms))
          return null;

        return automatizms.FirstOrDefault(a => a.Belief == 2);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список успешных автоматизмов (Usefulness > 0)
    /// </summary>
    public List<Automatizm> GetSuccessAutomatizms()
    {
      _lock.EnterReadLock();
      try
      {
        return _automatizmSuccessFromId.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает автоматизм для узла дерева (лучший подходящий)
    /// </summary>
    public Automatizm GetAutomatizmFromNodeId(int nodeId)
    {
      _lock.EnterReadLock();
      try
      {
        // Сначала проверяем штатный автоматизм
        var belief2 = GetBelief2AutomatizmFromTreeId(nodeId);
        if (belief2 != null && belief2.Usefulness >= 0)
          return belief2;

        // Ищем автоматизмы для этого узла
        var automatizms = GetMotorsAutomatizmListFromTreeId(nodeId);
        if (automatizms.Count == 0)
          return null;

        // Выбираем самый успешный автоматизм
        return automatizms
            .Where(a => a.Usefulness >= 0)
            .OrderByDescending(a => a.Usefulness)
            .ThenByDescending(a => a.Count)
            .FirstOrDefault();
      }
      finally
      {
        _lock.ExitReadLock();
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

    private string GetAutomatizmFilePath()
    {
      return Path.Combine(_psychicDataPath, $"{AutomatizmFileName}.dat");
    }

    /// <summary>
    /// Загружает автоматизмы из файла
    /// </summary>
    private void LoadAutomatizm()
    {
      string filePath = GetAutomatizmFilePath();

      // Если файл не существует или невалиден, создаем новый
      if (!File.Exists(filePath) || !FileValidator.IsValidAutomatizmFile(filePath))
      {
        try
        {
          EnsureDataDirectory();
          var lines = new List<string>
            {
                FileValidator.FileHeaders.AutomatizmFormat,
                FileValidator.FileHeaders.AutomatizmFields1,
                FileValidator.FileHeaders.AutomatizmFields2,
                FileValidator.FileHeaders.AutomatizmFields3,
                FileValidator.FileHeaders.AutomatizmFields4,
                FileValidator.FileHeaders.AutomatizmFields5,
                FileValidator.FileHeaders.AutomatizmFields6,
                FileValidator.FileHeaders.AutomatizmFields7,
                FileValidator.FileHeaders.AutomatizmFields8,
                FileValidator.FileHeaders.AutomatizmFields9
            };

          File.WriteAllLines(filePath, lines);
          InitializeAutomatizms();
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
        InitializeAutomatizms();

        var saveNoWarningCreateShow = _noWarningCreateShow;
        _noWarningCreateShow = true;

        // Пропускаем строки заголовков (первые 10 строк)
        int lineNumber = 0;
        foreach (var line in File.ReadLines(filePath))
        {
          lineNumber++;
          if (lineNumber <= 10) // Пропускаем 10 строк заголовков
            continue;

          var trimmedLine = line.Trim();
          if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            continue;

          var parts = trimmedLine.Split('|');
          if (parts.Length < 8)
            continue;

          if (!int.TryParse(parts[0], out int id))
            continue;

          if (!int.TryParse(parts[1], out int branchId))
            continue;

          if (!int.TryParse(parts[2], out int usefulness))
            usefulness = 0;

          if (!int.TryParse(parts[3], out int actionsImageId))
            continue;

          if (!int.TryParse(parts[4], out int nextId))
            nextId = 0;

          if (!int.TryParse(parts[5], out int energy))
            energy = 5;

          if (!int.TryParse(parts[6], out int belief))
            belief = 0;

          if (!int.TryParse(parts[7], out int count))
            count = 0;

          var gomeoIdSuccesArr = new List<int>();
          if (parts.Length > 8 && !string.IsNullOrWhiteSpace(parts[8]))
          {
            var gomeoParts = parts[8].Split(',');
            foreach (var part in gomeoParts)
            {
              if (int.TryParse(part.Trim(), out int gomeoId))
                gomeoIdSuccesArr.Add(gomeoId);
            }
          }

          // Создаем без блокировки для загрузки
          var (newId, automatizm) = CreateNewAutomatizm(branchId, actionsImageId, false);
          if (automatizm != null && newId == id)
          {
            automatizm.Usefulness = usefulness;
            automatizm.NextID = nextId;
            automatizm.Energy = energy;
            automatizm.Count = count;
            automatizm.GomeoIdSuccesArr = gomeoIdSuccesArr;

            SetAutomatizmBelief(automatizm, belief);

            if (usefulness > 0)
              _automatizmSuccessFromId[id] = automatizm;
          }
        }

        _noWarningCreateShow = saveNoWarningCreateShow;
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Инициализирует структуры автоматизмов
    /// </summary>
    private void InitializeAutomatizms()
    {
      _automatizmsById.Clear();
      _lastAutomatizmId = 0;
      _automatizmBelief2FromTreeNodeId.Clear();
      _automatizmFromActionId.Clear();
      _automatizmFromPhraseId.Clear();
      _automatizmSuccessFromId.Clear();
    }

    /// <summary>
    /// Сохраняет автоматизмы в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveAutomatizm()
    {
      _lock.EnterReadLock();
      try
      {
        return SaveAutomatizmNoLock();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Сохраняет автоматизмы в файл (без блокировки)
    /// </summary>
    private (bool Success, string ErrorMessage) SaveAutomatizmNoLock()
    {
      try
      {
        var lines = new List<string>
        {
            FileValidator.FileHeaders.AutomatizmFormat,
            FileValidator.FileHeaders.AutomatizmFields1,
            FileValidator.FileHeaders.AutomatizmFields2,
            FileValidator.FileHeaders.AutomatizmFields3,
            FileValidator.FileHeaders.AutomatizmFields4,
            FileValidator.FileHeaders.AutomatizmFields5,
            FileValidator.FileHeaders.AutomatizmFields6,
            FileValidator.FileHeaders.AutomatizmFields7,
            FileValidator.FileHeaders.AutomatizmFields8,
            FileValidator.FileHeaders.AutomatizmFields9
        };

        foreach (var kvp in _automatizmsById.OrderBy(x => x.Key))
        {
          var v = kvp.Value;
          var line = $"{v.ID}|{v.BranchID}|{v.Usefulness}|{v.ActionsImageID}|" +
                     $"{v.NextID}|{v.Energy}|{v.Belief}|{v.Count}|";

          if (v.GomeoIdSuccesArr != null && v.GomeoIdSuccesArr.Count > 0)
          {
            line += string.Join(",", v.GomeoIdSuccesArr);
          }

          lines.Add(line);
        }

        var result = FileValidator.SafeSaveFile(
            GetAutomatizmFilePath(),
            lines,
            content => FileValidator.IsValidAutomatizmFile(string.Join(Environment.NewLine, content)),
            minLinesCount: 10,
            fileDescription: "автоматизмов");

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
    /// Освобождает ресурсы, используемые объектом AutomatizmSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveAutomatizm();
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