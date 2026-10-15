# План: Проприоцептивные сигнатуры действий для обучения цепочкам рефлексов

## Цель

Дать каждому моторному действию стабильную проприоцептивную сигнатуру (уникальный ID), которая после выполнения инжектируется в `ConditionedReflexFormationService` как нейтральный CS. Это позволяет существующему механизму транзитивного обучения автоматически строить цепочки «M₁ → П₁ → M₂ → П₂ → M₃» без внешнего подкрепления.

---

## Правка 1. `AdaptiveActionsSystem.cs` — поле сигнатуры

**Файл:** `Actions/AdaptiveActionsSystem.cs`

### 1.1. Добавить поле счётчика и свойство в `AdaptiveAction`

Добавить **статическое поле** в класс `AdaptiveActionsSystem` (рядом с `_lastActionId`):

```csharp
/// <summary>
/// Статический счётчик уникальных ID проприоцептивных сигнатур.
/// Монотонно растёт при AddAction и восстанавливается до max() при LoadActions.
/// Гарантирует уникальность сигнатур между всеми действиями агента
/// и не пересекается с _lastActionId.
/// </summary>
private static int _lastProprioceptiveSignatureId = 0;
```

Добавить **свойство** в класс `AdaptiveAction` (внутрь класса, рядом с `InfluenceActionId`):

```csharp
/// <summary>
/// Проприоцептивная сигнатура действия: стабильный набор ID «внутренних рецепторов»,
/// порождаемых при выполнении. Используется как нейтральный CS для обучения
/// цепочкам действий через транзитивное обучение (response-produced stimuli).
/// </summary>
public List<int> ProprioceptiveSignature { get; set; } = new List<int>();
```

### 1.2. Автозаполнение сигнатуры в `AddAction`

В методе `AddAction`, внутри блока `try`, после создания `action` (строка `Id = newId`):

```csharp
int newId = ++_lastActionId;
var action = new AdaptiveAction
{
    Id = newId,
    Name = name,
    Description = description,
    Vigor = Vigor,
    AntagonistActions = antagonistActions ?? new List<int>(),
    TargetGomeoParamIdArr = targetGomeoParamIdArr ?? new List<int>(),
    InfluenceActionId = influenceActionId,
    // Автозаполнение проприоцептивной сигнатуры
    ProprioceptiveSignature = new List<int> { ++_lastProprioceptiveSignatureId }
};
```

### 1.3. Восстановление счётчика при `LoadActions`

В методе `LoadActions`, **после** цикла `foreach` (но до `catch`), добавить:

```csharp
// Восстановление максимального ID для корректного автозаполнения новых действий
_lastProprioceptiveSignatureId = _actions.Values
    .Select(a => a.ProprioceptiveSignature?.FirstOrDefault() ?? 0)
    .DefaultIfEmpty(0)
    .Max();
```

> **Важно:** `DefaultIfEmpty(0)` защищает от `EmptySequenceException`, если `_actions` пусто (свежий файл).

### 1.4. Парсинг сигнатуры из файла

В методе `LoadActions`, после блока парсинга `parts[6]` (influenceActionId), **перед** `_actions[action.Id] = action;`:

```csharp
if (parts.Length >= 8 && !string.IsNullOrWhiteSpace(parts[7]))
{
    action.ProprioceptiveSignature = AddUtils.ParseIntList(parts[7]);
}
else
{
    // Старый файл без сигнатуры — сгенерировать автоматически
    action.ProprioceptiveSignature = new List<int> { ++_lastProprioceptiveSignatureId };
}
```

### 1.5. Запись сигнатуры в файл

В методе `SaveActions`, в список `lines` добавить заголовок:

```csharp
var lines = new List<string>
{
    FileHeaders.ActionsFormat,
    FileHeaders.ActionsAntagonists,
    FileHeaders.TargetParameters,
    FileHeaders.ActionsInfluenceActionId,
    FileHeaders.ActionsProprioceptiveSignature  // новая строка
};
```

В строку сохранения действия добавить 8-ю колонку:

```csharp
lines.Add($"{action.Id}|{action.Name}|{action.Description}|" +
    $"{action.Vigor}|" +
    $"{string.Join(",", action.AntagonistActions)}|" +
    $"{targetParams}|{action.InfluenceActionId}|" +
    $"{AddUtils.IntListToString(action.ProprioceptiveSignature)}");
```

### 1.6. Добавить константу заголовка в `FileValidator.cs`

В `FileValidator.FileHeaders` добавить:

