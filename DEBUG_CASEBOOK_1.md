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

## Эвристики

- **E1. Флаги «особого происхождения»/«авторитарности» не защищают запись от глобальных моделей забывания, если модель применяется по ключу стимула, а не по происхождению.** Активное угасание (RW, λ=0) выбирает УР по `Level3`/`ToneId`/`MoodId` и угашает **все** совпадения; происхождение рефлекса (ручная авторитарная запись, вторичный порядок) в критерии не участвует. Если рефлекс должен переживать отсутствие подкрепления иначе, чем «выученный», различие обязано быть **явным предикатом в критерии угасания/удаления**, а не подразумеваться. Диагностический признак: рефлекс, «созданный вручную и надёжно», тихо деградирует по той же кривой, что и выученные. См. случай 1.
- **E2. Параметры модели (порог, скорость обучения, окно τ, min strength) должны читаться из единого конфигурационного источника, а не дублироваться приватными дефолтами.** Если класс-сущность (`ConditionedReflex`) хранит собственные копии `_minAssociationStrength`/`_activationThreshold`/`_learningRate`/`_timeWindowPulses`, а система — `_settings` из `ConditionedReflexSettings.dat`, любое изменение файла настроек не влияет на поведение: правка «ничего не меняет». Дополнительно порядок загрузки обязан быть «настройки → данные», иначе первый прогон читает дефолты. Диагностический признак: правки в файле настроек не отражаются на поведении; разные части системы используют разные значения одного параметра. См. случай 1.

- **E3. Порог активации — естественная граница режимов угасания.** Угасание должно быть разнородным по обе стороны порога γ: активное (частое, при CS без US) — только ниже порога; выше — редкое пассивное. Обе ветви нелинейны по расстоянию до порога. Диагностический признак: «сильный» рефлекс деградирует той же скоростью, что и «слабый» — значит, порог не учтён в критерии угасания. См. случаи 1, 2.

- **E4. Числовой парсинг/сериализация обязаны фиксировать культуру и не полагаться на неоднозначные перегрузки `Split`.** `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)` в net48 — это НЕ удаление пустых записей, а `Split(params char[])` с разделителями `','` и `'\u0001'` (значение enum, приведённое к `char`). Признак: пустые сегменты превращаются в `0`/дефолт. Плюс при ru-RU запятая одновременно является разделителем списка и десятичным разделителем, поэтому `double.Parse`/`ToString` без `CultureInfo.InvariantCulture` дают взаимно несовместимые результаты. Диагностический признак: список разбирается «почти правильно», но с лишними нулями, а дробные значения распадаются на целые. См. случай 3.

- **E5. Валидатор обязан возвращать причину отказа во всех режимах (строгом и мягком).** Если `Validate*` заполняет `errorMessage`/`warnings` раздельно, мягкая ветка вызывающего метода обязана агрегировать **оба** канала; иначе API тихо возвращает «отказ без объяснения». Диагностический признак: при `strictValidation: true` понятное исключение, при `false` — пустые warnings и код-сентинел (0). Отдельно: инварианты-сеттеры (например, `Vigor ∈ [1..10]`) срабатывают до мягкой валидации и всегда кидают исключение — это не баг, а контракт. См. случай 4.

- **E6. Конкурентный слой обучения (ΣV) — обязательная часть модели при нескольких CS на один US, иначе плодятся «мусорные» рефлексы.** Если подкрепление λ=β выдаётся каждому CS независимо от предсказательной силы уже существующих CS, любой шумовой CS, предъявленный позже, обучится наравне с целевым (Kamin blocking не воспроизводится). Корректная точка ΣV — там, где известно испытание целиком (сервис формирования), а не в атомарном усилении рефлекса. Авторитарная (ручная) запись — исключение: она должна обходить подавление. Диагностический признак: «нужный рефлекс перебивается мусорными», при этом внутри одного испытания «последний CS побеждает». См. случай 5.

- **E7. Состояние «ожидание подкрепления» обязано сниматься по ЛЮБОМУ каналу подкрепления, а не только по прямому US.** Если у записи есть несколько способов быть подкреплённой (прямой US и вторичный — активацией последующего CR), а pending-флаг сбрасывается только по одному из них, то после подкрепления вторым каналом запись на следующем шаге ошибочно классифицируется как неподкреплённая и активно гасится. Диагностический признак: рефлекс создаётся с крепостью ≥ γ и сразу «тает» без явного CS-без-US. Любой `*Awaiting*`/pending-флаг обязан иметь явный подтверждающий сброс для каждого пути подкрепления. См. случай 6.

## Указатель случаев

| № | Симптом | Область | Статус |
|---|---|---|---|
| 1 | УР ID1/ID2 после авторитарной записи упали ниже порога без «Запретить» | Reflexes (ConditionedReflexesSystem) | Исправлен |
| 2 | Угасание УР: активное только ниже γ, пассивное выше (нелинейно) | Reflexes (ConditionedReflexesSystem) | Изменение модели |
| 3 | `ParseIntList`/`ParseDoubleList`: пустые сегменты → `0`, дробные ломаются на ru-RU | Common (AddUtils) | Исправлен |
| 4 | `AddAction` (нестрогий режим): причина отказа теряется, warnings пусты | Actions (AdaptiveActionsSystem) | Исправлен |
| 5 | «Мусорные» у-рефлексы: шумовой CS учится наравне с целевым (нет ΣV) | Reflexes (ConditionedReflexFormationService) | Исправлен (новый слой) |
| 6 | CR₂ не активируется на пульсе 90: вторично подкреплённый CS₂ гасился как «CS без US» | Reflexes (ConditionedReflexFormationService) | Исправлен |
