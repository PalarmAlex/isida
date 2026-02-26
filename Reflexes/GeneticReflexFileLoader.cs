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
  /// Загрузчик безусловных рефлексов и цепочек из текстового формата.
  /// Формат строки: Состояние|Стили|Триггер|Действие|Цепочка
  /// Стили: имена через +. Цепочка: 1.Действие(успех,неудача);2.Действие(0,0) — 0 = конец.
  /// </summary>
  public sealed class GeneticReflexFileLoader : IDisposable
  {
    private const string ReflexGenerateListFileName = "reflex_generate_list.txt";
    private const string PromptReflexGenerateFileName = "prompt_reflex_generate.txt";

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
    /// <param name="bootDataFolder">Каталог с файлами reflex_generate_list.txt и prompt_reflex_generate.txt</param>
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
    /// <returns>Количество успешно обработанных строк</returns>
    public int LoadFromContent(string content)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(GeneticReflexFileLoader));
      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст генерации рефлексов не задан.", nameof(content));

      var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      int validCount = 0;
      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
          continue;
        if (ParseAndApplyLine(trimmed))
          validCount++;
      }
      if (validCount == 0)
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
      return validCount;
    }

    /// <summary>
    /// Загружает из файла данных генерации (тот же каталог, что и для автоматизмов).
    /// </summary>
    public int LoadFromFile()
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(GeneticReflexFileLoader));
      string path = GetGenerateListFilePath();
      if (!File.Exists(path))
      {
        Logger.Info($"Файл не найден: {path}");
        return 0;
      }
      string text = File.ReadAllText(path, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(text))
        return 0;
      return LoadFromContent(text);
    }

    /// <summary>
    /// Возвращает полный путь к файлу списка рефлексов для генерации (reflex_generate_list.txt).
    /// </summary>
    public string GetGenerateListFilePath() =>
        Path.Combine(_bootDataFolder, ReflexGenerateListFileName);

    /// <summary>
    /// Возвращает полный путь к файлу промпта для генерации рефлексов (prompt_reflex_generate.txt).
    /// </summary>
    public string GetPromptFilePath() =>
        Path.Combine(_bootDataFolder, PromptReflexGenerateFileName);

    private bool ParseAndApplyLine(string line)
    {
      var parts = line.Split('|');
      if (parts.Length < 4)
        return false;

      string stateStr = parts[0].Trim();
      string stylesStr = parts[1].Trim();
      string triggerStr = parts[2].Trim();
      string actionStr = parts[3].Trim();
      string chainStr = parts.Length > 4 ? parts[4].Trim() : "";

      if (!TryParseState(stateStr, out int level1))
        return false;
      if (!TryParseStyles(stylesStr, out List<int> level2) || level2 == null || level2.Count == 0)
        return false;
      if (!TryParseTrigger(triggerStr, out List<int> level3))
        return false;
      if (!TryParseAction(actionStr, out int actionId))
        return false;

      var adaptiveActions = new List<int> { actionId };
      int? chainId = null;
      if (!string.IsNullOrWhiteSpace(chainStr) && TryParseAndCreateChain(chainStr, actionStr, out int cid))
        chainId = cid;

      var gr = GeneticReflexesSystem.Instance;
      try
      {
        var (reflexId, _) = gr.AddGeneticReflex(level1, level2, level3, adaptiveActions);
        if (chainId.HasValue && reflexId > 0)
          gr.AttachChainToReflex(reflexId, chainId.Value);
        return true;
      }
      catch (Exception ex)
      {
        Logger.Warning($"Строка не применена: {line}. {ex.Message}");
        return false;
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

    private bool TryParseTrigger(string triggerStr, out List<int> level3)
    {
      level3 = new List<int>();
      if (string.IsNullOrWhiteSpace(triggerStr))
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
      level3.Add(action.Id);
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
        Logger.Warning($"Адаптивное действие не найдено: {actionStr}");
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

      int nextLinkId = GetNextLinkIdBase();
      var chainLinks = new List<ReflexChainsSystem.ChainLink>();
      var ordinalToLinkId = new Dictionary<int, int>();
      int idx = 0;
      foreach (var (ord, actionName, successOrd, failureOrd) in links.OrderBy(x => x.Ordinal))
      {
        int linkId = nextLinkId + idx;
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

    private static int GetNextLinkIdBase()
    {
      var chains = ReflexChainsSystem.Instance;
      var all = chains.GetAllReflexChains();
      int max = 0;
      foreach (var chain in all.Values)
        foreach (var link in chain.Links)
          if (link.ID > max)
            max = link.ID;
      return max + 1;
    }

    /// <summary>
    /// Освобождает ресурсы, используемые загрузчиком.
    /// </summary>
    public void Dispose()
    {
      if (_disposed) return;
      _disposed = true;
    }
  }
}
