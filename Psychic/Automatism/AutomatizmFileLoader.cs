using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Psychic.Automatism;
using ISIDA.Sensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ISIDA.Psychic.Automatism
{
  /// <summary>
  /// Класс для загрузки автоматизмов из файла.
  /// </summary>
  /// <remarks>
  /// Поле Belief из файла применяется через <see cref="AutomatizmSystem.SetAutomatizmBelief"/> — при Belief=2 на ветке
  /// остаётся один штатный автоматизм (см. инвариант в <see cref="AutomatizmSystem"/>).
  /// </remarks>
  public sealed class AutomatizmFileLoader : IDisposable
  {
    private const string AutomatizmChainsFileName = "automatizm_chains_list.txt";

    private static readonly Dictionary<string, int> StateMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
      { "Плохо", -1 },
      { "Норма", 0 },
      { "Хорошо", 1 }
    };
    private readonly Dictionary<string, int> _phraseIdCache = new Dictionary<string, int>();
    private readonly string _bootDataFolder;
    private bool _disposed = false;

    // Ссылки на системы, инициализируемые в конструкторе
    private readonly AutomatizmSystem _automatizmSystem;
    private readonly AutomatizmTreeSystem _treeSystem;
    private readonly ActionsImagesSystem _actionsImagesSystem;
    private readonly VerbalBrocaImagesSystem _verbalBrocaSystem;
    private readonly VerbalSensorChannel _verbalChannel;
    private readonly EmotionsImageSystem _emotionsImageSystem;

    private static AutomatizmFileLoader _instance;

    /// <summary>
    /// Глобальный экземпляр загрузчика автоматизмов
    /// </summary>
    public static AutomatizmFileLoader Instance => _instance ??
        throw new InvalidOperationException("AutomatizmFileLoader не инициализирован. Вызовите InitializeInstance().");

    /// <summary>
    /// Флаг инициализации класса
    /// </summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр загрузчика автоматизмов
    /// </summary>
    public static void InitializeInstance(string bootDataFolder)
    {
      if (_instance != null)
        throw new InvalidOperationException("AutomatizmFileLoader уже инициализирован.");

      _instance = new AutomatizmFileLoader(bootDataFolder);
    }

    private AutomatizmFileLoader(string bootDataFolder)
    {
      _bootDataFolder = bootDataFolder ?? throw new ArgumentNullException(nameof(bootDataFolder));

      // Проверяем, что все необходимые системы инициализированы
      if (!AutomatizmSystem.IsInitialized)
        throw new InvalidOperationException("AutomatizmSystem не инициализирован");
      if (!AutomatizmTreeSystem.IsInitialized)
        throw new InvalidOperationException("AutomatizmTreeSystem не инициализирован");
      if (!ActionsImagesSystem.IsInitialized)
        throw new InvalidOperationException("ActionsImagesSystem не инициализирован");
      if (!VerbalBrocaImagesSystem.IsInitialized)
        throw new InvalidOperationException("VerbalBrocaImagesSystem не инициализирован");
      if (!SensorySystem.IsInitialized)
        throw new InvalidOperationException("SensorySystem не инициализирован");
      if (!EmotionsImageSystem.IsInitialized)
        throw new InvalidOperationException("EmotionsImageSystem не инициализирован");

      // Инициализируем ссылки на системы
      _automatizmSystem = AutomatizmSystem.Instance;
      _treeSystem = AutomatizmTreeSystem.Instance;
      _actionsImagesSystem = ActionsImagesSystem.Instance;
      _verbalBrocaSystem = VerbalBrocaImagesSystem.Instance;
      _verbalChannel = SensorySystem.Instance.VerbalChannel;
      _emotionsImageSystem = EmotionsImageSystem.Instance;
    }

    /// <summary>
    /// Загружает автоматизмы из текста цепочек (валидация в движке).
    /// </summary>
    /// <param name="csvContent">Текст цепочек: каждая строка — фразы через «;» или « - ».</param>
    /// <param name="baseId">Базовое состояние.</param>
    /// <param name="styleIds">Идентификаторы стилей.</param>
    /// <returns>Количество обработанных цепочек.</returns>
    /// <exception cref="ArgumentException">Текст пуст или не содержит корректных цепочек.</exception>
    public int LoadFromContent(string csvContent, int baseId, List<int> styleIds)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      if (string.IsNullOrWhiteSpace(csvContent))
        throw new ArgumentException("Текст цепочек не задан или пуст. Введите строки в формате: фраза1;фраза2;фраза3 или фраза1 - фраза2 - фраза3.", nameof(csvContent));

      csvContent = GenerateListContentPreprocessor.Preprocess(csvContent);
      if (string.IsNullOrWhiteSpace(csvContent))
        throw new ArgumentException("Текст цепочек не задан или пуст. Введите строки в формате: фраза1;фраза2;фраза3 или фраза1 - фраза2 - фраза3.", nameof(csvContent));

      var lines = csvContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      int validLinesCount = 0;
      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
          continue;
        var stimuli = ParseStimuliLine(trimmed);
        if (stimuli != null && stimuli.Count >= 2)
          validLinesCount++;
      }

      if (validLinesCount == 0)
        throw new ArgumentException(
          "Текст не содержит корректных цепочек. Ожидается формат: в каждой строке несколько фраз, разделённых «;» или « - » (например: привет;как дела;нормально).",
          nameof(csvContent));

      if (!CheckSystems()) return 0;

      _phraseIdCache.Clear();

      if (!PreloadAllPhrases(lines))
      {
        Logger.Error("Не удалось загрузить фразы");
        return 0;
      }

      _automatizmSystem.SetSuppressCreateLogging(true);
      _emotionsImageSystem.SetSuppressFoundExistingLog(true);
      _verbalBrocaSystem.SetSuppressFoundExistingLog(true);
      try
      {
        int processedChains = 0;
        foreach (var line in lines)
        {
          var trimmedLine = line.Trim();
          if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            continue;

          var stimuli = ParseStimuliLine(trimmedLine);
          if (stimuli.Count < 2) continue;

          if (ProcessChainDirect(stimuli, baseId, styleIds, 0, 0))
            processedChains++;
        }
        return processedChains;
      }
      finally
      {
        _automatizmSystem.SetSuppressCreateLogging(false);
        _emotionsImageSystem.SetSuppressFoundExistingLog(false);
        _verbalBrocaSystem.SetSuppressFoundExistingLog(false);
      }
    }

    /// <summary>
    /// Загружает автоматизмы из текста. Формат строки: Состояние|Комбинации стилей|фраза1 - фраза2 - фраза3|Тон|Настроение.
    /// Состояние: Плохо, Норма, Хорошо. Комбинации стилей: имена через +. Тон и Настроение — по справочнику (опционально, пусто = 0).
    /// </summary>
    public int LoadFromContent(string content)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст не задан. Ожидается формат: Состояние|Комбинации стилей|фраза1 - фраза2 - фраза3|Тон|Настроение.", nameof(content));

      content = GenerateListContentPreprocessor.Preprocess(content);
      if (string.IsNullOrWhiteSpace(content))
        throw new ArgumentException("Текст не задан. Ожидается формат: Состояние|Комбинации стилей|фраза1 - фраза2 - фраза3|Тон|Настроение.", nameof(content));

      var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
      var validLines = new List<(int baseId, List<int> styleIds, string phrasePart, int toneId, int moodId)>();

      foreach (var line in lines)
      {
        var trimmed = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("#"))
          continue;

        var parts = trimmed.Split('|');
        if (parts.Length < 5)
          continue;

        string stateStr = parts[0].Trim();
        string stylesStr = parts[1].Trim();
        string phrasePart = parts[2].Trim();
        string toneStr = parts[3].Trim();
        string moodStr = parts[4].Trim();

        if (!TryParseState(stateStr, out int baseId))
          continue;
        if (!TryParseStyles(stylesStr, out List<int> styleIds) || styleIds == null || styleIds.Count == 0)
          continue;

        int toneId = string.IsNullOrWhiteSpace(toneStr) ? 0 : ActionsImagesSystem.GetToneIdByText(toneStr);
        int moodId = string.IsNullOrWhiteSpace(moodStr) ? 0 : ActionsImagesSystem.GetMoodIdByText(moodStr);
        string normalToneText = ActionsImagesSystem.GetToneText(0);
        if (!string.IsNullOrWhiteSpace(toneStr) && toneId == 0 && !string.Equals(toneStr, normalToneText, StringComparison.OrdinalIgnoreCase))
        {
          var codes = string.Join(" ", toneStr.Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
          Logger.Warning($"Тон не распознан (будет Нормальный): \"{toneStr}\". Коды: {codes}. Допустимы: Вялый, Нормальный, Повышенный.");
        }
        string normalMoodText = ActionsImagesSystem.GetMoodText(0);
        if (!string.IsNullOrWhiteSpace(moodStr) && moodId == 0 && !string.Equals(moodStr, normalMoodText, StringComparison.OrdinalIgnoreCase))
        {
          var codes = string.Join(" ", moodStr.Take(15).Select(c => "U+" + ((int)c).ToString("X4")));
          Logger.Warning($"Настроение не распознано (будет Нормальное): \"{moodStr}\". Коды: {codes}. Допустимы: Нормальное, Хорошее, Плохое, Игривое, Учитель, Агрессивное, Защитное, Протест.");
        }

        var stimuli = ParseStimuliLine(phrasePart);
        if (stimuli == null || stimuli.Count < 2)
          continue;

        validLines.Add((baseId, styleIds, phrasePart, toneId, moodId));
      }

      if (validLines.Count == 0)
        throw new ArgumentException(
          "Нет корректных строк. Ожидается формат: Состояние|Комбинации стилей|фраза1 - фраза2 - фраза3|Тон|Настроение (например: Норма|Поиск+Игра|привет - как дела - все ок|Нормальный|Хорошее).",
          nameof(content));

      if (!CheckSystems()) return 0;

      _phraseIdCache.Clear();

      if (!PreloadAllPhrasesFromPhraseParts(validLines.Select(x => x.phrasePart)))
      {
        Logger.Error("Не удалось загрузить фразы");
        return 0;
      }

      _automatizmSystem.SetSuppressCreateLogging(true);
      _emotionsImageSystem.SetSuppressFoundExistingLog(true);
      _verbalBrocaSystem.SetSuppressFoundExistingLog(true);
      try
      {
        int processedChains = 0;
        foreach (var (baseId, styleIds, phrasePart, toneId, moodId) in validLines)
        {
          var stimuli = ParseStimuliLine(phrasePart);
          if (stimuli != null && stimuli.Count >= 2 && ProcessChainDirect(stimuli, baseId, styleIds, toneId, moodId))
            processedChains++;
        }
        return processedChains;
      }
      finally
      {
        _automatizmSystem.SetSuppressCreateLogging(false);
        _emotionsImageSystem.SetSuppressFoundExistingLog(false);
        _verbalBrocaSystem.SetSuppressFoundExistingLog(false);
      }
    }

    /// <summary>
    /// Загружает автоматизмы из файла (формат: Состояние|Комбинации стилей|фраза1 - фраза2).
    /// </summary>
    public int LoadFromFile()
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      if (!CheckSystems()) return 0;

      string filePath = Path.Combine(_bootDataFolder, AutomatizmChainsFileName);
      if (!File.Exists(filePath))
      {
        Logger.Info($"Файл не найден: {filePath}");
        return 0;
      }

      string content = File.ReadAllText(filePath, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(content)) return 0;

      try
      {
        return LoadFromContent(content);
      }
      catch (ArgumentException)
      {
        return 0;
      }
    }

    /// <summary>
    /// Загружает автоматизмы из файла (старый формат: только цепочки фраз, одно состояние и стили для всего файла).
    /// </summary>
    public int LoadFromFile(int baseId, List<int> styleIds)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      if (!CheckSystems()) return 0;

      string filePath = Path.Combine(_bootDataFolder, AutomatizmChainsFileName);
      if (!File.Exists(filePath))
      {
        Logger.Info($"Файл не найден: {filePath}");
        return 0;
      }

      string content = File.ReadAllText(filePath, Encoding.UTF8);
      if (string.IsNullOrWhiteSpace(content)) return 0;

      try
      {
        return LoadFromContent(content, baseId, styleIds);
      }
      catch (ArgumentException)
      {
        return 0;
      }
    }

    private static bool TryParseState(string stateStr, out int baseId)
    {
      baseId = 0;
      if (string.IsNullOrWhiteSpace(stateStr))
        return false;
      return StateMap.TryGetValue(stateStr.Trim(), out baseId);
    }

    private bool TryParseStyles(string stylesStr, out List<int> styleIds)
    {
      styleIds = new List<int>();
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
        styleIds.Add(style.Id);
      }
      styleIds = styleIds.OrderBy(x => x).Distinct().ToList();
      return styleIds.Count > 0;
    }

    private bool PreloadAllPhrasesFromPhraseParts(IEnumerable<string> phraseParts)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var allUniquePhrases = new HashSet<string>();

      foreach (var phrasePart in phraseParts)
      {
        foreach (var stimulus in ParseStimuliLine(phrasePart))
        {
          string normalized = stimulus.Trim().ToLowerInvariant();
          if (!string.IsNullOrEmpty(normalized))
            allUniquePhrases.Add(normalized);
        }
      }

      bool originalMode = _verbalChannel.AuthoritativeMode;
      _verbalChannel.AuthoritativeMode = true;

      int successCount = 0;

      try
      {
        foreach (var phrase in allUniquePhrases)
        {
          var phraseIds = _verbalChannel.RecognizeText(phrase, authoritativeWrite: true);

          if (phraseIds != null && phraseIds.Count > 0)
          {
            _phraseIdCache[phrase] = phraseIds[0];
            successCount++;
          }
          else
          {
            int phraseId = _verbalChannel.FindPhraseId(phrase);
            if (phraseId > 0)
            {
              _phraseIdCache[phrase] = phraseId;
              successCount++;
            }
          }
        }
      }
      finally
      {
        _verbalChannel.AuthoritativeMode = originalMode;
      }

      return successCount > 0;
    }

    private bool CheckSystems()
    {
      // Проверяем, что все системы доступны
      return _automatizmSystem != null &&
             _treeSystem != null &&
             _actionsImagesSystem != null &&
             _verbalBrocaSystem != null &&
             _verbalChannel != null &&
             _emotionsImageSystem != null;
    }

    private static string[] ReadAllLinesWithEncoding(string filePath)
    {
      try { return File.ReadAllLines(filePath, Encoding.UTF8); }
      catch { return File.ReadAllLines(filePath, Encoding.Default); }
    }

    private bool PreloadAllPhrases(string[] lines)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var allUniquePhrases = new HashSet<string>();

      foreach (var line in lines)
      {
        var trimmedLine = line.Trim();
        if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
          continue;

        foreach (var stimulus in ParseStimuliLine(trimmedLine))
        {
          string normalized = stimulus.Trim().ToLowerInvariant();
          if (!string.IsNullOrEmpty(normalized))
            allUniquePhrases.Add(normalized);
        }
      }

      bool originalMode = _verbalChannel.AuthoritativeMode;
      _verbalChannel.AuthoritativeMode = true;

      int successCount = 0;

      try
      {
        foreach (var phrase in allUniquePhrases)
        {
          var phraseIds = _verbalChannel.RecognizeText(phrase, authoritativeWrite: true);

          if (phraseIds != null && phraseIds.Count > 0)
          {
            _phraseIdCache[phrase] = phraseIds[0];
            successCount++;
          }
          else
          {
            int phraseId = _verbalChannel.FindPhraseId(phrase);
            if (phraseId > 0)
            {
              _phraseIdCache[phrase] = phraseId;
              successCount++;
            }
          }
        }
      }
      finally
      {
        _verbalChannel.AuthoritativeMode = originalMode;
      }

      return successCount > 0;
    }

    private bool ProcessChainDirect(List<string> stimuli, int baseId, List<int> styleIds, int toneId, int moodId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      bool anySuccess = false;
      int? previousNodeId = null;

      for (int i = 0; i < stimuli.Count; i++)
      {
        string operatorStimulus = stimuli[i].Trim().ToLowerInvariant();

        if (!_phraseIdCache.TryGetValue(operatorStimulus, out int phraseId))
          continue;

        int actionsImageId = CreateActionsImageForStimulus(phraseId, toneId, moodId);
        if (actionsImageId <= 0) continue;

        // ВАЖНО: Для каждого стимула создаем уникальный узел
        int nodeId = CreateTreeNode(
            operatorStimulus,
            phraseId,
            baseId,
            styleIds,
            toneId,
            moodId);

        if (nodeId <= 0) continue;

        if (i == 0)
        {
          // Первый стимул: создаем эхо-автоматизм
          var (parrotId, parrotAtmz) = _automatizmSystem.CreateNewAutomatizm(nodeId, actionsImageId, true);
          if (parrotAtmz != null)
          {
            parrotAtmz.Usefulness = 0;
            parrotAtmz.Count = 0;
            _automatizmSystem.SetAutomatizmBelief(parrotAtmz, 2);
            anySuccess = true;
          }
        }
        else if (previousNodeId.HasValue)
        {
          // Последующие стимулы: создаем связующий автоматизм от предыдущего узла к текущему действию
          var (mirrorId, mirrorAtmz) = _automatizmSystem.CreateNewAutomatizm(previousNodeId.Value, actionsImageId, true);
          if (mirrorAtmz != null)
          {
            mirrorAtmz.Usefulness = 2;
            mirrorAtmz.Count = 1;
            _automatizmSystem.SetAutomatizmBelief(mirrorAtmz, 2);
            anySuccess = true;
          }
        }

        previousNodeId = nodeId;
      }

      return anySuccess;
    }

    private int CreateTreeNode(
        string stimulus,
        int phraseId,
        int baseId,
        List<int> styleIds,
        int toneId,
        int moodId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      // Получаем ID эмоции из списка стилей
      int emotionId = 0;
      if (styleIds != null && styleIds.Count > 0)
      {
        var (id, _) = _emotionsImageSystem.CreateNewEmotionsImage(styleIds, true);
        emotionId = id;
      }

      int activityId = 0;
      int toneMoodId = PsychicSystem.GetToneMoodID(toneId, moodId);
      int firstSimbol = GetFirstSymbol(stimulus);
      int verbId = CreateVerbalImage(stimulus, firstSimbol, phraseId, toneId, moodId);

      // Найти или создать узел с учётом иерархии (база → эмоция → activity → toneMood → simbol/verb)
      return FindOrCreateTreeNodeByCondition(baseId, emotionId, activityId, toneMoodId, firstSimbol, verbId, 0);
    }

    /// <summary>
    /// Находит узел по условиям или создаёт его и всю цепочку родителей (не вешая фразовые узлы прямо на базовую ветку).
    /// </summary>
    /// <param name="baseId">Базовое состояние.</param>
    /// <param name="emotionId">ID образа эмоций.</param>
    /// <param name="activityId">ID образа действий.</param>
    /// <param name="toneMoodId">ID тона и настроения.</param>
    /// <param name="simbolId">ID первого символа фразы.</param>
    /// <param name="verbId">ID вербального образа.</param>
    /// <param name="visualId">Код зрительного канала (<see cref="ISIDA.Reflexes.AgentVisualColor"/>); для загрузки цепочек из файла — 0.</param>
    private int FindOrCreateTreeNodeByCondition(
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbId,
        int visualId)
    {
      var existing = _treeSystem.FindAutomatizmTreeNodeFromCondition(
          baseId, emotionId, activityId, toneMoodId, simbolId, verbId, visualId);
      if (existing.Node != null)
        return existing.Id;

      AutomatizmNode parentNode = GetParentNodeForCondition(baseId, emotionId, activityId, toneMoodId, simbolId, verbId, visualId);
      if (parentNode == null)
        return 0;

      var (newNodeId, newNode) = _treeSystem.CreateNewAutomatizmNode(
          parentNode,
          0,
          baseId,
          emotionId,
          activityId,
          toneMoodId,
          simbolId,
          verbId,
          visualId,
          true);

      return newNodeId;
    }

    /// <summary>
    /// Возвращает родительский узел для заданных условий: на один уровень иерархии выше.
    /// При необходимости рекурсивно создаёт промежуточные узлы.
    /// </summary>
    private AutomatizmNode GetParentNodeForCondition(
        int baseId,
        int emotionId,
        int activityId,
        int toneMoodId,
        int simbolId,
        int verbId,
        int visualId)
    {
      var (pBase, pEmo, pAct, pTone, pSim, pVerb, pVis) = GetParentCondition(baseId, emotionId, activityId, toneMoodId, simbolId, verbId, visualId);

      // Родитель — базовая ветка (прямой потомок корня)
      if (IsBaseBranchCondition(pBase, pEmo, pAct, pTone, pSim, pVerb, pVis))
      {
        foreach (var child in _treeSystem.Tree.Children)
        {
          if (child.BaseID == pBase)
            return child;
        }
        Logger.Error($"Не найден корневой узел с baseId={pBase}");
        return null;
      }

      // Родитель — промежуточный узел; находим или создаём его
      int parentId = FindOrCreateTreeNodeByCondition(pBase, pEmo, pAct, pTone, pSim, pVerb, pVis);
      if (parentId <= 0)
        return null;

      return _treeSystem.GetNodeById(parentId);
    }

    private static bool IsBaseBranchCondition(int baseId, int emotionId, int activityId, int toneMoodId, int simbolId, int verbId, int visualId)
    {
      return emotionId == 0 && activityId == 0 && toneMoodId == 0 && simbolId == 0 && verbId == 0 && visualId == 0
          && (baseId == -1 || baseId == 0 || baseId == 1);
    }

    private static (int BaseID, int EmotionID, int ActivityID, int ToneMoodID, int SimbolID, int VerbID, int VisualID) GetParentCondition(
        int baseId, int emotionId, int activityId, int toneMoodId, int simbolId, int verbId, int visualId)
    {
      if (visualId != 0)
        return (baseId, emotionId, activityId, toneMoodId, simbolId, verbId, 0);
      if (verbId != 0)
        return (baseId, emotionId, activityId, toneMoodId, simbolId, 0, 0);
      if (simbolId != 0)
        return (baseId, emotionId, activityId, toneMoodId, 0, 0, 0);
      if (toneMoodId != 0)
        return (baseId, emotionId, activityId, 0, 0, 0, 0);
      if (activityId != 0)
        return (baseId, emotionId, 0, 0, 0, 0, 0);
      if (emotionId != 0)
        return (baseId, 0, 0, 0, 0, 0, 0);
      return (baseId, 0, 0, 0, 0, 0, 0);
    }

    private static List<string> ParseStimuliLine(string line)
    {
      var result = new List<string>();

      if (line.Contains(';'))
      {
        result.AddRange(line.Split(';')
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s)));
      }
      else if (line.Contains(" - "))
      {
        result.AddRange(line.Split(new[] { " - " }, StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s)));
      }
      else
      {
        result.Add(line.Trim());
      }

      return result;
    }

    private int CreateActionsImageForStimulus(int phraseId, int toneId, int moodId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var (id, _) = _actionsImagesSystem.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: new List<int> { phraseId },
          toneId: toneId,
          moodId: moodId,
          checkUnicum: true);

      return id;
    }

    private int GetFirstSymbol(string word)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      if (string.IsNullOrEmpty(word)) return 0;
      return _verbalChannel.GetPrimarySensorId(word[0]);
    }

    private int CreateVerbalImage(string stimulus, int firstSimbol, int phraseId, int toneId, int moodId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var (id, _) = _verbalBrocaSystem.CreateNewVerbalBrocaImage(
          firstSimbol,
          new List<int> { phraseId },
          toneId,
          moodId,
          true);

      return id;
    }

    #region IDisposable Implementation

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AutomatizmFileLoader
    /// </summary>
    public void Dispose()
    {
      Dispose(true);
      GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Освобождает ресурсы, используемые объектом AutomatizmFileLoader
    /// </summary>
    /// <param name="disposing">True если вызвано из Dispose, false если из финализатора</param>
    private void Dispose(bool disposing)
    {
      if (_disposed)
        return;

      if (disposing)
      {
        _phraseIdCache.Clear();
      }
      _disposed = true;
      _instance = null;
    }

    #endregion
  }
}