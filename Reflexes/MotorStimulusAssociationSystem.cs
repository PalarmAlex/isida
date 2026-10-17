using ISIDA.Common;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using static ISIDA.Common.FileValidator;

namespace ISIDA.Reflexes
{
  /// <summary>
  /// Направленные ассоциации «моторное действие → образ-стимул» (модель Рескорла–Вагнера).
  /// Связывают выполненное моторное действие с нейтральным стимулом, следующим за ним, что
  /// позволяет строить цепочки у-рефлексов поверх безусловных стимулов (мост «мотор → стимул»).
  /// </summary>
  public sealed class MotorStimulusAssociationSystem : IDisposable
  {
    private readonly ReaderWriterLockSlim _lock = new ReaderWriterLockSlim();
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly ConditionedReflexesSystem _conditionedReflexes;
    private bool _disposed;

    private const string MotorStimulusAssociationsFileName = "MotorStimulusAssociations";

    /// <summary>
    /// Запись направленной связи «моторное действие → образ-стимул».
    /// </summary>
    public class MotorStimulusAssociation
    {
      /// <summary>ID моторного действия</summary>
      public int MotorId { get; set; }

      /// <summary>ID образа-стимула</summary>
      public int StimulusImageId { get; set; }

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

    private static MotorStimulusAssociationSystem _instance;

    /// <summary>Глобальный экземпляр системы мотор→стимул ассоциаций</summary>
    public static MotorStimulusAssociationSystem Instance => _instance ??
        throw new InvalidOperationException("MotorStimulusAssociationSystem не инициализирован.");

    /// <summary>Флаг инициализации класса</summary>
    public static bool IsInitialized => _instance != null;

    /// <summary>
    /// Инициализирует глобальный экземпляр системы мотор→стимул ассоциаций.
    /// </summary>
    /// <param name="geneticReflexes">Система безусловных рефлексов (директория файлов данных).</param>
    /// <param name="conditionedReflexes">Система условных рефлексов (настройки, время жизни агента).</param>
    /// <exception cref="InvalidOperationException">Система уже инициализирована.</exception>
    public static void InitializeInstance(
        GeneticReflexesSystem geneticReflexes,
        ConditionedReflexesSystem conditionedReflexes)
    {
      if (_instance != null)
        throw new InvalidOperationException("MotorStimulusAssociationSystem уже инициализирован.");

      _instance = new MotorStimulusAssociationSystem(geneticReflexes, conditionedReflexes);
    }

