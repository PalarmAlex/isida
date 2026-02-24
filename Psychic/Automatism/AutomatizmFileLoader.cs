using ISIDA.Common;
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
  /// Класс для загрузки автоматизмов из файла
  /// </summary>
  public sealed class AutomatizmFileLoader : IDisposable
  {
    private const string AutomatizmChainsFileName = "automatizm_generate_list.csv";
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
    /// Загружает автоматизмы из файла
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

      var lines = ReadAllLinesWithEncoding(filePath);
      if (lines.Length == 0) return 0;

      _phraseIdCache.Clear();

      if (!PreloadAllPhrases(lines))
      {
        Logger.Error("Не удалось загрузить фразы");
        return 0;
      }

      // Отключаем детальное логирование при массовой генерации (ускоряет загрузку по 100+ строкам)
      _automatizmSystem.SetSuppressCreateLogging(true);
      _emotionsImageSystem.SetSuppressFoundExistingLog(true);
      _verbalBrocaSystem.SetSuppressFoundExistingLog(true);
      try
      {
        int processedChains = 0;
        int totalLines = 0;

        foreach (var line in lines)
        {
          var trimmedLine = line.Trim();
          if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith("#"))
            continue;

          totalLines++;
          var stimuli = ParseStimuliLine(trimmedLine);
          if (stimuli.Count < 2) continue;

          if (ProcessChainDirect(stimuli, baseId, styleIds))
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

      _verbalChannel.WordTree.Save();
      _verbalChannel.PhraseTree.Save();

      return successCount > 0;
    }

    private bool ProcessChainDirect(List<string> stimuli, int baseId, List<int> styleIds)
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

        int actionsImageId = CreateActionsImageForStimulus(phraseId);
        if (actionsImageId <= 0) continue;

        // ВАЖНО: Для каждого стимула создаем уникальный узел
        int nodeId = CreateTreeNode(
            operatorStimulus,
            phraseId,
            baseId,
            styleIds);

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
            mirrorAtmz.Usefulness = 1;
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
        List<int> styleIds)
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
      int toneMoodId = PsychicSystem.GetToneMoodID(0, 0);
      int firstSimbol = GetFirstSymbol(stimulus);
      int verbId = CreateVerbalImage(stimulus, firstSimbol, phraseId);

      var existingNode = _treeSystem.FindAutomatizmTreeNodeFromCondition(
          baseId, emotionId, activityId, toneMoodId, firstSimbol, verbId);

      if (existingNode.Node != null)
        return existingNode.Id;

      AutomatizmNode parentNode = null;
      foreach (var child in _treeSystem.Tree.Children)
      {
        if (child.BaseID == baseId)
        {
          parentNode = child;
          break;
        }
      }

      if (parentNode == null)
      {
        Logger.Error($"Не найден корневой узел с baseId={baseId}");
        return 0;
      }

      // Создаем новый узел с полным набором параметров
      var (newNodeId, newNode) = _treeSystem.CreateNewAutomatizmNode(
          parentNode,
          0,
          baseId,
          emotionId,
          activityId,
          toneMoodId,
          firstSimbol,
          verbId,
          true);

      return newNodeId;
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

    private int CreateActionsImageForStimulus(int phraseId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var (id, _) = _actionsImagesSystem.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: new List<int> { phraseId },
          toneId: 0,
          moodId: 0,
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

    private int CreateVerbalImage(string stimulus, int firstSimbol, int phraseId)
    {
      if (_disposed)
        throw new ObjectDisposedException(nameof(AutomatizmFileLoader));

      var (id, _) = _verbalBrocaSystem.CreateNewVerbalBrocaImage(
          firstSimbol,
          new List<int> { phraseId },
          0,
          0,
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
    }

    #endregion
  }
}