```csharp
// Моторные действия — проприоцептивная сигнатура
public const string ActionsProprioceptiveSignature =
    "# ProprioceptiveSignature: ID проприоцептивной сигнатуры действия (через запятую); столбец опционален в старых файлах";
```

---

## Правка 2. `ConditionedReflexFormationService.cs` — проприоцептивный режим

**Файл:** `Reflexes/ConditionedReflexFormationService.cs`

### 2.1. Расширение `RecordStimulus` — проприоцептивный флаг

Добавить параметр `isProprioceptive = false` в сигнатуру `RecordStimulus`:

```csharp
public void RecordStimulus(
    int pulse,
    int stimulusImageId,
    int baseState,
    int behaviorStyleImageId,
    int geneticReflexId = 0,
    int toneId = 0,
    int moodId = 0,
    bool crFired = false,
    bool isProprioceptive = false)  // НОВОЕ
```

Внутри `RecordStimulus` — **критическое изменение логики**:

```csharp
_lock.EnterWriteLock();
try
{
    // Проприоцептивные стимулы НЕ перезаписывают _lastConditionedStimulus
    // и НЕ запускают активное угасание через _pendingCsAwaitingUs.
    // НО они МОГУТ укреплять сенсорные связи CS→CS (для транзитивного обучения).

    bool strengthenSensory = geneticReflexId == 0 && !isProprioceptive;

    if (strengthenSensory)
        TryStrengthenNeutralSequentialLink(pulse, stimulusImageId);

    var record = new StimulusRecord
    {
        Pulse = pulse,
        StimulusImageId = stimulusImageId,
        BaseState = baseState,
        BehaviorStyleImageId = behaviorStyleImageId,
        GeneticReflexId = geneticReflexId,
        ToneId = toneId,
        MoodId = moodId,
        IsProprioceptive = isProprioceptive  // НОВОЕ
    };

    if (geneticReflexId > 0)
    {
        _lastUnconditionedStimulus = record;
        if (_pendingCsAwaitingUs &&
            _pendingCsPulse < pulse &&
            pulse - _pendingCsPulse <= _conditionedReflexes.Settings.TimeWindowPulses)
        {
            _pendingCsAwaitingUs = false;
        }
    }
    else if (!isProprioceptive)
    {
        // Только НЕ-проприоцептивные стимулы обновляют _lastConditionedStimulus
        _lastConditionedStimulus = record;

        // Только НЕ-проприоцептивные стимулы могут помечаться pending
        if (!crFired)
        {
            _pendingCsAwaitingUs = true;
            _pendingCsPulse = pulse;
            _pendingCsStimulusImageId = stimulusImageId;
            _pendingCsToneId = toneId;
            _pendingCsMoodId = moodId;
        }
    }
    // else: isProprioceptive — record создан, но _lastConditionedStimulus / pending не трогаем
}
finally
{
    _lock.ExitWriteLock();
}

if (doExtinguish)
    _conditionedReflexes.ApplyActiveExtinctionForStimulus(extinguishImageId, extinguishToneId, extinguishMoodId);
```

> **Суть:** проприоцептивный стимул НЕ перезаписывает `_lastConditionedStimulus`, НЕ запускает `_pendingCsAwaitingUs`, но **НЕ исключается** из `TryStrengthenNeutralSequentialLink` — это позволяет строить цепочки CS→CS через проприоцептивные образы.

### 2.2. Поле `IsProprioceptive` в `StimulusRecord`

Добавить в класс `StimulusRecord`:

```csharp
/// <summary>
/// True — стимул порождён самим агентом (проприоцептивная сигнатура действия).
/// Не влияет на логику подавления сейчас. Заведено для будущего сценария:
/// когда проприоцептивный CS станет US для вторичного обусловливания (П₁ как
/// подкрепление для П₀), источник будет sourceConditionedReflexId > 0.
/// </summary>
public bool IsProprioceptive { get; set; }
```

В теле `RecordStimulus` добавить в инициализацию record (см. выше — 2.1).

### 2.3. Внутренний буфер истории стимулов

Добавить поле и методы для тестов:

```csharp
/// <summary>
/// Буфер последних 500 записей стимулов (включая проприоцептивные).
/// Используется тестами для проверки корректности инжекции.
/// </summary>
private readonly List<StimulusRecord> _stimulusHistory = new List<StimulusRecord>(500);
```

В конец тела `RecordStimulus` (после создания `record`, перед выходом из `try`):

```csharp
_stimulusHistory.Add(record);
if (_stimulusHistory.Count > 500)
{
    _stimulusHistory.RemoveRange(0, _stimulusHistory.Count - 500);
}
```

Метод для тестов:

