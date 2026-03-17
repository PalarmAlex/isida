# План реализации иерархии уровней мышления 1 и 2 (isida)

По аналогии с BOT (`brain/psychic/understanding.go`: `consciousnessElementary`). Рефлексы не меняем. Циклы мышления пока не делаем; после 2-го уровня — заглушка «проблема не решена на 2 уровне» как подготовка к модулям циклов.

---

## 1. Соответствие BOT ↔ isida (уже есть)

| BOT | isida | Комментарий |
|-----|--------|-------------|
| Штатный автоматизм (Belief==2), `atmtzmActualTreeNodeID` | `GetBelief2AutomatizmFromTreeId(nodeId)`, `GetAutomatizmFromNode(nodeId, 0)` | Уже есть |
| Оценка автоматизма (Usefulness, заблокирован) | `Automatizm.Usefulness`, проверка в `GetAutomatizmFromNode` и `ExecuteAutomatizm` | Уже есть |
| Danger, VeryActualSituation | `InformationEnvironment.Danger`, `VeryActualSituation` (заполняется в `GetCurrentInformationEnvironment`) | Уже есть |
| NeedThinkingAboutAutomatizm | `InformationEnvironment.NeedThinkingAboutAutomatizm` | Уже есть |
| Поиск правила по стимулу | `EpisodicMemorySystem.GetSingleBestRule(3, triggerId)`, `GetTargetChain(triggerId)` | Уже есть |
| Создание автоматизма по правилу | `_automatizmSystem.CreateNewAutomatizm(automatizmNodeId, rule.ActionId, true)` | Уже есть в блоке Stage >= 4 |
| Запуск автоматизма | `ExecuteAutomatizm(atmz)` | Уже есть |
| Ориентировочный рефлекс (если нет автоматизма) | `OrientationReflex(foundAutomatizm?.ID, ...)` | Уже есть |

**Чего нет:** явной последовательности «сначала только уровень 1 (штатный автоматизм), затем при неуспехе — уровень 2 (правила)» и заглушки «не решено на 2 уровне».

---

## 2. Целевая схема (как в BOT)

```
Стимул → Активация дерева автом. и Understanding (как сейчас)
       → Обновить информационную среду (GetCurrentInformationEnvironment)
       → УРОВЕНЬ 1: штатный автоматизм
           • Есть штатный и Usefulness >= 0?
             - Опасность (Danger) → выполнить штатный, выход.
             - Важная ситуация, без опасности → (опционально) подвергнуть сомнению; при блокировке/негативном прогнозе → не выполнять, перейти на уровень 2.
             - Штатный заблокирован (Usefulness < 0) → NeedThinkingAboutAutomatizm, перейти на уровень 2.
           • Нет штатного → перейти на уровень 2.
       → УРОВЕНЬ 2: правила
           • GetTargetChain(actionsImageId) или GetSingleBestRule(3, actionsImageId).
           • Если правило есть и ActionId > 0: найти/создать автоматизм по правилу, выполнить → выход.
           • Иначе → заглушка: «проблема не решена на 2 уровне» (подготовка к циклам), выход без выполнения.
```

Ориентировочный рефлекс (ОР1/ОР2) по текущей логике вызывается, когда автоматизм не найден; его можно оставить после уровня 2 как «последнюю попытку» до заглушки или вызывать только при определённых стадиях — по желанию (в плане ниже ОР остаётся после уровней 1–2).

---

## 3. Что реализовать

### 3.1. Точка входа: вызов уровней 1 и 2 в PsychicSystem

**Файл:** `PsychicSystem.cs`, метод `SensorActivation`, блок `if (automatizmNodeId > 0)`.

**Идея:** не менять рефлексы; внутри этого блока после установки `AppGlobalState.AutomatizmNodeId` и (при необходимости) зеркалирования/оценки оператора:

1. Вызвать `GetCurrentInformationEnvironment(currentEmotionId, actionsImageId)` в начале блока (если ещё не вызывается), чтобы актуальны были `Danger`, `VeryActualSituation`.
2. Ввести один метод-оркестратор, например:
   - `TryProcessThinkingLevels(automatizmNodeId, actionsImageId, currentBaseId, currentEmotionId, currentActivityId, toneMood, activationType, ...)`  
   возвращает `(bool problemSolved, Automatizm executedAutomatizm)`.
