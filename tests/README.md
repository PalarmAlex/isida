# Isida.Tests — тесты движка ISIDA

Отдельный xUnit-проект для проверки **публичных** модулей движка `isida`.
В отличие от подхода с линковкой исходников, здесь тестовый проект ссылается
на уже собранную `isida.dll` и вызывает её публичные API.

## Почему ссылка на DLL, а не линковка исходников

- `isida.csproj` — не-SDK проект под .NET Framework 4.8 с WPF (`PresentationCore`,
  `PresentationFramework`, `System.Windows.Forms`) и зависимостями из `packages\`.
  Линковка классов потянула бы все эти зависимости и внутренние типы движка.
- Ссылка на `isida.dll` тестирует реальный код, включая типы, зависящие от других
  модулей движка (`FileValidator` → `AgentVisualColor` и т.п.).
- Ограничение: `internal`-типы не видны по умолчанию. Для `ValidationService`,
  `StyleCombinationsManager` и внутренних методов `HomeostasisCalculator` в
  `Properties/AssemblyInfo.cs` добавлен `[assembly: InternalsVisibleTo("Isida.Tests")]`.

## Предварительное условие

Перед запуском тестов `isida.dll` должен быть собран в конфигурации Debug:

```powershell
& "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "D:\Программы\ISIDA\Programms\isida\isida.csproj" `
  /t:Build /p:Configuration=Debug /v:minimal
