# HANDOFF — контекст для нового чата (ISIDA)

> Прочитать первым делом: `DEBUG_CASEBOOK_1.md` (протокол отладки + эвристики), затем этот файл. Соблюдать правила `AGENTS.md` (если есть в проекте).

## Проект
- ISIDA — движок (C#/.NET, не-SDK csproj `isida.csproj`), папка: `D:\Программы\ISIDA\Programms\isida`.
- ISIDA — отдельный workspace от Velum (`D:\Программы\Velum\Velum`, SolidWorks add-in). Из чата Velum править ISIDA нельзя (инструменты ограничены workspace) — новый чат открывать в workspace ISIDA.
- Сборка только через MSBuild:
  - `MSBuild isida.csproj -t:Build -p:Configuration=Debug`
  - MSBuild: `/c/Program Files/Microsoft Visual Studio/18/Professional/MSBuild/Current/Bin/MSBuild.exe`
  - чистая сборка: `-t:Rebuild`

## Текущая задача (выполнена в прошлом чате)
Нелинейное угасание условных рефлексов (УР):
- Активное угасание — только для рефлексов ниже порога γ (`ActivationThreshold`, по умолчанию 0.6).
- Выше γ — медленное пассивное угасание раз в `PassiveDecayPeriodPulses` (по умолчанию 1000) пульсов.
- Обе ветви нелинейны: активное тем сильнее, чем ниже C от γ; пассивное тем слабее, чем выше C от γ.

## Состояние файла `Reflexes\ConditionedReflexesSystem.cs` (~2078 строк, UTF-8 BOM, CRLF)
- `ConditionedReflex`: добавлены `PassiveDecayPeriodPulses = 1000`, `PassiveDecayAccumulator` (стр. 176, 181).
- `ApplyActiveExtinction`: только при `C < γ`; нелинейный множитель `drop=(γ−C)/γ`, `alphaEffective=alpha·(1+2·drop)` (стр. 416–422).
- `ApplyActiveExtinctionForStimulus`: ранний `continue`, если `AssociationStrength >= _settings.ActivationThreshold` (стр. 1039+).
- Новый `ApplyPassiveDecay()` (стр. 913): только `C ≥ γ`; шаги `delta/period`; `rate = baseRate·(1−above)·steps`, где `above=(C−γ)/(1−γ)`; кламп по `MinAssociationStrength`; логирование.
- `ApplyDecay()` вызывает `ApplyPassiveDecay()` + `RemoveExpiredReflexes()` (стр. 961+).
- `UpdateAgentLifetime` (стр. 1472): при накоплении `period` вызывает `ApplyPassiveDecay()`.
- `ConditionedReflexSettings`: поле `PassiveDecayPeriodPulses` (стр. 526); парсинг (стр. 1919); сохранение (стр. 2022).
- `LoadConditionedReflexes`: `reflex.PassiveDecayAccumulator = reflex.LastActivation` (стр. 1801).
- Ранее: `ConditionedReflex` переведён на `_settings` вместо захардкоженных полей; `LoadConditionedReflexSettings()` до `LoadConditionedReflexes()`; логирование активного угасания; убран двойной дележ α в `StrengthenAssociation`.

## Проверки
- `MSBuild isida.csproj -t:Build -p:Configuration=Debug` → успешно; `bin\Debug\isida.dll` обновлён.
- Ручная: рефлекс выше γ не проваливается за десятки импульсов без US; рефлекс ниже γ угашает активнее по мере приближения к 0.

## Осталось
- Ручной прогон в живом ISIDA для подтверждения обеих ветвей (в логе — редкие строки пассивного угасания для «сильных» УР).
- Бэкапы рядом с файлом: `.bak`, `.bak2`, `.bak3`, `.bak4` (удалить после подтверждения).
- Кейсбук `DEBUG_CASEBOOK_1.md`: Случай 1 (ID1/ID2), Случай 2 (модель угасания), эвристики E1–E3.

## Ограничения среды
- Кириллицу передавать в shell только через файлы (не инлайн): `printf`/`echo` в bash ломают кодировку.
- Читать/писать файлы через `py`-скрипты с bytes/`:raw`.
- Проверка кодировки: `py -c "d=open('f','rb').read(); print(d[:3]==b'\xef\xbb\xbf', d.count(b'\r\n'))"`.