3. Внутри оркестратора:
   - **Уровень 1:** вызвать `ProcessLevel1(...)` (см. ниже). Если вернул выполненный автоматизм → `problemSolved = true`, выйти из SensorActivation с `ExecuteAutomatizm`.
   - **Уровень 2:** если уровень 1 не решил — вызвать `ProcessLevel2(...)`. Если вернул автоматизм → выполнить и выйти. Если не вернул → заглушка «не решено на 2 уровне».
   - Если после уровней по-прежнему не решено и по текущей логике нужен ОР — вызвать `OrientationReflex` и при наличии результата выполнить его (как сейчас).

Таким образом, существующая логика (предпочтение по правилу, создание из правила при отсутствии автоматизма, ОР) переносится в уровни 1 и 2 и при необходимости в «после уровня 2».

---

### 3.2. Уровень 1 (первый уровень осмысления)

**Назначение:** решение только за счёт штатного/текущего автоматизма. Без поиска правил.

**Метод (новый):** например, `ProcessLevel1(int automatizmNodeId, int actionsImageId, int currentEmotionId, out Automatizm toExecute)`  
или возврат структуры `(bool resolved, Automatizm automatizm, bool passToLevel2)`.

**Использовать уже существующее:**

- Получение штатного автоматизма:
  - `_automatizmSystem.GetBelief2AutomatizmFromTreeId(automatizmNodeId)` — штатный;
  - при отсутствии — «лучший» по узлу без учёта правила: `GetAutomatizmFromNode(automatizmNodeId, 0)` (preferredActionId = 0, чтобы не подмешивать правило в уровень 1).
- Контекст:
  - `_informationEnvironmentSystem.CurrentInformationEnvironment` (Danger, VeryActualSituation).
- Выполнение:
  - `ExecuteAutomatizm(automatizm)` (уже проверяет Usefulness < 0).

**Логика по аналогии с BOT:**

1. Взять штатный/текущий автоматизм для узла (без предпочтения по правилу).
2. Если автоматизма нет → вернуть `passToLevel2 = true`, `toExecute = null`.
3. Если автоматизм есть и `Usefulness < 0` (заблокирован):
   - Установить `CurrentInformationEnvironment.NeedThinkingAboutAutomatizm = true`.
   - Вернуть `passToLevel2 = true`, `toExecute = null`.
4. Если автоматизм есть и `Usefulness >= 0`:
   - Если `Danger` → вернуть этот автоматизм как `toExecute` (вызовющий выполнит его), уровень 1 решил проблему.
   - Если `VeryActualSituation && !Danger`: опционально «подвергнуть сомнению» (проверка по правилам/прогнозу). Если в будущем появится аналог `checkAutomatizm`/`getPrognoze` — здесь вызывать его; если результат «не запускать» → `NeedThinkingAboutAutomatizm = true`, вернуть `passToLevel2 = true`.
   - Иначе (спокойная ситуация или автоматизм прошёл проверку) → вернуть этот автоматизм как `toExecute`.

Уровень 1 не вызывает поиск правил и не создаёт автоматизмы по правилам.

---

### 3.3. Уровень 2 (второй уровень осмысления)

**Назначение:** попытка решить за счёт правил эпизодической памяти (найти/создать автоматизм по правилу и выполнить).

**Метод (новый):** например, `ProcessLevel2(int automatizmNodeId, int actionsImageId, out Automatizm toExecute)`.

**Использовать уже существующее:**

- `_episodicMemorySystem.GetTargetChain(actionsImageId)` — цепочка правил;
- `_episodicMemorySystem.GetSingleBestRule(3, actionsImageId)` — одно лучшее правило;
- поиск автоматизма по действию: `_automatizmSystem.GetMotorsAutomatizmListFromTreeId(automatizmNodeId).FirstOrDefault(a => a.ActionsImageID == rule.ActionId)`;
- создание: `_automatizmSystem.CreateNewAutomatizm(automatizmNodeId, rule.ActionId, true)`.

**Логика:**

