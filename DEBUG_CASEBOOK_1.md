# DEBUG_CASEBOOK.md — Протокол отладки ISIDA

Журнал разобранных ошибок проекта: симптом → проверенные гипотезы (включая **тупиковые**) → корень → исправление → эвристики.

## Как пользоваться

**Перед поиском причины бага — прочитать раздел «Эвристики» и «Указатель случаев».** Если симптом похож на описанный — начинать с уже известного корня, а не с обхода кодовой базы. Повторение заведомо тупиковой ветки считается ошибкой агента.

**После каждого успешно исправленного бага — добавить случай в этот файл** (шаблон ниже). Это обязательная часть работы, наравне с обновлением справки.

Правила ведения:

- Один случай — 25–45 строк. Кратко, но с якорями в коде (`Файл.cs:строка`) и дословными сообщениями лога.
- **Тупиковые ветки обязательны** и пишутся с причиной опровержения. Ценность протокола — в основном в них.
- Случаи не удалять и не перенумеровывать — номера закрепляются в ссылках.
- Эвристику добавлять в раздел «Эвристики» только если она обобщает случай (иначе — остаётся внутри случая).
- Если новый случай дублирует старый — дополнить существующий, а не заводить новый.

## Шаблон случая

```
## Случай N. <Краткий симптом — как его формулирует пользователь>

- **Дата / сборка:**
- **Симптом:** что видно пользователю; при каких условиях воспроизводится и при каких нет.
- **Область:** подсистема, классы, события.
- **Гипотезы и проверки:** нумерованный список; для каждой — что проверено, чем (код/лог/рефлексия), итог «корень» / «опровергнуто: причина».
- **Корень:** причинно-следственная цепочка, одно звено на строку.
- **Доказательство:** лог/строка кода, которая однозначно подтверждает корень.
- **Исправление:** файлы, суть правки, что откачено из неудачных итераций.
- **Проверка:** как убедиться, что починено и что не сломано смежное.
- **Эвристики:** → N, N (номера добавленных правил).
```

## Случай 10. Сенсорная суммация не срабатывала на стимулы с командным каналом: `CompoundModalityCount` не учитывал `CommandPatternIdList`

- **Дата / сборка:** 2026-10-11, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error).
- **Симптом:** в Velum при предъявлении двух CS через **командный канал** (`CommandPatternIdList`) одновременно — условный рефлекс не суммировался; активация шла только по одному CS, второй игнорировался. Оператор сформулировал как «суммация не работает на команды». При предъявлении того же через действия/фразы суммация штатная.
- **Область:** `Reflexes\PerceptionImagesSystem.cs` — `CompoundModalityCount`, `StimulusImagesHierarchyCompatible`, `PerceptionImagesEqual`, `GetTriggerSpecificityTier`.
- **Гипотезы и проверки:**
  1. *Velum не передаёт `CommandPatternIdList`* — **опровергнуто дампом**: `VerbalCommandBrocaSystem` корректно заполняет `CommandPatternIdList` в `PerceptionImage`.
  2. *Достаточно починить `CompoundModalityCount`* — **опровергнуто кодом**: `StimulusImagesHierarchyCompatible` тоже сравнивает только `InfluenceActionsList` и `PhraseIdList`, поэтому богаче-беднее подмножество не проходит по команде; `PerceptionImagesEqual` игнорирует `CommandPatternIdList` — два разных образа считаются равными; `GetTriggerSpecificityTier` возвращает 1 для чистого командного CS.
  3. *Достаточно `Any()` вместо `Count()`* — подтверждено как минимально корректное условие (список — `List<int>`, `Any()` достаточно).
- **Корень:** `PerceptionImage` ввёл командный канал (`CommandPatternIdList`), но три метода иерархии/суммации (`CompoundModalityCount`, `StimulusImagesHierarchyCompatible`, `PerceptionImagesEqual`) и `GetTriggerSpecificityTier` не были обновлены — канал не попадал ни в подсчёт модальностей, ни в сравнение подмножеств.
- **Доказательство:** `PerceptionImagesSystem.cs` (до правки): `CompoundModalityCount`: `if (img.InfluenceActionsList?.Any() == true) n++; if (img.PhraseIdList?.Any() == true) n++;` — `CommandPatternIdList` отсутствует; `StimulusImagesHierarchyCompatible`: `IsIntListSubset(reflexTrigger.PhraseIdList, stimulus.PhraseIdList)` — без `CommandPatternIdList`.
- **Исправление:** `Reflexes\PerceptionImagesSystem.cs`:
  - `CompoundModalityCount`: добавлено `if (img.CommandPatternIdList?.Any() == true) n++;`;
  - `StimulusImagesHierarchyCompatible`: `IsIntListSubset(reflexTrigger.CommandPatternIdList, stimulus.CommandPatternIdList)` и симметрично в `sSubsetI`;
  - `PerceptionImagesEqual`: `a.CommandPatternIdList.OrderBy(x => x).SequenceEqual(b.CommandPatternIdList.OrderBy(x => x))`;
  - `GetTriggerSpecificityTier`: XML-`<summary>` дополнен («+ команда/цвет как опора»).
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 552/552. Ручная: предъявить два CS через командный канал в Velum — суммация и иерархия богаче-беднее работают.
- **Эвристики:** → E11, E12.

## Случай 11. Сенсорная прекондиция не срабатывала на стимул через командный канал: `StimulusImagesHierarchyCompatible` отфильтровывала CS по команде против триггера по фразе

- **Дата / сборка:** 2026-10-11, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error).
- **Симптом:** в сценарии сенсорной прекондиции (AIStudio `Scenario_4.dat`) после 12 парных предъявлений SW2 (команда) и «п» (фраза), связь CS₁→CS₂ выше порога γ=0,6. Предъявление «п» → УР активируется. Предъявление SW2 → УР **не** активируется, хотя ассоциация крепкая. В логе отчёта Velum: шаг 26 (SW2) — «Усл. рефлекс: ожид. «1», факт «-»».
- **Область:** `Reflexes\PerceptionImagesSystem.cs` — `StimulusImagesHierarchyCompatible`; `Reflexes\ConditionedReflexesSystem.cs` — `ResolveHierarchicalConditionedActivation` (строка 1393, фильтр `!StimulusImagesHierarchyCompatible(S, img)`).
- **Гипотезы и проверки:**
  1. *Сенсорная связь не создана или ниже порога* — **опровергнуто дампом `SensoryAssociations.dat`**: связь SW2→«п» существует, крепость ≈ 0,65 > γ=0,6.
  2. *Сенсорная суммация (E11) не учитывает команду* — **опровергнуто**: E11 про `CompoundModalityCount` и формулу подкрепления ΣV; здесь связь уже создана и проверяется активация.
  3. *Образы несовместимы по иерархии* — **подтверждено**: триггер рефлекса = фраза «п» (пустой `CommandPatternIdList`), стимул = команда SW2 (пустой `PhraseIdList`). `StimulusImagesHierarchyCompatible` проверяет подмножества: `IsIntListSubset([п], [])` = false, `IsIntListSubset([SW2], [])` = false. Оба направления (iSubsetS и sSubsetI) дают false → образ отфильтровывается ДО проверки сенсорной прекондиции (`IsLinkActivatable`).
- **Корень:**
  - коммит c753bb5 добавил `CommandPatternIdList` в `StimulusImagesHierarchyCompatible` (для кросс-канальной суммации);
  - при этом добавлена **строгая** проверка подмножества для командного канала, без учёта того, что разные модальности (команда ↔ фраза) могут образовывать сенсорную связь CS₁→CS₂;
  - стимул по команде vs триггер по фразе не проходит строгую проверку подмножества → рефлекс отсеивается до гейта сенсорной прекондиции.
- **Доказательство:** `ConditionedReflexesSystem.cs:1393` — `if (!PerceptionImagesSystem.StimulusImagesHierarchyCompatible(S, img)) return false;` выполняется **до** `IsLinkActivatable` (строка 1400). `PerceptionImagesSystem.StimulusImagesHierarchyCompatible` (до правки): `iSubsetS` требует `IsIntListSubset(reflexTrigger.CommandPatternIdList, stimulus.CommandPatternIdList)` — пустой список триггера ⊆ непустой стимул = true, но `sSubsetI` требует `IsIntListSubset(stimulus.CommandPatternIdList, reflexTrigger.CommandPatternIdList)` — непустой ⊆ пустой = false. Для направления «команда-стимул, фраза-триггер» оба направления дают false.
- **Исправление:** `Reflexes\PerceptionImagesSystem.cs` — `StimulusImagesHierarchyCompatible`: добавлено условие `crossChannel` — если у стимула команда + пустая фраза, а у триггера фраза + пустая команда (и наоборот) — образы считаются совместимыми для целей сенсорной прекондиции.
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 554/552. Ручная: сценарий сенсорной прекондиции, шаг 26 (SW2) — УР активируется, «Запуск экспорта PDF» срабатывает. Регрессия: `ResolveHierarchicalConditionedActivationTests.CrossChannel_StimulusCommand_TriggerPhrase_ActivatesRichReflex` (успех), `CrossChannel_StimulusPhrase_TriggerCommand_NotActivated_NoLink` (ожидается failure ветки `IsPoorStimulusRichReflex`).

## Случай 1. Условные рефлексы ID1/ID2, созданные «авторитарной записью» (крепость 0,95), через полчаса упали ниже порога активации (0,57 / 0,40) без нажатия «Запретить»