    private MotorStimulusAssociationSystem(
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

    private readonly Dictionary<(int Motor, int Stimulus), MotorStimulusAssociation> _links =
        new Dictionary<(int Motor, int Stimulus), MotorStimulusAssociation>();

    /// <summary>
    /// Индекс исходящих рёбер: мотор → список (образ-стимул, крепость).
    /// Обновляется при StrengthenLink / ApplyDecay / Load.
    /// </summary>
    private readonly Dictionary<int, List<(int Stimulus, float Strength)>> _outLinks =
        new Dictionary<int, List<(int Stimulus, float Strength)>>();

    private ConditionedReflexesSystem.ConditionedReflexSettings Settings =>
        _conditionedReflexes.Settings;

    #endregion

    #region Внутренние методы — индекс

    /// <summary>
    /// Перестраивает индекс исходящих рёбер из _links. Вызывается из Load и ApplyDecay.
    /// </summary>
    private void RebuildOutLinksIndex()
    {
      _outLinks.Clear();
      foreach (var kv in _links)
      {
        var key = kv.Key;
        var link = kv.Value;
        if (!_outLinks.TryGetValue(key.Motor, out var list))
        {
          list = new List<(int Stimulus, float Strength)>();
          _outLinks[key.Motor] = list;
        }
        list.Add((key.Stimulus, link.Strength));
      }
    }

    /// <summary>
    /// Инкрементально обновляет/добавляет одно исходящее ребро в индекс
    /// (используется в StrengthenLink, чтобы не перестраивать весь индекс).
    /// </summary>
    private void UpsertOutLink(int motorId, int stimulusImageId, float strength)
    {
      if (!_outLinks.TryGetValue(motorId, out var list))
      {
        list = new List<(int Stimulus, float Strength)>();
        _outLinks[motorId] = list;
      }

      for (int i = 0; i < list.Count; i++)
      {
        if (list[i].Stimulus == stimulusImageId)
        {
          list[i] = (stimulusImageId, strength);
          return;
        }
      }
      list.Add((stimulusImageId, strength));
    }

    #endregion

    #region Публичный API

    /// <summary>
    /// Усиливает направленную связь motorId → stimulusImageId по модели Рескорла–Вагнера:
    /// C ← C + α·(β − C).
    /// </summary>
    /// <param name="motorId">ID моторного действия.</param>
    /// <param name="stimulusImageId">ID образа-стимула.</param>
    public void StrengthenLink(int motorId, int stimulusImageId)
    {
      if (motorId <= 0 || stimulusImageId <= 0)
        return;

      int currentPulse = GetAgentLifetime();
      var key = (motorId, stimulusImageId);

      _lock.EnterWriteLock();
      try
      {
        if (!_links.TryGetValue(key, out var link))
        {
          link = new MotorStimulusAssociation
          {
            MotorId = motorId,
            StimulusImageId = stimulusImageId,
            Strength = 0f,
            BirthTimePulse = currentPulse
          };
          _links[key] = link;
        }

        float alpha = Settings.MotorAssociationLearningRate;
        float beta = Settings.MotorAssociationMaxStrength;
        link.Strength = link.Strength + alpha * (beta - link.Strength);
        link.Strength = Math.Min(link.Strength, beta);

        if (link.Strength > link.MaxAchievedStrength)
          link.MaxAchievedStrength = link.Strength;

        link.LastStrengthenPulse = currentPulse;

        // Индекс исходящих рёбер синхронизируется инкрементально (без полной перестройки).
        UpsertOutLink(motorId, stimulusImageId, link.Strength);
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>Получает крепость связи motorId → stimulusImageId.</summary>
    /// <param name="motorId">ID моторного действия.</param>
    /// <param name="stimulusImageId">ID образа-стимула.</param>
    /// <param name="strength">Крепость связи; 0, если связи нет.</param>
    /// <returns>True, если связь существует.</returns>
    public bool TryGetStrength(int motorId, int stimulusImageId, out float strength)
    {
      strength = 0f;
      if (motorId <= 0 || stimulusImageId <= 0)
        return false;

      _lock.EnterReadLock();
      try
      {
        if (_links.TryGetValue((motorId, stimulusImageId), out var link))
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

    /// <summary>Проверяет, достаточна ли крепость связи для активации звена (C ≥ γ).</summary>
    /// <param name="motorId">ID моторного действия.</param>
    /// <param name="stimulusImageId">ID образа-стимула.</param>
    /// <returns>True, если крепость связи не ниже порога активации.</returns>
    public bool IsLinkActivatable(int motorId, int stimulusImageId)
    {
      return TryGetStrength(motorId, stimulusImageId, out float strength) &&
             strength >= Settings.MotorAssociationActivationThreshold;
    }

    /// <summary>
    /// Возвращает образы-стимулы, чья связь с данным мотором достигла порога γ.
    /// </summary>
    /// <param name="motorId">ID моторного действия.</param>
    /// <returns>Список ID образов-стимулов (может быть пустым).</returns>
    public List<int> GetActivatableStimuli(int motorId)
    {
      var result = new List<int>();
      if (motorId <= 0)
        return result;

      float threshold = Settings.MotorAssociationActivationThreshold;

      _lock.EnterReadLock();
      try
      {
        if (_outLinks.TryGetValue(motorId, out var list))
        {
          foreach (var edge in list)
          {
            if (edge.Strength >= threshold)
              result.Add(edge.Stimulus);
          }
        }
      }
      finally
      {
        _lock.ExitReadLock();
      }

      return result;
    }

    /// <summary>Применяет затухание ко всем связям и удаляет ослабленные ниже C_min.</summary>
    public void ApplyDecay()
    {
      int currentPulse = GetAgentLifetime();
      // Период затухания — из настроек; срабатывание на кратных пульсах (как у сенсорных связей).
      int period = Settings.MotorAssociationDecayPeriodPulses > 0
          ? Settings.MotorAssociationDecayPeriodPulses
          : 1000;
      if (period <= 0 || currentPulse % period != 0)
        return;

      _lock.EnterWriteLock();
      try
      {
        var keysToRemove = new List<(int Motor, int Stimulus)>();
        float decayRate = Settings.MotorAssociationDecayRate;

        foreach (var kv in _links)
        {
          var link = kv.Value;
          link.Strength *= decayRate;

          if (link.Strength < Settings.MotorAssociationMinStrength)
            keysToRemove.Add(kv.Key);
        }

        foreach (var key in keysToRemove)
          _links.Remove(key);

        // Индекс перестраивается полностью после удаления слабых звеньев.
        RebuildOutLinksIndex();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    /// <summary>
    /// Полностью очищает все связи и сохраняет пустое состояние в файл.
    /// Используется при переходе на стадию 0 эволюции.
    /// </summary>
    public void ClearAll()
    {
      _lock.EnterWriteLock();
      try
      {
        _links.Clear();
        _outLinks.Clear();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
      Save();
    }

    /// <summary>Сохраняет связи в файл.</summary>
    /// <returns>Признак успеха и текст ошибки.</returns>
    public (bool Success, string ErrorMessage) Save()
    {
      _lock.EnterReadLock();
      try
      {
        var lines = new List<string>
        {
          FileHeaders.MotorStimulusAssociationsFormat,
          FileHeaders.MotorStimulusAssociationsFields
        };

        var inv = CultureInfo.InvariantCulture;
        foreach (var link in _links.Values
            .Where(l => l.Strength >= Settings.MotorAssociationMinStrength)
            .OrderBy(l => l.MotorId)
            .ThenBy(l => l.StimulusImageId))
        {
          lines.Add($"{link.MotorId}|{link.StimulusImageId}|{link.Strength.ToString(inv)}|" +
                    $"{link.LastStrengthenPulse}|{link.BirthTimePulse}|{link.MaxAchievedStrength.ToString(inv)}");
        }

        return FileValidator.SafeSaveFile(
            GetFilePath(),
            lines,
            content => true,
            minLinesCount: 2,
            fileDescription: "мотор→стимул ассоциаций");
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

    /// <summary>Загружает связи из файла.</summary>
    public void Load()
    {
      string filePath = GetFilePath();
      if (!File.Exists(filePath))
        return;

      _lock.EnterWriteLock();
      try
      {
        _links.Clear();

        // Файл — машинный формат: десятичный разделитель обязан быть точкой независимо от культуры ОС.
        var inv = CultureInfo.InvariantCulture;
        foreach (var line in File.ReadLines(filePath))
        {
          if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
            continue;

          var parts = line.Split('|');
          if (parts.Length < 6)
            continue;

          if (!int.TryParse(parts[0], NumberStyles.Integer, inv, out int motorId) ||
              !int.TryParse(parts[1], NumberStyles.Integer, inv, out int stimulusId))
            continue;

          if (motorId <= 0 || stimulusId <= 0)
            continue;

          if (!float.TryParse(parts[2], NumberStyles.Float, inv, out float strength))
            continue;

          var link = new MotorStimulusAssociation
          {
            MotorId = motorId,
            StimulusImageId = stimulusId,
            Strength = strength,
            LastStrengthenPulse = int.TryParse(parts[3], NumberStyles.Integer, inv, out int last) ? last : 0,
            BirthTimePulse = int.TryParse(parts[4], NumberStyles.Integer, inv, out int birth) ? birth : 0,
            MaxAchievedStrength = float.TryParse(parts[5], NumberStyles.Float, inv, out float max) ? max : strength
          };

          if (link.Strength >= Settings.MotorAssociationMinStrength)
            _links[(motorId, stimulusId)] = link;
        }

        // Индекс исходящих рёбер строится один раз после загрузки всего графа.
        RebuildOutLinksIndex();
      }
      finally
      {
        _lock.ExitWriteLock();
      }
    }

    #endregion

    #region Внутренние методы

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
      return Path.Combine(directory, $"{MotorStimulusAssociationsFileName}.dat");
    }

    #endregion

    #region IDisposable

    /// <summary>Сохраняет связи и освобождает ресурсы системы.</summary>
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