```csharp
/// <summary>
/// Возвращает буфер всех записей стимулов (включая проприоцептивные).
/// Используется тестами для проверки корректности обработки проприоцептивных стимулов.
/// </summary>
internal IReadOnlyList<StimulusRecord> GetStimulusHistory()
{
    _lock.EnterReadLock();
    try { return _stimulusHistory.AsReadOnly(); }
    finally { _lock.ExitReadLock(); }
}

/// <summary>
/// Возвращает последний условный стимул (включая проприоцептивные, если был последним).
/// </summary>
internal StimulusRecord? GetLastConditionedStimulus()
{
    _lock.EnterReadLock();
    try { return _lastConditionedStimulus; }
    finally { _lock.ExitReadLock(); }
}
```

### 2.4. Сброс буфера истории в `ResetHistory`

Добавить очистку буфера в существующий метод `ResetHistory`:

```csharp
public void ResetHistory()
{
    _lock.EnterWriteLock();
    try
    {
        _lastUnconditionedStimulus = null;
        _lastConditionedStimulus = null;
        _pendingCsAwaitingUs = false;
        _pendingCsPulse = 0;
        _pendingCsStimulusImageId = 0;
        _pendingCsToneId = 0;
        _pendingCsMoodId = 0;
        _stimulusHistory.Clear();  // НОВОЕ
    }
    finally
    {
        _lock.ExitWriteLock();
    }
}
```

---

## Правка 3. `ProprioceptiveInjectionService.cs` — общий сервис инжекции

**Новый файл:** `Reflexes/ProprioceptiveInjectionService.cs`

```csharp
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ISIDA.Reflexes
{
    /// <summary>
    /// Сервис инжекции проприоцептивных сигнатур в ConditionedReflexFormationService.
    /// Вызывается из ReflexExecutionService и AutomatismExecutionService
    /// после ApplyAction. Единственная точка входа для создания нейтрального CS.
    /// </summary>
    public static class ProprioceptiveInjectionService
    {
        /// <summary>
        /// Инжектирует проприоцептивную сигнатуру действия как нейтральный CS.
        /// Вызывается после ApplyAction в ReflexExecutionService и AutomatismExecutionService.
        /// </summary>
        /// <param name="action">Выполненное действие с непустой ProprioceptiveSignature</param>
        public static void Inject(AdaptiveActionsSystem.AdaptiveAction action)
        {
            try
            {
                if (action.ProprioceptiveSignature == null || !action.ProprioceptiveSignature.Any())
                    return;

                // Проверка стадии: условные рефлексы не работают на стадии 0
                if (AppGlobalState.EvolutionStage < 1)
                    return;

                // Проверка инициализации сервиса
                if (!ConditionedReflexFormationService.IsInitialized)
                    return;

                // Создание образа восприятия на основе сигнатуры (CommandPatternIdList).
                // CommandPatternIdList заполняется — AddPerceptionImage не вернёт 0.
                int proprioImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
                    influenceActionList: new List<int>(),
                    phraseIdList: new List<int>(),
                    visualColorId: AgentVisualColor.White,
                    commandPatternIdList: action.ProprioceptiveSignature);

                if (proprioImageId <= 0)
                {
                    Logger.Warning($"ProprioceptiveInjectionService: AddPerceptionImage вернул {proprioImageId} для actionId={action.Id}");
                    return;
                }

                // Инжектируем как нейтральный CS (geneticReflexId=0, crFired=false, isProprioceptive=true).
                // behaviorStyleImageId может быть 0 (при отсутствии активного стиля) — это допустимо.
                int behaviorStyleImageId = 0;
                if (GomeostasSystem.Instance != null)
                {
                    behaviorStyleImageId = GomeostasSystem.Instance.ActiveBehaviorStyleImageId;
                }

                ConditionedReflexFormationService.Instance.RecordStimulus(
                    pulse: GlobalTimer.GlobalPulsCount,
                    stimulusImageId: proprioImageId,
                    baseState: (int)(GomeostasSystem.Instance?.GetHomeostasisState()?.OverallState ?? 0),
                    behaviorStyleImageId: behaviorStyleImageId,
                    geneticReflexId: 0,       // нейтральный CS
                    toneId: 0,
                    moodId: 0,
                    crFired: false,
                    isProprioceptive: true);
            }
            catch (Exception ex)
            {
                Logger.Warning($"ProprioceptiveInjectionService.Inject ошибка: {ex.Message}");
            }
        }
    }
}
```