- **Дата / сборка:** 2026-10-01, движок ISIDA.
- **Симптом:** пользователь создал УР ID1/ID2 с флажком авторитарной записи (крепость 0,95). Через ~полчаса ID1 → 0,57, ID2 → 0,40 (ниже порога активации 0,6). Кнопку «Запретить» не нажимали. Вербальные триггеры «пп»/«дд» активируют «другие» рефлексы, которые при этом прокачиваются. Подозрение пользователя: «быстрое устаревание» или «сенсорная прекондиция».
- **Область:** `Reflexes\ConditionedReflexesSystem.cs` (класс `ConditionedReflex`, `ApplyActiveExtinctionForStimulus`, `LoadConditionedReflexes`), `Reflexes\ConditionedReflexFormationService.cs` (`RecordStimulus`, `ProcessPendingExtinction`), `Reflexes\SensoryAssociationSystem.cs` (сенсорная прекондиция).
- **Гипотезы и проверки:**
  1. *TTL/`ExpiresAt` убивает рефлексы («быстрое устаревание»)* — **опровергнуто по данным**: в `ConditionedReflexes.dat` для всех записей `ExpiresAt == LastActivation + LifetimePulses` (ID1: 213511+44236800=44450311; ID2: 212954+88473600=88686554), то есть TTL согласован и никого не истёк.
  2. *Немонотонный `now` (Lifetime) сдвигает `ExpiresAt` назад и «убивает» рефлекс* — **опровергнуто**: тот же расчёт — `ExpiresAt` монотонно больше `LastActivation`; данные не подтверждают сброс/перезагрузку `Lifetime` в опасную сторону.
  3. *Сенсорная прекондиция напрямую угашает «пп»/«дд»* — **опровергнуто трассировкой**: прекондиция (`SensoryAssociationSystem`) порождает вторичные УР; наблюдаемое падение ID1/ID2 — результат активного угасания по их собственному `Level3`, а не отдельной ветки прекондиции.
  4. *«Авторитарная запись»/крепость 0,95 защищена от угасания* — **опровергнуто кодом**: флаг авторитарности управляет только запретом/ручным сбросом, но `ApplyActiveExtinctionForStimulus` применяется ко **всем** УР с данным `Level3`, включая «сильные».
  5. *Активное угасание и есть причина падения* — **подтверждено**: `ApplyActiveExtinction`: `C ← C + α_ext·(0 − C)`, α_ext = `ActiveExtinctionRate` = 0,05. За ~55–60 импульсов «CS без US в окне τ» 0,95 → ~0,40 (0,95·0,95^55 ≈ 0,40), что точно совпадает с ID2=0,40084.
  6. *Рассинхронизация настроек и порядка загрузки* — **подтверждено кодом**: методы класса `ConditionedReflex` читали захардкоженные поля вместо `_settings`; в конструкторе `LoadConditionedReflexes()` вызывался **до** `LoadConditionedReflexSettings()`.
- **Корень:**
  - механизм активного угасания (`ApplyActiveExtinctionForStimulus`) угашает **все** УР с данным `Level3` при любом CS без US в окне τ — включая рефлексы, созданные «авторитарной записью»; за десятки импульсов «дд»/«пп» без US крепость падает ниже порога 0,6;
  - класс `ConditionedReflex` не видел `ConditionedReflexSettings.dat`: `ShouldBeRemoved`/`CanBeActivated`/`StrengthenAssociation`/`TimeWindowPulses` использовали приватные дефолты (`_minAssociationStrength=0.1f`, `_activationThreshold=0.6f`, `_learningRate=0.2f`, `_timeWindowPulses=5`), а не настройки системы;
  - в конструкторе `ConditionedReflexesSystem` `LoadConditionedReflexes()` выполнялся до `LoadConditionedReflexSettings()`, поэтому при загрузке читались дефолтные `MinAssociationStrength`/`GetInitialLifetimeForOrder`, а не значения из файла.
- **Доказательство:** `ConditionedReflexes.dat`: ID2 `…|0,40084|212954|212625|…|88473600|88686554`; ID1 `…|0,7895626|213511|212523|…|44236800|44450311`; `ConditionedReflexSettings.dat`: `ActiveExtinctionRate=0.05`, `ActivationThreshold=0.6`, `LearningRate=0.2`, `TimeWindowPulses=5`. Код: `ConditionedReflex.ApplyActiveExtinction` (`C ← C + α_ext·(0−C)`), `ConditionedReflexesSystem.ApplyActiveExtinctionForStimulus` (фильтр по `Level3`/`ToneId`/`MoodId`).
- **Исправление:**
  - `ConditionedReflex.ShouldBeRemoved` → `Instance.Settings.MinAssociationStrength`; `CanBeActivated` → `Instance.Settings.ActivationThreshold`; `StrengthenReflexInternal` → `_settings.LearningRate`; окна времени → `_settings.TimeWindowPulses`;
  - конструктор `ConditionedReflexesSystem`: `LoadConditionedReflexSettings()` вызывается **перед** `LoadConditionedReflexes()` (порядок и комментарий-якорь на случай регресса);
  - в `ApplyActiveExtinctionForStimulus` добавлено логирование угасания, симметричное логированию усиления: `Logger.Info("Крепость у-рефлекса ID=… угашена активным угасанием: {old:F3} → {new:F3}")`.
- **Проверка:** `MSBuild isida.csproj -t:Build -p:Configuration=Debug` → EXIT=0, `bin\Debug\isida.dll` обновлён. Ручная: создать УР «авторитарной записью», не подавать US в окне τ — в логе появляются строки «угашена активным угасанием» с динамикой крепости; при подаче US в окне — «Усилен условный рефлекс ID=…».
- **Эвристики:** → E1, E2.

## Случай 2. Угасание УР: активное только ниже порога γ, пассивное (медленное) выше; обе ветви нелинейны

- **Дата / сборка:** 2026-10-01, движок ISIDA (Debug, Rebuild успешен).
- **Симптом (требование):** активное угасание УР не должно затрагивать «сильные» рефлексы (крепкость ≥ порога активации γ). Выше γ — только редкое медленное пассивное угасание (раз в `PassiveDecayPeriodPulses` пульсов), тем слабее, чем выше крепкость. Ниже γ — активное угасание, тем сильнее, чем ниже крепкость от порога. Обе ветви нелинейны.
- **Область:** `Reflexes\ConditionedReflexesSystem.cs` (класс `ConditionedReflex`, `ApplyActiveExtinction`, `ApplyActiveExtinctionForStimulus`, `ApplyPassiveDecay`, `ApplyDecay`, `UpdateAgentLifetime`, `ConditionedReflexSettings`, загрузка/сохранение настроек).
- **Гипотезы и проверки:**
  1. *Достаточно ввести порог γ в `ApplyActiveExtinctionForStimulus`* — подтверждено: ранний `continue`, если `reflex.AssociationStrength >= _settings.ActivationThreshold`.
  2. *Активное угасание должно усиливаться по мере удаления от порога вниз* — подтверждено кодом: `drop = (γ − C)/γ`, `alphaEffective = alpha·(1 + 2·drop)`.
  3. *Выше γ угасание должно быть редким и слабым* — подтверждено: новый `ApplyPassiveDecay()` работает только при `C ≥ γ`, шагами `delta/period`; `rate = baseRate·(1−above)·steps`, где `above = (C−γ)/(1−γ)`; чем выше C, тем меньше rate.
  4. *Пассивное угасание должно вызываться периодически, а не каждый пульс* — подтверждено: `UpdateAgentLifetime` вызывает `ApplyPassiveDecay()` при накоплении `PassiveDecayPeriodPulses` (по умолчанию 1000) в `PassiveDecayAccumulator`.
  5. *Настройки должны читаться из файла и сохраняться* — подтверждено: добавлено поле `PassiveDecayPeriodPulses` в `ConditionedReflexSettings`, парсинг `case "PassiveDecayPeriodPulses"`, сохранение `lines.Add(...)`.
  6. *Побочный эффект: двойной дележ α на порядок в `StrengthenAssociation`* — подтверждено и убрано (α/K уже учтён в `StrengthenReflexInternal`).
- **Корень:** до правки `ApplyActiveExtinctionForStimulus` угашал **все** УР с данным `Level3`/`ToneId`/`MoodId` линейно (`C ← C + α_ext·(0−C)`), без учёта порога и без пассивного канала; «сильные» рефлексы деградировали так же, как «слабые» (см. Случай 1).
- **Доказательство:** `ConditionedReflexesSystem.cs:913` (`ApplyPassiveDecay`), `:1039`/`:1063` (`ApplyActiveExtinctionForStimulus` + фильтр по γ), `:416–422` (нелинейный множитель), `:1472–1474` (периодический вызов), `:176`/`:181`/`:526` (поля), `:1919` (парсинг), `:2022` (сохранение).
- **Исправление:** `Reflexes\ConditionedReflexesSystem.cs`: активное угасание — только при `C < γ`, с нелинейным ускорением; новое пассивное угасание — только при `C ≥ γ`, реже и слабее с ростом C; период — `PassiveDecayPeriodPulses` (1000). Убран двойной дележ α в `StrengthenAssociation`.
- **Проверка:** `MSBuild isida.csproj -t:Build -p:Configuration=Debug` → успешно, `bin\Debug\isida.dll` обновлён. Ручная: рефлекс с крепкостью выше γ не должен проваливаться за десятки импульсов без US (в логе — редкие строки пассивного угасания); рефлекс ниже γ угашает активнее по мере приближения к 0.
- **Эвристики:** → E3.

