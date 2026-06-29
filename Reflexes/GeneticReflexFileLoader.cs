using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Результат загрузки рефлексов из списка: сколько строк обработано, создано, пропущено и по каким причинам.
  /// </summary>
  public sealed class GeneticReflexLoadResult
  {
    /// <summary>Общее количество строк в загруженном тексте (файле).</summary>
    public int TotalLines { get; set; }

    /// <summary>Количество строк, пропущенных как пустые или начинающиеся с # (комментарии).</summary>
    public int SkippedEmptyOrComment { get; set; }

    /// <summary>Количество успешно созданных рефлексов.</summary>
    public int Created { get; set; }

    /// <summary>Количество строк с ошибкой формата (меньше 4 полей или неверное состояние).</summary>
    public int InvalidFormat { get; set; }

    /// <summary>Количество строк, где указанный стиль не найден в справочнике.</summary>
    public int NotFoundStyle { get; set; }

    /// <summary>Количество строк, где указанное внешнее воздействие не найдено в справочнике.</summary>
    public int NotFoundTrigger { get; set; }

    /// <summary>Количество строк, где указанное действие не найдено в справочнике.</summary>
    public int NotFoundAction { get; set; }

    /// <summary>Количество строк, пропущенных из-за дубликата (рефлекс с такими условиями уже существует).</summary>
    public int Duplicate { get; set; }

    /// <summary>Количество строк с прочими ошибками при добавлении рефлекса.</summary>
    public int OtherError { get; set; }

    /// <summary>Число строк с данными (всего строк минус пропущенные пустые и комментарии).</summary>
    public int DataLines => TotalLines - SkippedEmptyOrComment;

    /// <summary>Общее число строк, по которым не удалось создать рефлекс (все категории ошибок).</summary>
    public int Failed => InvalidFormat + NotFoundStyle + NotFoundTrigger + NotFoundAction + Duplicate + OtherError;

    /// <summary>Формирует текстовый отчёт по результату загрузки для отображения пользователю.</summary>
    /// <returns>Многострочная строка с итогами.</returns>
    public string ToSummaryString()
    {
      var parts = new List<string>
      {
        $"Всего строк в файле: {TotalLines}",
        $"Пропущено (пустые/комментарии): {SkippedEmptyOrComment}",
        $"Строк с данными: {DataLines}",
        $"Создано рефлексов: {Created}"
      };
      if (Failed > 0)
      {
        if (InvalidFormat > 0) parts.Add($"Ошибка формата (меньше 4 полей): {InvalidFormat}");
        if (NotFoundStyle > 0) parts.Add($"Стиль не найден в справочнике: {NotFoundStyle}");
        if (NotFoundTrigger > 0) parts.Add($"Внешнее воздействие не найдено: {NotFoundTrigger}");
        if (NotFoundAction > 0) parts.Add($"Действие не найдено в справочнике: {NotFoundAction}");
        if (Duplicate > 0) parts.Add($"Дубликат (рефлекс уже есть): {Duplicate}");
        if (OtherError > 0) parts.Add($"Прочие ошибки: {OtherError}");
      }
      return string.Join("\n", parts);
    }
  }

  /// <summary>
  /// Загрузчик безусловных рефлексов и цепочек из текстового формата.
  /// Формат строки: Состояние|Стили|Триггер|Действие|Цепочка
  /// Стили: имена через +. Триггер: имя внешнего воздействия или "Нет" — тогда рефлекс без триггера (только гомеостаз + стили).
  /// Цепочка: 1.Действие(успех,неудача);2.Действие(0,0) — 0 = конец.
  /// </summary>
  public sealed class GeneticReflexFileLoader : IDisposable
  {
    private const string ReflexGenerateListFileName = "genetic_reflex_generate_list.txt";
    private const string PromptReflexGenerateFileName = "prompt_genetic_reflex_generate.txt";

    private readonly string _bootDataFolder;
    private bool _disposed;

    private static GeneticReflexFileLoader _instance;

    /// <summary>
    /// Глобальный экземпляр загрузчика безусловных рефлексов. Должен быть инициализирован через <see cref="InitializeInstance"/>.
    /// </summary>
    public static GeneticReflexFileLoader Instance => _instance ??
        throw new InvalidOperationException("GeneticReflexFileLoader не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Признак того, что загрузчик инициализирован.
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр загрузчика с указанным каталогом данных (тот же, что для автоматизмов).
    /// </summary>
    /// <param name="bootDataFolder">Каталог с файлами genetic_reflex_generate_list.txt и prompt_genetic_reflex_generate.txt</param>
    public static void InitializeInstance(string bootDataFolder)
    {
      if (_instance != null)
        throw new InvalidOperationException("GeneticReflexFileLoader уже инициализирован.");
      _instance = new GeneticReflexFileLoader(bootDataFolder);
    }

    private GeneticReflexFileLoader(string bootDataFolder)
    {
      _bootDataFolder = bootDataFolder ?? throw new ArgumentNullException(nameof(bootDataFolder));
    }

    /// <summary>
    /// Загружает безусловные рефлексы и цепочки из текста.
    /// Одна строка — один рефлекс и при необходимости одна цепочка.
    /// </summary>
    /// <param name="content">Текст в формате: Состояние|Стили|Триггер|Действие|Цепочка</param>
    /// <returns>Детальный результат: сколько создано, сколько пропущено и по каким причинам</returns>
    public GeneticReflexLoadResult LoadFromContent(string content)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(GeneticReflexFileLoader));
      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст генерации рефлексов не задан.", nameof(content));

      content = GenerateListContentPreprocessor.Preprocess(content);
      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст генерации рефлексов не задан.", nameof(content));

      var result = new GeneticReflexLoadResult();
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
            case "State": result.InvalidFormat++; break;
            case "Style": result.NotFoundStyle++; break;
            case "Trigger": result.NotFoundTrigger++; break;
            case "Action": result.NotFoundAction++; break;
            case "Duplicate": result.Duplicate++; break;
            default: result.OtherError++; break;
          }
        }
      }

      if (result.Created == 0)
        throw new ArgumentException(
          "Нет корректных строк. Ожидается формат: Состояние|Стили|Триггер|Действие|Цепочка (например: Норма|Расслабление+Игра|Поощрить|Радуется|1.Смеется(0,2);2.Удивляется(0,0)).",
          nameof(content));

      var gr = GeneticReflexesSystem.Instance;
      var (saveOk, saveErr) = gr.SaveGeneticReflexes();
      if (!saveOk)
        Logger.Warning($"Сохранение рефлексов после загрузки: {saveErr}");
      if (ReflexChainsSystem.IsInitialized)
      {
        var chains = ReflexChainsSystem.Instance;
        var (chainSaveOk, chainSaveErr) = chains.SaveReflexChains();
        if (!chainSaveOk)
          Logger.Warning($"Сохранение цепочек после загрузки: {chainSaveErr}");
      }
      return result;
    }

    /// <summary>
    /// Загружает из файла данных генерации (тот же каталог, что и для автоматизмов).
    /// </summary>
    public GeneticReflexLoadResult LoadFromFile()
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(GeneticReflexFileLoader));
      string path = GetGenerateListFilePath();
      if (!File.Exists(path))
      {
        Logger.Info($"Файл не найден: {path}");
        return new GeneticReflexLoadResult();
      }
      string text = File.ReadAllText(path, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(text))
        return new GeneticReflexLoadResult();
      return LoadFromContent(text);
    }

    /// <summary>
    /// Возвращает полный путь к файлу списка рефлексов для генерации (genetic_reflex_generate_list.txt).
    /// </summary>
    public string GetGenerateListFilePath() =>
        Path.Combine(_bootDataFolder, ReflexGenerateListFileName);

    /// <summary>
    /// Возвращает полный путь к файлу промпта для генерации рефлексов (prompt_genetic_reflex_generate.txt).
    /// </summary>
    public string GetPromptFilePath() =>
        Path.Combine(_bootDataFolder, PromptReflexGenerateFileName);

    /// <summary>Обрабатывает одну строку. Возвращает (успех, причина сбоя: Format|State|Style|Trigger|Action|Duplicate|Other).</summary>
    private (bool success, string failReason) ParseAndApplyLine(string line)
    {
      var parts = line.Split('|');
      if (parts.Length < 4)
        return (false, "Format");

      string stateStr = parts[0].Trim();
      string stylesStr = parts[1].Trim();
      string triggerStr = parts[2].Trim();
      string actionStr = parts[3].Trim();
      string chainStr = parts.Length > 4 ? parts[4].Trim() : "";

      if (!TryParseState(stateStr, out int level1))
        return (false, "State");
      if (!TryParseStyles(stylesStr, out List<int> level2) || level2 == null || level2.Count == 0)
        return (false, "Style");
      if (!TryParseTrigger(triggerStr, out List<int> influenceActionIds))
        return (false, "Trigger");
      var commandPatternIds = new List<int>();
      if (!TryParseAction(actionStr, out int actionId))
        return (false, "Action");

      var adaptiveActions = new List<int> { actionId };
      int? chainId = null;
      if (!string.IsNullOrWhiteSpace(chainStr) && TryParseAndCreateChain(chainStr, actionStr, out int cid))
        chainId = cid;

      var gr = GeneticReflexesSystem.Instance;
      try
      {
        var (reflexId, _) = gr.AddGeneticReflex(level1, level2, influenceActionIds, commandPatternIds, adaptiveActions);
        if (chainId.HasValue && reflexId > 0)
        {
          gr.AttachChainToReflex(reflexId, chainId.Value);
          UpdateReflexTreeChainBinding(level1, level2, influenceActionIds, commandPatternIds, chainId.Value);
        }
        return (true, null);
      }
      catch (ArgumentException ex) when (ex.Message != null && ex.Message.IndexOf("Дублирование", StringComparison.OrdinalIgnoreCase) >= 0)
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

    /// <summary>
    /// Обновляет привязку цепочки в дереве рефлексов. Цепочки запускаются через ReflexesActivator,
    /// который ищет ReflexChainID в узлах дерева ReflexTreeSystem. При генерации из файла цепочка
    /// привязывается к рефлексу (GeneticReflexesSystem), но узел дерева создаётся с ReflexChainID=0
    /// (событие GeneticReflexCreated вызывается до AttachChainToReflex). Поэтому нужно явно обновить узел.
    /// </summary>
    private static void UpdateReflexTreeChainBinding(
        int level1, List<int> level2, List<int> influenceActionIds, List<int> commandPatternIds, int chainId)
    {
      if (!ReflexTreeSystem.IsInitialized || !PerceptionImagesSystem.IsInitialized)
      {
        Logger.Warning("ReflexTreeSystem или PerceptionImagesSystem не инициализированы — привязка цепочки к дереву пропущена");
        return;
      }

      try
      {
        int styleImageId = 0;
        if (level2 != null && level2.Any())
          styleImageId = PerceptionImagesSystem.Instance.AddBehaviorStyleImage(level2);

        int actionImageId = 0;
        if ((influenceActionIds != null && influenceActionIds.Any()) ||
            (commandPatternIds != null && commandPatternIds.Any()))
        {
          var operatorInfluenceIds = InfluenceActionSystem.IsInitialized
              ? InfluenceActionSystem.Instance.FilterOperatorStimulusActionIds(influenceActionIds)
              : (influenceActionIds ?? new List<int>());
          actionImageId = PerceptionImagesSystem.Instance.AddPerceptionImage(
              operatorInfluenceIds,
              new List<int>(),
              commandPatternIdList: commandPatternIds ?? new List<int>());
        }

        var (nodeId, node) = ReflexTreeSystem.Instance.FindReflexTreeNodeFromCondition(level1, styleImageId, actionImageId);
        if (node != null && nodeId > 0)
        {
          ReflexTreeSystem.Instance.AttachChainToNode(nodeId, chainId);
          var (saveOk, saveErr) = ReflexTreeSystem.Instance.SaveReflexTree();
          if (!saveOk)
            Logger.Warning($"Сохранение дерева рефлексов после привязки цепочки: {saveErr}");
        }
        else
        {
          Logger.Warning($"Узел дерева не найден для условий [Level1={level1}, StyleId={styleImageId}, ActionId={actionImageId}] — цепочка {chainId} не привязана к дереву");
        }
      }
      catch (Exception ex)
      {
        Logger.Warning($"Ошибка привязки цепочки {chainId} к дереву рефлексов: {ex.Message}");
      }
    }

    private static readonly Dictionary<string, int> StateMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
      { "Плохо", -1 },
      { "Норма", 0 },
      { "Хорошо", 1 }
    };

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

    /// <summary>
    /// Парсит триггер. Если в ячейке указано "Нет" — рефлекс привязывается только к внутренним условиям гомеостаза (состояние + стили), без внешнего триггера.
    /// </summary>
    private bool TryParseTrigger(string triggerStr, out List<int> influenceActionIds)
    {
      influenceActionIds = new List<int>();
      if (string.IsNullOrWhiteSpace(triggerStr))
        return true;
      // "Нет" = рефлекс без триггера, только состояние симбионта + комбинации стилей
      if (string.Equals(triggerStr.Trim(), "Нет", StringComparison.OrdinalIgnoreCase))
        return true;
      var influence = InfluenceActionSystem.Instance;
      var all = influence.GetAllInfluenceActions();
      var action = all.FirstOrDefault(a =>
          string.Equals(a.Name, triggerStr.Trim(), StringComparison.OrdinalIgnoreCase));
      if (action == null)
      {
        Logger.Warning($"Внешнее воздействие не найдено: {triggerStr}");
        return false;
      }
      influenceActionIds.Add(action.Id);
      return true;
    }

    private bool TryParseAction(string actionStr, out int actionId)
    {
      actionId = 0;
      if (string.IsNullOrWhiteSpace(actionStr))
        return false;
      var adaptive = AdaptiveActionsSystem.Instance;
      var all = adaptive.GetAllAdaptiveActionsList();
      var action = all.FirstOrDefault(a =>
          string.Equals(a.Name, actionStr.Trim(), StringComparison.OrdinalIgnoreCase));
      if (action == null)
      {
        Logger.Warning($"Моторное действие не найдено: {actionStr}");
        return false;
      }
      actionId = action.Id;
      return true;
    }

    // Цепочка: 1.Смеется(0,2);2.Удивляется(0,0) — номер.Действие(успех,неудача)
    private static readonly Regex ChainLinkRegex = new Regex(
        @"(\d+)\.([^(]+)\((\d+),(\d+)\)",
        RegexOptions.Compiled);

    private bool TryParseAndCreateChain(string chainStr, string defaultActionName, out int chainId)
    {
      chainId = 0;
      var matches = ChainLinkRegex.Matches(chainStr);
      if (matches.Count == 0)
        return false;

      var links = new List<(int Ordinal, string ActionName, int SuccessOrdinal, int FailureOrdinal)>();
      foreach (Match m in matches)
      {
        if (!m.Success || m.Groups.Count < 5) continue;
        int ord = int.Parse(m.Groups[1].Value);
        string name = m.Groups[2].Value.Trim();
        int succ = int.Parse(m.Groups[3].Value);
        int fail = int.Parse(m.Groups[4].Value);
        links.Add((ord, name, succ, fail));
      }
      if (links.Count == 0)
        return false;

      foreach (var link in links)
      {
        if (link.SuccessOrdinal < 0 || link.SuccessOrdinal > 2 || link.FailureOrdinal < 0 || link.FailureOrdinal > 2)
        {
          Logger.Warning($"В скобках цепочки допускаются только числа 0, 1, 2. Получено: ({link.SuccessOrdinal},{link.FailureOrdinal}). Цепочка не создана, рефлекс создаётся без неё.");
          return false;
        }
      }

      var adaptive = AdaptiveActionsSystem.Instance;
      var allActions = adaptive.GetAllAdaptiveActionsList();
      var ordinalToActionId = new Dictionary<int, int>();
      foreach (var (ord, actionName, _, _) in links.OrderBy(x => x.Ordinal))
      {
        var action = allActions.FirstOrDefault(a =>
            string.Equals(a.Name, actionName, StringComparison.OrdinalIgnoreCase));
        if (action == null)
        {
          Logger.Warning($"Действие в цепочке не найдено: {actionName}");
          return false;
        }
        ordinalToActionId[ord] = action.Id;
      }

      // Нумерация звеньев индивидуальна для каждой цепочки: 1, 2, 3...
      var chainLinks = new List<ReflexChainsSystem.ChainLink>();
      var ordinalToLinkId = new Dictionary<int, int>();
      int idx = 0;
      foreach (var (ord, actionName, successOrd, failureOrd) in links.OrderBy(x => x.Ordinal))
      {
        int linkId = idx + 1;
        ordinalToLinkId[ord] = linkId;
        idx++;
      }
      foreach (var (ord, actionName, successOrd, failureOrd) in links.OrderBy(x => x.Ordinal))
      {
        int linkId = ordinalToLinkId[ord];
        int actionId = ordinalToActionId[ord];
        int successLinkId = successOrd > 0 && ordinalToLinkId.TryGetValue(successOrd, out int s) ? s : 0;
        int failureLinkId = failureOrd > 0 && ordinalToLinkId.TryGetValue(failureOrd, out int f) ? f : 0;
        chainLinks.Add(new ReflexChainsSystem.ChainLink
        {
          ID = linkId,
          ChainID = 0,
          ActionId = actionId,
          SuccessNextLink = successLinkId,
          FailureNextLink = failureLinkId,
          Description = actionName
        });
      }

      var chains = ReflexChainsSystem.Instance;
      string chainName = string.IsNullOrWhiteSpace(defaultActionName)
          ? "Цепочка"
          : $"Реакция на {defaultActionName}";
      var (cid, warnings) = chains.AddReflexChain(chainName, "", chainLinks);
      chainId = cid;
      var chain = chains.GetChain(cid);
      if (chain != null)
        foreach (var l in chain.Links)
          l.ChainID = cid;
      foreach (var w in warnings)
        Logger.Warning(w);
      return true;
    }

    /// <summary>
    /// Освобождает ресурсы, используемые загрузчиком.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
      _instance = null;
    }
  }
}