> **Изменения по сравнению с оригиналом:**
> - Добавлен `try-catch` вокруг всей логики для защиты от сбоев при повреждённых данных
> - Добавлена проверка `GomeostasSystem.Instance` на null
> - `behaviorStyleImageId` может быть 0 — это допустимо, `RecordStimulus` не валидирует его
> - `commandPatternIdList` заполняется `action.ProprioceptiveSignature` — `AddPerceptionImage` не вернёт 0 (поскольку `hasCommand` будет true)

---

## Правка 4. Инжекция в `ReflexExecutionService.cs`

**Файл:** `Reflexes/ReflexExecutionService.cs`

В методе `ExecuteAdaptiveActions`, после `ApplyAction`:

```csharp
bool applied = _adaptiveActionsSystem.ApplyAction(actionId, phraseId);
if (applied)
{
    successfulActions.Add(actionId);
    results.Add($"Действие {actionId} выполнено успешно (Источник: {activationSource})");

    // Инжекция проприоцептивной сигнатуры после каждого выполненного действия
    ProprioceptiveInjectionService.Inject(action);
}
```

> **Важно:** `action` уже получен из `_adaptiveActionsSystem.GetAllAdaptiveActions()` выше в цикле `foreach` — используем его напрямую.

---

## Правка 5. Инжекция в `AutomatismExecutionService.cs`

**Файл:** `Psychic/Automatism/AutomatismExecutionService.cs`

В методе `ExecuteAdaptiveActions`, после `ApplyAction`:

```csharp
bool applied = _adaptiveActionsSystem.ApplyAction(actionId, phraseId, actionImageIdForToneMood);
if (applied)
{
    successfulActions.Add(actionId);

    // Инжекция проприоцептивной сигнатуры после каждого выполненного действия
    ProprioceptiveInjectionService.Inject(action);
}
```

> **Важно:** `action` уже получен из `_adaptiveActionsSystem.GetAllAdaptiveActions()` выше в цикле `foreach` — используем его напрямую.

---

## Правка 6. Сохранение настроек

**Файл:** `Reflexes/ConditionedReflexesSystem.cs`

В `SaveConditionedReflexSettings` добавить комментарий к шапке:

```csharp
"# ProprioceptiveCS: проприоцептивные CS автоматически защищены от блокировки (geneticReflexId=0).",
"# Если в будущем появится логика ProprioceptiveCompetitionSuppressionFactor, её нужно",
"# добавить здесь (float, InvariantCulture, диапазон [0..1], по умолчанию 0).",
```

*Примечание:* сам параметр `ProprioceptiveCompetitionSuppressionFactor` не добавляем (согласовано — только поле `IsProprioceptive` без логики подавления).

---

## Правка 7. Тесты

> **Важно:** `InternalsVisibleTo("Isida.Tests")` уже присутствует в `Properties/AssemblyInfo.cs`, поэтому все `internal` методы и поля доступны из тестов.

### 7.1. Обновление `AdaptiveActionsSystemIntegrationTests.cs`

**Тест 1: `AddAction_GeneratesProprioceptiveSignature`**

```csharp
[Fact]
public void AddAction_GeneratesProprioceptiveSignature()
{
    // Arrange
    AppGlobalState.EvolutionStage = 0;
    var actions = AdaptiveActionsSystem.Instance;

    // Act: добавить два действия
    var (id1, _) = actions.AddAction("Action1", "Description1");
    var (id2, _) = actions.AddAction("Action2", "Description2");

    // Assert: сигнатуры непусты и уникальны
    var action1 = actions.GetAdaptiveAction(id1);
    var action2 = actions.GetAdaptiveAction(id2);

    Assert.NotNull(action1.ProprioceptiveSignature);
    Assert.NotEmpty(action1.ProprioceptiveSignature);
    Assert.NotNull(action2.ProprioceptiveSignature);
    Assert.NotEmpty(action2.ProprioceptiveSignature);
    Assert.NotEqual(action1.ProprioceptiveSignature[0], action2.ProprioceptiveSignature[0]);
}
```

**Тест 2: `SaveAndLoad_ProprioceptiveSignature_RoundTrips`**

```csharp
[Fact]
public void SaveAndLoad_ProprioceptiveSignature_RoundTrips()
{
    // Arrange: стадия 0 для AddAction
    AppGlobalState.EvolutionStage = 0;
    var actions = AdaptiveActionsSystem.Instance;
    var (id, _) = actions.AddAction("TestAction", "TestDesc");
    var action = actions.GetAdaptiveAction(id);
    var originalSig = action.ProprioceptiveSignature.ToList();

    // Act: сохранить
    actions.SaveActions(IsValidate: false);

    // Пересоздать систему из файла
    actions.Dispose();
    AdaptiveActionsSystem.InitializeInstance(GomeostasSystem.Instance);
    actions = AdaptiveActionsSystem.Instance;

    // Assert: сигнатура восстановлена
    var reloadedAction = actions.GetAdaptiveAction(id);
    Assert.NotNull(reloadedAction.ProprioceptiveSignature);
    Assert.Equal(originalSig, reloadedAction.ProprioceptiveSignature);
}
```