## Случай 3. `AddUtils.ParseIntList`/`ParseDoubleList`: пустые сегменты превращаются в `0`, а дробные значения ломаются на ru-RU

- **Дата / сборка:** 2026-10-01, движок ISIDA (Debug). Найдено юнит-тестами `tests/Isida.Tests` (модуль `ISIDA.Common.AddUtils`).
- **Симптом:** при разборе строк вида `"1,,2"` и `"1,2,,"` в списке появляются лишние нули (`[1,0,2]`, `[1,2,0,0]`). При культуре процесса ru-RU (десятичный разделитель — запятая) строка `"1,5"` разбирается как список `[1,5]`, а не как одно дробное значение `1.5`. Пользователь симптома напрямую не замечал — данные `.dat` содержат только целые числа, но логика парсинга неверна.
- **Область:** `Common\AddUtils.cs` — методы `ParseIntList`, `ParseDoubleList` (используются при парсинге/сериализации `.dat`: `ActionsImages`, `PerceptionImages`, `GeneticReflexes`, `ConditionedReflexes`, `EmotionsImage`, `VerbalBroca`, `CommandBroca`, `InfluenceActionImages`; `ParseDoubleList` вызовов в движке не имеет).
- **Гипотезы и проверки:**
  1. *Вызов `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)` удаляет пустые сегменты* — **опровергнуто тестами**: в net48 этот вызов связывается с перегрузкой `Split(params char[])` (значение enum `RemoveEmptyEntries == 1` приводится к `char '\u0001'`), то есть разделителями становятся `','` и `'\u0001'`, а **не** флаг удаления пустых записей. Пустые сегменты сохраняются и `int.Parse("")`/`double.Parse("")` → исключение, а `AddUtils` глушит его дефолтом `0`.
  2. *Проблема только в пустых сегментах* — **опровергнуто**: `ParseDoubleList` дополнительно делит строку по запятой, что конфликтует с ru-RU, где запятая — десятичный разделитель.
  3. *Правка нужна в вызывающем коде* — **опровергнуто**: ошибка локализована в самих `Parse*List`, все потребители используют их единообразно.
- **Корень:**
  - `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)` — неверная перегрузка: вместо флага `StringSplitOptions` в `char[]`-перегрузку попадает символ `'\u0001'`; пустые сегменты не отбрасываются;
  - `ParseDoubleList` разбирает по `','` в текущей культуре, поэтому ru-RU путает разделитель списка и десятичный разделитель.
- **Доказательство:** юнит-тесты `AddUtilsTests` (net48): `ParseIntList("1,,2")` → `[1,0,2]`, `ParseIntList("1,2,,")` → `[1,2,0,0]`; `ParseDoubleList("1,5")` при `ru-RU` → `[1,5]`. Код: `Common\AddUtils.cs`, методы `ParseIntList`/`ParseDoubleList`.
- **Исправление:** `Common\AddUtils.cs`: `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)` → `Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)` в `ParseIntList` и `ParseDoubleList` (пустые сегменты теперь отбрасываются). `ParseDoubleList` разбирает числа через `double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, …)`, `DoubleListToString` сериализует через `ToString(CultureInfo.InvariantCulture)` — запятая остаётся только разделителем списка, точка — десятичным разделителем, независимо от текущей культуры.
- **Проверка:** пересборка `isida.csproj` (Debug) → `bin\Debug\isida.dll` обновлён; `dotnet test` в `tests\Isida.Tests` → 199 тестов зелёные. Тесты `AddUtilsTests` обновлены: `ParseIntList("1,,2")` → `[1,2]`, `ParseIntList("1,2,,")` → `[1,2]`, `ParseDoubleList("1.5")` при ru-RU → `[1.5]`, `ParseDoubleList("1,5")` → `[1,5]`.
- **Эвристики:** → E4.

## Случай 4. `AdaptiveActionsSystem.AddAction`: в нестрогом режиме причина отказа (validationError) теряется, вызывающая сторона получает пустой список предупреждений

- **Дата / сборка:** 2026-10-01, движок ISIDA (Debug). Найдено интеграционными тестами `tests/Isida.Tests` (`AdaptiveActionsSystemIntegrationTests`).
- **Симптом:** `AddAction(..., strictValidation: false)` с заведомо невалидными данными (например, несуществующий `targetGomeoParamIdArr`) возвращает `(0, warnings)`, где `warnings` пуст — при этом причина отказа непуста (`validationError`). Пользователь/вызывающий код не может узнать, почему действие не создано; при `strictValidation: true` то же сообщение корректно попадает в исключение.
- **Область:** `Actions\AdaptiveActionsSystem.cs`, метод `AddAction` (ветка обработки неуспешной `ValidateSingleAction`).
- **Гипотезы и проверки:**
  1. *Валидатор не заполняет `validationError`* — **опровергнуто кодом**: `ValidateSingleAction` складывает ошибки в `errors` и возвращает `errorMessage = string.Join("\n", errors)`; сообщение непусто.
  2. *Ошибка локализована в `SettingsValidator.ValidateVigorAction`* — **опровергнуто**: падение `Vigor: 0` происходит раньше, в сеттере `AdaptiveAction.Vigor` (`ArgumentOutOfRangeException`, инвариант [1..10]), до мягкой ветки дело не доходит — это ожидаемое поведение инварианта, а не баг.
  3. *Мягкая ветка теряет именно `validationError`* — **подтверждено кодом**: `return (0, validationWarnings.Split(...))` использует `validationWarnings`, тогда как все ошибки лежат в `validationError` (warnings-список валидатора всегда пуст).
- **Корень:** в нестрогом режиме ошибки валидации возвращаются как предупреждения, но код берёт для этого `validationWarnings` (пустой) вместо `validationError`.
- **Доказательство:** `AdaptiveActionsSystem.cs:515–523` до правки: `if (!ValidateSingleAction(...)) { if (strictValidation) throw ...; var warnings = validationWarnings.Split(...); return (0, warnings); }`. Тест `AddAction_UnknownTargetParameter_ReturnsZeroWithWarnings` до правки падал на `Assert.NotEmpty(warnings)`.
- **Исправление:** `Actions\AdaptiveActionsSystem.cs`: в нестрогой ветке объединить `validationError` и `validationWarnings` (`errors.Concat(warnings).ToArray()`), чтобы причина отказа всегда доходила до вызывающей стороны.
- **Проверка:** пересборка `isida.csproj` (Debug); `dotnet test --filter AdaptiveActionsSystemIntegrationTests` → зелёные. Тест `AddAction_InvalidVigor_ReturnsZeroWithWarnings` заменён на `AddAction_InvalidVigor_ThrowsFromInvariant` (фиксирует инвариант сеттера `Vigor`), добавлен `AddAction_UnknownTargetParameter_ReturnsZeroWithWarnings`.
- **Эвристики:** → E5.

## Случай 5. «Мусорные» у-рефлексы в SolidWorks-адаптере: шумовой CS, предъявленный после окрепшего целевого, обучается наравне с ним (нет конкурентного слоя ΣV)

- **Дата / сборка:** 2026-10-02, движок ISIDA (Debug).
- **Симптом:** в адаптере SolidWorks нужные у-рефлексы «перебиваются» мусорными: шумовой CS, который предъявлялся после окрепшего целевого, рос по той же кривой, что и целевой, и тоже становился активируемым; побочные эффекты (сенсорная прекондиция) вызывали не к месту диалоговые формы; целевой у-рефлекс при нажатии стимула срабатывал не всегда. Гипотеза стороннего агента: отсутствует конкурентный слой обучения (ΣV в ошибке Рескорла–Вагнера).
- **Область:** `Reflexes\ConditionedReflexFormationService.cs` (`ProcessConditionedAssociation`, `ProcessSecondaryConditionedAssociation`), `Reflexes\ConditionedReflexesSystem.cs` (`ConditionedReflexSettings`, `StrengthenAssociationWithRate`).
- **Гипотезы и проверки:**
  1. *Внутри одного испытания конкуренция есть («последний CS побеждает»)* — подтверждено: US подкрепляет только последний предшествующий CS (`_lastConditionedStimulus`), промежуточные шумовые CS не обучаются.
  2. *Между испытаниями каждый CS обучается независимо полным α·(β−C)* — **подтверждено** тестом `CompetitiveLearningTests.Disabled_EachCsLearnsIndependently_NoBlocking` и ранее `SeparateTrials_EachCsLearnsIndependently_NoBlocking`: шумовой CS догоняет целевой, хотя тот «захватил» US раньше. Это и есть отсутствие ΣV.
  3. *Достаточно запретить обучение при окрепшем конкуренте* — **опровергнуто как единственная мера**: жёсткий запрет создаёт «замороженные» мусорные CS, если конкурент позже протухнет. Нужна плавная ΣV-коррекция подкрепления.
  4. *ΣV можно внести только в `StrengthenReflexInternal`* — **опровергнуто**: там нет контекста «какие CS конкурируют за один US в этом испытании»; корректная точка — сервис формирования, где известны `_lastConditionedStimulus`/`_lastUnconditionedStimulus` и условия (Level1/Level2/Tone/Mood).
  5. *Авторитарная запись должна быть защищена от подавления* — подтверждено требованием: оператор явно подтверждает рефлекс, ΣV к нему не применяется.
