using ISIDA.Psychic.Automatism;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Actions
{
  /// <summary>
  /// Система управления внешними воздействиями на агента
  /// </summary>
  public sealed class InfluenceActionSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private PerceptionImagesSystem _perceptionImagesSystem;
    private bool _disposed = false;
    private readonly GomeostasSystem _gomeostas;

    /// <summary>Событие активации триггерного стимула (действия с пульта)</summary>
    public event Action<int, List<int>, bool> TriggerStimulusActivated;

    /// <summary>Событие активации фразового стимула (фразы с пульта)</summary>
    public event Action<int, List<int>, List<int>, int, int> PhraseStimulusActivated;

    #region Инициализация

    private static InfluenceActionSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы внешних воздействий. Должен быть инициализирован через InitializeInstance().
    /// </summary>
    public static InfluenceActionSystem Instance => _instance ??
      throw new InvalidOperationException("InfluenceAction не инициализирован. Вызовите InitializeInstance() с путями.");

    /// <summary>
    /// Устанавливает систему образов восприятия
    /// </summary>
    public void SetPerceptionImagesSystem(PerceptionImagesSystem perceptionImagesSystem)
    {
      _perceptionImagesSystem = perceptionImagesSystem ?? throw new ArgumentNullException(nameof(perceptionImagesSystem));
    }

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы внешних воздействий с указанными путями к данным и шаблонам, 
    /// а также ссылкой на систему гомеостаза, на которую действия будут оказывать влияние.
    /// Должен быть вызван один раз при старте приложения, после инициализации GomeostasSystem.
    /// </summary>
    /// <param name="gomeostas">Инициализированный экземпляр GomeostasSystem, управляющий параметрами гомеостаза</param>
    /// <param name="actionsFolderPath">Путь к папке с данными действий. Если null — используется путь по умолчанию</param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        string actionsFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("AdaptiveActionsSystem уже инициализирован.");

      _instance = new InfluenceActionSystem(gomeostas, actionsFolderPath);
    }

    private InfluenceActionSystem(GomeostasSystem gomeostas, string actionsFolderPath = null)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));

      // Установка путей
      _influenceActionsFolderPath = string.IsNullOrWhiteSpace(actionsFolderPath)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISIDA", "Data", "Actions")
        : actionsFolderPath;
      try
      {
        EnsureDataDirectory();
        LoadInfluenceActions();
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"InfluenceActionSystem: Ошибка инициализации AdaptiveActionsSystem: {ex.Message}");
        throw;
      }
    }

    #endregion

    #region Константы и структуры

    private const string InfluenceActionsFileName = "InfluenceActions";
    private readonly string _influenceActionsFolderPath;

    private string GetInfluenceActionsFilePath() =>
      Path.Combine(_influenceActionsFolderPath, $"{InfluenceActionsFileName}.dat");

    /// <summary>
    /// Представляет внешнее гомеостатическое воздействие на агента
    /// </summary>
    public class GomeostasisInfluenceAction
    {
      /// <summary>
      /// Уникальный идентификатор действия
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Наименование воздействия
      /// </summary>
      public string Name { get; set; }

      /// <summary>
      /// Подробное описание воздействия
      /// </summary>
      public string Description { get; set; }

      private Dictionary<int, int> _influences = new Dictionary<int, int>();
      /// <summary>
      /// Влияние возддействия на параметры гомеостаза (положительное или отрицательное)
      /// </summary>
      /// <remarks>
      /// Ключ - ID параметра, значение - величина воздействия (-10..+10)
      /// </remarks>
      public Dictionary<int, int> Influences
      {
        get => _influences;
        set
        {
          if (value == null)
          {
            _influences = new Dictionary<int, int>();
            return;
          }

          foreach (var kvp in value)
          {
            var validation = SettingsValidator.ValidateInfluencesParametr(kvp.Value);
            if (!validation.isValid)
              throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);
          }
          _influences = new Dictionary<int, int>(value);
        }
      }

      /// <summary>
      /// Список ID воздействий-антагонистов, которые несовместимы с данным воздействием
      /// </summary>
      public List<int> AntagonistInfluences { get; set; } = new List<int>();
    }

    #endregion

    #region Поля и свойства

    private readonly Dictionary<int, GomeostasisInfluenceAction> _influenceActions = new Dictionary<int, GomeostasisInfluenceAction>();
    private readonly List<GomeostasisInfluenceAction> _influenceActiveActions = new List<GomeostasisInfluenceAction>();
    private int _lastGomeoActionId = 0;

    /// <summary>
    /// Событие удаления гомеостатического воздействия
    /// </summary>
    public event Action<int> InfluenceActionDeleted;

    #endregion

    #region Управление гомеостатическими воздействиями

    /// <summary>
    /// Добавляет новое гомеостатическое воздействие
    /// </summary>
    /// <param name="name">Наименование воздействия</param>
    /// <param name="description">Описание воздействия</param>
    /// <param name="influences">Словарь влияний на параметры гомеостаза (ID параметра -> величина воздействия). Отражает полезный/вредный эффект воздействия.</param>
    /// <param name="antagonistInfluence">Список ID антагонистических действий, которые несовместимы с данным действием</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров. При значении true — выбрасывает исключение при выходе значений за допустимые пределы (-10..+10)</param>
    /// <returns>ID созданного воздействия и массив предупреждений (если были скорректированы значения)</returns>
    /// <exception cref="ArgumentException">Выбрасывается при пустом или null имени воздействия</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях в влияниях (вне диапазона -10..+10)</exception>    
    public (int ActionId, string[] Warnings) AddInfluenceAction(
        string name,
        string description,
        Dictionary<int, int> influences,
        List<int> antagonistInfluence = null,
        bool strictValidation = false)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с гомеостатическими воздействиями разрешена только в стадии 0");

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование воздействия не может быть пустым", nameof(name));

      var warnings = new List<string>();

      // Проверка влияний
      if (influences != null)
      {
        foreach (var influence in influences)
        {
          if (influence.Value < -10 || influence.Value > 10)
          {
            string message = $"Влияние на параметр {influence.Key} скорректировано с {influence.Value} до " +
                            $"{ClampInt(influence.Value, -10, 10)} " +
                            "(допустимый диапазон: -10..+10)";
            if (strictValidation)
              throw new ArgumentOutOfRangeException(nameof(influences), influence.Value, message);
            warnings.Add(message);
          }
        }
      }

      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastGomeoActionId;
        var action = new GomeostasisInfluenceAction
        {
          Id = newId,
          Name = name,
          Description = description,
          Influences = influences?.ToDictionary(kvp => kvp.Key, kvp => ClampInt(kvp.Value, -10, 10)) ?? new Dictionary<int, int>(),
          AntagonistInfluences = antagonistInfluence?.Where(id => id > 0).Distinct().ToList() ?? new List<int>()
        };
        _influenceActions.Add(newId, action);

        return (newId, warnings.ToArray());
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет существующее гомеостатическое воздействие
    /// </summary>
    /// <param name="action">Обновляемое воздействие</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров</param>
    /// <returns>Предупреждения (если есть)</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается при null воздействие</exception>
    /// <exception cref="KeyNotFoundException">Выбрасывается если воздействие не найдено</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях</exception>
    public string[] UpdateAction(GomeostasisInfluenceAction action, bool strictValidation = false)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с гомкостатическими возействиями разрешена только в стадии 0");

      if (action == null)
        throw new ArgumentNullException(nameof(action));

      var warnings = new List<string>();

      // Проверка влияний
      foreach (var influence in action.Influences)
      {
        if (influence.Value < -10 || influence.Value > 10)
        {
          string message = $"Влияние на параметр {influence.Key} скорректировано с {influence.Value} до " +
                          $"{ClampInt(influence.Value, -10, 10)} " +
                          "(допустимый диапазон: -10..+10)";
          if (strictValidation)
            throw new ArgumentOutOfRangeException(nameof(action.Influences), influence.Value, message);
          warnings.Add(message);
          action.Influences[influence.Key] = ClampInt(influence.Value, -10, 10);
        }
      }

      _lock.EnterWriteLock();
      try
      {
        if (!_influenceActions.ContainsKey(action.Id))
          throw new KeyNotFoundException($"Гомеостатическое воздействие с ID {action.Id} не найдено");

        _influenceActions[action.Id] = action;
        return warnings.ToArray();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет гомеостатическое воздействие по указанному ID
    /// </summary>
    /// <param name="actionId">ID удаляемого воздействия</param>
    /// <returns>True, если воздействие было успешно удалено, иначе False</returns>
    public bool RemoveAction(int actionId)
    {
      _lock.EnterWriteLock();
      try
      {
        if (_gomeostas.GetAgentState().EvolutionStage > 0)
          throw new InvalidOperationException("Работа с гомеостатическими воздействиями разрешена только в стадии 0");

        if (!_influenceActions.ContainsKey(actionId))
          return false;

        if (IsActionUsedInPerceptionImages(actionId))
        {
          var actionName = _influenceActions[actionId].Name;
          throw new InvalidOperationException($"Воздействие '{actionName}' (ID: {actionId}) используется в образах восприятия и не может быть удалено");
        }

        bool removed = _influenceActions.Remove(actionId);

        _influenceActiveActions.RemoveAll(a => a.Id == actionId);

        foreach (var action in _influenceActions.Values)
        {
          if (action.AntagonistInfluences.Contains(actionId))
          {
            action.AntagonistInfluences.Remove(actionId);
          }
        }
        InfluenceActionDeleted?.Invoke(actionId);

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Проверяет, используется ли воздействие в каких-либо образах PerceptionImage
    /// </summary>
    /// <param name="actionId">ID проверяемого воздействия</param>
    /// <returns>True если воздействие используется, иначе False</returns>
    private bool IsActionUsedInPerceptionImages(int actionId)
    {
      try
      {
        if (_perceptionImagesSystem == null || !PerceptionImagesSystem.IsInitialized)
          return false;

        var perceptionImages = _perceptionImagesSystem.GetAllPerceptionImagesList();
        foreach (var image in perceptionImages)
        {
          if (image.InfluenceActionsList != null && image.InfluenceActionsList.Contains(actionId))
            return true;
        }

        return false;
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"IsActionUsedInPerceptionImages: Ошибка при проверке использования воздействия {actionId} в образах восприятия: {ex.Message}");
        return true;
      }
    }

    /// <summary>
    /// Удаляет все активные акции действий с пульта
    /// </summary>
    internal void ClearActiveAction()
    {
      foreach (var action in _influenceActions.Values)
      {
        _influenceActiveActions.RemoveAll(a => a.Id == action.Id);
      }
    }

    /// <summary>
    /// Текущий ID полного образа сочетаний пусковых стимулов: действие + фраза
    /// Используется как стимул у-рефлексов и автоматизмов
     /// </summary>
    internal int ActiveCurTriggerStimulusID = 0;

    /// <summary>
    /// Текущий ID частичного образа сочетаний пусковых стимулов: только действия.
    /// Используется как стимул б/у рефлексов
    /// </summary>
    internal int ActiveCurReflexTriggerStimulusID = 0;

    /// <summary>
    /// Применяет множественные воздействия и создает образ восприятия или возвращает его ID, если такой уже есть
    /// </summary>
    public (bool Success, string ErrorMessage) ApplyMultipleInfluenceActions(
        List<int> actionIdList,
        List<int> phraseIdList,
        bool authoritativeMode = false,
        int toneId = 0,
        int moodId = 0)
    {
      string errorMessage = string.Empty;

      // Безопасная проверка состояния агента
      if (!_gomeostas.TryEnsureAgentState(AgentCheck.NotDead | AgentCheck.IsActive, silent: true))
        return (false, "Агент неактивен или мертв - воздействие невозможно");

      _lock.EnterWriteLock();
      try
      {
        _influenceActiveActions.Clear();
        var errors = new List<string>();

        // собираем все воздействия
        var actionsToApply = new List<GomeostasisInfluenceAction>();
        foreach (var actionId in actionIdList ?? new List<int>())
        {
          if (_influenceActions.TryGetValue(actionId, out var action))
            actionsToApply.Add(action);
          else
            errors.Add($"Воздействие с ID {actionId} не найдено");
        }

        // Заполняем активные воздействия ДО их применения
        _influenceActiveActions.AddRange(actionsToApply);
        ActiveCurTriggerStimulusID = CreatePerceptionImage(actionIdList, phraseIdList ?? new List<int>());
        // для стимула б/у рефлексов фразу игнорируем
        ActiveCurReflexTriggerStimulusID = CreatePerceptionImage(actionIdList, new List<int>());

        if (phraseIdList?.Any() == true)
          PhraseStimulusActivated?.Invoke(GlobalTimer.GlobalPulsCount, actionIdList, phraseIdList, toneId, moodId);
        if (actionIdList?.Any() == true)
          TriggerStimulusActivated?.Invoke(GlobalTimer.GlobalPulsCount, actionIdList, authoritativeMode);

        // Применение воздействий (после вызова событий)
        foreach (var action in actionsToApply)
        {
          var result = ApplySingleInfluenceActionInternal(action);
          if (!result.Success)
            errors.Add($"Воздействие ID {action.Id}: {result.ErrorMessage}");
        }

        // Формируем итоговое сообщение
        if (errors.Any())
        {
          errorMessage = $"Частично успешно. Ошибки: {string.Join("; ", errors)}";
          return (true, errorMessage);
        }

        return (true, "Все воздействия успешно применены");
      }
      catch (Exception ex)
      {
        return (false, $"Системная ошибка: {ex.Message}");
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Внутренний метод применения одиночного воздействия (без блокировки)
    /// </summary>
    private (bool Success, string ErrorMessage) ApplySingleInfluenceActionInternal(GomeostasisInfluenceAction action)
    {
      try
      {        
        if (!_gomeostas.TryEnsureAgentState(AgentCheck.NotDead | AgentCheck.IsActive, silent: true))
          return (false, "Агент неактивен или мертв - воздействие невозможно");

        var parameters = _gomeostas.GetAllParameters();
        bool isCriticalImpact = _gomeostas.Calculator.IsExternalImpactCritical(
            action.Influences, parameters);


        foreach (var influence in action.Influences)
        {
          int parameterId = influence.Key;
          int effectValue = influence.Value;

          var param = _gomeostas.GetParameter(parameterId);
          if (param == null)
            return (false, $"Параметр с ID {parameterId} не найден");

          float originalValue = param.Value;
          float newValue = ClampFloat(originalValue + effectValue, 0f, 100f);

          param.Value = newValue;
        }
        _gomeostas.OnExternalInfluenceApplied(isCriticalImpact);
       
        return (true, string.Empty);
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"{ex.Message}");
        return (false, ex.Message);
      }
    }

    /// <summary>
    /// Создает образ восприятия из примененных воздействий и фраз
    /// </summary>
    private int CreatePerceptionImage(List<int> actionIdList, List<int> phraseIdList)
    {
      try
      {
        if (_perceptionImagesSystem == null)
          return 0;

        int imageId = _perceptionImagesSystem.AddPerceptionImage(actionIdList, phraseIdList);

        // Синхронное сохранение вместо асинхронного
        if (imageId > 0)
          _perceptionImagesSystem.SavePerceptionImages();

        return imageId;
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"CreatePerceptionImage: Ошибка создания образа восприятия: {ex.Message}");
        return 0;
      }
    }

    /// <summary>
    /// Получает список всех гомеостатических воздействий
    /// </summary>
    /// <returns>ReadOnlyCollection всех воздействий</returns>
    public ReadOnlyCollection<GomeostasisInfluenceAction> GetAllInfluenceActions()
    {
      return new ReadOnlyCollection<GomeostasisInfluenceAction>(_influenceActions.Values.ToList());
    }

    /// <summary>
    /// Получает список текущих активных гомеостатических воздействий
    /// </summary>
    /// <returns>ReadOnlyCollection активных воздействий</returns>
    public ReadOnlyCollection<GomeostasisInfluenceAction> GetActiveInfluenceActions()
    {
      return new ReadOnlyCollection<GomeostasisInfluenceAction>(_influenceActiveActions.ToList());
    }

    #endregion

    #region Валидация и коррекция антагонистов

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи для гомеостатических воздействий в переданной коллекции
    /// </summary>
    /// <param name="influences">Коллекция воздействий для исправления</param>
    /// <returns>Количество исправленных связей</returns>
    public int FixInfluenceAntagonistSymmetry(IEnumerable<GomeostasisInfluenceAction> influences)
    {
      int fixesCount = 0;
      var influenceList = influences.ToList();
      var influenceDict = influenceList.ToDictionary(i => i.Id, i => i);

      foreach (var influence in influenceList)
      {
        foreach (var antagonistId in influence.AntagonistInfluences.ToList())
        {
          if (influenceDict.ContainsKey(antagonistId))
          {
            var antagonist = influenceDict[antagonistId];

            if (!antagonist.AntagonistInfluences.Contains(influence.Id))
            {
              antagonist.AntagonistInfluences.Add(influence.Id);
              fixesCount++;
            }
          }
        }
      }

      return fixesCount;
    }

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи для гомеостатических воздействий
    /// </summary>
    /// <returns>Количество исправленных связей</returns>
    public int FixInfluenceAntagonistSymmetry()
    {
      int fixesCount = 0;
      _lock.EnterWriteLock();
      try
      {
        var influences = _influenceActions.Values.ToList();
        fixesCount = FixInfluenceAntagonistSymmetry(influences);

        return fixesCount;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Находит гомеостатические воздействия с асимметричными антагонистическими связями
    /// </summary>
    /// <returns>Список проблемных воздействий</returns>
    public List<GomeostasisInfluenceAction> FindAsymmetricInfluences(IEnumerable<GomeostasisInfluenceAction> influences)
    {
      return FindUnpairedInfluencesForValidation(influences.ToList());
    }

    /// <summary>
    /// Находит воздействия с несимметричными антагонистическими связями для валидации
    /// </summary>
    private List<GomeostasisInfluenceAction> FindUnpairedInfluencesForValidation(List<GomeostasisInfluenceAction> influences)
    {
      var unpaired = new List<GomeostasisInfluenceAction>();
      var influenceDict = influences.ToDictionary(i => i.Id, i => i);

      foreach (var influence in influences)
      {
        foreach (var antagonistId in influence.AntagonistInfluences)
        {
          if (influenceDict.ContainsKey(antagonistId))
          {
            var antagonist = influenceDict[antagonistId];
            if (!antagonist.AntagonistInfluences.Contains(influence.Id))
            {
              if (!unpaired.Contains(influence))
                unpaired.Add(influence);

              break;
            }
          }
        }
      }

      return unpaired;
    }

    #endregion

    #region Работа с файлами

    /// <summary>
    /// Создает каталог параметров действий, если его нет
    /// </summary>
    private void EnsureDataDirectory()
    {
      if (!Directory.Exists(_influenceActionsFolderPath))
      {
        Directory.CreateDirectory(_influenceActionsFolderPath);
      }
    }

    /// <summary>
    /// Загружает гомеостатические воздействия из файла
    /// </summary>
    private void LoadInfluenceActions()
    {
      var path = GetInfluenceActionsFilePath();

      try
      {
        if (FileValidator.IsInfluenceValidActionsFile(path))
        {
          _influenceActions.Clear();
          _lastGomeoActionId = 0;

          foreach (var line in File.ReadLines(path))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');
            if (parts.Length < 3)
            {
              continue;
            }

            if (!int.TryParse(parts[0], out int id))
            {
              continue;
            }

            var action = new GomeostasisInfluenceAction
            {
              Id = id,
              Name = parts[1].Trim(),
              Description = parts[2].Trim(),
              Influences = ParseInfluences(parts[3])
            };

            if (parts.Length >= 4)
            {
              action.AntagonistInfluences = parts[4]
                  .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Select(s => int.TryParse(s.Trim(), out int aid) ? aid : 0)
                  .Where(aid => aid != 0)
                  .ToList();
            }

            _influenceActions[action.Id] = action;
            if (action.Id > _lastGomeoActionId)
              _lastGomeoActionId = action.Id;
          }
        }
        else
        {
          EnsureDataDirectory();
          var lines = new List<string>
          {
            FileValidator.FileHeaders.InfluenceActionsFormat,
            FileValidator.FileHeaders.InfluenceActionsBenefit,
            FileValidator.FileHeaders.InfluenceAntagonists
          };
          File.WriteAllLines(path, lines);
          _influenceActions.Clear();
          _lastGomeoActionId = 0;
        }
      }
      catch
      {
        throw;
      }
    }

    /// <summary>
    /// Сохраняет все гомеостатические воздействия в файл
    /// </summary>
    /// <returns>Кортеж (успех, сообщение об ошибке)</returns>
    public (bool Success, string ErrorMessage) SaveInfluenceActions(bool IsValidate = true)
    {
      if (_gomeostas.GetAgentState().EvolutionStage > 0)
        throw new InvalidOperationException("Работа с гомеостатическими воздействиями разрешена только в стадии 0");
      
      if (IsValidate)
      {
        if (!ValidateAllInfluenceActions(_influenceActions.Values, out string errorMessage))
          return (false, errorMessage);
      }
      EnsureDataDirectory();

      _lock.EnterWriteLock();
      try
      {
        var lines = new List<string>
        {
          FileValidator.FileHeaders.InfluenceActionsFormat,
          FileValidator.FileHeaders.InfluenceActionsBenefit,
          FileValidator.FileHeaders.InfluenceAntagonists
        };

        foreach (var action in _influenceActions.Values.OrderBy(a => a.Id))
        {
          lines.Add($"{action.Id}|{action.Name}|{action.Description}|" +
                   $"{InfluencesToString(action.Influences)}|" +
                   $"{string.Join(",", action.AntagonistInfluences)}");
        }
        var linCount = 4;
        if (lines.Count == 3)
          linCount = 3; // для случая очистки всего кроме шапки

        var result = FileValidator.SafeSaveFile(
            GetInfluenceActionsFilePath(),
            lines,
            content => FileValidator.IsInfluenceValidActionsFile(string.Join(Environment.NewLine, content)),
            minLinesCount: linCount,
            fileDescription: "гомеостатических воздействий");

        if (!result.Success)
        {
        }

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

    /// <summary>
    /// Выполняет комплексную валидацию всех гомеостатических воздействий.
    /// Проверяет: дубликаты ID, пустые имена, ссылки на несуществующие антагонисты и антагонистические конфликты.
    /// </summary>
    /// <param name="influences">Список гомеостатических воздействий</param>
    /// <param name="errorMessage">Сообщение об ошибке, если валидация не прошла.</param>
    /// <returns>True, если валидация успешна, иначе false.</returns>
    public bool ValidateAllInfluenceActions(IEnumerable<GomeostasisInfluenceAction> influences, out string errorMessage)
    {
      errorMessage = string.Empty;
      try
      {
        var actions = influences.ToList();

        if (!actions.Any())
        {
          errorMessage = "Нет гомеостатических воздействий для валидации.";
          return false;
        }

        var existingIds = actions.Select(a => a.Id).ToHashSet();

        // Проверка: дубликаты ID (уже гарантируется словарём, но на всякий случай)
        if (existingIds.Count != actions.Count)
        {
          errorMessage = "Обнаружены дубликаты ID гомеостатических воздействий.";
          return false;
        }

        // Проверка: пустые или null имена
        var invalidNameAction = actions.FirstOrDefault(a => string.IsNullOrWhiteSpace(a.Name));
        if (invalidNameAction != null)
        {
          errorMessage = $"Гомеостатическое воздействие с ID {invalidNameAction.Id} имеет пустое или null имя.";
          return false;
        }

        // Проверка: антагонисты ссылаются только на существующие ID
        foreach (var action in actions)
        {
          var invalidAntagonist = action.AntagonistInfluences
              .FirstOrDefault(aid => !existingIds.Contains(aid));

          if (invalidAntagonist != 0)
          {
            errorMessage = $"Гомеостатическое воздействие с ID {action.Id} ссылается на несуществующий антагонист: {invalidAntagonist}";
            return false;
          }
        }

        // Проверка асимметричных антагонистов
        var unpairedInfluences = FindUnpairedInfluencesForValidation(actions);
        if (unpairedInfluences.Any())
        {
          var unpairedList = string.Join(", ", unpairedInfluences.Select(s => $"{s.Name} (ID:{s.Id})"));
          errorMessage = $"AsymmetricInfluences: Обнаружены несимметричные антагонистические связи:\n{unpairedList}\n\n";
          return false;
        }

        // Проверку в образах делать не надо, потому как эта валидация так же при обновлении используется
        return true;
      }
      catch (Exception ex)
      {
        errorMessage = $"Ошибка при валидации воздействий: {ex.Message}";
        return false;
      }
    }
    /// <summary>
    /// Парсит строку влияний в словарь
    /// </summary>
    private Dictionary<int, int> ParseInfluences(string influenceStr)
    {
      var influences = new Dictionary<int, int>();
      if (string.IsNullOrWhiteSpace(influenceStr)) return influences;

      var pairs = influenceStr.Split(';');
      foreach (var pair in pairs)
      {
        var kv = pair.Split(':');
        if (kv.Length == 2 &&
            int.TryParse(kv[0], out int paramId) &&
            int.TryParse(kv[1], out int effect))
        {
          influences[paramId] = GomeostasSystem.ClampInt(effect, -10, 10);
        }
      }
      return influences;
    }

    /// <summary>
    /// Преобразует словарь влияний в строку
    /// </summary>
    private string InfluencesToString(Dictionary<int, int> influences)
    {
      return string.Join(";", influences.Select(kv => $"{kv.Key}:{kv.Value}"));
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AdaptiveActionsSystem
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      try
      {
        SaveInfluenceActions();
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"Error during disposal: {ex.Message}");
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