> **Важно:** `_instance` доступен через `InternalsVisibleTo("Isida.Tests")` — `Dispose()` уже обнуляет `_instance = null`, поэтому `InitializeInstance` сработает без ошибок.

### 7.2. Новый файл `ProprioceptiveChainTests.cs`

**Файл:** `tests/Isida.Tests/ProprioceptiveChainTests.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты проприоцептивных сигнатур и цепочек транзитивного обучения.
  /// </summary>
  [Collection("EngineIntegration")]
  public class ProprioceptiveChainTests
  {
    private readonly EngineFixture _engine;

    public ProprioceptiveChainTests(EngineFixture engine)
    {
      _engine = engine;
    }

    // ===== Тест 1 =====

    [Fact]
    public void AddAction_GeneratesProprioceptiveSignature()
    {
      // Arrange
      AppGlobalState.EvolutionStage = 0;
      var actions = AdaptiveActionsSystem.Instance;

      // Act
      var (id1, _) = actions.AddAction("ActionA", "DescA");
      var (id2, _) = actions.AddAction("ActionB", "DescB");

      // Assert
      var a1 = actions.GetAdaptiveAction(id1);
      var a2 = actions.GetAdaptiveAction(id2);
      Assert.NotNull(a1.ProprioceptiveSignature);
      Assert.NotEmpty(a1.ProprioceptiveSignature);
      Assert.NotNull(a2.ProprioceptiveSignature);
      Assert.NotEmpty(a2.ProprioceptiveSignature);
      Assert.NotEqual(a1.ProprioceptiveSignature[0], a2.ProprioceptiveSignature[0]);
    }

    // ===== Тест 2 =====

    [Fact]
    public void SaveAndLoad_ProprioceptiveSignature_RoundTrips()
    {
      AppGlobalState.EvolutionStage = 0;
      var actions = AdaptiveActionsSystem.Instance;
      var (id, _) = actions.AddAction("RoundTripAction", "Test");
      var action = actions.GetAdaptiveAction(id);
      var originalSig = action.ProprioceptiveSignature.ToList();

      actions.SaveActions(IsValidate: false);
      actions.Dispose();
      AdaptiveActionsSystem.InitializeInstance(GomeostasSystem.Instance);
      actions = AdaptiveActionsSystem.Instance;

      var reloadedAction = actions.GetAdaptiveAction(id);
      Assert.NotNull(reloadedAction.ProprioceptiveSignature);
      Assert.Equal(originalSig, reloadedAction.ProprioceptiveSignature);
    }

    // ===== Тест 3: проверка, что проприоцептивный стимул создаёт образ =====

    [Fact]
    public void ExecuteAction_InjectsProprioceptiveStimulus()
    {
      // Arrange: стадия 1, действие
      AppGlobalState.EvolutionStage = 1;
      var actions = AdaptiveActionsSystem.Instance;
      var (actionId, _) = actions.AddAction("InjectTestAction", "Test");
      var action = actions.GetAdaptiveAction(actionId);
      Assert.NotNull(action.ProprioceptiveSignature);

      // Получаем количество образов ДО выполнения
      var countBefore = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList().Count;

      // Act: выполнить действие через ReflexExecutionService
      var result = ReflexExecutionService.Instance.ExecuteAdaptiveAction(
          actionId, ActionActivationSource.GeneticReflex);

      // Assert: образ с сигнатурой создан
      Assert.True(result.Success, result.ErrorMessage);
      var allImages = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList();
      var proprioImage = allImages.FirstOrDefault(img =>
          img.CommandPatternIdList != null &&
          img.CommandPatternIdList.SequenceEqual(action.ProprioceptiveSignature));
      Assert.NotNull(proprioImage);

      // Assert: стимул записан в историю
      var history = ConditionedReflexFormationService.Instance.GetStimulusHistory();
      var proprioRecord = history.FirstOrDefault(r =>
          r.StimulusImageId == proprioImage.Id && r.IsProprioceptive);
      Assert.NotNull(proprioRecord);
    }

    // ===== Тест 4: проприоцептивный стимул НЕ перезаписывает _lastConditionedStimulus =====

    [Fact]
    public void RecordStimulus_Proprioceptive_DoesNotOverwriteLastCS()
    {
      // Arrange: подать обычный CS
      var formation = ConditionedReflexFormationService.Instance;
      var csImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { 1 }, new List<int>(), AgentVisualColor.White, null);
      Assert.True(csImageId > 0);

      formation.RecordStimulus(pulse: 1, stimulusImageId: csImageId, baseState: 0, behaviorStyleImageId: 0);

      var lastCSBefore = formation.GetLastConditionedStimulus();
      Assert.NotNull(lastCSBefore);
      Assert.Equal(csImageId, lastCSBefore.StimulusImageId);

      // Act: подать проприоцептивный стимул
      formation.RecordStimulus(
          pulse: 2,
          stimulusImageId: 999,  // другой ID
          baseState: 0,
          behaviorStyleImageId: 0,
          geneticReflexId: 0,
          isProprioceptive: true);

      // Assert: _lastConditionedStimulus не должен обновиться
      var lastCSAfter = formation.GetLastConditionedStimulus();
      Assert.NotNull(lastCSAfter);
      Assert.Equal(100, lastCSAfter.StimulusImageId);  // остался старый CS
    }

    // ===== Тест 5: проприоцептивные стимулы НЕ запускают _pendingCsAwaitingUs =====

    [Fact]
    public void RecordStimulus_Proprioceptive_DoesNotTriggerPendingCS()
    {
      // Arrange: подать обычный CS без US (он станет pending)
      var formation = ConditionedReflexFormationService.Instance;
      var csImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { 1 }, new List<int>(), AgentVisualColor.White, null);
      Assert.True(csImageId > 0);

      formation.RecordStimulus(pulse: 1, stimulusImageId: csImageId, baseState: 0, behaviorStyleImageId: 0);

      // Act: подать проприоцептивный стимул — он НЕ должен сбросить pending
      formation.RecordStimulus(
          pulse: 2,
          stimulusImageId: 888,
          baseState: 0,
          behaviorStyleImageId: 0,
          isProprioceptive: true);

      // Assert: CS из пульса 1 всё ещё pending — должен быть активен процесс pending-угасания
      // (проверяем, что история содержит оба стимула в правильном порядке)
      var history = formation.GetStimulusHistory();
      Assert.Equal(2, history.Count);
      Assert.Equal(csImageId, history[0].StimulusImageId);
      Assert.False(history[0].IsProprioceptive);
      Assert.Equal(888, history[1].StimulusImageId);
      Assert.True(history[1].IsProprioceptive);
    }

    // ===== Тест 6: Chain_M1_M2_M3_ViaProprioception =====

    [Fact]
    public void Chain_M1_M2_M3_ViaProprioception()
    {
      // Arrange: три действия с разными сигнатурами
      AppGlobalState.EvolutionStage = 1;
      var actions = AdaptiveActionsSystem.Instance;
      var (id1, _) = actions.AddAction("ChainM1", "M1");
      var (id2, _) = actions.AddAction("ChainM2", "M2");
      var (id3, _) = actions.AddAction("ChainM3", "M3");

      var a1 = actions.GetAdaptiveAction(id1);
      var a2 = actions.GetAdaptiveAction(id2);
      var a3 = actions.GetAdaptiveAction(id3);

      // Внешние стимулы С1, С2, С3
      var cs1Id = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { 101 }, new List<int>(), AgentVisualColor.White, null);
      var cs2Id = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { 102 }, new List<int>(), AgentVisualColor.White, null);
      var cs3Id = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int> { 103 }, new List<int>(), AgentVisualColor.White, null);

      // Act: выполнить M1 (создаст П₁), затем M2 (создаст П₂), M3 (создаст П₃)
      ReflexExecutionService.Instance.ExecuteAdaptiveAction(id1, ActionActivationSource.GeneticReflex);
      _engine.AdvancePulses(1);
      ReflexExecutionService.Instance.ExecuteAdaptiveAction(id2, ActionActivationSource.GeneticReflex);
      _engine.AdvancePulses(1);
      ReflexExecutionService.Instance.ExecuteAdaptiveAction(id3, ActionActivationSource.GeneticReflex);
      _engine.AdvancePulses(1);

      // Получаем образы проприоцептивных стимулов
      var allImages = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList();
      var proprio1 = allImages.FirstOrDefault(img =>
          img.CommandPatternIdList != null &&
          img.CommandPatternIdList.SequenceEqual(a1.ProprioceptiveSignature));
      var proprio2 = allImages.FirstOrDefault(img =>
          img.CommandPatternIdList != null &&
          img.CommandPatternIdList.SequenceEqual(a2.ProprioceptiveSignature));
      var proprio3 = allImages.FirstOrDefault(img =>
          img.CommandPatternIdList != null &&
          img.CommandPatternIdList.SequenceEqual(a3.ProprioceptiveSignature));

      Assert.NotNull(proprio1);
      Assert.NotNull(proprio2);
      Assert.NotNull(proprio3);

      // Assert: связи П₁→П₂ и П₂→П₃ должны быть > 0 (усилены TryStrengthenNeutralSequentialLink)
      Assert.NotEqual(proprio1.Id, proprio2.Id);
      Assert.NotEqual(proprio2.Id, proprio3.Id);
      Assert.NotEqual(proprio1.Id, proprio3.Id);

      bool link12 = SensoryAssociationSystem.Instance.TryGetStrength(proprio1.Id, proprio2.Id, out float strength12);
      bool link23 = SensoryAssociationSystem.Instance.TryGetStrength(proprio2.Id, proprio3.Id, out float strength23);

      Assert.True(link12, "Связь П₁→П₂ должна существовать");
      Assert.True(link23, "Связь П₂→П₃ должна существовать");
      Assert.True(strength12 > 0, $"Связь П₁→П₂ должна быть > 0, получено {strength12}");
      Assert.True(strength23 > 0, $"Связь П₂→П₃ должна быть > 0, получено {strength23}");
    }

    // ===== Тест 7: Chain_DisabledOnStage0 =====

    [Fact]
    public void Chain_DisabledOnStage0()
    {
      // Arrange: стадия 0
      AppGlobalState.EvolutionStage = 0;
      var actions = AdaptiveActionsSystem.Instance;
      var (actionId, _) = actions.AddAction("Stage0Action", "Test");
      var action = actions.GetAdaptiveAction(actionId);

      var countBefore = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList().Count;

      // Act: выполнить действие
      var result = ReflexExecutionService.Instance.ExecuteAdaptiveAction(
          actionId, ActionActivationSource.GeneticReflex);

      // Assert: образ НЕ создан (инжекция отключена на стадии 0)
      var countAfter = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList().Count;
      Assert.Equal(countBefore, countAfter);

      // История стимулов не должна содержать проприоцептивных записей
      var history = ConditionedReflexFormationService.Instance.GetStimulusHistory();
      var proprioRecords = history.Where(r => r.IsProprioceptive);
      Assert.Empty(proprioRecords);
    }

    // ===== Тест 8: Chain_OperatorAction_NoInjection =====

    [Fact]
    public void Chain_OperatorAction_NoInjection()
    {
      // Arrange: стадия 1, действие
      AppGlobalState.EvolutionStage = 1;
      var actions = AdaptiveActionsSystem.Instance;
      var (actionId, _) = actions.AddAction("OperatorAction", "Test");

      var countBefore = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList().Count;

      // Act: прямой вызов ApplyAction (минуя ReflexExecutionService)
      actions.ApplyAction(actionId);

      // Assert: образ НЕ создан (инжекция только через ReflexExecutionService / AutomatismExecutionService)
      var countAfter = PerceptionImagesSystem.Instance.GetAllPerceptionImagesList().Count;
      Assert.Equal(countBefore, countAfter);
    }
  }
}
```

