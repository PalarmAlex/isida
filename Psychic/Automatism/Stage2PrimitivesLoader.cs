using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.Linq;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Результат загрузки базовых примитивов (эхо-автоматизмы с цепочкой) по шаблону для стадии 2.
  /// </summary>
  public sealed class Stage2PrimitivesLoadResult
  {
    /// <summary>Общее количество строк в загруженном тексте.</summary>
    public int TotalLines { get; set; }

    /// <summary>Количество строк, пропущенных как пустые или начинающиеся с # (комментарии).</summary>
    public int SkippedEmptyOrComment { get; set; }

    /// <summary>Количество успешно созданных автоматизмов с цепочкой.</summary>
    public int Created { get; set; }

    /// <summary>Количество строк с ошибкой формата (ожидается 5 полей через |).</summary>
    public int InvalidFormat { get; set; }

    /// <summary>Количество строк, где состояние не распознано (Плохо/Норма/Хорошо).</summary>
    public int NotFoundState { get; set; }

    /// <summary>Количество строк, где указанный стиль не найден в справочнике.</summary>
    public int NotFoundStyle { get; set; }

    /// <summary>Количество строк, где трёхсложный паттерн не распознан (слова не найдены в вербальном канале).</summary>
    public int NotFoundPattern { get; set; }

    /// <summary>Количество строк, где тон или настроение не распознаны.</summary>
    public int NotFoundToneOrMood { get; set; }

    /// <summary>Количество строк с прочими ошибками при создании.</summary>
    public int OtherError { get; set; }

    /// <summary>Общее число строк, по которым не удалось создать примитив (все категории ошибок).</summary>
    public int Failed => InvalidFormat + NotFoundState + NotFoundStyle + NotFoundPattern + NotFoundToneOrMood + OtherError;

    /// <summary>Формирует текстовый отчёт по результату загрузки для отображения пользователю.</summary>
    /// <returns>Многострочная строка с итогами.</returns>
    public string ToSummaryString()
    {
      var parts = new List<string>
      {
        $"Всего строк: {TotalLines}",
        $"Пропущено (пустые/комментарии): {SkippedEmptyOrComment}",
        $"Создано автоматизмов с цепочкой: {Created}"
      };
      if (Failed > 0)
      {
        if (InvalidFormat > 0) parts.Add($"Ошибка формата: {InvalidFormat}");
        if (NotFoundState > 0) parts.Add($"Состояние не распознано: {NotFoundState}");
        if (NotFoundStyle > 0) parts.Add($"Стиль не найден: {NotFoundStyle}");
        if (NotFoundPattern > 0) parts.Add($"Паттерн (слова) не распознан: {NotFoundPattern}");
        if (NotFoundToneOrMood > 0) parts.Add($"Тон/настроение не распознаны: {NotFoundToneOrMood}");
        if (OtherError > 0) parts.Add($"Прочие ошибки: {OtherError}");
      }
      return string.Join("\n", parts);
    }
  }

  /// <summary>
  /// Загрузчик базовых примитивов (эхо-автоматизм + цепочка по трёхсложному паттерну) для стадии 2.
  /// Формат строки: Состояние|Стили|Трехсложный паттерн|Тон|Настроение
  /// </summary>
  public sealed class Stage2PrimitivesLoader
  {
    private static readonly Dictionary<string, int> StateMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
      { "Плохо", -1 },
      { "Норма", 0 },
      { "Хорошо", 1 }
    };

    private readonly GomeostasSystem _gomeostas;
    private readonly EmotionsImageSystem _emotionsImageSystem;
    private readonly SensorySystem _sensorySystem;
    private readonly VerbalBrocaImagesSystem _verbalBrocaImages;
    private readonly AutomatizmTreeSystem _automatizmTreeSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly MirrorAutomatizmService _mirrorService;

    /// <summary>
    /// Создаёт загрузчик базовых примитивов по шаблону для стадии 2.
    /// </summary>
    /// <param name="gomeostas">Система гомеостаза (состояние, стили).</param>
    /// <param name="emotionsImageSystem">Система образов эмоций (стили → emotionId).</param>
    /// <param name="sensorySystem">Сенсорная система (вербальный канал для паттерна).</param>
    /// <param name="verbalBrocaImages">Система вербальных образов Брока.</param>
    /// <param name="automatizmTreeSystem">Дерево автоматизмов (активация узла).</param>
    /// <param name="actionsImagesSystem">Система образов действий.</param>
    /// <param name="mirrorService">Сервис зеркальных/эхо-автоматизмов (создание эхо+цепочка).</param>
    public Stage2PrimitivesLoader(
        GomeostasSystem gomeostas,
        EmotionsImageSystem emotionsImageSystem,
        SensorySystem sensorySystem,
        VerbalBrocaImagesSystem verbalBrocaImages,
        AutomatizmTreeSystem automatizmTreeSystem,
        ActionsImagesSystem actionsImagesSystem,
        MirrorAutomatizmService mirrorService)
    {
      _gomeostas = gomeostas ?? throw new ArgumentNullException(nameof(gomeostas));
      _emotionsImageSystem = emotionsImageSystem ?? throw new ArgumentNullException(nameof(emotionsImageSystem));
      _sensorySystem = sensorySystem ?? throw new ArgumentNullException(nameof(sensorySystem));
      _verbalBrocaImages = verbalBrocaImages ?? throw new ArgumentNullException(nameof(verbalBrocaImages));
      _automatizmTreeSystem = automatizmTreeSystem ?? throw new ArgumentNullException(nameof(automatizmTreeSystem));
      _actionsImagesSystem = actionsImagesSystem ?? throw new ArgumentNullException(nameof(actionsImagesSystem));
      _mirrorService = mirrorService ?? throw new ArgumentNullException(nameof(mirrorService));
    }

    /// <summary>
    /// Загружает примитивы из текста. Только для стадии 2.
    /// </summary>
    public Stage2PrimitivesLoadResult LoadFromContent(string content)
    {
      if (AppGlobalState.EvolutionStage != 2)
        throw new InvalidOperationException("Создание базовых примитивов по шаблону разрешено только на стадии 2.");

      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст примитивов не задан.", nameof(content));

      content = GenerateListContentPreprocessor.Preprocess(content);
      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст примитивов не задан.", nameof(content));

      var result = new Stage2PrimitivesLoadResult();
      var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      result.TotalLines = lines.Length;

      // При загрузке по шаблону новые слова/фразы из паттерна должны сразу попадать в словарь (авторитарный режим).
      var verbal = _sensorySystem?.VerbalChannel;
      bool wasAuthoritative = verbal?.AuthoritativeMode ?? false;
      if (verbal != null)
        verbal.AuthoritativeMode = true;

      try
      {
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
              case "Pattern": result.NotFoundPattern++; break;
              case "ToneMood": result.NotFoundToneOrMood++; break;
              default: result.OtherError++; break;
            }
          }
        }
      }
      finally
      {
        if (verbal != null)
          verbal.AuthoritativeMode = wasAuthoritative;
      }

      return result;
    }

    private (bool success, string failReason) ParseAndApplyLine(string line)
    {
      var parts = line.Split('|');
      if (parts.Length < 5)
        return (false, "Format");

      string stateStr = parts[0].Trim();
      string stylesStr = parts[1].Trim();
      string patternStr = parts[2].Trim();
      string toneStr = parts[3].Trim();
      string moodStr = parts[4].Trim();

      if (!TryParseState(stateStr, out int baseId))
        return (false, "State");
      if (!TryParseStyles(stylesStr, out List<int> styleIds) || styleIds == null || styleIds.Count == 0)
        return (false, "Style");

      int toneId = string.IsNullOrWhiteSpace(toneStr) ? 0 : ActionsImagesSystem.GetToneIdByText(toneStr);
      int moodId = string.IsNullOrWhiteSpace(moodStr) ? 0 : ActionsImagesSystem.GetMoodIdByText(moodStr);
      string normalToneText = ActionsImagesSystem.GetToneText(0);
      if (!string.IsNullOrWhiteSpace(toneStr) && toneId == 0 && !string.Equals(toneStr.Trim(), normalToneText, StringComparison.OrdinalIgnoreCase))
      {
        var codes = string.Join(" ", toneStr.Trim().Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
        Logger.Warning($"Тон не распознан (будет Нормальный): \"{toneStr}\". Коды: {codes}. Допустимы: Вялый, Нормальный, Повышенный.");
      }
      string normalMoodText = ActionsImagesSystem.GetMoodText(0);
      if (!string.IsNullOrWhiteSpace(moodStr) && moodId == 0 && !string.Equals(moodStr.Trim(), normalMoodText, StringComparison.OrdinalIgnoreCase))
      {
        var codes = string.Join(" ", moodStr.Trim().Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
        Logger.Warning($"Настроение не распознано (будет Нормальное): \"{moodStr}\". Коды: {codes}. Допустимы: Нормальное, Хорошее, Плохое, Игривое, Учитель, Агрессивное, Защитное, Протест.");
      }

      var partPhraseIds = GetPartPhraseIdsFromPattern(patternStr);
      if (partPhraseIds == null || partPhraseIds.Count == 0)
        return (false, "Pattern");

      (int emotionId, _) = _emotionsImageSystem.CreateNewEmotionsImage(styleIds, true);
      int activityId = 0;
      int toneMoodId = PsychicSystem.GetToneMoodID(toneId, moodId);

      // Вербальная часть образа восприятия — введённая строка без пробелов (напр. "со ба ка" → "собака")
      string fullPhraseText = string.Concat(patternStr.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
      var wordIdOpt = _sensorySystem.VerbalChannel.ProcessWord(fullPhraseText);
      if (wordIdOpt.HasValue)
        _sensorySystem.VerbalChannel.ProcessPhrase(new List<int> { wordIdOpt.Value });
      int fullPhraseId = _sensorySystem.VerbalChannel.FindPhraseId(fullPhraseText);
      if (fullPhraseId == 0)
        return (false, "Pattern");

      // Образ стимула (полная фраза как на пульте + тон/настроение)
      var (fullStimulusImageId, _) = _actionsImagesSystem.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: new List<int> { fullPhraseId },
          toneId: toneId,
          moodId: moodId,
          checkUnicum: true);
      if (fullStimulusImageId <= 0)
        return (false, "Other");

      // Узел дерева по полной вербальной фразе (как ввели на пульте)
      int mergedPhraseId = fullPhraseId;
      int firstSymbol = _sensorySystem.VerbalChannel.GetFirstSymbolFromPhraseId(mergedPhraseId);
      (int verbId, _) = _verbalBrocaImages.CreateNewVerbalBrocaImage(
          firstSymbol, new List<int> { mergedPhraseId }, toneId, moodId, true);

      int nodeId = _automatizmTreeSystem.AutomatizmTreeActivation(
          baseId, emotionId, activityId, toneMoodId, firstSymbol, verbId, 0, 0, false);
      if (nodeId <= 0)
        return (false, "Other");

      int echoId = _mirrorService.TryCreateStage2EchoWithChain(
          nodeId,
          fullStimulusImageId,
          partPhraseIds,
          new List<int>(),
          toneId,
          moodId);

      return (echoId > 0, echoId > 0 ? null : "Other");
    }

    private static bool TryParseState(string stateStr, out int baseId)
    {
      baseId = 0;
      if (string.IsNullOrWhiteSpace(stateStr)) return false;
      return StateMap.TryGetValue(stateStr.Trim(), out baseId);
    }

    private bool TryParseStyles(string stylesStr, out List<int> styleIds)
    {
      styleIds = new List<int>();
      if (string.IsNullOrWhiteSpace(stylesStr)) return false;
      var names = stylesStr.Split('+').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
      if (names.Count == 0) return false;
      var allStyles = _gomeostas.GetAllBehaviorStyles();
      if (allStyles == null) return false;
      foreach (var name in names)
      {
        var style = allStyles.Values.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
        if (style == null)
        {
          Logger.Warning($"Стиль не найден: {name}");
          return false;
        }
        styleIds.Add(style.Id);
      }
      styleIds = styleIds.OrderBy(x => x).Distinct().ToList();
      return styleIds.Count > 0;
    }

    /// <summary>
    /// Разбивает паттерн по пробелам и дефисам, добавляет части в канал, возвращает список phraseId по частям.
    /// Вербальный триггер с пульта (напр. "со ба ка" или "тик-так") даёт автоматизм по первой части и цепочку по остальным.
    /// </summary>
    private List<int> GetPartPhraseIdsFromPattern(string pattern)
    {
      if (string.IsNullOrWhiteSpace(pattern)) return null;
      var words = pattern.Trim().Split(new[] { ' ', '-' }, StringSplitOptions.RemoveEmptyEntries);
      if (words.Length == 0) return null;
      var partPhraseIds = new List<int>();
      var verbal = _sensorySystem.VerbalChannel;
      foreach (var word in words)
      {
        if (string.IsNullOrEmpty(word)) continue;
        var wordIdOpt = verbal.ProcessWord(word);
        if (wordIdOpt.HasValue)
          verbal.ProcessPhrase(new List<int> { wordIdOpt.Value });
        int phraseId = verbal.FindPhraseId(word);
        if (phraseId != 0)
          partPhraseIds.Add(phraseId);
      }
      return partPhraseIds.Count > 0 ? partPhraseIds : null;
    }
  }
}
