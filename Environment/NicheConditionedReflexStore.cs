using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace ISIDA.Niche
{
  /// <summary>
  /// Условные рефлексы Niche (стадия 1): загрузка из Data/Niche/Reflexes, упрощённая активация по действию Creature.
  /// </summary>
  public sealed class NicheConditionedReflexStore
  {
    private readonly string _reflexesFolder;
    private readonly GeneticReflexesSystem _geneticReflexes;
    private readonly List<NicheConditionedReflexEntry> _entries = new List<NicheConditionedReflexEntry>();
    private float _activationThreshold = 0.6f;

    /// <summary>
    /// Создаёт хранилище и загружает ConditionedReflexes.dat.
    /// </summary>
    public NicheConditionedReflexStore(string reflexesFolder, GeneticReflexesSystem geneticReflexes)
    {
      _reflexesFolder = reflexesFolder ?? string.Empty;
      _geneticReflexes = geneticReflexes ?? throw new ArgumentNullException(nameof(geneticReflexes));
      Reload();
    }

    /// <summary>Загруженные записи (только чтение).</summary>
    public IReadOnlyList<NicheConditionedReflexEntry> Entries => _entries;

    /// <summary>Перечитывает файлы с диска.</summary>
    public void Reload()
    {
      _entries.Clear();
      LoadSettings();
      string path = Path.Combine(_reflexesFolder, "ConditionedReflexes.dat");
      if (!File.Exists(path))
        return;

      foreach (var line in File.ReadAllLines(path))
      {
        string t = line.Trim();
        if (t.Length == 0 || t.StartsWith("#"))
          continue;

        string[] parts = t.Split('|');
        if (parts.Length < 8)
          continue;

        if (!int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
          continue;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level1))
          continue;
        if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out int level3))
          continue;
        if (!float.TryParse(parts[4], NumberStyles.Float, CultureInfo.InvariantCulture, out float strength))
          strength = 0f;

        int sourceGeneticId = parts.Length > 7 && int.TryParse(parts[7].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sg)
            ? sg
            : 0;

        var level2 = ISIDA.Common.AddUtils.ParseIntList(parts[2]);
        _entries.Add(new NicheConditionedReflexEntry
        {
          Id = id,
          Level1 = level1,
          Level2 = level2,
          CreatureActionId = level3,
          SourceGeneticReflexId = sourceGeneticId,
          AssociationStrength = strength
        });
      }
    }

    /// <summary>
    /// Активирует УР Niche по действию Creature.
    /// </summary>
  public int ApplyAfterCreatureAction(
        Gomeostas.GomeostasSystem nicheGomeostas,
        int creatureActionId,
        Func<int, bool> applyGeneticReflexById)
    {
      if (creatureActionId <= 0 || _entries.Count == 0 || nicheGomeostas == null)
        return 0;

      var slice = nicheGomeostas.DetachedGetHomeostasisSlice();
      int applied = 0;

      foreach (var entry in _entries)
      {
        if (entry.AssociationStrength < _activationThreshold)
          continue;
        if (entry.Level1 != slice.BaseStateId)
          continue;
        if (entry.CreatureActionId != creatureActionId)
          continue;
        if (entry.Level2 != null && entry.Level2.Count > 0)
        {
          if (!entry.Level2.All(id => slice.ActiveStyleIds.Contains(id)) ||
              !slice.ActiveStyleIds.All(id => entry.Level2.Contains(id)))
            continue;
        }

        if (applyGeneticReflexById != null && applyGeneticReflexById(entry.SourceGeneticReflexId))
          applied++;
      }

      return applied;
    }

    private void LoadSettings()
    {
      string path = Path.Combine(_reflexesFolder, "ConditionedReflexSettings.dat");
      if (!File.Exists(path))
        return;

      foreach (var line in File.ReadAllLines(path))
      {
        string t = line.Trim();
        if (t.Length == 0 || t.StartsWith("#"))
          continue;
        string[] parts = t.Split('|');
        if (parts.Length < 2)
          continue;
        if (parts[0].Trim().Equals("ActivationThreshold", StringComparison.OrdinalIgnoreCase) &&
            float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float th))
          _activationThreshold = th;
      }
    }
  }

  /// <summary>Запись условного рефлекса Niche (упрощённая модель).</summary>
  public sealed class NicheConditionedReflexEntry
  {
    /// <summary>ID условного рефлекса.</summary>
    public int Id { get; set; }

    /// <summary>Интегральное базовое состояние гомеостаза Niche (Level1).</summary>
    public int Level1 { get; set; }

    /// <summary>Активные стили поведения (Level2).</summary>
    public List<int> Level2 { get; set; } = new List<int>();

    /// <summary>ID действия Creature как пусковой стимул (Level3 в файле).</summary>
    public int CreatureActionId { get; set; }

    /// <summary>ID исходного безусловного рефлекса Niche.</summary>
    public int SourceGeneticReflexId { get; set; }

    /// <summary>Крепость ассоциативной связи [0, 1].</summary>
    public float AssociationStrength { get; set; }
  }
}