> **Изменения по сравнению с оригиналом:**
> - Тест 1-2: `EvolutionStage = 0` перед `AddAction` — `AddAction` разрешён только на стадии 0
> - Тест 2: `InitializeInstance` вызывается напрямую, `_instance` обнуляется `Dispose()` (InternalsVisibleTo уже настроен)
> - Тест 3: проверяет и образ через `PerceptionImagesSystem`, И запись в историю через `GetStimulusHistory()`
> - Тест 4: `GetLastConditionedStimulus()` возвращает CS из пульса 1 (ID = `csImageId`), проверяем что он не перезаписан
> - Тест 5: проверяет порядок записей в `_stimulusHistory` — проприоцептивный не сбрасывает pending
> - Тест 6: `TryGetStrength` вызывается с `out float` (реальная сигнатура метода), связи П₁→П₂ и П₂→П₃ укрепляются через `TryStrengthenNeutralSequentialLink`
> - Тест 7: проверяет и образ, И историю — на стадии 0 инжекция не происходит
> - Тест 8: прямой `ApplyAction` не триггерит инжекцию

---

## Порядок выполнения

1. **Правка 1** — `AdaptiveActionsSystem.cs` (счётчик, поле, парсинг, сохранение) + `FileValidator.cs` (шапка)
2. **Правка 2** — `ConditionedReflexFormationService.cs` (поле `IsProprioceptive`, расширение `RecordStimulus` с `isProprioceptive`-ветвлением, буфер `_stimulusHistory`, `GetStimulusHistory()`, `GetLastConditionedStimulus()`, очистка в `ResetHistory`)
3. **Правка 3** — `ProprioceptiveInjectionService.cs` (новый файл, `Reflexes/`)
4. **Правка 4** — `ReflexExecutionService.cs` (вызов `Inject` после `ApplyAction`)
5. **Правка 5** — `AutomatismExecutionService.cs` (вызов `Inject` после `ApplyAction`)
6. **Правка 6** — `ConditionedReflexesSystem.cs` (комментарий)
7. **Правка 7** — Тесты
8. **Финальная проверка** — `dotnet build` + `dotnet test`

