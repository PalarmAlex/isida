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
  /// Статический класс для загрузки автоматизмов из файла
  /// </summary>
  public static class AutomatizmFileLoader
  {
    private const string AutomatizmChainsFileName = "automatizm_generate_list_1.csv";
    private static readonly Dictionary<string, int> _phraseIdCache = new Dictionary<string, int>();

    /// <summary>
    /// Загружает автоматизмы из файла
    /// </summary>
    public static int LoadFromFile(string bootDataFolder, int baseId, List<int> styleIds)
    {
      if (!CheckSystems()) return 0;

      string filePath = Path.Combine(bootDataFolder, AutomatizmChainsFileName);
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

    private static bool CheckSystems()
    {
      if (!AutomatizmSystem.IsInitialized) return false;
      if (!AutomatizmTreeSystem.IsInitialized) return false;
      if (!ActionsImagesSystem.IsInitialized) return false;
      if (!VerbalBrocaImagesSystem.IsInitialized) return false;
      if (!SensorySystem.IsInitialized) return false;
      return true;
    }

    private static string[] ReadAllLinesWithEncoding(string filePath)
    {
      try { return File.ReadAllLines(filePath, Encoding.UTF8); }
      catch { return File.ReadAllLines(filePath, Encoding.Default); }
    }

    private static bool PreloadAllPhrases(string[] lines)
    {
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

      var verbalChannel = SensorySystem.Instance.VerbalChannel;
      bool originalMode = verbalChannel.AuthoritativeMode;
      verbalChannel.AuthoritativeMode = true;

      int successCount = 0;

      try
      {
        foreach (var phrase in allUniquePhrases)
        {
          var phraseIds = verbalChannel.RecognizeText(phrase, authoritativeWrite: true);

          if (phraseIds != null && phraseIds.Count > 0)
          {
            _phraseIdCache[phrase] = phraseIds[0];
            successCount++;
          }
          else
          {
            int phraseId = verbalChannel.FindPhraseId(phrase);
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
        verbalChannel.AuthoritativeMode = originalMode;
      }

      verbalChannel.WordTree.Save();
      verbalChannel.PhraseTree.Save();

      return successCount > 0;
    }

    private static bool ProcessChainDirect(List<string> stimuli, int baseId, List<int> styleIds)
    {
      var treeSystem = AutomatizmTreeSystem.Instance;
      var actionsImagesSystem = ActionsImagesSystem.Instance;
      var verbalBrocaSystem = VerbalBrocaImagesSystem.Instance;
      var verbalChannel = SensorySystem.Instance.VerbalChannel;
      var automatizmSystem = AutomatizmSystem.Instance;

      bool anySuccess = false;
      int? previousNodeId = null;

      for (int i = 0; i < stimuli.Count; i++)
      {
        string operatorStimulus = stimuli[i].Trim().ToLowerInvariant();

        if (!_phraseIdCache.TryGetValue(operatorStimulus, out int phraseId))
          continue;

        int actionsImageId = CreateActionsImageForStimulus(actionsImagesSystem, phraseId);
        if (actionsImageId <= 0) continue;

        // ВАЖНО: Для каждого стимула создаем уникальный узел
        int nodeId = CreateTreeNode(
            operatorStimulus,
            phraseId,
            baseId,
            treeSystem,
            verbalBrocaSystem,
            verbalChannel,
            styleIds);

        if (nodeId <= 0) continue;

        if (i == 0)
        {
          // Первый стимул: создаем эхо-автоматизм
          var (parrotId, parrotAtmz) = automatizmSystem.CreateNewAutomatizm(nodeId, actionsImageId, true);
          if (parrotAtmz != null)
          {
            parrotAtmz.Usefulness = 0;
            parrotAtmz.Count = 0;
            automatizmSystem.SetAutomatizmBelief(parrotAtmz, 2);
            anySuccess = true;
          }
        }
        else if (previousNodeId.HasValue)
        {
          // Последующие стимулы: создаем связующий автоматизм от предыдущего узла к текущему действию
          var (mirrorId, mirrorAtmz) = automatizmSystem.CreateNewAutomatizm(previousNodeId.Value, actionsImageId, true);
          if (mirrorAtmz != null)
          {
            mirrorAtmz.Usefulness = 1;
            mirrorAtmz.Count = 1;
            automatizmSystem.SetAutomatizmBelief(mirrorAtmz, 2);
            anySuccess = true;
          }
        }

        previousNodeId = nodeId;
      }

      return anySuccess;
    }

    private static int CreateTreeNode(
        string stimulus,
        int phraseId,
        int baseId,
        AutomatizmTreeSystem treeSystem,
        VerbalBrocaImagesSystem verbalBrocaSystem,
        VerbalSensorChannel verbalChannel,
        List<int> styleIds)
    {
      // Получаем ID эмоции из списка стилей
      int emotionId = 0;
      if (styleIds != null && styleIds.Count > 0)
      {
        var (id, _) = EmotionsImageSystem.Instance.CreateNewEmotionsImage(styleIds, true);
        emotionId = id;
      }

      int activityId = 0;
      int toneMoodId = PsychicSystem.GetToneMoodID(0, 0);
      int firstSimbol = GetFirstSymbol(verbalChannel, stimulus);
      int verbId = CreateVerbalImage(verbalBrocaSystem, stimulus, firstSimbol, phraseId);

      var existingNode = treeSystem.FindAutomatizmTreeNodeFromCondition(
          baseId, emotionId, activityId, toneMoodId, firstSimbol, verbId);

      if (existingNode.Node != null)
        return existingNode.Id;

      AutomatizmNode parentNode = null;
      foreach (var child in treeSystem.Tree.Children)
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
      var (newNodeId, newNode) = treeSystem.CreateNewAutomatizmNode(
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

    private static int CreateActionsImageForStimulus(ActionsImagesSystem actionsImagesSystem, int phraseId)
    {
      var (id, _) = actionsImagesSystem.CreateNewActionsImage(
          kind: 0,
          actIdList: new List<int>(),
          phraseIdList: new List<int> { phraseId },
          toneId: 0,
          moodId: 0,
          checkUnicum: true);

      return id;
    }

    private static int GetFirstSymbol(VerbalSensorChannel verbalChannel, string word)
    {
      if (string.IsNullOrEmpty(word)) return 0;
      return verbalChannel.GetPrimarySensorId(word[0]);
    }

    private static int CreateVerbalImage(VerbalBrocaImagesSystem verbalBrocaSystem, string stimulus, int firstSimbol, int phraseId)
    {
      var (id, _) = verbalBrocaSystem.CreateNewVerbalBrocaImage(
          firstSimbol,
          new List<int> { phraseId },
          0,
          0,
          true);

      return id;
    }
  }
}