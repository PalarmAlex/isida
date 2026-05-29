# ISIDA Engine — Техническое описание архитектуры

> Версия движка: **V3.2** (сборка 2026.04.26)  
> Платформа: **.NET Framework 4.8**, C# class library  
> Корневое пространство имён: `isida`  
> Зависимости: Newtonsoft.Json 13.0.3, System.Text.Json 9.0.10  
> Документация: https://scorcher.ru/isida/iadaptive_agents_guide.php  

---

## 1. Назначение и концепция

ISIDA — библиотека для построения **автономных гомеостатических агентов («симбионтов»)** по теории МВАП (Мотивационно-Волевой Адаптивный Процесс). Аналог проекта [Beast](https://scorcher.ru/beast/index.php), переосмысленный в рамках теории адаптивного поведения.

Ключевая идея: **симбионт не исполняет команды оператора напрямую** — он поддерживает собственный гомеостаз и вырабатывает поведение через многоуровневую иерархию рефлексов, автоматизмов и мышления. Оператор может влиять на среду (подавать стимулы, менять параметры), но не управляет агентом непосредственно.

---

## 2. Иерархия уровней поведения

Поведение симбионта организовано в **5 уровней снизу вверх**:

```
┌──────────────────────────────────────────────┐
│  5. Мышление (ThinkingCyclesSystem)           │  stage ≥ 4
│     info-функции 1–24, стратегии             │
├──────────────────────────────────────────────┤
│  4. Автоматизмы (AutomatizmSystem)            │  stage ≥ 2
│     дерево контекстов, Belief=2 инвариант    │
├──────────────────────────────────────────────┤
│  3. Условные рефлексы (ConditionedReflexes)  │  stage ≥ 1
│     временна́я корреляция стимул→реакция     │
├──────────────────────────────────────────────┤
│  2. Генетические рефлексы (GeneticReflexes)  │  stage ≥ 0
│     врождённые, загружаются из файлов        │
├──────────────────────────────────────────────┤
│  1. Гомеостаз (GomeostasSystem)              │  всегда
│     параметры, зоны, дрейф, стили           │
└──────────────────────────────────────────────┘
```

Каждый уровень **дополняет**, но не заменяет нижележащие.

---

## 3. Стадии эволюции (`EvolutionStage`, 0–5)

`AppGlobalState.EvolutionStage` — единый gate-флаг, разблокирующий подсистемы:

| Стадия | Разблокируется |
|--------|---------------|
| **0** | Только генетические рефлексы; пульс не запускается |
| **1** | Запуск пульса, гомеостаз с дрейфом скорости |
| **2** | Психика: автоматизмы, дерево автоматизмов (pulse ≥ 4) |
| **3** | Mirror-автоматизмы, окна оценки оператора |
| **4** | Эпизодическая память, деревья понимания, циклы мышления |
| **5** | Расширенная стадия |

**Переход стадий** управляется `EvolutionStageService`:
- Прыжок вперёд более чем на 1 шаг заблокирован без `force=true`.
- Откат назад автоматически очищает данные всех промежуточных стадий через `ClearStageData`.
- Есть специальный метод `ClearStageDataOnlyForScenarioPreRun` — сброс автоматизмов без смены стадии (используется в тестовых сценариях).

---

## 4. Точка входа и инициализация (`IsidaEngine`)

### 4.1 `IsidaConfig`

Конфигурация, передаваемая в `IsidaEngine.Create(IsidaConfig)`:

| Поле | Назначение |
|------|-----------|
| `BaseDirectory` | Корень файлового хранилища (по умолчанию `%ProgramData%\ISIDA\`) |
| `RecognitionThreshold` | Порог распознавания стимулов (вербальный / командный каналы) |
| `WaitingPeriodForActionsVal` | Пульсов ожидания реакции оператора |
| `HomeostasisPulseSpeedDriftEnabled` | Включает дрейф скоростей параметров за пульс |
| `ThinkingCycleFadeConfig` | Затухание весов циклов мышления |
| `LogFormat` | None / Text / Html / All |

Статический метод `IsidaConfig.WithDefaultFolders()` генерирует конфиг с разумными значениями.

### 4.2 `IsidaContext`

Объект-контейнер, содержащий публичные ссылки на **все** подсистемы движка. Создаётся `IsidaEngine.Create` и живёт до вызова `Dispose()`.

Ключевые свойства контекста:

- `GlobalTimer` — пульс-таймер
- `GomeostasSystem`, `SensorySystem`, `AdaptiveActionsSystem`, `InfluenceActionSystem`
- `GeneticReflexesSystem`, `ConditionedReflexesSystem`, `ReflexesActivator`
- `AutomatizmSystem`, `AutomatizmTreeSystem`, `AutomatismExecutionService`
- `PsychicSystem`, `ThinkingCyclesSystem`, `EpisodicMemorySystem`
- `UnderstandingTreeSystem`, `ProblemTreeSystem`
- `EvolutionStageService`, `AgentSleepOrchestrator`
- `ResearchLogger`, `AppGlobalState`

Метод `CancelWaitingPeriodAndResetMirror()` — сброс ожидания реакции и зеркального диалога.

### 4.3 Порядок инициализации (36 шагов)

`IsidaEngine.Create` выполняет строго упорядоченные 36 шагов:

1. Логи, `FileValidator`
2. `InformationEnvironmentSystem`, `AgentSleepOrchestrator`
3. `GomeostasSystem`
4–5. `AdaptiveActionsSystem`, `InfluenceActionSystem` + образы влияния
6–7. `SensorySystem`
8–10. Генетические рефлексы, цепи, загрузчик файлов
11–12. Образы восприятия, условные рефлексы
13–16. Дерево рефлексов, исполнение, формирование, `ReflexesActivator`
17. `ResearchLogger`, привязка к `GlobalTimer`
18–24. Образы действий, дерево автоматизмов, деревья ситуаций/тем/целей/проблем/понимания/ментальных эпизодов, `AutomatizmSystem`, Broca/эмоции
25–27. Трекер результатов, эпизодическая память, сервис правил
28. `PsychicSystem` + зеркальный сервис
29–34. Генетика целей, загрузчики, исполнение автоматизмов, ориентировочный рефлекс, цепи
35. `ConditionedReflexToAutomatizmConverter`, `Stage2PrimitivesLoader`
36. `EvolutionStageService`, конфиг циклов мышления, `GlobalTimer.InitializeSystems`

**Двухпроходная инициализация** нужна для разрыва циклических зависимостей: часть систем создаётся на первом проходе, а зависимости внедряются на втором через `SetDependentSystems`.

`Dispose()` — обратный порядок, психика освобождается раньше гомеостаза.

---

## 5. Пульс-цикл (`GlobalTimer`)

### 5.1 Тактирование

`GlobalTimer` — синхронный таймер с периодом ~1 секунда (реальное время). Поддерживает **ускорение до 20×** через `SetPulseWallClockAcceleration` (ускорение использует `volatile int` для корректной работы с WPF Dispatcher без дедлоков).

Монотонный счётчик: `GlobalTimer.GlobalPulsCount`.  
Старт требует `EvolutionStage >= 1`; `Stop()` безусловен.

### 5.2 Порядок шагов одного пульса (`ProcessAgentPulse`)

```
1.  ThinkingThemePulseResolver.ResolveAtPulseStart(pulse)
2.  OnPulseBeforeGomeostasis  [хук хоста: сценарные стимулы]
3.  GomeostasSystem.UpdateStateOnly()
         → параметры → стили → опасность/настроение → IE
4.  Проверка смерти → Stop() если мёртв
5.  OnPulseAfterGomeostasisBeforePsychic  [хук хоста: стимулы оператора]
6.  PsychicSystem.ProcessPsychicPulse(styles, pulse, sleepingType)
7.  ConditionedReflexesSystem.UpdateAgentLifetime()  [если не спит глубоко]
8.  ReflexesActivator.ProcessReflexPulse()  [если не в sleep-фазе]
9.  ResearchLogger.FlushBufferedAgentRowToMemoryNow()
10. Адаптивные действия cleanup; ThinkingThemePulseResolver.RecordEndOfPulseAgentEvents
11. OnPulseCompleted  [хук хоста]
    finally: ReflexesActivator.ResetStates
```

### 5.3 События (хуки для хост-приложения)

| Событие | Момент | Типичное использование |
|---------|--------|----------------------|
| `OnPulseBeforeGomeostasis` | До обновления параметров | Подача физических стимулов |
| `OnPulseAfterGomeostasisBeforePsychic` | После гомеостаза, до психики | Вербальные стимулы оператора |
| `OnPulseCompleted` | Конец пульса | Обновление UI |

---

## 6. Гомеостаз (`GomeostasSystem`)

Самый объёмный модуль (~3762 строки).

### 6.1 Параметры

Каждый параметр (`ParameterData`, `INotifyPropertyChanged`) имеет:
- Текущее значение, скорость изменения (дрейф)
- Зоны: **критическая, плохая, нормальная, хорошая**
- Связанный **стиль поведения** (`BehaviorStyle`)

`HomeostasisPulseSpeedDriftEnabled` в конфиге включает изменение скорости на каждом пульсе (адаптивный дрейф).

### 6.2 Стили поведения (`BehaviorStyle`)

Каждый стиль:
- Условие активации (`StyleActivationCondition`) — диапазон параметра
- Антагонизмы (`StyleAntagonism`) — взаимоисключающие стили
- Комбинации (`StyleCombinationsManager`) — образы из нескольких активных стилей одновременно

Активные стили публикуются в `AppGlobalState` и используются как ключ в дереве автоматизмов.

### 6.3 Итоговое состояние

`HomeostasisCalculator.CalculateAgentState` → `HomeostasisOverallState` ∈ {Bad=-1, Normal=0, Well=1}

На каждом пульсе результат вместе с **опасностью** и **настроением** записывается в `InformationEnvironmentSystem` (текущий кадр `InformationEnvironment`).

---

## 7. Сенсорная система (`SensorySystem`)

Два канала:

| Канал | Класс | Стимулы |
|-------|-------|---------|
| **Вербальный** | `VerbalSensorChannel` | Фразы оператора |
| **Командный** | `CommandSensorChannel` | Командные сигналы |

Каждый канал: `RecognitionThreshold`, режим `AuthoritativeMode` (подача стимула сразу минует порог), дерево образов `SensorTree`.

Стимулы поступают в `PsychicSystem.SensorActivation` → ориентировочный рефлекс → дерево автоматизмов → зеркальный диалог → период ожидания.

---

## 8. Рефлексы

### 8.1 Генетические рефлексы (`GeneticReflexesSystem`)

- Врождённые, загружаются из файлов через `GeneticReflexFileLoader`
- Каждый `GeneticReflex`: `ID`, `TriggerId` (стимул-US), связанные действия
- Организованы в `ReflexTreeSystem` — дерево активации

### 8.2 Условные рефлексы (`ConditionedReflexesSystem`)

- Формируются динамически через `ConditionedReflexFormationService` на основе временны́х корреляций CS→US
- Каждый `ConditionedReflex`: `ID`, время жизни в пульсах, режим активации
- Обновление времени жизни: `UpdateAgentLifetime()` каждый пульс

### 8.3 Цепи рефлексов (`ReflexChainsSystem`)

Многошаговые последовательности рефлексов; состояние цепи хранится в `AppGlobalState.IsReflexChainActive`.

### 8.4 `ReflexesActivator`

Центральный координатор (~1742 строки):
- Подписывается на события `InfluenceActionSystem` (триггер/фраза)
- `ActiveFromAction(US)` и `ActiveFromPhrase(CS)` — пути активации
- `ProcessReflexPulse` — пульсовая обработка всех ожидающих рефлексов
- `ResetStates` — сброс в конце каждого пульса (в `finally`)

Переход условного рефлекса в автоматизм: `ConditionedReflexToAutomatizmConverter` (stage 1→2).

---

## 9. Психика (`PsychicSystem`)

Центральный координатор психической жизни (~2242 строки). Вызывается раз в пульс из `GlobalTimer`.

### 9.1 `ProcessPsychicPulse` (главный метод)

```
stage ≥ 2:
  if stage < 4: clear thinking cycles
  обработка сна (AgentSleepOrchestrator)
  отложенная оценка оператора (evaluation window)
  циклы мышления (ThinkingCyclesSystem) → ThinkingDecision
  исполнение автоматизмов / зеркала по решению
```

### 9.2 `SensorActivation` (реакция на стимул от оператора)

```
стимул → ориентировочный рефлекс (OR1/OR2)
       → активация дерева автоматизмов (AutomatizmTreeActivation)
       → эпизодические правила (EpisodicMemoryRulesService)
       → зеркальный диалог (MirrorAutomatizmService)
       → период ожидания (waiting period)
```

### 9.3 `AutomatizmTreeActivation`

Навигация по дереву автоматизмов с минимального пульса 4:
- Ключ поиска: текущий базовый ID + активные стили + триггер
- Выбор автоматизма в узле: по `Belief` (приоритет 2→1→0) и `Usefulness` (-10..10)

---

## 10. Дерево автоматизмов (`AutomatizmSystem` + `AutomatizmTreeSystem`)

### 10.1 Структура `Automatizm`

| Поле | Описание |
|------|---------|
| `ID` | Уникальный идентификатор |
| `BranchID` | Ветка дерева |
| `Usefulness` | Полезность: -10..10 (обновляется трекером результатов) |
| `ActionsImageID` | Образ действий для исполнения |
| `NextID` | Следующий автоматизм в цепи |
| `Belief` | 0 / 1 / **2** — степень убеждённости |
| `Count` | Счётчик активаций |
| `Energy` | Энергетический ресурс |
| `GomeoIdSuccesArr` | Гомеостатические ID успеха |

### 10.2 Инвариант Belief=2

**Не более одного автоматизма с `Belief=2` на одну ветку (`BranchID`)** — «канонический» автоматизм ветки.

Устанавливается только через `AutomatizmSystem.SetAutomatizmBelief(id, 2)`, который проверяет кэш и автоматически понижает предыдущий Belief=2 той же ветки.

### 10.3 `AutomatizmNode` — узел дерева

Дерево индексировано контекстом:
```
[BaseStateId] → [StyleCombinationId] → [TriggerId] → [SituationId] → ...
```
Каждый узел хранит список автоматизмов, доступных в данном контексте.

### 10.4 Трекер результатов (`AutomatismResultTracker`)

После исполнения автоматизма:
- Отслеживает изменения гомеостаза в окне оценки
- Обновляет `Usefulness` (+/-) по результату
- Запускает хуки к `EpisodicMemoryRulesService`
- Управляет блокировками нежелательных автоматизмов

---

## 11. Информационная среда (`InformationEnvironmentSystem`)

Структура `InformationEnvironment` — кадр текущего состояния агента:

| Поле | Содержание |
|------|-----------|
| `Mood` / `PsyMood` | Настроение (физическое / психическое) |
| `Danger` | Уровень опасности |
| `VeryActualSituation` | Текущая актуальная ситуация |
| `ActionsImageID` | Выбранный образ действий |
| `AnswerImageID` | Образ ответа |
| `IsWaitingPeriod` | Флаг периода ожидания реакции оператора |
| `ExtremImportanceObjectID` | ID объекта экстремальной важности |
| `ActualEpisodicMemoryID` | Актуальный эпизод |
| `DominantaID` | Доминирующая потребность |
| `NeedThinkingAboutAutomatizm` | Флаг: нужно мышление об автоматизме |

`InformationEnvironmentSystem` — singleton; хранит краткосрочный список кадров; буфер очищается во время сна.

---

## 12. Ориентировочный рефлекс (`OrientationReflexSystem`)

Два уровня:

- **OR1** — реакция на новизну стимула (неизвестный стимул)
- **OR2** — реакция на несоответствие ожидаемому (рассогласование)

Активируется из `PsychicSystem.SensorActivation` до обращения к дереву автоматизмов.

---

## 13. Сон (`AgentSleepOrchestrator`)

Фазы сна:

| Фаза | Поведение |
|------|----------|
| Бодрствование | Полный пульс |
| Поверхностный сон | Гомеостаз обновляется; рефлексы не активируются |
| Глубокий сон | Гомеостаз + консолидация эпизодической памяти |
| Сновидения | `MentalEpisodicTreeSystem` переработка ментальных эпизодов |

Оркестратор управляется `ProcessPsychicPulse`; переход в сон при соответствующем состоянии гомеостаза или команде.

---

## 14. Деревья понимания (stage ≥ 4)

### 14.1 `UnderstandingTreeSystem` — 4 уровня понимания ситуации

```
Уровень 1: SituationTypeSystem  — тип ситуации
Уровень 2: SituationImageSystem — образ ситуации
Уровень 3: ThemeImageSystem     — тема мышления
Уровень 4: PurposeImageSystem   — цель
```

Метод `ActivateSituation` продвигает понимание на следующий уровень при наличии опыта.

### 14.2 `ProblemTreeSystem` — доминирующая проблема

Отслеживает текущий узел проблемы (`ProblemTreeNode`) как ключ для циклов мышления.

### 14.3 `ThinkingThemePulseResolver`

В начале каждого пульса определяет `ResolvedThinkingThemeTypeId` по буферу событий **предыдущего** пульса (stimulus events + influence actions + silence-threshold). Результат используется `ThinkingCyclesSystem` как тема нового цикла.

---

## 15. Эпизодическая память (`EpisodicMemorySystem`, stage ≥ 4)

### 15.1 Структура эпизода

Ключ: `(BaseStateId, EmotionId, UnderstandingNodeId, ProblemNodeId)`

Каждый `EpisodicMemoryNode`:
- ID триггера, ID действия
- Эффект на гомеостаз (прямой + через стимулы)
- Флаг прерывания цепи (`SetInterruption`)

### 15.2 Персистентность

`EpisodicMemoryStorage` — `.dat` файлы под `BaseDirectory`.  
`EpisodicMemorySearch` — поиск по ключу.

### 15.3 Правила

`EpisodicMemoryRules` + `EpisodicMemoryRulesService` — применяют правила учителя/прямые правила из эпизодов к выбору действий.

### 15.4 Ментальные эпизоды

`MentalEpisodicTreeSystem` — цепи информационных функций (мышление), переработанные во сне.

---

## 16. Циклы мышления (`ThinkingCyclesSystem`, stage ≥ 4)

### 16.1 Структура

Один **главный цикл** + N **фоновых циклов**.

Каждый `ThinkingCycleInfo`:
- Вес (decay каждый пульс)
- Тема (`ThemeImageId`), цель (`PurposeImageId`), проблема (`ProblemNodeId`)
- Состояние: активный / ожидание оценки / завершён
- Лог решений

### 16.2 Стратегии (`IThinkingStrategy`)

```csharp
public interface IThinkingStrategy {
    string Id { get; }
    ThinkingDecision TryStep(ThinkingStrategyContext ctx);
}
```

Зарегистрированные стратегии (5):

| Стратегия | Логика |
|-----------|-------|
| `InfoFunctionsStrategy` | Основная: циклы по info-функциям 1–24 с опытом и ментальными сессиями |
| `EpisodicRuleStrategy` | Применение правил из эпизодической памяти |
| `RandomBranchAutomatizmStrategy` | Случайный автоматизм из доступной ветки |
| `AskOperatorStrategy` | Запрос подсказки у оператора |
| *(пользовательская)* | Регистрируется через `RegisterStrategy` |

`ThinkingDecision` — результат шага:

```csharp
public sealed class ThinkingDecision {
    public Automatizm AutomatizmToExecute { get; set; }
    public int ActionsImageIdToAutomatize { get; set; }
    public bool RequestParrotFromOperator { get; set; }
    public bool CloseCycleImmediately { get; set; }
    public bool HasConcreteProposal => ...;
}
```

### 16.3 Информационные функции (1–24)

`InfoFunctionsCatalog` — каталог из 24 info-функций, описывающих когнитивные операции агента (анализ ситуации, поиск аналогий, проверка гипотез и т.д.).

`InfoFunctionsStrategy` итерирует по функциям с учётом `ThinkingExperienceMemory` (рекомендации, ключованные по `(ProblemId, ThemeId, PurposeId)`) и `MentalAutomatizmSession` (буфер текущей ментальной сессии).

### 16.4 Оценка оператора

**Ключевое ограничение**: оценка оператора происходит только в `ProcessPsychicPulse`, но НЕ в `SensorActivation` — чтобы дельты гомеостаза после ввода оператора были уже учтены.

Окно оценки управляется через `AppGlobalState.StartWaitingForOperatorEvaluation` / `IsEvaluationTime` / `IsOperatorResponseWithinWaitingWindow`.

---

## 17. Зеркальный диалог (`MirrorAutomatizmService`, stage ≥ 3)

Echo и shift автоматизмы: агент «зеркалит» паттерны оператора и может их сдвигать.

Сброс: `IsidaContext.CancelWaitingPeriodAndResetMirror()`.

---

## 18. Действия агента

### 18.1 Адаптивные действия (`AdaptiveActionsSystem`)

Выходные реакции агента. Каждое `AdaptiveAction` связано с образом действий (`ActionsImageID`). Система хранит очередь и выполняет cleanup в конце пульса.

### 18.2 Действия влияния (`InfluenceActionSystem`)

Входные воздействия от оператора → `ParameterInfluence` (дельты параметров гомеостаза). Генерируют события, на которые подписан `ReflexesActivator`.

### 18.3 Образы действий

| Система | Содержание |
|---------|-----------|
| `ActionsImagesSystem` | Образы адаптивных действий агента |
| `InfluenceActionImagesSystem` | Образы воздействий оператора |
| `VerbalBrocaImagesSystem` | Вербальные выходные фразы |
| `CommandBrocaImagesSystem` | Командные выходные сигналы |
| `EmotionsImageSystem` | Эмоциональные образы (из комбинаций стилей) |

---

## 19. Глобальное состояние (`AppGlobalState`)

Thread-safe (собственный `ReaderWriterLockSlim`) хранилище runtime-данных. Ключевые группы:

**Автоматизмы:**
- `CurrentActiveAutomatizmId`, `UpdateAutomatizmInfo`, `ResetAutomatizmInfo`

**Рефлексы:**
- `DetectedReflexNodeId`, `UpdateGlobalGeneticReflexesActions`, `IsReflexChainActive`

**Оценка оператора:**
- `StartWaitingForOperatorEvaluation`, `IsEvaluationTime`, `IsOperatorResponseWithinWaitingWindow`
- Снапшоты параметров до/после окна оценки

**Мышление:**
- `UpdateMainThinkingCycleSnapshot`, `GetMainThinkingCycleSnapshot`
- `UpdatePublishedBackgroundThinkingCycles`

**Стимулы (для темы):**
- `RecordStimulusAgentEvent`, `RecordStimulusInfluenceActions`
- `TakeStimulusSnapshotForThemeResolution` — снимок берётся в конце пульса и используется в начале следующего

**LLM-интеграция:**
- `AgentPropertiesPromptContent` — глобальное свойство, содержащее промпт-описание текущего состояния агента для передачи в LLM

---

## 20. Потокобезопасность

Основные подходы:

| Механизм | Где используется |
|----------|-----------------|
| `ReaderWriterLockSlim` | `AppGlobalState`, большинство крупных подсистем |
| `volatile int` | Счётчик ускорения пульса в `GlobalTimer` (избегает дедлока с WPF Dispatcher) |
| Двухпроходная инициализация | Разрыв циклических зависимостей без взаимных блокировок |

Все публичные методы подсистем являются thread-safe относительно пульс-потока и UI-потока хоста.

---

## 21. Персистентность данных

Всё хранится в файловой системе под `IsidaConfig.BaseDirectory` (`%ProgramData%\ISIDA\` по умолчанию).

Структура каталогов:

```
BaseDirectory/
├── GeneticReflexes/       — врождённые рефлексы (.dat)
├── ConditionedReflexes/   — условные рефлексы (.dat)
├── PsychicData/
│   ├── Automatism/        — автоматизмы (automatizm файл)
│   ├── EpisodicMemory/    — эпизодическая память (.dat)
│   └── Understanding/     — деревья понимания
├── Homeostasis/           — параметры, стили, комбинации
└── Logs/                  — research logs
```

`FileValidator` проверяет целостность при старте.  
`ProjectDirectoryTemplateNode` описывает ожидаемую структуру каталогов.

---

## 22. Сценарии (`OperatorScenarioRunner`)

Система автоматизированного тестирования и обучения:

- `ScenarioDocument` — документ сценария (строки `ScenarioLineRow` с пульсовыми интервалами)
- `OperatorScenarioEngine` — нормализует расписание, вычисляет `PulseGapBetweenSteps`
- `ScenarioPulseSchedule` — назначает `PulseWithinScenario` для каждого шага
- `IOperatorScenarioPult` — интерфейс хост-приложения для подачи стимулов и чтения состояния
- `ScenarioLogExpectationModels` — ожидаемые значения в логах для автопроверки

Перед запуском сценария вызывается `ClearStageDataOnlyForScenarioPreRun` для чистого старта без смены стадии.

---

## 23. Логирование (`ResearchLogger`)

Per-пульс строка агента для UI и файлов.

Форматы (`LogFormat`): None / Text / Html / All.

Содержит:
- Состояние гомеостаза
- Активные стили
- Активированные рефлексы / автоматизмы
- Решения циклов мышления

Промежуточный буфер сбрасывается в `FlushBufferedAgentRowToMemoryNow()` в шаге 9 пульса.

---

## 24. Расширяемость

### Регистрация новой системы (обязательный чеклист из `.cursor/rules/isida-engine-strict-checklist.mdc`):

1. Добавить свойство в `IsidaContext`
2. Добавить шаг инициализации в `IsidaEngine.Create` (в правильном порядке зависимостей)
3. Добавить `SafeDispose` в `IsidaContext.Dispose` (обратный порядок)
4. Обновить `IsidaContext.IsFullyInitialized`

### Регистрация стратегии мышления:

```csharp
context.ThinkingCyclesSystem.RegisterStrategy(new MyThinkingStrategy());
```

---

## 25. Диаграмма потоков данных (один пульс)

```
HostApp / Pult
    │
    ├─ OnPulseBeforeGomeostasis ──────────────────────────────────────────────┐
    │                                                                          │
    │   GomeostasSystem.UpdateStateOnly()                                      │
    │     parameters[i].Update()                                               │
    │     HomeostasisCalculator.CalculateAgentState() → HomeostasisState       │
    │     → InformationEnvironmentSystem (mood, danger, VeryActualSituation)   │
    │                                                                          │
    ├─ OnPulseAfterGomeostasisBeforePsychic ──────────────────────────────────┘
    │   (SensorySystem.SubmitStimulus → PsychicSystem.SensorActivation)
    │
    │   PsychicSystem.ProcessPsychicPulse()
    │     AgentSleepOrchestrator.CheckSleepTransition()
    │     [eval window] AutomatismResultTracker.ProcessEvaluationWindow()
    │     ThinkingCyclesSystem.ProcessPulse()
    │       ThinkingThemePulseResolver.ResolvedThinkingThemeTypeId ──────────┐
    │       strategies[].TryStep(ctx) → ThinkingDecision                     │
    │       AppGlobalState.UpdateMainThinkingCycleSnapshot()                 │
    │     AutomatismExecutionService.Execute(decision.AutomatizmToExecute)   │
    │     MirrorAutomatizmService [stage ≥ 3]                                │
    │                                                                         │
    │   ConditionedReflexesSystem.UpdateAgentLifetime()                      │
    │                                                                         │
    │   ReflexesActivator.ProcessReflexPulse()                               │
    │     GeneticReflexesSystem → ReflexExecutionService                     │
    │     ConditionedReflexesSystem → ReflexExecutionService                 │
    │                                                                         │
    │   ResearchLogger.FlushBufferedAgentRowToMemoryNow()                    │
    │                                                                         │
    │   ThinkingThemePulseResolver.RecordEndOfPulseAgentEvents() ────────────┘
    │     AppGlobalState.TakeStimulusSnapshotForThemeResolution()
    │
    └─ OnPulseCompleted
```

---

## 26. Краткий глоссарий

| Термин | Значение |
|--------|---------|
| **Симбионт** | Автономный агент ISIDA |
| **Пульс** | Один такт таймера (~1 с) |
| **Гомеостаз** | Система жизненных параметров; смерть при критическом отклонении |
| **Стиль** | Поведенческий стиль, активируемый диапазоном параметра |
| **Образ** | Именованный набор стимулов/действий (восприятия, Broca, действий) |
| **Автоматизм** | Выученная реакция на контекст с оценкой полезности |
| **Belief=2** | Канонический автоматизм ветки; уникален в рамках BranchID |
| **IE** | Information Environment — кадр текущего состояния агента |
| **OR** | Ориентировочный рефлекс (OR1 — новизна, OR2 — рассогласование) |
| **Цикл мышления** | Итерация по info-функциям для поиска оптимального действия |
| **Info-функция** | Одна из 24 когнитивных операций в `InfoFunctionsCatalog` |
| **Эпизод** | Запись (триггер, действие, эффект) в эпизодической памяти |
| **Тема** | `ThemeImageId` — контекст текущего цикла мышления |
| **МВАП** | Мотивационно-Волевой Адаптивный Процесс — теоретическая база |