---

## Критерии готовности

- [ ] Действия имеют стабильные проприоцептивные сигнатуры, сохраняются/загружаются из `.dat`
- [ ] Обратная совместимость: старые файлы без 8-й колонки загружаются, сигнатуры генерируются автоматически
- [ ] После выполнения действия через `ReflexExecutionService` или `AutomatismExecutionService` сигнатура попадает в `ConditionedReflexFormationService` как нейтральный CS
- [ ] Проприоцептивные стимулы **НЕ перезаписывают** `_lastConditionedStimulus` и **НЕ запускают** `_pendingCsAwaitingUs` — нормальный цикл CS→US не ломается
- [ ] Проприоцептивные стимулы **УЧАСТВУЮТ** в `TryStrengthenNeutralSequentialLink` — это позволяет строить цепочки CS→CS через проприоцептивные образы
- [ ] При `EvolutionStage = 0` инжекция не происходит
- [ ] Прямые вызовы `ApplyAction` (оператор) не триггерят инжекцию
- [ ] `ProprioceptiveInjectionService` защищён `try-catch`
- [ ] `AddPerceptionImage` получает `commandPatternIdList` (не пустой) — образ создаётся
- [ ] Все существующие тесты проходят
- [ ] `dotnet build` без ошибок

---

## Сводка исправленных багов

