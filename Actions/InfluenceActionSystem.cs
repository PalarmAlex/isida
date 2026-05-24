using ISIDA.Psychic.Automatism;
using ISIDA.Common;
using ISIDA.Niche;
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
  /// Система управления внешними воздействиями на симбионта
  /// </summary>
  public sealed class InfluenceActionSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private PerceptionImagesSystem _perceptionImagesSystem;
    private bool _disposed = false;
    private readonly GomeostasSystem _gomeostas;
    private CouplingBridge _couplingBridge;
    private TriadOrchestrator _triadOrchestrator;

    /// <summary>Тип последнего применённого сигнала оператора (§6.2).</summary>
    public AssessmentType LastAppliedAssessmentType { get; private set; } = AssessmentType.Bootstrap;

    /// <summary>Событие активации триггерного стимула (действия с пульта)</summary>
    public event Action<int, List<int>, bool> TriggerStimulusActivated;

    /// <summary>Событие активации фразового стимула (фразы с пульта)</summary>
    public event Action<int, List<int>, List<int>, int, int, bool> PhraseStimulusActivated;

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
    /// Подключает CouplingBridge для определения <see cref="AssessmentType"/> по фазе триады.
    /// </summary>
    /// <param name="couplingBridge">Мост триады или null.</param>
    public void SetCouplingBridge(CouplingBridge couplingBridge)
    {
      _couplingBridge = couplingBridge;
    }

    /// <summary>
    /// Подключает оркестратор триады для probe-key контура Niche.
    /// </summary>
    /// <param name="orchestrator">TriadOrchestrator или null.</param>
    public void SetTriadOrchestrator(TriadOrchestrator orchestrator)
    {
      _triadOrchestrator = orchestrator;
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
        throw new InvalidOperationException("InfluenceActionSystem уже инициализирован.");

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
    /// Представляет внешнее гомеостатическое воздействие на симбионта
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

      /// <summary>
      /// Ключ пробы метрики среды (хост, сопоставляет с сэмплером); пусто — воздействие не от среды.
      /// </summary>
      public string EnvironmentMetricProbeKey { get; set; } = string.Empty;
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
    /// <param name="environmentMetricProbeKey">Ключ пробы метрики среды для хоста; null или пустая строка — воздействие не привязано к среде</param>
    /// <param name="strictValidation">Флаг строгой проверки параметров. При значении true — выбрасывает исключение при выходе значений за допустимые пределы (-10..+10)</param>
    /// <returns>ID созданного воздействия и массив предупреждений (если были скорректированы значения)</returns>
    /// <exception cref="ArgumentException">Выбрасывается при пустом или null имени воздействия</exception>
    /// <exception cref="ArgumentOutOfRangeException">Выбрасывается при строгой проверке и недопустимых значениях в влияниях (вне диапазона -10..+10)</exception>    
    public (int ActionId, string[] Warnings) AddInfluenceAction(
        string name,
        string description,
        Dictionary<int, int> influences,
        List<int> antagonistInfluence = null,
        string environmentMetricProbeKey = null,
        bool strictValidation = false)
    {
      if (AppGlobalState.EvolutionStage > 0)
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

      string probeKey = environmentMetricProbeKey?.Trim() ?? string.Empty;
      var probeCheck = SettingsValidator.ValidateEnvironmentMetricProbeKey(probeKey);
      if (!probeCheck.isValid)
      {
        if (strictValidation)
          throw new ArgumentException(probeCheck.errorMessage, nameof(environmentMetricProbeKey));
        warnings.Add(probeCheck.errorMessage);
        probeKey = string.Empty;
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
          AntagonistInfluences = antagonistInfluence?.Where(id => id > 0).Distinct().ToList() ?? new List<int>(),
          EnvironmentMetricProbeKey = probeKey
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
      if (AppGlobalState.EvolutionStage > 0)
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

      string probeTrim = action.EnvironmentMetricProbeKey?.Trim() ?? string.Empty;
      var probeValidation = SettingsValidator.ValidateEnvironmentMetricProbeKey(probeTrim);
      if (!probeValidation.isValid)
      {
        if (strictValidation)
          throw new ArgumentException(probeValidation.errorMessage, nameof(action.EnvironmentMetricProbeKey));
        warnings.Add(probeValidation.errorMessage);
        probeTrim = string.Empty;
      }

      action.EnvironmentMetricProbeKey = probeTrim;

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
        if (AppGlobalState.EvolutionStage > 0)
          throw new InvalidOperationException("Работа с гомеостатическими воздействиями разрешена только в стадии 0");

        if (!_influenceActions.ContainsKey(actionId))
          return false;

        if (IsActionUsedInPerceptionImages(actionId))
        {
          var actionName = _influenceActions[actionId].Name;
          throw new InvalidOperationException($"Воздействие '{actionName}' (ID: {actionId}) используется в образах восприятия и не может быть удалено");
        }

        if (AdaptiveActionsSystem.IsInitialized &&
            AdaptiveActionsSystem.Instance.IsInfluenceActionIdUsedForMirroring(actionId))
        {
          var actionName = _influenceActions[actionId].Name;
          throw new InvalidOperationException($"Воздействие '{actionName}' (ID: {actionId}) используется для отзеркаливания в действиях симбионта и не может быть удалено");
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
        Logger.Error(ex.Message);
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
    /// Текущий ID полного образа сочетаний пусковых стимулов: действие + фраза + цвет
    /// Используется как стимул у-рефлексов и автоматизмов
     /// </summary>
    internal int ActiveCurTriggerStimulusID = 0;

    /// <summary>Код зрительного канала, применённый в последнем <see cref="ApplyMultipleInfluenceActions"/>.</summary>
    public int LastAppliedVisualColorId { get; private set; }

    /// <summary>Паттерны команды, применённые в последнем <see cref="ApplyMultipleInfluenceActions"/>.</summary>
    public List<int> LastAppliedCommandPatternIdList { get; private set; } = new List<int>();

    /// <summary>
    /// Текущий ID частичного образа сочетаний пусковых стимулов: только действия.
    /// Используется как стимул б/у рефлексов
    /// </summary>
    internal int ActiveCurReflexTriggerStimulusID = 0;

    /// <summary>
    /// Применяет множественные воздействия с пульта, создаёт образ восприятия и обновляет пару значимости стимула для эпизодической памяти
    /// (сдвиг выполняется после фактического применения, кроме режима наблюдения).
    /// </summary>
    /// <param name="actionIdList">ID гомеостатических воздействий; пустой список — стимул без кнопок гомеостаза (значимость этого тика — 0).</param>
    /// <param name="phraseIdList">ID фраз стимула для образа восприятия.</param>
    /// <param name="commandPatternIdList">ID паттернов команды стимула для образа восприятия.</param>
    /// <param name="authoritativeMode">Режим передачи в событие фразового стимула.</param>
    /// <param name="toneId">ID тона сообщения.</param>
    /// <param name="moodId">ID настроения.</param>
    /// <param name="visualColorId">Код зрительного канала сцены.</param>
    /// <param name="emergencyOverride">Аварийное переопределение (§6.2 EmergencyOverride).</param>
    /// <returns>Признак успеха и текст сообщения об ошибках при частичном применении.</returns>
    public (bool Success, string ErrorMessage) ApplyMultipleInfluenceActions(
        List<int> actionIdList,
        List<int> phraseIdList,
        List<int> commandPatternIdList = null,
        bool authoritativeMode = false,
        int toneId = 0,
        int moodId = 0,
        int visualColorId = 0,
        bool emergencyOverride = false)
    {
      string errorMessage = string.Empty;

      if (!GlobalTimer.IsPulsationRunning)
        return (false, "Пульсация выключена — воздействия не применяются");

      // Безопасная проверка состояния симбионта
      if (!_gomeostas.TryEnsureAgentState(AgentCheck.NotDead | AgentCheck.IsActive, silent: true))
        return (false, "Симбионт неактивен или мертв - воздействие невозможно");

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
        if (!AgentVisualColor.IsValidCode(visualColorId))
          visualColorId = AgentVisualColor.White;
        LastAppliedVisualColorId = visualColorId;
        LastAppliedCommandPatternIdList = commandPatternIdList?.ToList() ?? new List<int>();
        ActiveCurTriggerStimulusID = CreatePerceptionImage(
            actionIdList,
            phraseIdList ?? new List<int>(),
            commandPatternIdList ?? new List<int>(),
            visualColorId);
        // для стимула б/у рефлексов фразу, команду и цвет игнорируем
        ActiveCurReflexTriggerStimulusID = CreatePerceptionImage(
            actionIdList,
            new List<int>(),
            new List<int>(),
            AgentVisualColor.White);
        AppGlobalState.LastTriggerStimulusID = ActiveCurTriggerStimulusID;

        bool verbalReflexPath = phraseIdList?.Any() == true
            || commandPatternIdList?.Any() == true
            || visualColorId != AgentVisualColor.White;
        if (verbalReflexPath)
          PhraseStimulusActivated?.Invoke(GlobalTimer.GlobalPulsCount, actionIdList, phraseIdList, toneId, moodId, authoritativeMode);
        if (actionIdList?.Any() == true)
          TriggerStimulusActivated?.Invoke(GlobalTimer.GlobalPulsCount, actionIdList, authoritativeMode);

        Dictionary<int, float> homeostasisSnapshotBeforeApply = null;
        bool hasHomeostasisActions = OperatorStimulusHasHomeostasisActionComponents(actionIdList) && actionsToApply.Count > 0;
        bool routeToNiche = false;

        if (hasHomeostasisActions && _couplingBridge != null && _couplingBridge.IsActive)
        {
          if (_couplingBridge.IsOperatorCreatureInfluenceBlocked && !emergencyOverride)
          {
            routeToNiche = true;
            Logger.Info("Triad фаза C: прямое влияние на Creature заблокировано — маршрут Operator→Niche");
          }
        }

        if (!AppGlobalState.ObservationMode && hasHomeostasisActions && !routeToNiche)
          homeostasisSnapshotBeforeApply = SnapshotHomeostasisParameterValues(_gomeostas);

        // Применение воздействий (после вызова событий)
        LastAppliedAssessmentType = ResolveAssessmentTypeForApply(emergencyOverride);

        if (routeToNiche)
        {
          int nicheApplied = _couplingBridge.ApplyOperatorInfluencesToNiche(actionIdList ?? new List<int>());
          if (nicheApplied == 0)
            errors.Add("Фаза C: нет Operator→Niche mapping для воздействий с пульта");
        }
        else
        {
          foreach (var action in actionsToApply)
          {
            var result = ApplySingleInfluenceActionInternal(action);
            if (!result.Success)
              errors.Add($"Воздействие ID {action.Id}: {result.ErrorMessage}");
          }
        }

        if (LastAppliedAssessmentType == AssessmentType.RitualViolation &&
            !hasHomeostasisActions &&
            AppGlobalState.EvolutionStage >= 4)
        {
          Logger.Info("RitualViolation: meta-сигнал оператора (без прямого ±1 на Creature)");
        }

        ForwardContourProbeKey(actionsToApply);

        if (!AppGlobalState.ObservationMode)
        {
          int newStimulsEffect = homeostasisSnapshotBeforeApply != null
            ? ComputeStimulsEffectFromHomeostasisShift(homeostasisSnapshotBeforeApply, _gomeostas)
            : 0;
          AppGlobalState.AdvanceStimulusEffectPair(newStimulsEffect);
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
    /// Определяет, передан ли с пульта хотя бы один ID гомеостатического воздействия (непустой список).
    /// При пустом списке значимость стимула для эпизодики на этом тике принудительно обнуляется, чтобы не переносить эффект предыдущих кнопок.
    /// </summary>
    /// <param name="actionIdList">Список ID воздействий, как в <see cref="ApplyMultipleInfluenceActions"/>.</param>
    /// <returns>True, если список не null и содержит хотя бы один элемент.</returns>
    public static bool OperatorStimulusHasHomeostasisActionComponents(IList<int> actionIdList)
    {
      return actionIdList != null && actionIdList.Count > 0;
    }

    /// <summary>
    /// Снимает значения параметров гомеостаза для последующего сравнения после применения воздействий.
    /// </summary>
    /// <param name="gomeostas">Система гомеостаза.</param>
    /// <returns>Словарь ID параметра — значение.</returns>
    private static Dictionary<int, float> SnapshotHomeostasisParameterValues(GomeostasSystem gomeostas)
    {
      var list = gomeostas.GetAllParameters();
      var snapshot = new Dictionary<int, float>(list.Count);
      for (int i = 0; i < list.Count; i++)
      {
        var p = list[i];
        snapshot[p.Id] = p.Value;
      }
      return snapshot;
    }

    /// <summary>
    /// Суммирует изменения значений параметров гомеостаза между снимком и текущим состоянием, округляет и усечёт к диапазону −10…10.
    /// </summary>
    /// <param name="valuesBeforeApply">Снимок до применения воздействий.</param>
    /// <param name="gomeostas">Система гомеостаза после применения.</param>
    /// <returns>Оценка значимости стимула для записи в пару Prev/Current.</returns>
    private static int ComputeStimulsEffectFromHomeostasisShift(
      Dictionary<int, float> valuesBeforeApply,
      GomeostasSystem gomeostas)
    {
      if (valuesBeforeApply == null || valuesBeforeApply.Count == 0)
        return 0;

      var after = gomeostas.GetAllParameters();
      float sum = 0f;
      for (int i = 0; i < after.Count; i++)
      {
        var p = after[i];
        if (!valuesBeforeApply.TryGetValue(p.Id, out float oldValue))
          oldValue = p.Value;
        sum += p.Value - oldValue;
      }

      int rounded = (int)Math.Round(sum, MidpointRounding.AwayFromZero);
      return ClampInt(rounded, -10, 10);
    }

    /// <summary>
    /// Внутренний метод применения одиночного воздействия (без блокировки).
    /// В режиме наблюдения (AppGlobalState.ObservationMode) эффект на гомеостаз не применяется.
    /// </summary>
    private (bool Success, string ErrorMessage) ApplySingleInfluenceActionInternal(GomeostasisInfluenceAction action)
    {
      try
      {
        if (!_gomeostas.TryEnsureAgentState(AgentCheck.NotDead | AgentCheck.IsActive, silent: true))
          return (false, "Симбионт неактивен или мертв - воздействие невозможно");

        if (AppGlobalState.ObservationMode)
          return (true, string.Empty);

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
        _gomeostas.MarkDirectParameterInfluenceOrigin(StimulusOrigin.Operator);
        _gomeostas.OnExternalInfluenceApplied(isCriticalImpact);

        return (true, string.Empty);
      }
      catch (Exception ex)
      {
        FileValidator.LogError($"{ex.Message}");
        return (false, ex.Message);
      }
    }

    private AssessmentType ResolveAssessmentTypeForApply(bool emergencyOverride = false)
    {
      if (emergencyOverride)
        return AssessmentType.EmergencyOverride;

      if (_couplingBridge == null || !_couplingBridge.IsActive)
        return AssessmentType.Bootstrap;

      switch (_couplingBridge.EffectivePhase)
      {
        case TriadPhase.C:
          return AssessmentType.RitualViolation;
        case TriadPhase.B:
          return AssessmentType.RitualScaffold;
        default:
          return AssessmentType.Bootstrap;
      }
    }

    private void ForwardContourProbeKey(List<GomeostasisInfluenceAction> actionsToApply)
    {
      if (actionsToApply == null || actionsToApply.Count == 0)
        return;

      if (_triadOrchestrator == null && (_couplingBridge == null || !_couplingBridge.IsActive))
        return;

      string probeKey = string.Empty;
      for (int i = actionsToApply.Count - 1; i >= 0; i--)
      {
        string candidate = actionsToApply[i].EnvironmentMetricProbeKey?.Trim() ?? string.Empty;
        if (candidate.Length > 0)
        {
          probeKey = candidate;
          break;
        }
      }

      if (_triadOrchestrator != null)
        _triadOrchestrator.SetContourProbeKey(probeKey);
      else
        _couplingBridge.SetContourProbeKey(probeKey);
    }

    /// <summary>
    /// Создает образ восприятия из примененных воздействий и фраз
    /// </summary>
    private int CreatePerceptionImage(
        List<int> actionIdList,
        List<int> phraseIdList,
        List<int> commandPatternIdList,
        int visualColorId = 0)
    {
      try
      {
        if (_perceptionImagesSystem == null)
          return 0;

        return _perceptionImagesSystem.AddPerceptionImage(
            actionIdList,
            phraseIdList,
            visualColorId,
            commandPatternIdList);
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

    /// <summary>Сумма модулей величин воздействия по параметрам гомеостаза (сравнение «значимости» воздействия).</summary>
    public int GetInfluenceMagnitudeSum(int actionId)
    {
      if (!_influenceActions.TryGetValue(actionId, out var a) || a?.Influences == null || a.Influences.Count == 0)
        return 0;
      int s = 0;
      foreach (var v in a.Influences.Values)
        s += Math.Abs(v);
      return s;
    }

    /// <summary>Сумма со знаком влияний по всем параметрам для одного действия с пульта (оценка намерения оператора).</summary>
    public int GetSignedInfluenceSumForAction(int actionId)
    {
      if (!_influenceActions.TryGetValue(actionId, out var a) || a?.Influences == null || a.Influences.Count == 0)
        return 0;
      int s = 0;
      foreach (var v in a.Influences.Values)
        s += v;
      return s;
    }

    /// <summary>Сумма со знаком по списку действий (несколько кнопок в одном образе).</summary>
    public int GetSignedInfluenceSumForActions(IEnumerable<int> actionIds)
    {
      if (actionIds == null)
        return 0;
      int s = 0;
      foreach (var id in actionIds)
        s += GetSignedInfluenceSumForAction(id);
      return s;
    }

    /// <summary>
    /// Сумма «полезности намерения оператора» по списку воздействий с пульта: для каждого слагаемого effect на параметр
    /// учитывается <see cref="GomeostasSystem.ParameterData.Speed"/> — дефицит-ориентированные (Speed &lt; 0): положительный effect
    /// к значению параметра — улучшение; избыток-ориентированные (Speed &gt; 0) — наоборот.
    /// </summary>
    public int GetSignedOperatorValenceSumForActions(IEnumerable<int> actionIds)
    {
      if (actionIds == null || !GomeostasSystem.IsInitialized)
        return 0;

      int total = 0;
      foreach (var actionId in actionIds)
      {
        if (!_influenceActions.TryGetValue(actionId, out var a) || a?.Influences == null || a.Influences.Count == 0)
          continue;

        foreach (var kvp in a.Influences)
        {
          var param = _gomeostas.GetParameter(kvp.Key);
          if (param == null)
            continue;

          int speed = param.Speed;
          if (speed == 0)
            continue;

          // Дефицит (Speed < 0): +effect → к лучшему; избыток (Speed > 0): +effect → к худшему.
          int orientation = speed < 0 ? 1 : -1;
          total += kvp.Value * orientation;
        }
      }

      return total;
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
            if (parts.Length < 4)
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

            if (parts.Length >= 5)
            {
              action.AntagonistInfluences = parts[4]
                  .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                  .Where(s => !string.IsNullOrWhiteSpace(s))
                  .Select(s => int.TryParse(s.Trim(), out int aid) ? aid : 0)
                  .Where(aid => aid != 0)
                  .ToList();
            }

            if (parts.Length >= 6)
              action.EnvironmentMetricProbeKey = parts[5].Trim();

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
            FileValidator.FileHeaders.InfluenceAntagonists,
            FileValidator.FileHeaders.InfluenceActionsEnvironmentProbeKey
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
      if (AppGlobalState.EvolutionStage > 0)
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
            FileValidator.FileHeaders.InfluenceAntagonists,
            FileValidator.FileHeaders.InfluenceActionsEnvironmentProbeKey
          };

        foreach (var action in _influenceActions.Values.OrderBy(a => a.Id))
        {
          lines.Add($"{action.Id}|{action.Name}|{action.Description}|" +
                   $"{InfluencesToString(action.Influences)}|" +
                   $"{string.Join(",", action.AntagonistInfluences)}|" +
                   $"{(action.EnvironmentMetricProbeKey ?? string.Empty).Trim()}");
        }
        var linCount = 5;
        if (lines.Count == 4)
          linCount = 4; // для случая очистки всего кроме шапки

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

        foreach (var action in actions)
        {
          var pv = SettingsValidator.ValidateEnvironmentMetricProbeKey(action.EnvironmentMetricProbeKey ?? string.Empty);
          if (!pv.isValid)
          {
            errorMessage = $"Гомеостатическое воздействие с ID {action.Id}: {pv.errorMessage}";
            return false;
          }
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
        if (AppGlobalState.EvolutionStage == 0)
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
        _instance = null;
      }
    }

    #endregion
  }
}
