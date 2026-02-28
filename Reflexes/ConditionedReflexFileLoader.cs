using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Результат загрузки условных рефлексов из списка по шаблону.
  /// </summary>
  public sealed class ConditionedReflexLoadResult
  {
    /// <summary>Общее количество строк в загруженном тексте (файле).</summary>
    public int TotalLines { get; set; }

    /// <summary>Количество строк, пропущенных как пустые или начинающиеся с # (комментарии).</summary>
    public int SkippedEmptyOrComment { get; set; }

    /// <summary>Количество успешно созданных условных рефлексов.</summary>
    public int Created { get; set; }

    /// <summary>Количество строк с ошибкой формата (меньше 4 полей или неверное состояние).</summary>
    public int InvalidFormat { get; set; }

    /// <summary>Количество строк, где указанное состояние не распознано (Плохо/Норма/Хорошо).</summary>
    public int NotFoundState { get; set; }

    /// <summary>Количество строк, где указанный стиль не найден в справочнике.</summary>
    public int NotFoundStyle { get; set; }

    /// <summary>Количество строк, где указанное воздействие с пульта (триггер безусловного рефлекса) не найдено.</summary>
    public int NotFoundTrigger { get; set; }

    /// <summary>Количество строк, где не найден безусловный рефлекс по условиям (состояние, стили, триггер).</summary>
    public int NotFoundGeneticReflex { get; set; }

    /// <summary>Количество строк, где фраза (новый триггер) не найдена или не распознана.</summary>
    public int NotFoundOrInvalidPhrase { get; set; }

    /// <summary>Количество строк, пропущенных из-за дубликата (условный рефлекс с такими условиями уже существует).</summary>
    public int Duplicate { get; set; }

    /// <summary>Количество строк с прочими ошибками при добавлении рефлекса.</summary>
    public int OtherError { get; set; }

    /// <summary>Число строк с данными (всего строк минус пропущенные пустые и комментарии).</summary>
    public int DataLines => TotalLines - SkippedEmptyOrComment;

    /// <summary>Общее число строк, по которым не удалось создать рефлекс (все категории ошибок).</summary>
    public int Failed => InvalidFormat + NotFoundState + NotFoundStyle + NotFoundTrigger +
        NotFoundGeneticReflex + NotFoundOrInvalidPhrase + Duplicate + OtherError;

    /// <summary>Формирует текстовый отчёт по результату загрузки для отображения пользователю.</summary>
    /// <returns>Многострочная строка с итогами.</returns>
    public string ToSummaryString()
    {
      var parts = new List<string>
      {
        $"Всего строк в файле: {TotalLines}",
        $"Пропущено (пустые/комментарии): {SkippedEmptyOrComment}",
        $"Строк с данными: {DataLines}",
        $"Создано условных рефлексов: {Created}"
      };
      if (Failed > 0)
      {
        if (InvalidFormat > 0) parts.Add($"Ошибка формата (ожидается минимум 4 поля, опционально 6 с тоном и настроением): {InvalidFormat}");
        if (NotFoundState > 0) parts.Add($"Состояние не распознано: {NotFoundState}");
        if (NotFoundStyle > 0) parts.Add($"Стиль не найден: {NotFoundStyle}");
        if (NotFoundTrigger > 0) parts.Add($"Триггер (воздействие с пульта) не найден: {NotFoundTrigger}");
        if (NotFoundGeneticReflex > 0) parts.Add($"Безусловный рефлекс не найден: {NotFoundGeneticReflex}");
        if (NotFoundOrInvalidPhrase > 0) parts.Add($"Фраза не найдена или не распознана: {NotFoundOrInvalidPhrase}");
        if (Duplicate > 0) parts.Add($"Дубликат: {Duplicate}");
        if (OtherError > 0) parts.Add($"Прочие ошибки: {OtherError}");
      }
      return string.Join("\n", parts);
    }
  }

  /// <summary>
  /// Загрузчик условных рефлексов по шаблону из текстового формата.
  /// Формат строки: Состояние|Стили|Триггер безусловного рефлекса|Новый триггер условного рефлекса (фраза).
  /// Триггер безусловного рефлекса — одиночное действие с пульта (InfluenceAction).
  /// Новый триггер — фраза с пульта. Крепость созданных рефлексов 0,95. Запуск только в стадии 1.
  /// </summary>
  public sealed class ConditionedReflexFileLoader
  {
    private const string ConditionedReflexGenerateListFileName = "conditioned_reflex_generate_list.txt";
    private const string PromptConditionedReflexGenerateFileName = "prompt_conditioned_reflex_generate.txt";

    private static readonly Dictionary<string, int> StateMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
      { "Плохо", -1 },
      { "Норма", 0 },
      { "Хорошо", 1 }
    };

    private readonly string _bootDataFolder;

    /// <summary>
    /// Создаёт загрузчик условных рефлексов по шаблону с указанным каталогом данных.
    /// </summary>
    /// <param name="bootDataFolder">Каталог с файлами conditioned_reflex_generate_list.txt и prompt_conditioned_reflex_generate.txt (например, C:\ProgramData\ISIDA\BootData).</param>
    /// <exception cref="ArgumentNullException">Если <paramref name="bootDataFolder"/> равен null.</exception>
    public ConditionedReflexFileLoader(string bootDataFolder)
    {
      _bootDataFolder = bootDataFolder ?? throw new ArgumentNullException(nameof(bootDataFolder));
    }

    /// <summary>
    /// Путь к файлу списка для генерации условных рефлексов.
    /// </summary>
    public string GetGenerateListFilePath() =>
        Path.Combine(_bootDataFolder, ConditionedReflexGenerateListFileName);

    /// <summary>
    /// Путь к файлу промпта для генерации условных рефлексов.
    /// </summary>
    public string GetPromptFilePath() =>
        Path.Combine(_bootDataFolder, PromptConditionedReflexGenerateFileName);

    /// <summary>
    /// Загружает условные рефлексы из текста. Запускать только в стадии 1.
    /// </summary>
    public ConditionedReflexLoadResult LoadFromContent(string content)
    {
      if (AppGlobalState.EvolutionStage != 1)
        throw new InvalidOperationException("Генерация условных рефлексов по шаблону разрешена только в стадии 1.");

      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст генерации условных рефлексов не задан.", nameof(content));

      var result = new ConditionedReflexLoadResult();
      var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      result.TotalLines = lines.Length;

      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
        {
          result.SkippedEmptyOrComment++;
          continue;
        }
        var (ok, failReason) = ParseAndApplyLine(trimmed);
        if (ok)
          result.Created++;
        else
        {
          switch (failReason)
          {
            case "Format": result.InvalidFormat++; break;
            case "State": result.NotFoundState++; break;
            case "Style": result.NotFoundStyle++; break;
            case "Trigger": result.NotFoundTrigger++; break;
            case "GeneticReflex": result.NotFoundGeneticReflex++; break;
            case "Phrase": result.NotFoundOrInvalidPhrase++; break;
            case "Duplicate": result.Duplicate++; break;
            default: result.OtherError++; break;
          }
        }
      }

      if (result.Created == 0)
        throw new ArgumentException(
          "Нет корректных строк. Ожидается формат: Состояние|Стили|Триггер безусловного рефлекса|Новый триггер [|Тон|Настроение].",
          nameof(content));

      var cr = ConditionedReflexesSystem.Instance;
      try
      {
        var (saveOk, saveErr) = cr.SaveConditionedReflexes();
        if (!saveOk)
          Logger.Warning($"Сохранение условных рефлексов после загрузки: {saveErr}");
      }
      catch (Exception ex)
      {
        Logger.Warning($"Сохранение условных рефлексов: {ex.Message}");
      }

      return result;
    }

    /// <summary>Обрабатывает одну строку. Возвращает (успех, причина сбоя).</summary>
    /// <remarks>
    /// Формат: Состояние|Стили|Триггер|Новый триггер [|Тон|Настроение]. Разделитель полей — только |, внутри ячеек его быть не должно.
    /// Почему тон/настроение могут оказаться 0: (1) В тексте только 4 поля — загружен старый список или не тот файл; содержимое берётся из окна «Текст рефлексов», при открытии диалога подгружается conditioned_reflex_generate_list.txt. (2) Строка тона/настроения не совпадает со справочником (опечатка, лишние слова, неверный порядок колонок Тон|Настроение). (3) GetToneIdByText/GetMoodIdByText — только точное совпадение без учёта регистра; при несовпадении возвращают 0. В этих случаях в лог пишется предупреждение с полученной строкой.
    /// </remarks>
    private (bool success, string failReason) ParseAndApplyLine(string line)
    {
      var parts = line.Split('|');
      if (parts.Length < 4)
        return (false, "Format");

      string stateStr = parts[0].Trim();
      string stylesStr = parts[1].Trim();
      string triggerStr = parts[2].Trim();
      string phraseStr = parts[3].Trim();
      string toneStr = parts.Length > 4 ? parts[4].Trim() : string.Empty;
      string moodStr = parts.Length > 5 ? parts[5].Trim() : string.Empty;

      int toneId = string.IsNullOrEmpty(toneStr) ? 0 : ActionsImagesSystem.GetToneIdByText(toneStr);
      int moodId = string.IsNullOrEmpty(moodStr) ? 0 : ActionsImagesSystem.GetMoodIdByText(moodStr);

      // Если в строке были указаны тон/настроение, но не распознаны (получили 0 и это не «Нормальный»/«Нормальное») — в лог коды символов
      string normalToneText = ActionsImagesSystem.GetToneText(0);
      if (!string.IsNullOrEmpty(toneStr) && toneId == 0 && !string.Equals(toneStr.Trim(), normalToneText, StringComparison.OrdinalIgnoreCase))
      {
        var codes = string.Join(" ", toneStr.Trim().Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
        Logger.Warning($"Тон не распознан (будет Нормальный): \"{toneStr}\". Коды символов: {codes}. Допустимы: Вялый, Нормальный, Повышенный.");
      }
      string normalMoodText = ActionsImagesSystem.GetMoodText(0);
      if (!string.IsNullOrEmpty(moodStr) && moodId == 0 && !string.Equals(moodStr.Trim(), normalMoodText, StringComparison.OrdinalIgnoreCase))
      {
        var codes = string.Join(" ", moodStr.Trim().Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
        Logger.Warning($"Настроение не распознано (будет Нормальное): \"{moodStr}\". Коды символов: {codes}. Допустимы: Нормальное, Хорошее, Плохое, Игривое, Учитель, Агрессивное, Защитное, Протест.");
      }

      if (!TryParseState(stateStr, out int level1))
        return (false, "State");
      if (!TryParseStyles(stylesStr, out List<int> level2) || level2 == null || level2.Count == 0)
        return (false, "Style");
      if (!TryParseTrigger(triggerStr, out int influenceActionId))
        return (false, "Trigger");

      var geneticReflex = FindGeneticReflexByTrigger(level1, level2, influenceActionId);
      if (geneticReflex == null)
        return (false, "GeneticReflex");

      int phraseId = GetOrAddPhraseId(phraseStr);
      if (phraseId <= 0)
        return (false, "Phrase");

      int level3ImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
          new List<int>(),
          new List<int> { phraseId });
      if (level3ImageId <= 0)
        return (false, "Other");

      try
      {
        var (reflexId, warnings) = ConditionedReflexesSystem.Instance.AddConditionedReflex(
            level1,
            level2,
            level3ImageId,
            geneticReflex.Id,
            authoritativeMod: true,
            toneId: toneId,
            moodId: moodId);

        foreach (var w in warnings)
          Logger.Warning(w);

        return (reflexId > 0, reflexId > 0 ? null : "Duplicate");
      }
      catch (ArgumentException ex) when (ex.Message != null && ex.Message.IndexOf("уже существует", StringComparison.OrdinalIgnoreCase) >= 0)
      {
        Logger.Warning($"Строка не применена (дубликат): {line}. {ex.Message}");
        return (false, "Duplicate");
      }
      catch (Exception ex)
      {
        Logger.Warning($"Строка не применена: {line}. {ex.Message}");
        return (false, "Other");
      }
    }

    private static bool TryParseState(string stateStr, out int level1)
    {
      level1 = 0;
      if (string.IsNullOrWhiteSpace(stateStr))
        return false;
      return StateMap.TryGetValue(stateStr.Trim(), out level1);
    }

    private bool TryParseStyles(string stylesStr, out List<int> level2)
    {
      level2 = new List<int>();
      if (string.IsNullOrWhiteSpace(stylesStr))
        return false;
      var names = stylesStr.Split('+').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
      if (names.Count == 0)
        return false;
      var gomeostas = GomeostasSystem.Instance;
      var allStyles = gomeostas.GetAllBehaviorStyles();
      foreach (var name in names)
      {
        var style = allStyles.Values.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (style == null)
        {
          Logger.Warning($"Стиль не найден: {name}");
          return false;
        }
        level2.Add(style.Id);
      }
      level2 = level2.OrderBy(x => x).Distinct().ToList();
      return level2.Count > 0;
    }

    private bool TryParseTrigger(string triggerStr, out int influenceActionId)
    {
      influenceActionId = 0;
      if (string.IsNullOrWhiteSpace(triggerStr))
        return false;
      var influence = InfluenceActionSystem.Instance;
      var all = influence.GetAllInfluenceActions();
      var action = all.FirstOrDefault(a =>
          string.Equals(a.Name, triggerStr.Trim(), StringComparison.OrdinalIgnoreCase));
      if (action == null)
      {
        Logger.Warning($"Внешнее воздействие не найдено: {triggerStr}");
        return false;
      }
      influenceActionId = action.Id;
      return true;
    }

    private static GeneticReflexesSystem.GeneticReflex FindGeneticReflexByTrigger(
        int level1,
        List<int> level2,
        int influenceActionId)
    {
      var gr = GeneticReflexesSystem.Instance;
      var all = gr.GetAllGeneticReflexesList();
      if (all == null)
        return null;
      var level3List = new List<int> { influenceActionId };
      var sortedLevel2 = level2?.OrderBy(x => x).ToList() ?? new List<int>();
      return all.FirstOrDefault(r =>
          r.Level1 == level1 &&
          (r.Level2 != null && sortedLevel2.SequenceEqual(r.Level2.OrderBy(x => x))) &&
          r.Level3 != null && r.Level3.Count == 1 && r.Level3[0] == influenceActionId);
    }

    /// <summary>Получает или добавляет фразу в дерево; возвращает phraseId или 0.</summary>
    private static int GetOrAddPhraseId(string phraseText)
    {
      if (string.IsNullOrWhiteSpace(phraseText))
        return 0;
      var verbal = SensorySystem.Instance?.VerbalChannel;
      if (verbal == null)
        return 0;
      int existing = verbal.FindPhraseId(phraseText);
      if (existing > 0)
        return existing;
      var phraseIds = verbal.RecognizeText(phraseText.Trim(), authoritativeWrite: true);
      if (phraseIds != null && phraseIds.Count > 0)
        return phraseIds[0];
      return 0;
    }
  }
}