- **Корень:** в модели RW при CS→US использовалось полное подкрепление λ=β, без вычитания предсказательной силы уже существующих CS (ΣV). Поэтому шумовой CS получал α·(β−C) независимо от того, что тот же US уже предсказывался целевым CS.
- **Доказательство:** `ConditionedReflexFormationService.ProcessConditionedAssociation` до правки: `StrengthenAssociation(existingReflex.Id)` / `AddConditionedReflex(...)` без учёта конкурентов; тест `CompetitiveLearningTests.Disabled_EachCsLearnsIndependently_NoBlocking` показывает `cTarget ≈ cNoise` при отключённом слое.
- **Исправление:**
  - `ConditionedReflexSettings`: новые поля `EnableCompetitiveLearning` (по умолчанию `true`) и `CompetitionSuppressionCoefficient` (0..1, по умолчанию `1.0`), с парсингом (`EnableCompetitiveLearning`, `CompetitionSuppressionCoefficient` через `InvariantCulture`) и сохранением в `ConditionedReflexSettings.dat`;
  - `ConditionedReflexesSystem.StrengthenAssociationWithRate(reflexId, effectiveLearningRate)` — усиление с явной (подавленной) скоростью, каскад дочерних штатной скоростью;
  - `ConditionedReflexFormationService.ComputeCompetitionSuppression(...)`: ΣV = сумма крепостей активируемых конкурентов с тем же источником (UR для первичных, родительский CR для вторичных) и теми же условиями; `suppression = min(1, ΣV/β) · coefficient`;
  - при `suppression ≥ 1` обучение CS блокируется (Kamin blocking); иначе подкрепление идёт со скоростью `α_eff = α/K(order) · (1 − suppression)`. Авторитарная запись подавлению не подвергается.
- **Проверка:** пересборка `isida.csproj` (Debug); `dotnet test` → 453/453. `CompetitiveLearningTests`: `Enabled_TargetBlocksNoise_NoiseStaysBelowThreshold`, `Disabled_EachCsLearnsIndependently_NoBlocking`, `AuthoritativeMode_NotSuppressed`, `LastCsWins_IntermediateNoise_NotLearned`, `NoisePairs_DoNotWeakenExistingTargetReflex`.
- **Эвристики:** → E6.

## Случай 6. CR₂ не активируется на пульсе 90 (Scenario 2, AIStudio): вторично подкреплённый CS₂ тут же гасился как «CS без US»

- **Дата / сборка:** 2026-10-02, движок ISIDA (Debug). Воспроизводился сценарием Scenario 2 в AIStudio; зафиксирован тестами `CheckSecondaryConditioningTests`.
- **Симптом:** после серии CS₂→CS₁→CR₁ вторичный рефлекс CR₂ на CS₂ создавался (появлялся в реестре с Order=2), но по прибытии пульса ~90 не активировался: его крепость оказывалась ниже порога γ. При этом сам CR₂ формально «был создан успешно».
- **Область:** `Reflexes\ConditionedReflexFormationService.cs` (`RecordStimulus`, `ProcessPendingExtinction`, `CheckSecondaryConditioning`, `ProcessSecondaryConditionedAssociation`, `_pendingCsAwaitingUs` / `_pendingCsPulse`).
- **Гипотезы и проверки:**
  1. *CR₂ создаётся слабым (недобор RW за один шаг)* — **опровергнуто отладочным дампом реестра**: сразу после создания C(CR₂) ≥ γ, `CanBeActivated() == true`.
  2. *CR₂ протухает по TTL к пульсу 90* — **опровергнуто по данным**: `ExpiresAt` заведомо больше 90, TTL не при чём.
  3. *CR₂ активно угасает из-за «CS без US»* — **подтверждено**: `RecordStimulus` при записи очередного стимула вызывает `ProcessPendingExtinction`, который для pending-CS (CS₂) применяет активное угасание по его `Level3`, затрагивая в том числе CR₂.
  4. *Pending-статус CS₂ снимается при вторичном подкреплении* — **опровергнуто кодом (корень)**: `CheckSecondaryConditioning`/`ProcessSecondaryConditionedAssociation` создавали/усиливали CR₂, но НЕ сбрасывали `_pendingCsAwaitingUs` для CS₂. Флаг оставался `true`, и при следующем `RecordStimulus` CS₂ закрывался как «CS, так и не получивший US» → активное угасание.
- **Корень:** у CS₂ было два канала подкрепления — прямой US и вторичный (активация CR₁ на последующем CS₁). Флаг `_pendingCsAwaitingUs` (ожидание подкрепления) снимался только по прямому US. Вторичное подкрепление CR₂ не сбрасывало его, поэтому только что подкреплённый CS₂ на следующем шаге классифицировался как неподкреплённый и его CR активно гасился.
- **Доказательство:** `ConditionedReflexFormationService.cs` — до правки в `CheckSecondaryConditioning` отсутствовал сброс `_pendingCsAwaitingUs`; после `ProcessSecondaryConditionedAssociation(...) → reinforced == true` флаг всё ещё `true`, и `ProcessPendingExtinction` (ветка `_pendingCsAwaitingUs && pulse > _pendingCsPulse`) вызывал `ApplyActiveExtinctionForStimulus(cs2)`.
- **Исправление:** `Reflexes\ConditionedReflexFormationService.cs`: добавлен `ConfirmPendingCsReinforced()` (сбрасывает `_pendingCsAwaitingUs` под write-lock); вызывается в `CheckSecondaryConditioning`, когда `ProcessSecondaryConditionedAssociation` вернул `reinforced == true` (CS₂ реально подкреплён активацией CR₁).
- **Проверка:** пересборка `isida.csproj` (Debug); `dotnet test` зелёные. `CheckSecondaryConditioningTests` (CR₂ создаётся, Order=2, активируется по CS₂ без CS₁), `ConditionedReflexScenarioTests`.
- **Эвристики:** → E7.

## Случай 7. `NullReferenceException` в `GlobalTimer.ProcessAgentPulse` парными записями в `SaveErrors.log` при закрытии/перезапуске

- **Дата / сборка:** 2026-10-04, движок ISIDA (Debug).
- **Симптом:** в `SaveErrors.log` ошибка идёт ПАРАМИ: `[GlobalTimer.ProcessAgentPulse:547] NullReferenceException` и `[GlobalTimer.ProcessAgentPulse:664] NullReferenceException`. Стек-трейс содержит ОДИН фрейм `ProcessAgentPulse()` — значит NRE это разыменование null-ПОЛЯ внутри самого метода, а не внутри под-системы. Воспроизводится при выгрузке движка (закрытие проекта, перезапуск, смена симбионта), особенно на стадии 2 (длинный пульс). Связанный симптом 03.10: `AgentLogs.jsonl … используется другим процессом` — предыдущий экземпляр не выгрузился и держит файл.
- **Область:** `Common\GlobalTimer.cs` (`ProcessAgentPulse`, `TimerCallback`, `ClearSystems`), `Common\IsidaEngine.cs` (`Dispose`).
- **Гипотезы и проверки:**
  1. *NRE внутри под-системы (гомеостаз/психика/рефлексы)* — **опровергнуто стек-трейсом**: в трассе один фрейм `ProcessAgentPulse()`, внутренних фреймов нет → падение на разыменовании поля-параметра вызова, а не внутри callee.
  2. *Одна система стала null (например, только `_gomeostas`)* — **опровергнуто парностью**: строки 547 (`Logger.Error($"{gomeostasEx}")` в catch гомеостаза) и 664 (`Logger.Error($"{finalEx.Message}")` в catch блока `finally`) срабатывают одновременно ⇒ null и `_gomeostas`, и `_reflexesActivator`. Это ровно состояние ПОСЛЕ `ClearSystems()`.
  3. *`Thread.Sleep(200)` в `Dispose()` достаточно, чтобы пульс завершился* — **опровергнуто по таймингу**: пульс на стадии 2 длится дольше 200 мс, поэтому к моменту `ClearSystems()` обработчик ещё выполняется вне `lock(_timerLock)`.
  4. *Поток пульса читает статические поля вне дока, пока `ClearSystems()` обнуляет их под локом* — **подтверждено кодом (корень)**.
- **Корень:**
  - `TimerCallback` проверяет `if (!_isRunning)` под `lock(_timerLock)`, но тело `ProcessAgentPulse` (~530–660) читает статические поля `_gomeostas`/`_reflexesActivator`/… **вне** `_timerLock`;
  - `IsidaEngine.Dispose()` = `GlobalTimer.Stop(); Thread.Sleep(200); GlobalTimer.ClearSystems();`; `ClearSystems()` берёт `_timerLock` и обнуляет поля посреди незавершённого пульса;
  - пульс стадии 2 не укладывается в `Sleep(200)`, поэтому фиксированная пауза не гарантирует конца обработчика → разыменование уже обнулённого поля → NRE.
- **Доказательство:** пара строк 547+664 с одним фреймом в стеке; `IsidaEngine.cs:435–440` — `Stop(); Sleep(200); ClearSystems()`; `GlobalTimer.ClearSystems()` обнуляет `_gomeostas`/`_reflexesActivator` под `_timerLock`, тогда как `ProcessAgentPulse` читает те же поля без дока.
- **Исправление:**
  - `GlobalTimer.ProcessAgentPulse`: единовременный **снимок всех систем-участников под `lock(_timerLock)`** в локальные переменные (`gomeostas`/`psychic`/`actions`/`reflexesActivator`/`crs`/`researchLogger`), дальше тело работает только по локалям; при null в снимке — штатный ранний `return` (не ошибка); в `finally` — `reflexesActivator?.ResetStates(...)` (null-guard, чтобы второй NRE не затирал первый в логе);
  - новый `ManualResetEventSlim _pulseCompletionSignal` (signaled в простое): `TimerCallback` делает `Reset()` ровно перед `ProcessAgentPulse()`, `finally` пульса — `Set()` (любой путь выхода);
  - новый `GlobalTimer.WaitForPulseCompletion(timeoutMs)`; `ClearSystems()` вызывает его **до** входа в `_timerLock` (ожидание внутри дока дало бы взаимную блокировку: `ProcessAgentPulse` берёт тот же лок для снимка);
  - `IsidaEngine.Dispose()`: убран `Thread.Sleep(200)` — ожидание реального конца пульса берёт на себя `ClearSystems()`.