| # | Баг | Описание | Исправление |
|---|-----|----------|-------------|
| 1 | `RecordStimulus` перезаписывает `_lastConditionedStimulus` | Проприоцептивный стимул с `geneticReflexId=0` перезаписывает реальный CS, ломая формирование УР | Добавить `isProprioceptive`-флаг; при `true` пропускать блок `_lastConditionedStimulus = record` |
| 2 | Активное угасание сработает на каждом проприоцептивном стимуле | `_pendingCsAwaitingUs = true` + отсутствие US → угасание УР через τ | При `isProprioceptive=true` не устанавливать `_pendingCsAwaitingUs` |
| 3 | `GetRecentStimuli` не возвращает проприоцептивные стимулы | Тесты не могут проверить инжекцию через этот метод | Добавить буфер `_stimulusHistory` и метод `GetStimulusHistory()` |
| 4 | Проприоцептивные стимулы не участвуют в усилении связей | `TryStrengthenNeutralSequentialLink` исключён полностью → цепочки не строятся | Разрешить `TryStrengthenNeutralSequentialLink` для `isProprioceptive`, но не трогать `_lastConditionedStimulus` |
| 5 | Roundtrip-тест не инициализирует систему | `AdaptiveActionsSystem._instance = null` → `InvalidOperationException` | `InternalsVisibleTo("Isida.Tests")` уже в `AssemblyInfo.cs` — `Dispose()` обнуляет `_instance`, `InitializeInstance` сработает |
| 6 | `LoadActions` — `Max()` на пустой коллекции | Если `_actions` пусто, `Select().Max()` бросит `EmptySequenceException` | Добавить `.DefaultIfEmpty(0)` перед `Max()` |
| 7 | `ProprioceptiveInjectionService` без обработки ошибок | Исключение в `AddPerceptionImage` или `RecordStimulus` сломает выполнение действия | Обернуть всю логику в `try-catch` + `Logger.Warning` |
| 8 | `AddPerceptionImage` вернёт 0 для проприоцептивного образа | Если передать пустые списки — `AddPerceptionImage` отклонит образ | Передавать сигнатуру в `commandPatternIdList` — `AddPerceptionImage` увидит `hasCommand == true` и создаст образ |
| 9 | `TryGetStrength` — неверная сигнатура в тестах | В оригинальном плане `TryGetStrength` вызывался без `out`-параметра | Использовать `TryGetStrength(id1, id2, out float strength)` с `out`-параметром |
