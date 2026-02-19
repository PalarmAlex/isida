using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Windows;
using static ISIDA.Common.FileValidator;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Actions
{
  /// <summary>
  /// Система управления адаптивными действиями агента
  /// </summary>
  public sealed class AdaptiveActionsSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private bool _disposed = false;

    #region Инициализация

    private static AdaptiveActionsSystem _instance;

    /// <summary>
    /// Глобальный экземпляр системы адаптивных действий. Должен быть инициализирован через InitializeInstance()
    /// </summary>
    public static AdaptiveActionsSystem Instance => _instance ?? 
      throw new InvalidOperationException("AdaptiveActionsSystem не инициализирован. Вызовите InitializeInstance() с путями.");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы адаптивных действий с указанными путями к данным и шаблонам, 
    /// а также ссылкой на систему гомеостаза, на которую действия будут оказывать влияние
    /// Должен быть вызван один раз при старте приложения, после инициализации GomeostasSystem.
    /// </summary>
    /// <param name="gomeostas">Инициализированный экземпляр GomeostasSystem, управляющий параметрами гомеостаза</param>
    /// <param name="actionsFolderPath">Путь к папке с данными действий. Если null — используется путь по умолчанию </param>
    /// <exception cref="InvalidOperationException">Выбрасывается, если система уже была инициализирована ранее</exception>
    public static void InitializeInstance(
        GomeostasSystem gomeostas,
        string actionsFolderPath = null)
    {
      if (_instance != null)
        throw new InvalidOperationException("AdaptiveActionsSystem уже инициализирован.");

      _instance = new AdaptiveActionsSystem(gomeostas, actionsFolderPath);
    }

    private readonly GomeostasSystem _gomeostas;
    private AdaptiveActionsSystem(
        GomeostasSystem gomeostas,
        string actionsFolderPath = null)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));

      // Установка путей
      _actionsFolderPath = string.IsNullOrWhiteSpace(actionsFolderPath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ISIDA", "Data", "Actions")
            : actionsFolderPath;
      try
      {
        EnsureDataDirectory();
        LoadActions();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
     }

    #endregion

    #region Константы и структуры

    private const string ActionsFileName = "AdaptiveActions";
    private readonly string _actionsFolderPath;

    private string GetActionsFilePath() =>
        Path.Combine(_actionsFolderPath, $"{ActionsFileName}.dat");

    /// <summary>
    /// Представляет адаптивное действие агента
    /// </summary>
    public class AdaptiveAction
    {
      /// <summary>
      /// Вызывает событие PropertyChanged при изменении свойства
      /// </summary>
      public event PropertyChangedEventHandler PropertyChanged;

      /// <summary>
      /// Событие, возникающее при изменении свойств объекта
      /// </summary>
      protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
      {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
      }

      /// <summary>
      /// Источник активации действия
      /// </summary>
      public ActionActivationSource ActivationSource { get; set; } = 0;

      /// <summary>
      /// Время активации действия в пульсах
      /// </summary>
      public int ActivationPulse { get; set; } = 0;

      /// <summary>
      /// Уникальный идентификатор действия
      /// </summary>
      public int Id { get; set; }

      /// <summary>
      /// Наименование действия
      /// </summary>
      public string Name { get; set; }

      /// <summary>
      /// Подробное описание действия
      /// </summary>
      public string Description { get; set; }

      /// <summary>
      /// Список ID действий-антагонистов, которые несовместимы с данным действием
      /// </summary>
      public List<int> AntagonistActions { get; set; } = new List<int>();

      /// <summary>
      /// Список ID параметров гомеостаза, которые действие можено ПОТЕНЦИАЛЬНО улучшить
      /// </summary>
      /// <remarks>
      /// Крик о помощи, плач - непосредственно не воздействуют на гомеостаз, но провоцируют положительно воздействовать на него других
      /// </remarks>
      public List<int> TargetGomeoParamIdArr { get; set; }

      private int _vigor = 5;

      /// <summary>
      /// Интенсивность действия — уровень активности, скорости, физической нагрузки.
      /// Диапазон: 1..10. Используется при конкуретной борьбе с антагонистами.
      /// Не определяет тип действия, только его "масштаб".
      /// </summary>
      /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при присвоении значения вне диапазона [1,10]</exception>
      public int Vigor
      {
        get => _vigor;
        set
        {
          var validation = SettingsValidator.ValidateVigorAction(value);
          if (!validation.isValid)
            throw new ArgumentOutOfRangeException(nameof(value), validation.errorMessage);

          _vigor = value;
        }
      }

      ///// <summary>
      ///// Время последней активации действия (для расчета времени удержания действия)
      ///// </summary>
      //public DateTime LastActivated { get; set; } = DateTime.MinValue;

      /// <summary>
      /// Ссылка на систему действий (для доступа к модифицированной интенсивности)
      /// </summary>
      internal AdaptiveActionsSystem ActionsSystem { get; set; }

      /// <summary>
      /// Возвращает значимость действия — суммарную силу влияний по модулю.
      /// Используется при конкуретной борьбе с антагонистами для визуализации (размер шрифта, цвет).
      /// </summary>
      /// <returns>Сумма абсолютных значений влияний с учетом интенсивности</returns>
      public int GetSignificance()
      {
        int baseSignificance = Vigor;

        // Учитываем интенсивность в значимости
        float vigorRatio = ActionsSystem != null
            ? (float)ActionsSystem.GetModifiedVigor(Id) / 10f
            : (float)Vigor / 10f;

        // Логарифмическое масштабирование для лучшего визуального эффекта
        float logBase = (float)Math.Log10(baseSignificance + 1);
        float scaledSignificance = logBase * 20f;

        float minMultiplier = 0.3f;
        float multiplier = minMultiplier + (1 - minMultiplier) * vigorRatio;

        return Math.Max(1, (int)(scaledSignificance * multiplier));
      }
    }

    #endregion

    #region Поля и свойства

    /// <summary>
    /// Тип источника активации действия
    /// </summary>
    public enum ActionActivationSource
    {
      /// <summary>
      /// Действие от безусловного рефлекса
      /// </summary>
      GeneticReflex = 1,

      /// <summary>
      /// Действие от условного рефлекса
      /// </summary>
      ConditionedReflex = 2,

      /// <summary>
      /// Действие от автоматизма
      /// </summary>
      Automatizm = 3,

      /// <summary>
      /// Вербальный ответ, выполненный автоматизмом
      /// </summary>
      AutomatizmVerbalResponse = 4
    }

    private readonly Dictionary<int, AdaptiveAction> _actions = new Dictionary<int, AdaptiveAction>();
    private readonly List<AdaptiveAction> _activeActions = new List<AdaptiveAction>();
    private readonly Dictionary<int, int> _activeActionPhrases = new Dictionary<int, int>();

    /// <summary>
    /// Событие удаления адаптивного действия
    /// </summary>
    public event Action<int> AdaptiveActionDeleted;

    private int _defaultAdaptiveActionId = 0;
    /// <summary>
    /// ID существующего адаптивного действия по умолчанию
    /// </summary>
    /// 
    public int DefaultAdaptiveActionId
    {
      get => _defaultAdaptiveActionId;
      set
      {
        _defaultAdaptiveActionId = value;
        AppGlobalState.DefaultAdaptiveActionId = value;
      }
    }
    private int _lastActionId = 0;

    // Время удержания рефлекторных действий для визуализации (пульсов)
    private int _reflexActionDisplayDuration = 2;

    /// <summary>
    /// Время удержания рефлекторных действий для визуализации
    /// </summary>
    public int ReflexActionDisplayDuration
    {
      get => _reflexActionDisplayDuration;
      set
      {
        if (value < 0)
          throw new ArgumentOutOfRangeException(nameof(value), "Время удержания рефлекса не может быть отрицательным");

        if(value >= _gomeostas.DynamicTime)
          throw new ArgumentOutOfRangeException(nameof(value), "Время удержани рефлекса не может быть меньше или равно времени удержания состояния параметра");

        _reflexActionDisplayDuration = Math.Max(1, value); // минимум 1 секунда
      }
    }

    #endregion

    #region Управление действиями

    /// <summary>
    /// (Internal) Возвращает список активных адаптивных действий.
    /// </summary>
    /// <returns>Копия списка активных адаптивных действий</returns>
    internal List<AdaptiveAction> GetActiveAdaptiveActionsList()
    {
      _lock.EnterReadLock();
      try
      {
        return new List<AdaptiveAction>(_activeActions);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список текущих активных действий
    /// </summary>
    /// <returns>ReadOnlyCollection активных действий</returns>
    public ReadOnlyCollection<AdaptiveAction> GetActiveAdaptiveActions()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<AdaptiveAction>(_activeActions.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// (Internal) Возвращает список всех адаптивных действий.
    /// </summary>
    /// <returns>Копия списка всех адаптивных действий</returns>
    internal List<AdaptiveAction> GetAllAdaptiveActionsList()
    {
      _lock.EnterReadLock();
      try
      {
        return _actions.Values.ToList();
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает список всех адаптивных действий
    /// </summary>
    /// <returns>ReadOnlyCollection всех действий</returns>
    public ReadOnlyCollection<AdaptiveAction> GetAllAdaptiveActions()
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<AdaptiveAction>(_actions.Values.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает активные действия по источнику активации
    /// </summary>
    public ReadOnlyCollection<AdaptiveAction> GetActiveActionsBySource(ActionActivationSource source)
    {
      _lock.EnterReadLock();
      try
      {
        return new ReadOnlyCollection<AdaptiveAction>(
            _activeActions.Where(a => a.ActivationSource == source).ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает все активные действия сгруппированные по источнику
    /// </summary>
    public Dictionary<ActionActivationSource, List<AdaptiveAction>> GetActiveActionsGroupedBySource()
    {
      _lock.EnterReadLock();
      try
      {
        return _activeActions
            .GroupBy(a => a.ActivationSource)
            .ToDictionary(g => g.Key, g => g.ToList());
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Добавляет новое адаптивное действие
    /// </summary>
    /// <param name="name">Наименование действия</param>
    /// <param name="description">Описание действия</param>
    /// <param name="antagonistActions">Список ID антагонистических действий, которые несовместимы с данным действием</param>
    /// <param name="targetGomeoParamIdArr">Список ID параметров гомеостаза, которые потенциально может улучшить действие</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров. При значении true — выбрасывает исключение при выходе значений за допустимые пределы (-10..+10)</param>
    /// <param name="Vigor">Интенсивность действия [1...10], по умолчанию = 5</param>
    /// <exception cref="ArgumentException">Выбрасывается при пустом или null имени действия</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях в влияниях или затратах (вне диапазона -10..+10)</exception>
    public (int ActionId, string[] Warnings) AddAction(
        string name,
        string description,
        List<int> antagonistActions = null,
        List<int> targetGomeoParamIdArr = null,
        bool strictValidation = false,
        int Vigor = 5)
    {
      if (AppGlobalState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с адаптивными действиями разрешена только в стадии 0");

      if (string.IsNullOrWhiteSpace(name))
        throw new ArgumentException("Наименование действия не может быть пустым", nameof(name));

      // Создаем временный объект для валидации
      var tempAction = new AdaptiveAction
      {
        Id = 0, // Временный ID
        Name = name,
        Description = description,
        AntagonistActions = antagonistActions ?? new List<int>(),
        TargetGomeoParamIdArr = targetGomeoParamIdArr ?? new List<int>(),
        Vigor = Vigor,
        ActionsSystem = this
      };

      // Используем единую валидацию через вспомогательный метод
      if (!ValidateSingleAction(tempAction, out string validationError, out string validationWarnings))
      {
        if (strictValidation)
          throw new InvalidOperationException(validationError);

        var warnings = validationWarnings.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToArray();
        return (0, warnings);
      }

      // Если есть только предупреждения
      var finalWarnings = validationWarnings.Split('\n').Where(s => !string.IsNullOrEmpty(s)).ToArray();

      // Создаем действие после успешной валидации
      _lock.EnterWriteLock();
      try
      {
        int newId = ++_lastActionId;
        var action = new AdaptiveAction
        {
          Id = newId,
          Name = name,
          Description = description,
          Vigor = Vigor,
          AntagonistActions = antagonistActions ?? new List<int>(),
          TargetGomeoParamIdArr = targetGomeoParamIdArr ?? new List<int>()
        };

        _actions.Add(newId, action);

        return (newId, finalWarnings);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Обновляет существующее адаптивное действие
    /// </summary>
    /// <param name="action">Обновляемое действие</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров</param>
    /// <returns>Предупреждения (если есть)</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается при null действии</exception>
    /// <exception cref="KeyNotFoundException">Выбрасывается если действие не найдено</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях</exception>
    public string[] UpdateAction(AdaptiveAction action, bool strictValidation = false)
    {
      if (AppGlobalState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с адаптивными действиями разрешена только в стадии 0");

      if (action == null)
        throw new ArgumentNullException(nameof(action));

      var warnings = new List<string>();

      // Проверка что действие не блокирует само себя в антагонистах
      if (action.AntagonistActions?.Contains(action.Id) == true)
      {
        string selfAntagonistMessage = $"Действие '{action.Name}' (ID: {action.Id}) не может блокировать само себя в списке антагонистов";
        if (strictValidation)
          throw new InvalidOperationException(selfAntagonistMessage);
        warnings.Add(selfAntagonistMessage);

        // Удаляем само действие из списка антагонистов
        action.AntagonistActions.Remove(action.Id);
      }

      _lock.EnterWriteLock();
      try
      {
        if (!_actions.ContainsKey(action.Id))
          throw new KeyNotFoundException($"Действие с ID {action.Id} не найдено");

        _actions[action.Id] = action;
        return warnings.ToArray();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Удаляет адаптивное действие по указанному ID
    /// </summary>
    /// <param name="actionId">ID удаляемого действия</param>
    /// <returns>True, если действие было успешно удалено, иначе False</returns>
    public bool RemoveAction(int actionId)
    {
      if (AppGlobalState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с адаптивными действиями разрешена только в стадии 0");

      if (!_actions.ContainsKey(actionId))
        throw new InvalidOperationException($"Адаптивное действие c ID: {actionId} не найдено.");

      if (actionId == _defaultAdaptiveActionId)
        throw new InvalidOperationException($"Адаптивное действие {_actions[actionId].Name} задано действием по умолчанию и запрещёно для удаления.");

      _lock.EnterWriteLock();
      try
      {
        // Удаляем действие из коллекции
        bool removed = _actions.Remove(actionId);

        // Удаляем из активных действий
        _activeActions.RemoveAll(a => a.Id == actionId);
        AppGlobalState.UpdateActiveAdaptiveActions(_activeActions);
        _activeActionPhrases.Remove(actionId);

        // Удаляем ссылки на это действие как антагониста в других действиях
        foreach (var action in _actions.Values)
        {
          if (action.AntagonistActions.Contains(actionId))
            action.AntagonistActions.Remove(actionId);
        }

        // Вызываем событие удаления действия
        AdaptiveActionDeleted?.Invoke(actionId);

        return removed;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Принудительная очистка активных действий
    /// </summary>
    internal void ClearActiveAction()
    {
      try
      {
        _activeActions.Clear();
        AppGlobalState.UpdateActiveAdaptiveActions(_activeActions);
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Принудительная очистка активных слов
    /// </summary>
    internal void ClearActivePhrases()
    {
      try
      {
        _activeActionPhrases.Clear();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    /// <summary>
    /// Полная очистка всех активных действий и фраз. Вызывается при остановке пульсации.
    /// </summary>
    public void ClearAllActiveState()
    {
      ClearActiveAction();
      ClearActivePhrases();
    }

    /// <summary>
    /// Очищает рефлекторные действия, которые отображались дольше заданного времени
    /// </summary>
    internal void CleanupExpiredReflexActions()
    {
      var now = DateTime.UtcNow;
      var reflexActionsToRemove = _activeActions
          .Where(a => a.ActivationPulse > 0)
          .Where(a => (GlobalTimer.GlobalPulsCount - a.ActivationPulse) >= ReflexActionDisplayDuration)
          .ToList();

      foreach (var action in reflexActionsToRemove)
      {
        _activeActions.Remove(action);
        _activeActionPhrases.Remove(action.Id);
        action.ActivationSource = 0;
      }
      AppGlobalState.UpdateActiveAdaptiveActions(_activeActions);
    }

    /// <summary>
    /// Применяет указанное действие
    /// </summary>
    /// <param name="actionId">ID применяемого действия</param>
    /// <param name="phraseId">ID фразы, по умолчанию 0</param>
    /// <returns>True, если действие было успешно применено</returns>
    /// <exception cref="KeyNotFoundException">Выбрасывается если действие не найдено</exception>
    public bool ApplyAction(int actionId, int phraseId = 0)
    {
      _lock.EnterWriteLock();
      try
      {
        if (!_actions.TryGetValue(actionId, out var action))
          throw new KeyNotFoundException($"Действие с ID {actionId} не найдено");

        int modifiedVigor = GetModifiedVigor(actionId);
        int currentActionPower = modifiedVigor;

        foreach (var activeAction in _activeActions.ToList())
        {
          if (action.AntagonistActions.Contains(activeAction.Id) ||
              activeAction.AntagonistActions.Contains(action.Id))
          {
            // Для антагониста также получаем модифицированную интенсивность
            int activeActionModifiedVigor = GetModifiedVigor(activeAction.Id);
            int activeActionPower = activeActionModifiedVigor;

            // Если текущее действие сильнее - удаляем антагониста. = чтобы не было взаимной блокировки
            if (currentActionPower >= activeActionPower)
            {
              _activeActions.Remove(activeAction);
              _activeActionPhrases.Remove(activeAction.Id);
            }
            else
            {
              // Если антагонист сильнее или равен - блокируем применение
              return false;
            }
          }
        }

        // Добавляем в активные действия для визуализации
        if (!_activeActions.Any(a => a.Id == actionId))
        {
          _activeActions.Add(action);
          _activeActionPhrases[actionId] = phraseId;
          action.ActivationPulse = GlobalTimer.GlobalPulsCount;
          AppGlobalState.UpdateActiveAdaptiveActions(_activeActions);
        }

        return true;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Получить ID слова по ID действия
    /// </summary>
    public int GetPhraseIdForAction(int actionId)
    {
      _lock.EnterReadLock();
      try
      {
        return _activeActionPhrases.TryGetValue(actionId, out int phraseId) ? phraseId : 0;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>
    /// Получает интенсивность действия
    /// </summary>
    /// <param name="actionId">ID действия</param>
    /// <returns>Модифицированная интенсивность (1..10)</returns>
    public int GetModifiedVigor(int actionId)
    {
      try
      {
        if (!_actions.TryGetValue(actionId, out var action))
          return 5; // Значение по умолчанию

        int baseVigor = action.Vigor;

        return baseVigor; // пока оставим просто базовую, возможно потом будем менять как то
      }
      catch
      {
        return 5;
      }
    }

    #endregion

    #region Валидация и коррекция антагонистов

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи для адаптивных действий в переданной коллекции
    /// </summary>
    /// <param name="actions">Коллекция действий для исправления</param>
    /// <returns>Количество исправленных связей</returns>
    public int FixActionAntagonistSymmetry(IEnumerable<AdaptiveAction> actions)
    {
      int fixesCount = 0;
      var actionList = actions.ToList();
      var actionDict = actionList.ToDictionary(a => a.Id, a => a);

      foreach (var action in actionList)
      {
        foreach (var antagonistId in action.AntagonistActions.ToList())
        {
          if (actionDict.ContainsKey(antagonistId))
          {
            var antagonist = actionDict[antagonistId];

            if (!antagonist.AntagonistActions.Contains(action.Id))
            {
              antagonist.AntagonistActions.Add(action.Id);
              fixesCount++;
            }
          }
        }
      }

      return fixesCount;
    }

    /// <summary>
    /// Автоматически исправляет асимметричные антагонистические связи для адаптивных действий в текущих данных
    /// </summary>
    /// <returns>Количество исправленных связей</returns>
    public int FixActionAntagonistSymmetry()
    {
      int fixesCount = 0;
      _lock.EnterWriteLock();
      try
      {
        var actions = _actions.Values.ToList();
        fixesCount = FixActionAntagonistSymmetry(actions);

        return fixesCount;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Находит адаптивные действия с асимметричными антагонистическими связями
    /// </summary>
    /// <returns>Список проблемных действий</returns>
    public List<AdaptiveAction> FindAsymmetricActions(IEnumerable<AdaptiveAction> actions)
    {
      return FindUnpairedActionsForValidation(actions.ToList());
    }

    /// <summary>
    /// Находит действия с несимметричными антагонистическими связями для валидации
    /// </summary>
    private List<AdaptiveAction> FindUnpairedActionsForValidation(List<AdaptiveAction> actions)
    {
      var unpaired = new List<AdaptiveAction>();
      var actionDict = actions.ToDictionary(a => a.Id, a => a);

      foreach (var action in actions)
      {
        foreach (var antagonistId in action.AntagonistActions)
        {
          if (actionDict.ContainsKey(antagonistId))
          {
            var antagonist = actionDict[antagonistId];
            if (!antagonist.AntagonistActions.Contains(action.Id))
            {
              if (!unpaired.Contains(action))
                unpaired.Add(action);

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
      if (!Directory.Exists(_actionsFolderPath))
      {
        Directory.CreateDirectory(_actionsFolderPath);
      }
    }

    /// <summary>
    /// Загружает действия из файла
    /// </summary>
    private void LoadActions()
    {
      var path = GetActionsFilePath();

      try
      {
        if (IsValidActionsFile(path))
        {
          _actions.Clear();
          _activeActions.Clear();
          _lastActionId = 0;

          foreach (var line in File.ReadLines(path))
          {
            var trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
              continue;

            var parts = trimmedLine.Split('|');

            if (parts.Length < 3)
              continue;

            if (!int.TryParse(parts[0], out int id))
              continue;

            int vigor = 5;
            if (parts.Length > 3 && !string.IsNullOrWhiteSpace(parts[3]))
            {
              int.TryParse(parts[3].Trim(), out vigor);
              vigor = ClampInt(vigor, 1, 10);
            }

            var action = new AdaptiveAction
            {
              Id = id,
              Name = parts[1].Trim(),
              Description = parts[2].Trim(),
              Vigor = vigor,
              ActionsSystem = this
            };

            if (parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4]))
            {
              action.AntagonistActions = parts[4]
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s =>
                {
                  if (int.TryParse(s.Trim(), out int aid)) return aid;
                  return 0;
                })
                .Where(aid => aid != 0)
                .ToList();
            }
            else
              action.AntagonistActions = new List<int>();

            if (parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5]))
            {
             action.TargetGomeoParamIdArr = parts[5]
              .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
              .Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(s =>
              {
                if (int.TryParse(s.Trim(), out int paramId)) return paramId;
                return 0;
              })
              .Where(paramId => paramId != 0)
              .ToList();
            }
            else
              action.TargetGomeoParamIdArr = new List<int>();

            _actions[action.Id] = action;
            if (action.Id > _lastActionId)
              _lastActionId = action.Id;
          }
        }
        else
        {
          EnsureDataDirectory();
          var lines = new List<string>
            {
                FileHeaders.ActionsFormat,
                FileHeaders.ActionsAntagonists,
                FileHeaders.TargetParameters
            };
          File.WriteAllLines(path, lines);

          _actions.Clear();
          _activeActions.Clear();
          _lastActionId = 0;
        }
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
    }

    /// <summary>
    /// Сохраняет все действия в файл
    /// </summary>
    public (bool Success, string ErrorMessage) SaveActions(bool IsValidate = true)
    {
      if (AppGlobalState.EvolutionStage > 0)
        throw new InvalidOperationException("Работа с адаптивными действиями разрешена только в стадии 0");

      _lock.EnterWriteLock();
      try
      {
        if (IsValidate)
        {
          var (isValid, errors, warnings) = ValidateAction(_actions.Values);
          if (!isValid)
            return (false, errors);

          if (!string.IsNullOrEmpty(warnings))
          {
            var resultMsg = MessageBox.Show(
              $"{warnings}\n\nПродолжить сохранение?",
              "Предупреждения",
              MessageBoxButton.YesNo,
              MessageBoxImage.Warning);

            if (resultMsg == MessageBoxResult.No)
              return (false, warnings);
          }
        }

        EnsureDataDirectory();
        var lines = new List<string>
        {
          FileHeaders.ActionsFormat,
          FileHeaders.ActionsAntagonists,
          FileHeaders.TargetParameters
        };

        foreach (var action in _actions.Values.OrderBy(a => a.Id))
        {
          string targetParams = action.TargetGomeoParamIdArr != null
            ? string.Join(",", action.TargetGomeoParamIdArr)
            : "";

          lines.Add($"{action.Id}|{action.Name}|{action.Description}|" +
            $"{action.Vigor}|" +
            $"{string.Join(",", action.AntagonistActions)}|" +
            $"{targetParams}");
        }

        var minLinesCount = 4;
        if (lines.Count == 3)
          minLinesCount = 3;

        var result = SafeSaveFile(
            GetActionsFilePath(),
            lines,
            content => IsValidActionsFile(string.Join(Environment.NewLine, content)),
            minLinesCount: minLinesCount,
            fileDescription: "адаптивных действий");

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
    /// Валидация адаптивных действий
    /// </summary>
    /// <param name="adaptiveActions">Список адаптивных действий</param>
    /// <param name="isForDeletion">При установке True валидация удаления, по умолчанию False - валидация обновления</param>
    /// <returns>True если валидация успешна, иначе False</returns>
    public (bool IsValid, string Errors, string Warnings) ValidateAction(IEnumerable<AdaptiveAction> adaptiveActions, bool isForDeletion = false)
    {
      var errorMessage = string.Empty;
      var existingIds = adaptiveActions.Select(p => p.Id).ToHashSet();
      var allErrors = new List<string>();
      var allWarnings = new List<string>();

      // Проверка асимметричных антагонистов
      var unpairedActions = FindUnpairedActionsForValidation(adaptiveActions.ToList());
      if (unpairedActions.Any())
      {
        var unpairedList = string.Join(", ", unpairedActions.Select(s => $"{s.Name} (ID:{s.Id})"));
        errorMessage = $"AsymmetricAction: Обнаружены несимметричные антагонистические связи:\n{unpairedList}\n\n";
        return (false, errorMessage, "");
      }

      foreach (var action in adaptiveActions)
      {
        if (isForDeletion)
        {
          if (!existingIds.Contains(action.Id))
          {
            errorMessage = $"Адаптивное действие c ID: {action.Id} не найдено";
            return (false, errorMessage, "");
          }

          if (action.Id == _defaultAdaptiveActionId)
          {
            errorMessage = $"Адаптивное действие {_actions[action.Id].Name} задано действием по умолчанию и запрещёно для удаления";
            return (false, errorMessage, "");
          }
        }
        else
        {
          if (!ValidateSingleAction(action, out string singleError, out string singleWarnings))
            allErrors.Add($"Действие '{action.Name}' (ID: {action.Id}): {singleError}");

          if (!string.IsNullOrEmpty(singleWarnings))
            allWarnings.Add($"Действие '{action.Name}' (ID: {action.Id}): {singleWarnings}");
        }
      }

      string errorText = allErrors.Any() ? string.Join("\n\n", allErrors) : "";
      string warningText = allWarnings.Any() ? string.Join("\n", allWarnings) : "";

      return (!allErrors.Any(), errorText, warningText);
    }

    private bool ValidateSingleAction(AdaptiveAction action, out string errorMessage, out string warnings)
    {
      errorMessage = string.Empty;
      warnings = string.Empty;

      var errors = new List<string>();
      var warningList = new List<string>();

      if (action.AntagonistActions?.Contains(action.Id) == true)
        errors.Add("Действие блокирует само себя в списке антагонистов");

      if (action.TargetGomeoParamIdArr != null)
      {
        foreach (var paramId in action.TargetGomeoParamIdArr)
        {
          var param = _gomeostas.GetParameter(paramId);
          if (param == null)
            errors.Add($"Параметр с ID {paramId} не найден");
        }
      }

      if (action.Vigor < 1 || action.Vigor > 10)
        errors.Add($"Интенсивность действия вне допустимых пределов: {action.Vigor} (допустимый диапазон: 1..10)");

      if (errors.Any())
        errorMessage = string.Join("\n", errors);

      if (warningList.Any())
        warnings = string.Join("\n", warningList);

      return !errors.Any();
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
        if (AppGlobalState.EvolutionStage == 0)
          SaveActions();
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