- **Проверка:** `dotnet build isida.csproj -c Debug` → EXIT=0; `dotnet test` в `tests\Isida.Tests` зелёные, включая `GlobalTimerPulseRaceTests` (снимок не рвётся при `ClearSystems()` во время пульса; `WaitForPulseCompletion` не блокируется обнулением ссылок). Ручная: быстрый перезапуск/закрытие проекта с активным пульсом стадии 2 — в `SaveErrors.log` больше нет пар `ProcessAgentPulse` NRE; `AgentLogs.jsonl` освобождается.
- **Эвристики:** → E8.

## Случай 8. Откат транзитивных цепей CS→CS снёс адресный штраф сенсорной связи: «Запретить» в Velum убивает у-рефлекс целиком

- **Дата / сборка:** 2026-10-09, движок ISIDA (Debug).
- **Симптом:** кнопка «Запретить» в форме экспорта SolidWorks (Velum), открытой активацией через сенсорную прекондицию (бедный стимул CS₁ → богатый CS₂), штрафует у-рефлекс целиком вместо связи CS₁→CS₂; после наказания УР перестаёт срабатывать и по своему точному стимулу. Velum уходит в fallback `ResetAssociationStrengthToInitial` — значит, через рефлексию не нашлись методы движка.
- **Область:** ISIDA `Reflexes\SensoryAssociationSystem.cs`, `Common\AppGlobalState.cs`, `Reflexes\ReflexesActivator.cs`; адаптер Velum `Common\VelumConditionedReflexForbidHelper.cs` (рефлексия, не менялся).
- **Гипотезы и проверки:**
  1. *Проблема в Velum* — **опровергнуто**: `VelumConditionedReflexForbidHelper` уже резолвит `PenalizeLinkByOperator`/`TryGetAssociability` через `Type.GetType("ISIDA.Reflexes.SensoryAssociationSystem, isida")` и `AppGlobalState.CaptureAndClearCurrentReflexEpisode`; при их отсутствии честно уходит в fallback.
  2. *Достаточно вернуть два метода в `SensoryAssociationSystem`* — **опровергнуто**: без `CaptureAndClearCurrentReflexEpisode` Velum не отличает гейт-активацию от точного совпадения (пара CS₁→CS₂ к моменту открытия формы уже потеряна), поэтому штраф всё равно идёт по УР.
  3. *Восстановить код из git* — **опровергнуто**: история файлов вычищена (`git log -- Reflexes/SensoryAssociationSystem.cs` — только `fac4a8a`, `e4df0a2`, `9e93ae5`; удалённого кода в диффах нет).
- **Корень:** откат коммита с транзитивными цепями CS→CS удалил публичный контракт `SensoryAssociationSystem` (адресный штраф/чтение готовности) и статический эпизод гейта в `AppGlobalState`; Velum, вызывающий их только рефлексией (ради совместимости со старыми сборками), при их отсутствии не падает, а тихо наказывает УР целиком.
- **Доказательство:** `SensoryAssociationSystem.cs` (до правки) не содержал `PenalizeLinkByOperator`/`TryGetAssociability`; `AppGlobalState.cs` (до правки) не содержал `CaptureAndClearCurrentReflexEpisode`; в Velum `OnForbidLinkClick` при `linkResult == null` вызывал `ResetAssociationStrengthToInitial(episode.ReflexId)`.
- **Исправление:**
  - `Common\AppGlobalState.cs`: поля эпизода гейта `_currentReflexEpisodeGateCs1/_Cs2` + `SetCurrentSensoryGate`/`ClearCurrentSensoryGate` + атомарный `CaptureAndClearCurrentReflexEpisode()` → `int[] { reflexId, gateCs1, gateCs2 }` (массив — чтобы адаптер читал простой рефлексией без ValueTuple);
  - `Reflexes\SensoryAssociationSystem.cs`: `TryGetAssociability(cs1, cs2, out float)` (прямое звено, а при его отсутствии — сила лучшей транзитивной цепи) и `(bool, string) PenalizeLinkByOperator(cs1, cs2)` — умножение крепости на `OperatorPenaltyFactor = 0.3`: по прямому звену, а если его нет — по рёбрам лучшей цепи (для этого `FindBestChain` теперь попутно возвращает путь, `TryGetBestChainPath`); удаление звеньев ниже `MinAssociationStrength`, синхронизация индекса `_outLinks` (иначе транзитивные цепи видят старую крепость);
  - `Reflexes\ReflexesActivator.cs`: при иерархическом сборе пара гейта определяется публичным `ConditionedReflexesSystem.IsSensoryPreconditioningPair(cs1, cs2)` и запоминается в `_conditionedReflexGateLinks`; при исполнении пишется в `AppGlobalState` рядом с `CurrentConditionedReflexID` (точное совпадение и б/у обнуляют);
  - **откачено:** правка `ConditionedReflexesSystem` — пару гейта удалось определить публичным `IsSensoryPreconditioningPair` без нового поля `GateLinks`.
- **Проверка:** `dotnet build isida.csproj -c Debug` → EXIT=0 (0 предупреждений); `dotnet test tests\Isida.Tests` → 538/538, `SensoryAssociationSystemTests` 21/21 (новые: `TryGetAssociability_*`, `PenalizeLinkByOperator_*` включая штраф цепи, `CaptureAndClearCurrentReflexEpisode_ReturnsAndClears`); `Build-Velum.ps1 Debug` → EXIT=0.
- **Эвристики:** → E9.

## Случай 9. Флажок «очистка при старте» сценария не чистит `SensoryAssociations.dat`, хотя переключение стадии на 0 на пульте — чистит

- **Дата / сборка:** 2026-10-10, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error).
- **Симптом:** сценарий (`C:\ProgramData\VELUM\Scenarios\Scenario_4.dat`) с флажком очистки данных при старте (по смыслу — «чистить как при переходе на стадию 0») не очищает сенсорные ассоциации CS→CS: `SensoryAssociations.dat` переживает запуск сценария. При ручном переключении стадии на 0 на пульте тот же файл очищается корректно.
- **Область:** `Common\EvolutionStageService.cs` (`ClearStageDataOnlyForScenarioPreRun`, `ChangeEvolutionStage`, `ClearSensoryAssociationsData`), `Gomeostas\GomeostasSystem.cs` (`ClearEvolutionStageDataForScenarioPreRun`), `Reflexes\SensoryAssociationSystem.cs` (`ClearAll`).
- **Гипотезы и проверки:**
  1. *Флажок сценария идёт через `ChangeEvolutionStage(0)`* — **опровергнуто кодом**: хост зовёт `GomeostasSystem.ClearEvolutionStageDataForScenarioPreRun()` → `EvolutionStageService.ClearStageDataOnlyForScenarioPreRun(stage)`; это отдельный путь, `ChangeEvolutionStage` не вызывается.
  2. *Очистка `SensoryAssociations.dat` есть внутри `ClearStageData`* — **опровергнуто кодом**: `ClearStageData` покрывает стадии 1–5 (`ClearConditionedReflexes`, `ClearAllAutomatizm`, `ClearPsychicMemoryAndUnderstanding`), сенсорных ассоциаций там нет.
  3. *Сенсорные ассоциации чистятся где-то ещё в каскаде предзапуска* — **опровергнуто поиском**: единственные вызовы очистки — `EvolutionStageService.ClearSensoryAssociationsData` и `ClearActionsImagesData`, и обе вызывались только в ветке `targetStage == 0` метода `ChangeEvolutionStage`.
  4. *Сам метод очистки сенсорных ассоциаций отсутствует/неверен* — **опровергнуто**: `SensoryAssociationSystem.ClearAll()` (очистка `_links` + `_outLinks` под write-lock и `Save()`) уже был и работает — на пульте очистка при стадии 0 проходит.
  5. *Предзапуск сценария должен включать те же внестадийные очистки, что и переход на 0* — **подтверждено**: `ClearStageDataOnlyForScenarioPreRun` вызывал только `ClearSubsequentStagesData(0, max(stage,2))`, а очистки `action_images`/`SensoryAssociations` оставались за рамками.
- **Корень:**
  - переход на стадию 0 в `ChangeEvolutionStage` помимо `ClearSubsequentStagesData` делает две внестадийные очистки — `ClearActionsImagesData()` и `ClearSensoryAssociationsData()`;
  - `ClearStageDataOnlyForScenarioPreRun` (путь флажка «очистка при старте») эти две очистки не повторял, хотя задокументирован как «как при переходе на стадию 0»;
  - сенсорные связи CS→CS не привязаны к номерам стадий и потому не попадали ни под один `case` в `ClearStageData`.
