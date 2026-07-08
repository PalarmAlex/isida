using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Направленные сенсорные ассоциации CS₁→CS₂ между образами восприятия (модель Рескорла–Вагнера).
  /// </summary>
  public sealed class SensoryAssociationSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private bool _disposed;

    private const string SensoryAssociationsFileName = "SensoryAssociations";

    /// <summary>
    /// Запись направленной сенсорной связи между образами восприятия.
    /// </summary>
    public class SensoryAssociation
    {
      /// <summary>ID более раннего образа (CS₁)</summary>
      public int EarlierImageId { get; set; }

      /// <summary>ID более позднего образа (CS₂)</summary>
      public int LaterImageId { get; set; }

      /// <summary>Крепость связи C ∈ [0, β]</summary>
      public float Strength { get; set; }

      /// <summary>Пульс последнего усиления</summary>
      public int LastStrengthenPulse { get; set; }

      /// <summary>Пульс создания связи</summary>
      public int BirthTimePulse { get; set; }

      /// <summary>Максимальная достигнутая крепость</summary>
      public float MaxAchievedStrength { get; set; }
    }

    #region Инициализация

    private static SensoryAssociationSystem _instance;

    /// <summary>Глобальный экземпляр системы сенсорных ассоциаций</summary>
    public static SensoryAssociationSystem Instance => _instance ??
        throw new InvalidOperationException("SensoryAssociationSystem не инициализирован.");

    /// <summary>Флаг инициализации класса</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы сенсорных ассоциаций
    /// </summary>
    public static void InitializeInstance(
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      if (_instance != null)
        throw new InvalidOperationException("SensoryAssociationSystem уже инициализирован.");

      _instance = new SensoryAssociationSystem(geneticReflexes, conditionedReflexes);
    }

    private SensoryAssociationSystem(
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      _conditionedReflexes = conditionedReflexes ?? throw new ArgumentNullException(nameof(conditionedReflexes));

      try
      {
        EnsureDataDirectory();
        Load();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
        throw;
      }
    }

    #endregion

    #region Поля

    private readonly Dictionary<(int Earlier, int Later), SensoryAssociation> _links =
        new Dictionary<(int Earlier, int Later), SensoryAssociation>();

    private ConditionedReflexesSystem.ConditionedReflexSettings Settings =>
        _conditionedReflexes.Settings;

    #endregion

    #region Публичный API

    /// <summary>
    /// Усиливает направленную связь earlierImageId → laterImageId по модели Рескорла–Вагнера.
    /// </summary>
    public void StrengthenLink(int earlierImageId, int laterImageId)
    {
      if (earlierImageId <= 0 || laterImageId <= 0 || earlierImageId == laterImageId)
        return;

      int currentPulse = GetAgentLifetime();
      var key = (earlierImageId, laterImageId);

      _lock.EnterWriteLock();
      try
      {
        if (!_links.TryGetValue(key, out var link))
        {
          link = new SensoryAssociation
          {
            EarlierImageId = earlierImageId,
            LaterImageId = laterImageId,
            Strength = 0f,
            BirthTimePulse = currentPulse
          };
          _links[key] = link;
        }

        float alpha = Settings.LearningRate;
        float beta = Settings.MaxAssociationStrength;
        link.Strength = link.Strength + alpha * (beta - link.Strength);
        link.Strength = Math.Min(link.Strength, beta);

        if (link.Strength > link.MaxAchievedStrength)
          link.MaxAchievedStrength = link.Strength;

        link.LastStrengthenPulse = currentPulse;
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Получает крепость связи earlier → later</summary>
    public bool TryGetStrength(int earlierImageId, int laterImageId, out float strength)
    {
      strength = 0f;
      if (earlierImageId <= 0 || laterImageId <= 0)
        return false;

      _lock.EnterReadLock();
      try
      {
        if (_links.TryGetValue((earlierImageId, laterImageId), out var link))
        {
          strength = link.Strength;
          return true;
        }
        return false;
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Проверяет, достаточна ли крепость связи для иерархической активации (≥ γ)</summary>
    public bool IsLinkActivatable(int earlierImageId, int laterImageId)
    {
      return TryGetStrength(earlierImageId, laterImageId, out float strength) &&
             strength >= Settings.ActivationThreshold;
    }

    /// <summary>Применяет затухание ко всем связям и удаляет ослабленные</summary>
    public void ApplyDecay()
    {
      int currentPulse = GetAgentLifetime();
      if (currentPulse % 100 != 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        var keysToRemove = new List<(int Earlier, int Later)>();

        foreach (var kv in _links)
        {
          var link = kv.Value;
          ApplyDecayToLink(link);

          if (link.Strength < Settings.MinAssociationStrength)
            keysToRemove.Add(kv.Key);
        }

        foreach (var key in keysToRemove)
          _links.Remove(key);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Сохраняет связи в файл</summary>
    public (bool Success, string ErrorMessage) Save()
    {
      _lock.EnterReadLock();
      try
      {
        var lines = new List<string>
        {
          FileHeaders.SensoryAssociationsFormat,
          FileHeaders.SensoryAssociationsFields
        };

        foreach (var link in _links.Values
            .Where(l => l.Strength >= Settings.MinAssociationStrength)
            .OrderBy(l => l.EarlierImageId)
            .ThenBy(l => l.LaterImageId))
        {
          lines.Add($"{link.EarlierImageId}|{link.LaterImageId}|{link.Strength}|" +
                    $"{link.LastStrengthenPulse}|{link.BirthTimePulse}|{link.MaxAchievedStrength}");
        }

        return FileValidator.SafeSaveFile(
            GetFilePath(),
            lines,
            content => true,
            minLinesCount: 2,
            fileDescription: "сенсорных ассоциаций");
      }
      catch (Exception ex)
      {
        return (false, ex.Message);
      }
      finally
      {
        _lock.ExitReadLock();
      }
    }

    /// <summary>Загружает связи из файла</summary>
    public void Load()
    {
      string filePath = GetFilePath();
      if (!File.Exists(filePath))
        return;

      _lock.EnterWriteLock();
      try
      {
        _links.Clear();

        foreach (var line in File.ReadLines(filePath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

          var parts = line.Split('|');
          if (parts.Length < 6)
            continue;

          if (!int.TryParse(parts[0], out int earlierId) ||
              !int.TryParse(parts[1], out int laterId))
            continue;

          if (earlierId <= 0 || laterId <= 0 || earlierId == laterId)
            continue;

          if (!float.TryParse(parts[2], out float strength))
            continue;

          var link = new SensoryAssociation
          {
            EarlierImageId = earlierId,
            LaterImageId = laterId,
            Strength = strength,
            LastStrengthenPulse = int.TryParse(parts[3], out int last) ? last : 0,
            BirthTimePulse = int.TryParse(parts[4], out int birth) ? birth : 0,
            MaxAchievedStrength = float.TryParse(parts[5], out float max) ? max : strength
          };

          if (link.Strength >= Settings.MinAssociationStrength)
            _links[(earlierId, laterId)] = link;
        }
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Внутренние методы

    private void ApplyDecayToLink(SensoryAssociation link)
    {
      float decayRate = Settings.DecayRate;
      float strengthFactor = Math.Max(0.1f, link.Strength);
      float effectiveDecayRate;

      if (link.Strength > 0.8f)
        effectiveDecayRate = 0.998f;
      else if (link.Strength > 0.4f)
        effectiveDecayRate = (float)Math.Pow(decayRate, strengthFactor);
      else
        effectiveDecayRate = (float)Math.Pow(decayRate, Math.Sqrt(strengthFactor));

      link.Strength *= effectiveDecayRate;

      if (link.Strength > link.MaxAchievedStrength)
        link.MaxAchievedStrength = link.Strength;
    }

    private int GetAgentLifetime()
    {
      try
      {
        return _conditionedReflexes.GetCurrentAgentLifetime();
      }
      catch
      {
        return AppGlobalState.Lifetime;
      }
    }

    private void EnsureDataDirectory()
    {
      string directory = Path.GetDirectoryName(GetFilePath());
      if (!Directory.Exists(directory))
        Directory.CreateDirectory(directory);
    }

    private string GetFilePath()
    {
      string reflexesPath = _geneticReflexes.GetGeneticReflexesFilePath();
      string directory = Path.GetDirectoryName(reflexesPath);
      return Path.Combine(directory, $"{SensoryAssociationsFileName}.dat");
    }

    #endregion

    #region IDisposable

    /// <summary>Освобождает ресурсы системы</summary>
    public void Dispose()
    {
      if (_disposed) return;

      try
      {
        Save();
      }
      catch (Exception ex)
      {
        Logger.Error(ex.Message);
      }
      finally
      {
        _lock?.Dispose();
        _disposed = true;
        _instance = null;
      }
    }

    #endregion
  }
}
