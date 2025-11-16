using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Система управления безусловными рефлексами агента
  /// </summary>
  public sealed class GeneticReflexesSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly InfluenceActionSystem _influenceActionSystem;
    private readonly AdaptiveActionsSystem _adaptiveActionsSystem;
    private bool _disposed = false;

    #region Привязка к ReflexTreeSystem через события

    // В ReflexTreeSystem уже есть ссылка на GeneticReflexesSystem,
    // поэтому ссылаться на нее не стоит - будут циклические ссылки

    /// <summary>Событие удаления одиночного безусловного рефлекса</summary>
    public event Action<int> GeneticReflexDeleted;

    /// <summary>Событие массового удаления безусловных рефлексов</summary>
    public event Action<List<int>> MultipleGeneticReflexesDeleted;

    /// <summary>Событие создания нового безусловного рефлекса</summary>
    public event Action<GeneticReflexCreatedEventArgs> GeneticReflexCreated;
    
    /// <summary>Аргументы события создания рефлекса</summary>
    public class GeneticReflexCreatedEventArgs
    {
      /// <summary>ID созданного рефлекса</summary>
      public int ReflexId { get; }

      /// <summary>Базовое состояние гомеостаза</summary>
      public int Level1 { get; }

      /// <summary>Стили поведения</summary>
      public List<int> Level2 { get; }

      /// <summary>Внешние воздействия</summary>
      public List<int> Level3 { get; }

      /// <summary>Создает аргументы события</summary>
      public GeneticReflexCreatedEventArgs(int reflexId, int level1, List<int> level2, List<int> level3)
      {
        ReflexId = reflexId;
        Level1 = level1;
        Level2 = level2;
        Level3 = level3;
      }
    }

    private void OnGeneticReflexDeleted(int reflexId)
    {
      GeneticReflexDeleted?.Invoke(reflexId);
    }

    private void OnMultipleGeneticReflexesDeleted(List<int> reflexIds)
    {
      MultipleGeneticReflexesDeleted?.Invoke(reflexIds);
    }

    #endregion

    #region Инициализация

    private static GeneticReflexesSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы безусловных рефлексов. Должен быть инициализирован через InitializeInstance().
    /// </summary>
    public static GeneticReflexesSystem Instance => _instance ??
      throw new InvalidOperationException("GeneticReflexesSystem не инициализирован. Вызовите InitializeInstance() с путями.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы управления безусловными рефлексами с указанными путями к данным и шаблонам, 
    /// а также ссылкой на систему гомеостаза, на которую действия будут оказывать влияние.
    /// Должен быть вызван один раз при старте приложения, после инициализации GomeostasSystem.
    /// </summary>
    /// <param name="gomeostas">Инициализированный экземпляр GomeostasSystem, управляющий параметрами гомеостаза</param>
    /// <param name="reflexesFolderPath">Путь к папке с данными рефлексов. Если null — используется путь по умолчанию </param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        string reflexesFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("GeneticReflexesSystem уже инициализирован.");

      if (!InfluenceActionSystem.IsInitialized)
        throw new InvalidOperationException("InfluenceActionSystem должен быть инициализирован перед GeneticReflexesSystem");

      if (!AdaptiveActionsSystem.IsInitialized)
        throw new InvalidOperationException("AdaptiveActionsSystem должен быть инициализирован перед GeneticReflexesSystem");

      if (!SensorySystem.IsInitialized)
        throw new InvalidOperationException("SensorySystem должен быть инициализирован перед GeneticReflexesSystem для работы с WordId");

      _instance = new GeneticReflexesSystem(gomeostas, reflexesFolderPath);
    }

    private readonly GomeostasSystem _gomeostas;
    private GeneticReflexesSystem(
        GomeostasSystem gomeostas,
        string reflexesFolderPath = null)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _influenceActionSystem = InfluenceActionSystem.Instance;
      _adaptiveActionsSystem = AdaptiveActionsSystem.Instance;

      // Установка путей
      _reflexesFolderPath = string.IsNullOrWhiteSpace(reflexesFolderPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISIDA", "Data", "Reflexes")
            : reflexesFolderPath;

      // Подписываемся на события удаления стилей и воздействий
      _gomeostas.StyleDeleted += OnStyleDeleted;
      _influenceActionSystem.InfluenceActionDeleted += OnInfluenceActionDeleted;
      _adaptiveActionsSystem.AdaptiveActionDeleted += OnAdaptiveActionDeleted;

      try
      {
        EnsureDataDirectory();
        LoadGeneticReflexes();
      }
      catch (Exception ex)
      {
        LogError($"Ошибка инициализации AdaptiveActionsSystem: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string GeneticReflexesFileName = "GeneticReflexes";
    private readonly string _reflexesFolderPath;

    /// <summary>
    /// Получить каталог рефлексов
    /// </summary>
    public string GetGeneticReflexesFilePath() =>
        Path.Combine(_reflexesFolderPath, $"{GeneticReflexesFileName}.dat");

    /// <summary>
    /// Безусловный рефлекс агента
    /// </summary>
    public class GeneticReflex
    {
      /// <summary>
      /// Уникальный идентификатор рефлекса
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Первый уровень дерева триггера рефлекса: Интегральное базовое состояние гомеостаза
      /// </summary>
      public int Level1 { get; set; }

      /// <summary>
      /// Второй уровень дерева триггера рефлекса: Контексты реагирования
      /// </summary>
      public List<int> Level2 { get; set; } = new List<int>();

      /// <summary>
      /// Третий уровень дерева триггера рефлекса: Гомеостатические воздействия
      /// </summary>
      public List<int> Level3 { get; set; } = new List<int>();

      /// <summary>
      /// Адаптивные действия рефлекса
      /// </summary>
      public List<int> AdaptiveActions { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, GeneticReflex> _geneticReflexes = new Dictionary<int, GeneticReflex>();
    private readonly List<GeneticReflex> _activeGeneticReflexes = new List<GeneticReflex>();
    private int _lastGeneticReflexId = 0;

    #endregion

    #region Управление безусловнымм рефлексами

    /// <summary>
    /// (Internal) Возвращает список активных безусловных рефлексов.
    /// </summary>
    /// <returns>Копия списка активных безусловных рефлексов</returns>
    internal List<GeneticReflex> GetActiveGeneticReflexesList()
    {
      _lock.EnterReadLock();
      try
      {
        return new List<GeneticReflex>(_activeGeneticReflexes);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список текущих активных безусловных рефлексов
    /// </summary>
    /// <returns>ReadOnlyCollection активных безусловных рефлексов</returns>
    public ReadOnlyCollection<GeneticReflex> GetActiveGeneticReflexes()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<GeneticReflex>(_activeGeneticReflexes.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// (Internal) Возвращает список всех безусловных рефлексов.
    /// </summary>
    /// <returns>Копия списка всех безусловных рефлексов</returns>
    internal List<GeneticReflex> GetAllGeneticReflexesList()
    {
      _lock.EnterReadLock();
      try
      {
        return _geneticReflexes.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список всех безусловных рефлексов
    /// </summary>
    /// <returns>ReadOnlyCollection всех безусловных рефлексов</returns>
    public ReadOnlyCollection<GeneticReflex> GetAllGeneticReflexes()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<GeneticReflex>(_geneticReflexes.Values.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Добавляет новый безусловный рефлекс
    /// </summary>
    /// <param name="level1">Первый уровень дерева триггера рефлекса: Интегральное базовое состояние гомеостаза</param>
    /// <param name="level2">Второй уровень дерева триггера рефлекса: Контексты реагирования</param>
    /// <param name="level3">Третий уровень дерева триггера рефлекса: Внешние гомеостатические воздействия</param>
    /// <param name="adaptiveActions">Адаптивные действия рефлекса</param>
    /// <returns>ID созданного рефлекса и массив предупреждений (если были скорректированы значения)</returns>
    /// <exception cref="ArgumentException">Выбрасывается при пустом или null имени действия</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях в влияниях или затратах (вне диапазона -10..+10)</exception>    
    public (int ActionId, string[] Warnings) AddGeneticReflex(
        int level1,
        List<int> level2,
        List<int> level3,
        List<int> adaptiveActions)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с безусловными рефлексами разрешена только в стадии 0");

      var warnings = new List<string>();

      var validationResult = ValidateGeneticReflexParameters(level1, level2, level3, adaptiveActions);
      if (!validationResult.IsValid)
      {
        warnings.Add(validationResult.ErrorMessage);
        throw new ArgumentException(validationResult.ErrorMessage);
      }

      // проверка дублеров
      var candidateReflex = new GeneticReflex
      {
        Level1 = level1,
        Level2 = level2?.OrderBy(x => x).ToList() ?? new List<int>(),
        Level3 = level3?.OrderBy(x => x).ToList() ?? new List<int>(),
        AdaptiveActions = adaptiveActions?.OrderBy(x => x).ToList() ?? new List<int>()
      };

      _lock.EnterReadLock();
      try
      {
        bool isDuplicate = _geneticReflexes.Values.Any(existing =>
            AreReflexesSemanticallyEqual(existing, candidateReflex));

        if (isDuplicate)
        {
          string strErr = $"Безусловный рефлекс c указанными уровняим Level1, Level2, Level3, AdaptiveActions, WordId уже существует. Дублирование запрещено.";
          warnings.Add(strErr);
          throw new ArgumentException(strErr);
        }
      }
      finally
      {
        _lock.ExitReadLock();
      }

      int newId;

      _lock.EnterWriteLock();
      try
      {
        newId = ++_lastGeneticReflexId;
        var geneticReflex = new GeneticReflex
        {
          Id = newId,
          Level1 = level1,
          Level2 = level2 ?? new List<int>(),
          Level3 = level3 ?? new List<int>(),
          AdaptiveActions = adaptiveActions ?? new List<int>()
        };

        _geneticReflexes.Add(newId, geneticReflex);
      }
      finally
      {
        _lock.ExitWriteLock();
      }

      try
      {
        OnGeneticReflexCreated(newId, level1, level2, level3);
      }
      catch (Exception ex)
      {
        warnings.Add($"Ошибка при обработке создания рефлекса: {ex.Message}");
      }

      return (newId, warnings.ToArray());
    }

    /// <summary>Вызывает событие создания рефлекса</summary>
    private void OnGeneticReflexCreated(int reflexId, int level1, List<int> level2, List<int> level3)
    {
      var args = new GeneticReflexCreatedEventArgs(reflexId, level1, level2, level3);
      GeneticReflexCreated?.Invoke(args);
    }

    /// <summary>
    /// Обновляет существующий безусловный рефлекс
    /// </summary>
    public string[] UpdateGeneticReflex(GeneticReflex reflex)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с безусловными рефлексами разрешена только в стадии 0");

      if (reflex == null)
        throw new ArgumentNullException(nameof(reflex));

      // Сохраняем старый ID
      int oldReflexId = reflex.Id;
      GeneticReflex oldReflexCopy = null;

      _lock.EnterReadLock();
      try
      {
        if (!_geneticReflexes.ContainsKey(reflex.Id))
          throw new KeyNotFoundException($"Безусловный рефлекс с ID {reflex.Id} не найден");

        var originalReflex = _geneticReflexes[reflex.Id];
        oldReflexCopy = new GeneticReflex
        {
          Id = originalReflex.Id,
          Level1 = originalReflex.Level1,
          Level2 = new List<int>(originalReflex.Level2),
          Level3 = new List<int>(originalReflex.Level3),
          AdaptiveActions = new List<int>(originalReflex.AdaptiveActions)
        };
      }
      finally
      {
        _lock.ExitReadLock();
      }

      var warnings = new List<string>();

      var validationResult = ValidateGeneticReflexParameters(
          reflex.Level1,
          reflex.Level2,
          reflex.Level3,
          reflex.AdaptiveActions);

      if (!validationResult.IsValid)
      {
        warnings.Add(validationResult.ErrorMessage);
        throw new ArgumentException(validationResult.ErrorMessage);
      }

      // Проверяем, изменились ли условия триггера рефлекса
      bool conditionsChanged = !AreReflexesSemanticallyEqual(oldReflexCopy, reflex);

      if (!conditionsChanged)
      {
        // Если условия не изменились, просто обновляем рефлекс
        _lock.EnterWriteLock();
        try
        {
          _geneticReflexes[reflex.Id] = reflex;
          return warnings.ToArray();
        }
        finally
        {
          _lock.ExitWriteLock();
        }
      }

      // Если условия изменились - удаляем и создаем заново
      try
      {
        // Сохраняем параметры для создания нового рефлекса
        int level1 = reflex.Level1;
        List<int> level2 = reflex.Level2?.ToList() ?? new List<int>();
        List<int> level3 = reflex.Level3?.ToList() ?? new List<int>();
        List<int> adaptiveActions = reflex.AdaptiveActions?.ToList() ?? new List<int>();

        // Удаляем старый рефлекс (это вызовет событие удаления и очистит ссылки в дереве)
        bool removed = RemoveGeneticReflex(oldReflexId);
        if (!removed)
        {
          warnings.Add($"Не удалось удалить рефлекс ID {oldReflexId} для обновления");
          throw new InvalidOperationException($"Не удалось удалить рефлекс ID {oldReflexId}");
        }

        // Создаем новый рефлекс с теми же параметрами
        var (newReflexId, createWarnings) = AddGeneticReflex(level1, level2, level3, adaptiveActions);

        warnings.AddRange(createWarnings);

        if (newReflexId <= 0)
        {
          warnings.Add($"Не удалось создать обновленный рефлекс");
          throw new InvalidOperationException("Не удалось создать обновленный рефлекс");
        }

        // меняем новый ID рефлекса на старый
        _lock.EnterWriteLock();
        try
        {
          if (_geneticReflexes.ContainsKey(newReflexId))
          {
            var newReflex = _geneticReflexes[newReflexId];

            _geneticReflexes.Remove(newReflexId);
            newReflex.Id = oldReflexId;
            _geneticReflexes[oldReflexId] = newReflex;

            if (oldReflexId > _lastGeneticReflexId)
              _lastGeneticReflexId = oldReflexId;

            // обновляем ID в дереве рефлексов
            OnGeneticReflexDeleted(newReflexId);
            OnGeneticReflexCreated(oldReflexId, level1, level2, level3);
          }
        }
        finally
        {
          _lock.ExitWriteLock();
        }

        return warnings.ToArray();
      }
      catch (Exception ex)
      {
        warnings.Add($"Ошибка при обновлении рефлекса: {ex.Message}");
        throw new InvalidOperationException($"Ошибка при обновлении рефлекса: {ex.Message}", ex);
      }
    }

    /// <summary>
    /// Удаляет безусловный рефлекс по указанному ID
    /// </summary>
    /// <param name="reflexId">ID удаляемого безусловного рефлекса</param>
    /// <returns>True, если действие было успешно удалено, иначе False</returns>
    public bool RemoveGeneticReflex(int reflexId)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с безусловными рефлексами разрешена только в стадии 0");

      if (!_geneticReflexes.ContainsKey(reflexId))
        throw new KeyNotFoundException($"Безусловный рефлекс с ID {reflexId} не найден");

      _lock.EnterWriteLock();
      try
      {
        bool removed = _geneticReflexes.Remove(reflexId);
        _activeGeneticReflexes.RemoveAll(a => a.Id == reflexId);

        if (removed)
          OnGeneticReflexDeleted(reflexId);

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет все безусловные рефлексы
    /// </summary>
    /// <returns>True, если действие было успешно удалено, иначе False</returns>
    public bool RemoveAllGeneticReflex()
    {
      _lock.EnterWriteLock();
      try
      {
        var deletedReflexIds = _geneticReflexes.Keys.ToList();

        bool removed = true;
        foreach (var reflexId in deletedReflexIds)
        {
          removed = _geneticReflexes.Remove(reflexId);
          if (!removed)
            break;
          _activeGeneticReflexes.RemoveAll(a => a.Id == reflexId);
        }

        if (removed && deletedReflexIds.Any())
          OnMultipleGeneticReflexesDeleted(deletedReflexIds);

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обработчик удаления стиля из гомеостаза
    /// </summary>
    private void OnStyleDeleted(int styleId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_geneticReflexes == null) return;

        // Удаляем ссылки на стиль из всех рефлексов
        foreach (var reflex in _geneticReflexes.Values)
        {
          // Удаляем из Level2 (контексты реагирования)
          if (reflex.Level2.Contains(styleId))
            reflex.Level2.Remove(styleId);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обработчик удаления воздействия из системы воздействий
    /// </summary>
    private void OnInfluenceActionDeleted(int actionId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_geneticReflexes == null) return;

        // Удаляем ссылки на воздействие из всех рефлексов
        foreach (var reflex in _geneticReflexes.Values)
        {
          // Удаляем из Level3 (внешние воздействия)
          if (reflex.Level3.Contains(actionId))
            reflex.Level3.Remove(actionId);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обработчик удаления адаптивного действия
    /// </summary>
    private void OnAdaptiveActionDeleted(int actionId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_geneticReflexes == null) return;

        // Удаляем ссылки на действие из всех рефлексов
        foreach (var reflex in _geneticReflexes.Values)
        {
          // Удаляем из AdaptiveActions
          if (reflex.AdaptiveActions != null && reflex.AdaptiveActions.Contains(actionId))
            reflex.AdaptiveActions.Remove(actionId);
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Работа с файлами

    /// <summary>
    /// Создает каталог параметров безусловных рефлексов, если его нет
    /// </summary>
    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_reflexesFolderPath))
      {
        Directory.CreateDirectory(_reflexesFolderPath);
      }
    }

    /// <summary>
    /// Загружает безусловные рефлексы из файла
    /// </summary>
    private void LoadGeneticReflexes()
    {
      var path = GetGeneticReflexesFilePath();

      try
      {
        if (IsValidGeneticReflexesFile(path))
        {
          _geneticReflexes.Clear();
          _activeGeneticReflexes.Clear();
          _lastGeneticReflexId = 0;

          foreach (var line in File.ReadLines(path))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 5)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            if (!int.TryParse(parts[1].Trim(), out int level1))
              continue;

            // Парсим остальные поля
            var level2 = parts.Length > 2 ? ParseIntList(parts[2]) : new List<int>();
            var level3 = parts.Length > 3 ? ParseIntList(parts[3]) : new List<int>();
            var adaptiveActions = parts.Length > 4 ? ParseIntList(parts[4]) : new List<int>();

            var validationResult = ValidateGeneticReflexParameters(level1, level2, level3, adaptiveActions);
            if (!validationResult.IsValid)
              continue;

            var reflex = new GeneticReflex
            {
              Id = id,
              Level1 = level1,
              Level2 = level2,
              Level3 = level3,
              AdaptiveActions = adaptiveActions
            };

            _geneticReflexes[reflex.Id] = reflex;
            if (reflex.Id > _lastGeneticReflexId)
              _lastGeneticReflexId = reflex.Id;
          }
        }
        else
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileHeaders.GeneticReflexesFormat,
            FileHeaders.GeneticReflexesLevel1,
            FileHeaders.GeneticReflexesLevel2,
            FileHeaders.GeneticReflexesLevel3,
            FileHeaders.GeneticReflexesActions
          };
          File.WriteAllLines(path, lines);

          _geneticReflexes.Clear();
          _activeGeneticReflexes.Clear();
          _lastGeneticReflexId = 0;
        }
      }
      catch (Exception ex)
      {
        LogError($"LoadGeneticReflexes: Ошибка при загрузке рефлексов: {ex.Message}");
      }
    }

    /// <summary>
    /// Сохраняет все безусловные рефлексы в файл
    /// </summary>
    /// <returns>Кортеж (успех, сообщение об ошибке)</returns>
    public (bool Success, string ErrorMessage) SaveGeneticReflexes(bool IsValidate = true)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с безусловными рефлексами разрешена только в стадии 0");

      _lock.EnterWriteLock();
      try
      {
        string errorMessage = string.Empty;

        if (IsValidate)
        {
          // Используем метод валидации для каждого рефлекса
          foreach (var reflex in _geneticReflexes.Values)
          {
            var validationResult = ValidateGeneticReflexParameters(
                reflex.Level1,
                reflex.Level2,
                reflex.Level3,
                reflex.AdaptiveActions);

            if (!validationResult.IsValid)
            {
              errorMessage = $"Рефлекс ID: {reflex.Id} не прошел валидацию: {validationResult.ErrorMessage}";
              return (false, errorMessage);
            }
          }
        }

        EnsureDataDirectory();

        var lines = new List<string>
        {
          FileHeaders.GeneticReflexesFormat,
          FileHeaders.GeneticReflexesLevel1,
          FileHeaders.GeneticReflexesLevel2,
          FileHeaders.GeneticReflexesLevel3,
          FileHeaders.GeneticReflexesActions
        };

        foreach (var reflex in _geneticReflexes.Values.OrderBy(r => r.Id))
        {
          lines.Add($"{reflex.Id}|{reflex.Level1}|" +
                   $"{string.Join(",", reflex.Level2)}|" +
                   $"{string.Join(",", reflex.Level3)}|" +
                   $"{string.Join(",", reflex.AdaptiveActions)}");
        }

        var linCount = 5; // Минимум: шапка + 1 рефлекс
        if (lines.Count == 5)
          linCount = 5; // только шапка

        var result = SafeSaveFile(
            GetGeneticReflexesFilePath(),
            lines,
            content => IsValidGeneticReflexesFile(string.Join(Environment.NewLine, content)),
            minLinesCount: linCount,
            fileDescription: "безусловных рефлексов");

        return result;
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    private List<int> ParseIntList(string listStr)
    {
      if (string.IsNullOrWhiteSpace(listStr))
        return new List<int>();

      return listStr.Split(',')
          .Where(s => !string.IsNullOrWhiteSpace(s))
          .Select(s => int.TryParse(s.Trim(), out int result) ? result : (int?)null)
          .Where(i => i.HasValue)
          .Select(i => i.Value)
          .ToList();
    }

    #endregion

    #region Валидация безусловных рефлексов

    /// <summary>
    /// Проверяет, совпадают ли содержимое двух рефлексов по ключевым полям триггера и действий.
    /// </summary>
    private static bool AreReflexesSemanticallyEqual(GeneticReflex a, GeneticReflex b)
    {
      if (a == null || b == null) return false;
      if (a.Level1 != b.Level1) return false;
      if (!a.Level2.OrderBy(x => x).SequenceEqual(b.Level2.OrderBy(x => x))) return false;
      if (!a.Level3.OrderBy(x => x).SequenceEqual(b.Level3.OrderBy(x => x))) return false;
      if (!a.AdaptiveActions.OrderBy(x => x).SequenceEqual(b.AdaptiveActions.OrderBy(x => x))) return false;
      return true;
    }

    /// <summary>
    /// Валидирует параметры безусловного рефлекса
    /// </summary>
    private (bool IsValid, string ErrorMessage) ValidateGeneticReflexParameters(
        int level1,
        List<int> level2,
        List<int> level3,
        List<int> adaptiveActions)
    {
      // Проверка Level1 - базовые состояния гомеостаза
      var validBaseStates = new[] { -1, 0, 1 }; // Bad, Normal, Well
      if (!validBaseStates.Contains(level1))
      {
        return (false,
            $"Level1 должен быть одним из значений: {string.Join(", ", validBaseStates)} (Bad=-1, Normal=0, Well=1)");
      }

      if (!level2.Any())
        return (false, $"Список контекстов реагирования рефлекса не может быть пустым");

    // Проверка Level2 - контексты реагирования (стили поведения)
      var allBehaviorStyles = _gomeostas.GetAllBehaviorStyles();
      var invalidStyleIds = level2.Where(id => !allBehaviorStyles.ContainsKey(id)).ToList();

      if (invalidStyleIds.Any())
        return (false,
            $"Найдены несуществующие ID стилей поведения в Level2: {string.Join(", ", invalidStyleIds)}");

      // Проверка на дубликаты
      if (level2.Count != level2.Distinct().Count())
        return (false, "Level2 содержит дублирующиеся ID стилей");

      // Проверка на антагонистов
      var behaviorStyles = _gomeostas.GetAllBehaviorStyles();
      var behaviorAntagonists = new Dictionary<int, List<int>>();

      // Загружаем антагонисты для стилей поведения
      foreach (var style in behaviorStyles.Values)
      {
        behaviorAntagonists[style.Id] = style.AntagonistStyles ?? new List<int>();
      }

      var styleConflicts = AntagonistValidator.ValidateAntagonists(level2,
          id => behaviorAntagonists.ContainsKey(id) ? behaviorAntagonists[id] : new List<int>());

      if (styleConflicts.Any())
        return (false,
            $"Конфликты стилей поведения в Level2: {string.Join("; ", styleConflicts.Select(c => c.Message))}");

      // Проверка Level3 - внешние воздействия
      if (level3 != null && level3.Any())
      {
        var influenceSystem = InfluenceActionSystem.Instance;
        var allInfluenceActions = influenceSystem.GetAllInfluenceActions();
        var invalidInfluenceIds = level3.Where(id => !allInfluenceActions.Any(a => a.Id == id)).ToList();

        if (invalidInfluenceIds.Any())
          return (false,
              $"Найдены несуществующие ID внешних воздействий в Level3: {string.Join(", ", invalidInfluenceIds)}");

        // Проверка на дубликаты
        if (level3.Count != level3.Distinct().Count())
          return (false, "Level3 содержит дублирующиеся ID воздействий");

        // Проверка на антагонистов
        var influenceAntagonists = allInfluenceActions
            .ToDictionary(a => a.Id, a => a.AntagonistInfluences ?? new List<int>());

        var influenceConflicts = AntagonistValidator.ValidateAntagonists(level3,
            id => influenceAntagonists.ContainsKey(id) ? influenceAntagonists[id] : new List<int>());

        if (influenceConflicts.Any())
          return (false,
              $"Конфликты внешних воздействий в Level3: {string.Join("; ", influenceConflicts.Select(c => c.Message))}");
      }

      if (!adaptiveActions.Any())
        return (false, $"Список адаптивных действий рефлекса не может быть пустым");

      // Проверка AdaptiveActions - адаптивные действия
      if (adaptiveActions != null)
      {
        var adaptiveSystem = AdaptiveActionsSystem.Instance;
        var allAdaptiveActions = adaptiveSystem.GetAllAdaptiveActions();
        var invalidActionIds = adaptiveActions.Where(id => !allAdaptiveActions.Any(a => a.Id == id)).ToList();

        if (invalidActionIds.Any())
          return (false,
              $"Найдены несуществующие ID адаптивных действий: {string.Join(", ", invalidActionIds)}");

        // Проверка на дубликаты
        if (adaptiveActions.Count != adaptiveActions.Distinct().Count())
          return (false, "AdaptiveActions содержит дублирующиеся ID действий");

        // Проверка на антагонистов
        var actionAntagonists = allAdaptiveActions
            .ToDictionary(a => a.Id, a => a.AntagonistActions ?? new List<int>());

        var actionConflicts = AntagonistValidator.ValidateAntagonists(adaptiveActions,
            id => actionAntagonists.ContainsKey(id) ? actionAntagonists[id] : new List<int>());

        if (actionConflicts.Any())
          return (false,
              $"Конфликты адаптивных действий: {string.Join("; ", actionConflicts.Select(c => c.Message))}");
      }

      return (true, string.Empty);
    }

    /// <summary>
    /// Валидирует параметры безусловного рефлекса
    /// </summary>
    public bool ValidateGeneticReflex(GeneticReflex reflex, out string errorMessage)
    {    
      var validationResult = ValidateGeneticReflexParameters(
          reflex.Level1,
          reflex.Level2,
          reflex.Level3,
          reflex.AdaptiveActions);

      errorMessage = validationResult.ErrorMessage;
      return validationResult.IsValid;
    }

    /// <summary>
    /// Валидирует существующий безусловный рефлекс с возможностью отключения проверки дубликатов
    /// </summary>
    public (bool IsValid, string ErrorMessage) ValidateGeneticReflex(GeneticReflex reflex, bool skipDuplicateCheck = false)
    {
      if (reflex == null)
        return (false, "Рефлекс не может быть null");

      return ValidateGeneticReflexParameters(
          reflex.Level1,
          reflex.Level2,
          reflex.Level3,
          reflex.AdaptiveActions);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом GeneticReflexesSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        // Отписываемся от событий других систем
        if (_gomeostas != null)
          _gomeostas.StyleDeleted -= OnStyleDeleted;

        if (_influenceActionSystem != null)
          _influenceActionSystem.InfluenceActionDeleted -= OnInfluenceActionDeleted;

        if (_adaptiveActionsSystem != null)
          _adaptiveActionsSystem.AdaptiveActionDeleted -= OnAdaptiveActionDeleted;

        // Очищаем подписчиков на наши события
        GeneticReflexDeleted = null;
        MultipleGeneticReflexesDeleted = null;
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