- **Доказательство:** `EvolutionStageService.cs` — `ClearSensoryAssociationsData` вызывался только в блоке `if (targetStage == 0)` внутри `ChangeEvolutionStage`; в `ClearStageDataOnlyForScenarioPreRun` после `ClearSubsequentStagesData(0, clearThrough)` шёл сразу `_psychicSystem?.ClearThinkingCyclesExperienceMemory()`. Симптом воспроизведён регрессионным тестом `EvolutionStageScenarioPreRunClearTests` (до фикса — FAIL «связь должна быть очищена в памяти»).
- **Исправление:** `Common\EvolutionStageService.cs`: в `ClearStageDataOnlyForScenarioPreRun` после `ClearSubsequentStagesData(0, clearThrough)` добавлены вызовы `ClearActionsImagesData()` и `ClearSensoryAssociationsData()` (комментарий-якорь о том, что это те же внестадийные очистки, что и в ветке `targetStage == 0`); обновлён XML-`<summary>` метода. `Reflexes\SensoryAssociationSystem.cs`: публичный `ClearAll()` (память + сохранение пустого файла) — использован без изменений.
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 552/552 (новый `EvolutionStageScenarioPreRunClearTests.ScenarioPreRun_ClearsSensoryAssociations_InMemoryAndOnDisk`); при временном отключении вызовов тест падает (проверено). Ручная: запустить сценарий с флажком очистки — `SensoryAssociations.dat` содержит только шапку, связи CS→CS отсутствуют.
- **Эвристики:** → E10.

## Случай 12. На 2-й стадии режим наблюдения активируется, но автоматизмы не формируются: сессия не фиксирует мотор оператора из-за пустых Target-параметров у G_AD и проверки «focus вышел из Bad» по overall

- **Дата / сборка:** 2026-10-12, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error).
- **Симптом:** в Velum на `EvolutionStage == 2` на пульте появляется индикатор «Режим наблюдения», но `automatizm.dat` остаётся пустым — автоматизмы не создаются. В `AgentLogs.jsonl` состояние гомеостаза скачет Bad↔Normal, `ОР = ОР1`, активируются б/у рефлексы (например, ID=4), но колонка «Автоматизм» пуста.
- **Область:** `Psychic\PsychicSystem.cs` (`ProcessOperatorMotorObservationSession`, `CheckAndRecordOperatorMotor`), `Psychic\OperatorMotorObservationSession.cs` (`RecordOperatorMotor`, `FocusExitedBadZone`), данные среды Velum `AdaptiveActions.dat`.
- **Гипотезы и проверки:**
  1. *Сессия не открывается* — **опровергнуто**: индикатор на пульте (и `OpenSession`) требуют `IsActive`; сессия открыта.
  2. *Мотор не активируется (нет G_AD-активаций)* — **опровергнуто дампом б/у рефлексов**: `GeneticReflexes.dat` (ID=4: EA 64 → мотор 42) активирует G_AD; в логе `б/у рефлекс 4`.
  3. *`RecordOperatorMotor` отклоняет мотор* — **подтверждено данными**: в `C:\ProgramData\VELUM\Data\Actions\AdaptiveActions.dat` у всех G_AD (38, 40, 42, 43, 45, 46, 47, 48) колонка «Target параметры» пуста; `RecordOperatorMotor` требовал непустой `TargetGomeoParamIdArr` и возвращал false («is not a G_AD»).
  4. *`CheckAndRecordOperatorMotor` находит мотор* — **опровергнуто кодом**: фильтр `a.TargetGomeoParamIdArr.Contains(session.FocusParameterId)` при пустом Target отсекает все G_AD → `RecordOperatorMotor` вообще не вызывается.
  5. *Проверка «focus вышел из Bad» идёт по focus-виталу* — **опровергнуто кодом**: `FocusExitedBadZone` читал `AppGlobalState.CurrentOverallState` (общее состояние), а не состояние focus-параметра; проблема другого параметра держит сессию открытой и кредит не выдаётся (план ст. 2 §2.4).
- **Корень:**
  - все механизмы 2-й стадии завязаны на `TargetGomeoParamIdArr` у G_AD (usable atmz, фиксация мотора, механизм 3);
  - в среде Velum G_AD Target-параметров не имеют (связь «мотор ↔ витал» задана через моторные рефлексы: EA → G_AD), поэтому сессия не может связать мотор с focus и ничего не записывает;
  - дополнительно `CheckAndRecordOperatorMotor` не требовал `ActivationPulse > 0` (при `LastMotorPulse == 0` пропускал все действия), а `FocusExitedBadZone` смотрел на overall, а не на focus-параметр.
- **Доказательство:** `AdaptiveActions.dat` — `38|Имя документа|...|5|||0` (5-е поле Target пусто); `PsychicSystem.cs` (до правки) `a.TargetGomeoParamIdArr.Contains(session.FocusParameterId)`; `OperatorMotorObservationSession.cs` (до правки) `action.TargetGomeoParamIdArr == null || Count == 0 → return false`.
- **Исправление:**
  - `Psychic\OperatorMotorObservationSession.cs`: `RecordOperatorMotor` принимает G_AD с непустым Target **или** реально активированный (`ActivationPulse > 0`); добавлена перегрузка `FocusExitedBadZone(bool isFocusBad)` (кредит по focus-виталу), старая оставлена обёрткой над overall;
  - `Psychic\PsychicSystem.cs`: `CheckAndRecordOperatorMotor` фильтрует `ActivationPulse > 0` и ослабляет Target-фильтр (пустой Target допускается); `ProcessOperatorMotorObservationSession` вычисляет состояние focus-параметра через `IsFocusParameterBad` (`GomeostasSystem.GetParameter(id).CurrentState`).
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 569/569. Новые регрессионные тесты: `RecordOperatorMotor_GadWithoutTargetButActivated_Records`, `CreateAutomatizmFromMotor_GadWithoutTarget_CreatesUsefulAutomatizm`, `FocusExitedBadZone_ByFocusParameter_IgnoresOverallBadFromOtherParam`. Ручная: повторить сессию на стадии 2 — `automatizm.dat` пополняется; для механизма 3 (random probe) по-прежнему желательно заполнить Target у G_AD (через AIStudio).
- **Эвристики:** → E13.

## Случай 13. Цепочки у-рефлексов (реплей виртуальных стимулов): бесконечный цикл при повторном реплее одного стимула

- **Дата / сборка:** 2026-10-17, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error, `dotnet test tests\Isida.Tests` → 581/581).
- **Симптом:** при обучении цепочки у-рефлексов через мотор→стимул ассоциации (`MotorStimulusAssociationSystem`) и последующем реплее виртуальных стимулов реплей зацикливался: если мотор М2 ассоциирован со стимулом С6, то при попытке реплея С6 `TryGetNextChainStimulus` возвращал тот же С6 снова и снова — цепочка не завершалась. Пользователь наблюдал «бесконечный повтор одного стимула» вместо последовательного прогона цепочки С4→С5→С6.
- **Область:** `Reflexes\ConditionedReflexFormationService.cs` — `TryGetNextChainStimulus`, `MotorStimulusAssociationSystem.GetActivatableStimuli`.
- **Гипотезы и проверки:**
  1. *`GetActivatableStimuli` возвращает неправильный список* — **опровергнуто кодом**: метод корректно возвращает все стимулы с C ≥ γ для данного мотора (проверено тестами `MotorStimulusAssociationTests`).
  2. *Цепочка должна проверять уникальность стимула* — **подтверждено**: `TryGetNextChainStimulus` выбирал первый `activatable[0]`, но не проверял, не был ли этот стимул уже отреплейнут в текущей цепочке. Если мотор М2 ассоциирован только со С6, то при шаге 3 цепочки `GetActivatableStimuli(М2)` → [С6] → С6 возвращался снова и снова.
  3. *Достаточно добавить флаг «стимул отреплейнут»* — **подтверждено**: решение — `HashSet<int> _chainReplayedStimuli`, который пополняется при каждом реплее и фильтрует выбор.
- **Корень:**
  - `TryGetNextChainStimulus` не помнил, какие стимулы уже были отреплейнуты в текущей цепочке;
  - `GetActivatableStimuli` возвращал те же стимулы при каждом вызове (нет внутренней мутации);
  - при отсутствии защиты от цикла реплей «застревал» на одном стимуле, если мотор→стимул ассоциация давала только один стимул.
- **Доказательство:** `ConditionedReflexFormationService.cs` (до правки): `TryGetNextChainStimulus` → `activatable[0]` без проверки уникальности; тест `FullCycle_TrainThenReplay_WorksTwoSteps` падал на шаге 3 (`step3` возвращал true вместо false).
- **Исправление:** `Reflexes\ConditionedReflexFormationService.cs`:
  - добавлено поле `HashSet<int> _chainReplayedStimuli` — память об отреплейнутых стимулах текущей цепочки;
  - `RecordConditionedReflexActivation`: при step==0 очищает HashSet и добавляет триггер (защита от повтора первого стимула);
  - `TryGetNextChainStimulus`: ищет первый `activatable` стимул, НЕ в HashSet; добавляет выбранный в HashSet; если все стимулы в HashSet → завершение цепочки (`step = 0`);
  - `ResetChainState`: очищает HashSet;
  - тест `FullCycle_TrainThenReplay_WorksTwoSteps` обновлён: шаг 3 ожидает false (завершение).
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 581/581 (включая `MotorStimulusChainReplayTests.FullCycle_TrainThenReplay_WorksTwoSteps`).
- **Эвристики:** → E14.

## Случай 14. `MotorStimulusAssociations.dat` не создаётся при прогоне сценария и не очищается при переходе на стадию 0