1. Вызвать `GetTargetChain(actionsImageId)`; если цепочка непуста — взять первое правило, иначе `GetSingleBestRule(3, actionsImageId)`.
2. Если правила нет или `rule.ActionId <= 0` → переход к п. 4.
3. Найти или создать автоматизм с `ActionsImageID == rule.ActionId` в узле `automatizmNodeId` (как в текущем блоке Stage >= 4). Если найден/создан → вернуть его как `toExecute` (уровень 2 решил).
4. **Заглушка «проблема не решена на 2 уровне»:**  
   - Установить флаг/состояние для будущих модулей циклов мышления, например:
     - `CurrentInformationEnvironment.NeedThinkingAboutAutomatizm = true`, и/или
     - новое свойство типа `UnresolvedAtThinkingLevel2 = true` (и сохранить контекст: `automatizmNodeId`, `actionsImageId`, возможно baseId/emotionId).
   - Опционально: логировать/писать в исследовательский лог «Отработка уровня 2. Проблема не решена — для циклов мышления».
   - Вернуть `toExecute = null`.

Циклы пока не реализуем — только подготовка: флаг и контекст, чтобы позже модуль циклов мог взять нерешённую задачу.

---

### 3.4. Заглушка «не решено на 2 уровне»

**Вариант А (минимальный):**  
Только выставить `NeedThinkingAboutAutomatizm = true` и выйти без выполнения автоматизма (и без вызова ОР в этой ветке, если решите так).

**Вариант Б (подготовка к циклам):**  
- Ввести в `InformationEnvironment` или в `AppGlobalState` флаг, например:
  - `UnresolvedAtThinkingLevel2 : bool`
  - и при необходимости: `UnresolvedNodeId`, `UnresolvedActionsImageId`, `UnresolvedPulseCount`.
- В момент заглушки в `ProcessLevel2` выставлять эти поля и логировать.
- В будущем модуль циклов может проверять `UnresolvedAtThinkingLevel2` и брать контекст для «обдумывания».

Рекомендация: начать с Варианта Б (флаг + контекст), чтобы не переделывать при добавлении циклов.

---

### 3.5. Сводка изменений по файлам

| Файл | Действие |
|------|----------|
| `PsychicSystem.cs` | В блоке `automatizmNodeId > 0`: вызов `GetCurrentInformationEnvironment`; вызов оркестратора уровней (например `TryProcessThinkingLevels`); при `problemSolved` — выполнить возвращённый автоматизм и выйти; при не решено — при необходимости вызвать `OrientationReflex`; текущую разрозненную логику (preferredActionId, создание по правилу, ОР) заменить на вызовы Level1/Level2 и заглушку. |
| `PsychicSystem.cs` (или отдельный класс) | Добавить `ProcessLevel1(...)` и `ProcessLevel2(...)` (и при желании общий `TryProcessThinkingLevels`), используя только существующие сервисы (AutomatizmSystem, EpisodicMemorySystem, InformationEnvironmentSystem). |
| `InformationEnvironmentSystem.cs` (или `AppGlobalState`) | Опционально: добавить флаг/контекст для «не решено на 2 уровне» (UnresolvedAtThinkingLevel2 + контекст). |

Рефлексы, дерево рефлексов, `ReflexesActivator` не трогаем.

---

### 3.6. Порядок внедрения

1. Добавить флаг/контекст нерешённой проблемы на 2 уровне (если выбран Вариант Б).
2. Реализовать `ProcessLevel2`: правило → найти/создать автоматизм → выполнить или заглушка. Подключить к нему установку флага/контекста.
3. Реализовать `ProcessLevel1`: штатный/текущий автоматизм + Danger/VeryActualSituation → выполнить или передать на уровень 2.
4. Ввести оркестратор (Level1 → при неуспехе Level2) и встроить его в `SensorActivation` вместо/вместе с текущей разрозненной логикой выбора и создания автоматизма.
5. Убедиться, что ОР вызывается только там, где нужно (после уровней 1–2, при не решено), и что рефлексы не затронуты.

После этого иерархия «уровень 1 (штатный автоматизм) → уровень 2 (правила) → заглушка для циклов» будет явной и готовой к подключению модулей циклов мышления.