```

Проект тестов ссылается на `..\..\bin\Debug\isida.dll` (`<Private>true</Private>`),
поэтому собранные зависимости движка (Newtonsoft.Json и пр.) копируются в выход
тестов автоматически.

## Запуск

```powershell
cd "D:\Программы\ISIDA\Programms\isida\tests\Isida.Tests"
dotnet test
```

## Структура

- `Isida.Tests.csproj` — net48 + xUnit, ссылка на `isida.dll`.
- `Isida.Tests.sln` — классическое решение **для Visual Studio** (используйте его при работе в IDE).
- `Isida.Tests.slnx` — решение нового формата для `dotnet` CLI.
- `*Tests.cs` — наборы тестов по модулям движка.

> **Visual Studio:** открывайте `Isida.Tests.sln`, а не `.slnx`. NuGet-интеграция VS 17.14
> не выполняет restore из `.slnx` и при сборке показывает «Произошел сбой операции,
> так как не удалось загрузить подробности для проекта» (сама компиляция при этом
> проходит). В `.sln` restore работает штатно.

## Покрытые модули

| Модуль | Что проверяется |
| --- | --- |
| `ISIDA.Common.AddUtils` | парсинг/сериализация списков, сравнение, clamp |
| `ISIDA.Common.AntagonistValidator` | поиск антагонистических конфликтов |
| `ISIDA.Common.GenerateListContentPreprocessor` | очистка BOM/zero-width/Trim |
| `ISIDA.Common.TooltipMultilineText` | форматирование подсказок |
| `ISIDA.Common.IsidaDataPaths` | построение путей данных |
| `ISIDA.Common.SettingsValidator` | диапазоны настроек, шаблон каталогов |
| `ISIDA.Common.FileValidator` | валидация форматов файлов данных |
| `ISIDA.Common.EvolutionStageChangeResult` | модель результата смены стадии |
| `ISIDA.Gomeostas.HomeostasisCalculator` | оценка состояния, критические изменения, зоны |
| `ISIDA.Gomeostas.ParameterData` | валидация сеттеров, зоны активации стилей |
| `ISIDA.Gomeostas.StyleActivationCondition` / `StyleAntagonism` | модели стилей |
| `ISIDA.Actions.ParameterInfluence` | диапазон влияния |
| `ISIDA.Reflexes.AgentVisualColor` | коды зрительного канала |
| `ISIDA.Gomeostas.ValidationService` (internal) | валидация активаций, имена параметров |
| `ISIDA.Gomeostas.StyleCombinationsManager` (internal) | генерация/сохранение/загрузка комбинаций стилей |
| `ISIDA.Gomeostas.HomeostasisCalculator.GetStateForStyleActivation` (internal) | зоны активации стилей 0–6 |
| `ISIDA.Scenarios.ScenarioEnvironmentProbeFormat` | сериализация воздействий среды «+id,-id» |
| `ISIDA.Scenarios.ScenarioHomeostasisValuesFormat` | сериализация начального гомеостаза «id=value» |
| `ISIDA.Scenarios.ScenarioPulseSchedule` | расчёт номеров пульсов по шагам |
| `ISIDA.Scenarios.ScenarioLineRow` / `ScenarioHeader` / `ScenarioDocument` | модели сценария, клонирование, отображение |
| `ISIDA.Scenarios.ScenarioLogExpectationRow` / `…ColumnSkips` | модели ожидаемых логов |
| `ISIDA.Psychic.Importance.ExtremImportance` | модель экстремальной значимости |
| `ISIDA.Reflexes.PerceptionImagesSystem` (статические) | иерархия «часть — целое», подмножества, равенство образов |
| `ISIDA.Reflexes.ConditionedReflexesSystem.ConditionedReflex` | валидаторы, TTL, MaxAchieved, пороги |
| `ISIDA.Reflexes.ConditionedReflexesSystem` (интеграц.) | модель угасания УР на живом движке |
| `ISIDA.Reflexes.ConditionedReflexFormationService` (интеграц.) | формирование CS→US, вторичное обусловливание, сенсорная прекондиция |
| `ISIDA.Reflexes.ConditionedReflexFormationService` (ΣV) | конкурентное обучение: блокировка шумового CS окрепшим целевым, обратимость настройки |

## Интеграционные тесты

`ConditionedReflexesSystemIntegrationTests` поднимает **реальную цепочку движка**
на временном каталоге данных:

```
InformationEnvironmentSystem → GomeostasSystem → InfluenceActionSystem →
AdaptiveActionsSystem → SensorySystem → GeneticReflexesSystem →
PerceptionImagesSystem → ConditionedReflexesSystem
```

Инициализация занимает ~0.3 c, файлы данных пишутся в `%TEMP%\isida_engine_<guid>`
и удаляются по завершении. Тесты защищают модель угасания УР (Случай 1/2 кейсбука):

- «авторитарный» УР (крепость ≥ γ) не угашается активным угасанием;
- «слабый» УР (< γ) угашается активно, крепость падает;
- угасание по одному пусковому образу не затрагивает рефлексы другого образа;
- `GetInitialLifetimeForOrder` берёт TTL из настроек и делит на K для вторичных.

Синглтоны движка статические, поэтому интеграционные тесты объединены в коллекцию
`EngineIntegration` с `DisableParallelization = true` и общей фикстурой.

Помимо угасания, на живом движке проверяются:

- `ConditionedReflexFormationServiceTests` — CS→US в окне τ, накопление C по RW,
  активное угасание CS без US, «последний CS побеждает» при шуме, сенсорная
  прекондиция против вторичного CR;
- `CheckSecondaryConditioningTests` — вторичное обусловливание (порядки 2/3,
  каскад, слабый/протухший родитель);
- `CompetitiveLearningTests` — конкурентный слой ΣV: при включённом слое шумовой CS
  не достигает порога, при выключенном учится независимо, авторитарная запись
  не подавляется;
- `SensoryAssociationSystemTests` / `ResolveHierarchicalConditionedActivationTests` —
  второй канал (CS→CS) и иерархический гейт активации.

## Границы покрытия

Этими тестами покрыты **чистые и слабо-зависимые** модули (утилиты, модели данных,
валидаторы, форматы сериализации, расчётные функции) и **слой условных рефлексов**
через интеграционный запуск движка на временном каталоге.

Сознательно **не покрыты**:

- прочие системы-оркестраторы (`IsidaEngine`, `PsychicSystem`, `ThinkingCyclesSystem`,
  автоматизмы, цепочки) — требуют полного жизненного цикла и пульсации;
- загрузчики данных (`*FileLoader`) на реальных `.dat` из каталога проекта;
- UI-модели/представления и подсистемы логов (`Logger`, `ResearchLogger`) —
  побочные эффекты на диск и WPF-зависимости.

## Замечания по поведению движка, выявленные тестами

1. `AddUtils.ParseIntList`/`ParseDoubleList` ранее вызывали `Split(',', (char)StringSplitOptions.RemoveEmptyEntries)`.
   В net48 это связывалось с перегрузкой `Split(params char[])` — разделителями
   становились `','` и `'\0'`, а **не** удаление пустых записей, поэтому пустые
   сегменты (`1,,2`, `1,2,,`) превращались в `0`. **Исправлено:** используется
   `Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)`.
2. `AddUtils.ParseDoubleList` ранее использовал текущую культуру процесса и делил
   строку по запятой; при `ru-RU` дробные значения вроде `1,5` разбирались как
   список `[1, 5]`. **Исправлено:** разбор/сериализация выполняются через
   `CultureInfo.InvariantCulture` (точка — десятичный разделитель, запятая —
   только разделитель списка). См. `DEBUG_CASEBOOK_1.md`, Случай 3.