- **Дата / сборка:** 2026-10-18, движок ISIDA (Debug, `dotnet build isida.csproj -c Debug` → 0 warning / 0 error, `dotnet test tests\Isida.Tests` → 581/581).
- **Симптом:** в Velum (студия, данные в `C:\ProgramData\VELUM`) после прогона сценария `Scenario_7.dat` файл `C:\ProgramData\VELUM\Data\Reflexes\MotorStimulusAssociations.dat` не создаётся вообще. После первого исправления (инициализация) файл появился, но при переключении на 0-ю стадию не очищался, хотя `SensoryAssociations.dat` в той же папке очищается корректно.
- **Область:** `Common\IsidaEngine.cs` (`InitializeCore`, `Dispose`), `Common\EvolutionStageService.cs` (`ClearSensoryAssociationsData`), `Reflexes\MotorStimulusAssociationSystem.cs`, `Reflexes\ConditionedReflexFormationService.cs` (`_motorStimulusAssociations`).
- **Гипотезы и проверки:**
  1. *Файл не пишется, потому что `Save()` вызывается только при `Dispose`/`ClearAll` (не во время прогона)* — **подтверждено кодом как норма**: `Save()` из `Dispose` (`MotorStimulusAssociationSystem.cs:475`) и `ClearAll` (`:335`); это то же поведение, что и `SensoryAssociations.dat` (пишется при закрытии, а не по ходу). Само по себе это не баг.
  2. *Система вообще не инициализирована в живом движке* — **подтверждено кодом (корень №1)**: `MotorStimulusAssociationSystem.InitializeInstance` вызывался только в `tests\Isida.Tests\EngineFixture.cs`; в `IsidaEngine.InitializeCore` его не было (grep по `IsidaEngine.cs` — ноль вхождений). Значит `IsInitialized == false`, `_motorStimulusAssociations == null` в сервисе формирования, `TryStrengthenMotorStimulusLink` сразу `return` — связи не создаются даже в памяти, `Save()` не вызывается никогда.
  3. *После инициализации файл не очищается при стадии 0, потому что очистка мотор→стимул связей отсутствует* — **подтверждено кодом (корень №2)**: в `ClearSensoryAssociationsData` был добавлен только `ResetChainState()` (сброс шага цепочки), но не `MotorStimulusAssociationSystem.Instance.ClearAll()`. `SensoryAssociationSystem.ClearAll()` чистит только CS↔CS; мотор→стимул — отдельная система с отдельным файлом.
- **Корень:**
  - новая система была встроена только в тестовый харнесс (`EngineFixture`), но не в единую точку сборки живого движка `IsidaEngine` (шаг инициализации, вторичная инициализация `SetMotorStimulusAssociations`, звено выгрузки);
  - очистка при стадии 0 покрывала только сенсорные CS↔CS и шаг цепочки, но не сами мотор→стимул связи.
- **Доказательство:** grep `IsidaEngine.cs` до правки: `MotorStimulusAssociation` — 0 вхождений, `SensoryAssociationSystem.InitializeInstance` — есть (`:737`); `EvolutionStageService.ClearSensoryAssociationsData` до правки содержал `SensoryAssociationSystem.Instance.ClearAll()` + `ResetChainState()`, но не `MotorStimulusAssociationSystem.Instance.ClearAll()`.
- **Исправление:**
  - `Common\IsidaEngine.cs`: шаг 12 — `MotorStimulusAssociationSystem.InitializeInstance(GeneticReflexes, ConditionedReflexes)` сразу после сенсорной системы (те же зависимости); шаг 15 — `context.ConditionedReflexFormation.SetMotorStimulusAssociations(MotorStimulusAssociationSystem.Instance)`; `Dispose` — `SafeDispose(MotorStimulusAssociationSystem.Instance, ...)` рядом с сенсорной системой (зависимостно-безопасный порядок, до `GeneticReflexes`);
  - `Common\EvolutionStageService.cs`: в `ClearSensoryAssociationsData` добавлен `if (MotorStimulusAssociationSystem.IsInitialized) MotorStimulusAssociationSystem.Instance.ClearAll();` перед `ResetChainState()` (метод вызывается и при стадии 0 `:176`, и при предзапуске сценария `:225`).
- **Проверка:** `dotnet build isida.csproj -c Debug` → 0 warning / 0 error; `dotnet test tests\Isida.Tests` → 581/581; `Build-Velum.ps1 Debug` → EXIT=0. Ручная: перезапустить студию, прогнать сценарий, закрыть — `MotorStimulusAssociations.dat` создаётся; переключить на стадию 0 — файл очищается до шапки.
- **Эвристики:** → E15.

## Эвристики



- **E8. Если обработчик по таймеру читает разделяемое (в т.ч. статическое) состояние вне дока, а выгрузка обнуляет его под локом — снимай состояние в локальный снапшот под тем же локом и жди фактического конца обработчика перед обнулением.** `if (!_isRunning)` под `lock` защищает только вход; тело обработчика, читающее поля вне дока, всё равно видит их обнулёнными. Фиксированный `Thread.Sleep(N)` перед `Clear()`/`Dispose()` не является синхронизацией: если длительность обработчика превышает `N`, гонка воспроизводится. Корректно: (а) снять снимок всех используемых ссылок под тем же `lock`, что и мутатор, и дальше работать по локалям; (б) сигналом (`ManualResetEventSlim`) сообщать «активного обработчика нет» и ждать его **до** входа в лок мутатора (ожидание внутри дока = взаимная блокировка, если обработчик берёт тот же лок). Диагностический признак: стек-трейс с одним фреймом метода-обработчика и `NullReferenceException` на разыменовании поля; ошибки идут парами/пачками в момент закрытия. См. случай 7.

- **E9. Удаление публичного контракта движка не ломает адаптер на компиляции, если адаптер зовёт его рефлексией — оно ломает его тихо, в рантайме.** Если хост (Velum) обращается к методам движка через `Type.GetType(...).GetMethod(...)` ради совместимости со старыми сборками, отсутствие метода не даёт ошибку сборки: срабатывает `fallback`, который может быть семантически неверным (наказан не тот объект — УР вместо связи CS₁→CS₂). Правило: при откате/рефакторинге движка сверять не только `csproj`-ссылки, но и рефлексийные контракты адаптеров (`GetMethod`/`GetProperty` по строковым именам); при восстановлении возвращать и имя, и форму (в т.ч. `int[]` вместо кортежа) и покрывать регрессией. Диагностический признак: «фича адаптера молча деградировала до fallback» при зелёной сборке обоих проектов. См. случай 8.

- **E10. Несколько путей «сброса к состоянию X» должны звать один и тот же набор очисток; дублирование набора в разных методах — источник рассинхрона.** Если «переход на стадию 0» делает внестадийные очистки (файлы, не привязанные к номерам стадий: `action_images`, `SensoryAssociations` и т.п.) прямо в `ChangeEvolutionStage`, а «предзапуск сценария с флагом очистки» — отдельный метод, который эти же очистки не повторяет, то поведение расходится: на пульте чистится, при старте сценария — нет. Признак: очистка «работает в одном UI-действии и молчит в другом» при одинаковой формулировке («чистить как при переходе на 0»). Правило: вынести набор очисток стадии 0 в один приватный метод и звать его из обоих путей, а `ClearStageData(stage)` держать только для привязанных к стадии данных. См. случай 9.

- **E1. Флаги «особого происхождения»/«авторитарности» не защищают запись от глобальных моделей забывания, если модель применяется по ключу стимула, а не по происхождению.** Активное угасание (RW, λ=0) выбирает УР по `Level3`/`ToneId`/`MoodId` и угашает **все** совпадения; происхождение рефлекса (ручная авторитарная запись, вторичный порядок) в критерии не участвует. Если рефлекс должен переживать отсутствие подкрепления иначе, чем «выученный», различие обязано быть **явным предикатом в критерии угасания/удаления**, а не подразумеваться. Диагностический признак: рефлекс, «созданный вручную и надёжно», тихо деградирует по той же кривой, что и выученные. См. случай 1.
- **E2. Параметры модели (порог, скорость обучения, окно τ, min strength) должны читаться из единого конфигурационного источника, а не дублироваться приватными дефолтами.** Если класс-сущность (`ConditionedReflex`) хранит собственные копии `_minAssociationStrength`/`_activationThreshold`/`_learningRate`/`_timeWindowPulses`, а система — `_settings` из `ConditionedReflexSettings.dat`, любое изменение файла настроек не влияет на поведение: правка «ничего не меняет». Дополнительно порядок загрузки обязан быть «настройки → данные», иначе первый прогон читает дефолты. Диагностический признак: правки в файле настроек не отражаются на поведении; разные части системы используют разные значения одного параметра. См. случай 1.

- **E3. Порог активации — естественная граница режимов угасания.** Угасание должно быть разнородным по обе стороны порога γ: активное (частое, при CS без US) — только ниже порога; выше — редкое пассивное. Обе ветви нелинейны по расстоянию до порога. Диагностический признак: «сильный» рефлекс деградирует той же скоростью, что и «слабый» — значит, порог не учтён в критерии угасания. См. случаи 1, 2.

- **E4. Числовой парсинг/сериализация обязаны фиксировать культуру и не полагаться на неоднозначные перегрузки `Split`.** `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)` в net48 — это НЕ удаление пустых записей, а `Split(params char[])` с разделителями `','` и `'\u0001'` (значение enum, приведённое к `char`). Признак: пустые сегменты превращаются в `0`/дефолт. Плюс при ru-RU запятая одновременно является разделителем списка и десятичным разделителем, поэтому `double.Parse`/`ToString` без `CultureInfo.InvariantCulture` дают взаимно несовместимые результаты. Диагностический признак: список разбирается «почти правильно», но с лишними нулями, а дробные значения распадаются на целые. См. случай 3.

- **E5. Валидатор обязан возвращать причину отказа во всех режимах (строгом и мягком).** Если `Validate*` заполняет `errorMessage`/`warnings` раздельно, мягкая ветка вызывающего метода обязана агрегировать **оба** канала; иначе API тихо возвращает «отказ без объяснения». Диагностический признак: при `strictValidation: true` понятное исключение, при `false` — пустые warnings и код-сентинел (0). Отдельно: инварианты-сеттеры (например, `Vigor ∈ [1..10]`) срабатывают до мягкой валидации и всегда кидают исключение — это не баг, а контракт. См. случай 4.

- **E6. Конкурентный слой обучения (ΣV) — обязательная часть модели при нескольких CS на один US, иначе плодятся «мусорные» рефлексы.** Если подкрепление λ=β выдаётся каждому CS независимо от предсказательной силы уже существующих CS, любой шумовой CS, предъявленный позже, обучится наравне с целевым (Kamin blocking не воспроизводится). Корректная точка ΣV — там, где известно испытание целиком (сервис формирования), а не в атомарном усилении рефлекса. Авторитарная (ручная) запись — исключение: она должна обходить подавление. Диагностический признак: «нужный рефлекс перебивается мусорными», при этом внутри одного испытания «последний CS побеждает». См. случай 5.

- **E7. Состояние «ожидание подкрепления» обязано сниматься по ЛЮБОМУ каналу подкрепления, а не только по прямому US.** Если у записи есть несколько способов быть подкреплённой (прямой US и вторичный — активацией последующего CR), а pending-флаг сбрасывается только по одному из них, то после подкрепления вторым каналом запись на следующем шаге ошибочно классифицируется как неподкреплённая и активно гасится. Диагностический признак: рефлекс создаётся с крепостью ≥ γ и сразу «тает» без явного CS-без-US. Любой `*Awaiting*`/pending-флаг обязан иметь явный подтверждающий сброс для каждого пути подкрепления. См. случай 6.

- **E11. Формула подкрепления обязана учитывать ВСЕ каналы предъявления CS, а не только сенсорные.** Если в образе есть несколько независимых списков модальностей (запах, речь, команда, цвет), а `CompoundModalityCount` считает только сенсорные каналы, то CS, предъявленный через «вторичный» канал (командный, моторный), не попадает в формулу ΣV и не получает подавления. Корректная точка ΣV обязана читать все каналы. Диагностический признак: «сенсорная суммация молчит на вторичных каналах» при зелёной сборке. См. случай 10.

- **E12. При добавлении нового канала в метод сравнения образов нужно различать «тот же канал, строгое подмножество» и «разные каналы, допуск для кросс-канальной связи».** Если в `StimulusImagesHierarchyCompatible` (или аналогичном методе) добавляется проверка подмножества для нового канала (команда, цвет) симметрично существующим (запах, фраза), то стимул по новому каналу vs триггер по старому не проходит строгую проверку подмножества → образ отфильтровывается до гейта сенсорной прекондиции. Правило: при добавлении канала в сравнение — явно проверить, не отсекаются ли кросс-канальные пары (пустой список одного канала + непустой другого). Диагностический признак: «ассоциация крепкая, стимул предъявлен, а рефлекс не активируется» при зелёной сборке. См. случай 11.

- **E13. Контракт «обязательного» поля проверяй против фактических данных хоста, а не только против тестового харнесса.** Если движок требует у сущности непустой набор (например, `TargetGomeoParamIdArr` у G_AD) и все механизмы стадии завязаны на него, а хост (Velum) это поле не заполняет (связь задана в другом месте — через моторные рефлексы EA → G_AD), фича молча не работает при зелёной сборке и зелёных тестах: тесты создают сущности с полем (`AddAction(..., targetGomeoParamIdArr)`) — хост без. Диагностический признак: «индикатор режима показывается, а результата нет» + пустой файл-артефакт (`automatizm.dat`). Правило: при переносе механизма в хост — проверять фактический дамп данных хоста; обязательные поля либо заполняет хост, либо механизм имеет мягкий fallback (по факту активации, по probe-EA). См. случай 12.

- **E14. При реплее виртуальных стимулов память об отреплейнутых стимулах обязательна — иначе цикл.** Если цепочка у-рефлексов строится через мотор→стимул ассоциации (`MotorStimulusAssociationSystem`) и реплей идёт по `GetActivatableStimuli(motorId)`, то при отсутствии `HashSet` отреплейнутых стимулов один и тот же стимул будет возвращаться бесконечно — мотор→стимул связь не мутирует и даёт один и тот же результат. Диагностический признак: реплей «застревает» на одном стимуле, цепочка не завершается, `step` не обнуляется. Правило: `TryGetNextChainStimulus` обязан хранить память о отреплейнутых стимулах (`HashSet<int>`) и фильтровать их при выборе следующего; при исчерпании всех новых стимулов — завершать цепочку (`step = 0`). См. случай 13.

- **E15. Новая система, добавленная только в тестовую фикстуру, не работает в живом движке — это гарантированный баг «тихого отсутствия».**
 Если класс (сервис, ассамблия, система) зарегистрирован в `EngineFixture` и покрыт тестами, но не добавлен в `IsidaEngine.InitializeCore` (шаг инициализации + зависимостно-безопасный порядок + вторичная инициализация через `Set*`) и `IsidaContext.Dispose` (звено выгрузки), то в реальном сценарии `IsInitialized == false`, все ссылки `null`, методы выходят через ранний `return` — сборка и тесты зелёные, но поведение пустое. Диагностический признак: «в тесте работает, в студии нет» или «файл-артефакт не создаётся» при зелёной сборке. Правило: каждый новый `InitializeInstance`/`IDisposable`-класс обязан иметь три якоря в `IsidaEngine`: шаг инициализации, звено `Dispose`, при необходимости — вторичная инициализация. См. случай 14.

- **E16. При добавлении очистки данных при стадии 0 проверяй, что чистишь ВСЕ системы, а не только «первую встреченную».** Если новая система (мотор→стимул) имеет свой файл и свой метод очистки (`ClearAll`), а `ClearSensoryAssociationsData` (или аналогичный метод очистки стадии 0) содержит только вызов для `SensoryAssociationSystem`, то данные новой системы переживут переключение стадии — как если бы их не было. Диагностический признак: «одна система чистится при стадии 0, другая — нет» при зелёной сборке. Правило: `ClearSensoryAssociationsData` (и любые другие методы очистки стадии 0) обязан охватывать все подсистемы, владеющие файлами данных, не привязанными к номерам стадий. См. случай 14.



## Указатель случаев

| № | Симптом | Область | Статус |
|---|---|---|---|
| 1 | УР ID1/ID2 после авторитарной записи упали ниже порога без «Запретить» | Reflexes (ConditionedReflexesSystem) | Исправлен |
| 2 | Угасание УР: активное только ниже γ, пассивное выше (нелинейно) | Reflexes (ConditionedReflexesSystem) | Изменение модели |
| 3 | `ParseIntList`/`ParseDoubleList`: пустые сегменты → `0`, дробные ломаются на ru-RU | Common (AddUtils) | Исправлен |
| 4 | `AddAction` (нестрогий режим): причина отказа теряется, warnings пусты | Actions (AdaptiveActionsSystem) | Исправлен |
| 5 | «Мусорные» у-рефлексы: шумовой CS учится наравне с целевым (нет ΣV) | Reflexes (ConditionedReflexFormationService) | Исправлен (новый слой) |
| 6 | CR₂ не активируется на пульсе 90: вторично подкреплённый CS₂ гасился как «CS без US» | Reflexes (ConditionedReflexFormationService) | Исправлен |
| 7 | `NullReferenceException` в `GlobalTimer.ProcessAgentPulse` парами при выгрузке (гонка `ClearSystems` и пульса) | Common (GlobalTimer, IsidaEngine) | Исправлен |
| 8 | Откат цепей CS→CS снёс адресный штраф связи: «Запретить» в Velum бьёт по УР целиком | Reflexes (SensoryAssociationSystem, AppGlobalState, ReflexesActivator) | Восстановлен |
| 9 | Флажок «очистка при старте» сценария не чистит `SensoryAssociations.dat` (в отличие от перехода на 0 на пульте) | Common (EvolutionStageService, GomeostasSystem) | Исправлен |
| 10 | Сенсорная суммация не срабатывает на CS через командный канал (`CompoundModalityCount` не учитывает `CommandPatternIdList`) | Reflexes (PerceptionImagesSystem) | Исправлен |
| 11 | Сенсорная прекондиция не срабатывает на стимул через командный канал (`StimulusImagesHierarchyCompatible` отсекает кросс-канальные пары) | Reflexes (PerceptionImagesSystem, ConditionedReflexesSystem) | Исправлен |
| 12 | На 2-й стадии режим наблюдения активен, но автоматизмы не создаются (пустой `Target` у G_AD + проверка Bad по overall) | Psychic (PsychicSystem, OperatorMotorObservationSession) | Исправлен |
| 13 | Реплей цепочки у-рефлексов зацикливался: один и тот же стимул возвращался бесконечно (отсутствие защиты от цикла) | Reflexes (ConditionedReflexFormationService, MotorStimulusAssociationSystem) | Исправлен |
| 14 | `MotorStimulusAssociations.dat` не создавался и не очищался при стадии 0: новая система не встроена в живой движок (только в тестовую фикстуру) | Common (IsidaEngine, EvolutionStageService) | Исправлен |


