using ISIDA.Actions;
using ISIDA.Reflexes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ISIDA.Scenarios
{
  /// <summary>Как приращивать номер пульса при переходе к следующему шагу (см. <see cref="ScenarioPulseSchedule"/>).</summary>
  public enum ScenarioPulseStepIncrement
  {
    /// <summary>Следующий по порядку: +1 пульс на шаг.</summary>
    Sequential = 1,
    /// <summary>+ время удержания действий (пульсов) + 1.</summary>
    ActionHoldPlusOne = 2,
    /// <summary>+ время удержания состояний (пульсов) + 1.</summary>
    StateHoldPlusOne = 3,
    /// <summary>Только время удержания действий (пульсов), без дополнительного +1 к следующему шагу.</summary>
    ActionHold = 4
  }

  /// <summary>Тип шага сценария: воздействие с пульта или ожидание клика по плашке.</summary>
  public enum ScenarioLineKind
  {
    /// <summary>Воздействие оператора через пульт (действия, фраза, тон, настроение).</summary>
    Pult = 0,
    /// <summary>Только клик по плашке ожидания оценки (без стимулов).</summary>
    WaitClick = 1
  }

  /// <summary>Строка сценария: порядковый шаг и расчётный номер пульса внутри прогона.</summary>
  public sealed class ScenarioLineRow : INotifyPropertyChanged
  {
    private string _actionNamesDisplay = "";
    private int _stepIndex;
    private int _pulseWithinScenario = 1;

    /// <summary>Порядковый шаг сценария (1, 2, 3 …).</summary>
    public int StepIndex
    {
      get => _stepIndex;
      set
      {
        if (_stepIndex == value) return;
        _stepIndex = value;
        NotifyPropertyChanged();
      }
    }

    /// <summary>Номер пульса относительно старта прогона (рассчитывается по задержке между шагами).</summary>
    public int PulseWithinScenario
    {
      get => _pulseWithinScenario;
      set
      {
        if (_pulseWithinScenario == value) return;
        _pulseWithinScenario = value;
        NotifyPropertyChanged();
      }
    }

    /// <summary>Пульт или ожидание клика.</summary>
    public ScenarioLineKind Kind { get; set; }
    /// <summary>Идентификатор тона речи.</summary>
    public int ToneId { get; set; }
    /// <summary>Идентификатор настроения.</summary>
    public int MoodId { get; set; }
    /// <summary>Код зрительного канала (фон сцены), см. <see cref="AgentVisualColor"/>.</summary>
    public int VisualColorId { get; set; }
    /// <summary>Идентификаторы воздействий с пульта.</summary>
    public List<int> ActionIds { get; set; } = new List<int>();
    /// <summary>Текст фразы для подачи агенту.</summary>
    public string Phrase { get; set; } = "";

    /// <summary>Названия выбранных воздействий через запятую (только для отображения).</summary>
    public string ActionNamesDisplay
    {
      get => _actionNamesDisplay;
      private set
      {
        if (_actionNamesDisplay == value) return;
        _actionNamesDisplay = value;
        NotifyPropertyChanged(nameof(ActionNamesDisplay));
      }
    }

    private void NotifyPropertyChanged([CallerMemberName] string name = null)
    {
      PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Обновляет подпись воздействий по справочнику.</summary>
    public void RefreshActionNames(InfluenceActionSystem influenceActions)
    {
      if (ActionIds == null || ActionIds.Count == 0)
      {
        ActionNamesDisplay = "";
        return;
      }
      if (influenceActions == null)
      {
        ActionNamesDisplay = string.Join(", ",
            ActionIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
        return;
      }
      try
      {
        var lookup = new Dictionary<int, string>();
        foreach (var a in influenceActions.GetAllInfluenceActions())
        {
          if (!lookup.ContainsKey(a.Id))
            lookup[a.Id] = a.Name ?? "";
        }
        var parts = new List<string>();
        foreach (var id in ActionIds)
        {
          if (lookup.TryGetValue(id, out var name) && !string.IsNullOrEmpty(name))
            parts.Add(name);
          else
            parts.Add(id.ToString(CultureInfo.InvariantCulture));
        }
        ActionNamesDisplay = string.Join(", ", parts);
      }
      catch
      {
        ActionNamesDisplay = string.Join(", ",
            ActionIds.Select(id => id.ToString(CultureInfo.InvariantCulture)));
      }
    }

    /// <summary>Событие изменения свойств (INotifyPropertyChanged).</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Идентификаторы действий в виде строки «1,2,3» для ввода/отображения.</summary>
    public string ActionIdsText
    {
      get => ActionIds == null || ActionIds.Count == 0
          ? ""
          : string.Join(",", ActionIds.Select(i => i.ToString(CultureInfo.InvariantCulture)));
      set
      {
        ActionIds = new List<int>();
        if (string.IsNullOrWhiteSpace(value))
          return;
        foreach (var part in value.Split(','))
        {
          var s = part.Trim();
          if (s.Length == 0)
            continue;
          if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
            ActionIds.Add(id);
        }
      }
    }

    /// <summary>Сбросить таймер ожидания оценки оператора (если доступно по стадии).</summary>
    public bool ResetWaitingPeriod { get; set; }

    /// <summary>P — пульт, W — только клик по плашке ожидания.</summary>
    public string KindCode
    {
      get => Kind == ScenarioLineKind.WaitClick ? "W" : "P";
      set
      {
        var v = (value ?? "P").Trim();
        Kind = v.Equals("W", StringComparison.OrdinalIgnoreCase)
            ? ScenarioLineKind.WaitClick
            : ScenarioLineKind.Pult;
      }
    }

    /// <summary>Глубокая копия строки (списки действий копируются).</summary>
    public ScenarioLineRow Clone()
    {
      return new ScenarioLineRow
      {
        StepIndex = StepIndex,
        PulseWithinScenario = PulseWithinScenario,
        Kind = Kind,
        ToneId = ToneId,
        MoodId = MoodId,
        VisualColorId = VisualColorId,
        ActionIds = ActionIds?.ToList() ?? new List<int>(),
        Phrase = Phrase ?? "",
        ResetWaitingPeriod = ResetWaitingPeriod
      };
    }
  }

  /// <summary>Запись реестра сценариев.</summary>
  public sealed class ScenarioHeader
  {
    /// <summary>Уникальный идентификатор сценария в реестре.</summary>
    public int Id { get; set; }
    /// <summary>Краткое название.</summary>
    public string Title { get; set; } = "";
    /// <summary>Описание.</summary>
    public string Description { get; set; } = "";

    private const int DescriptionDisplayLimit = 100;

    /// <summary>Первые N символов описания (без переносов строк) для отображения в списках.</summary>
    public string DescriptionShort
    {
      get
      {
        if (string.IsNullOrEmpty(Description))
          return "";
        var flat = Description.Replace("\r\n", " ").Replace("\n", " ").Replace("\r", " ");
        return flat.Length <= DescriptionDisplayLimit
            ? flat
            : flat.Substring(0, DescriptionDisplayLimit) + "…";
      }
    }

    /// <summary>Дата или подпись версии (произвольная строка).</summary>
    public string DateText { get; set; } = "";
    /// <summary>Начальные значения параметров гомеостаза: «id=value» через «;» (см. <see cref="ScenarioHomeostasisValuesFormat"/>).</summary>
    public string InitialHomeostasisValues { get; set; } = "";

    /// <summary>Стадия для перехода перед запуском (0–5); −1 — не менять стадию.</summary>
    public int PreRunTargetStage { get; set; } = -1;

    /// <summary>Очищать данные при переходе на стадию (как при смене стадии в свойствах агента).</summary>
    public bool PreRunClearAgentData { get; set; }

    /// <summary>Перед запуском: выставить «норму» по ориентации Speed — дефицит (Speed &lt; 0) в 100, избыток (Speed &gt; 0) в 0; затем хост выдерживает паузу на стабилизацию.</summary>
    public bool PreRunNormalHomeostasisState { get; set; }

    /// <summary>При прогоне сценария: режим наблюдения (воздействия не меняют гомеостаз), как флажок на пульте.</summary>
    public bool ScenarioObservationMode { get; set; }

    /// <summary>При прогоне сценария: авторитарная запись вербального стимула, как флажок на пульте.</summary>
    public bool ScenarioAuthoritativeRecording { get; set; }

    /// <summary>
    /// Приращение пульса между шагами: 1 — по порядку (+1); 2 — + время удержания действий + 1; 3 — + время удержания состояний + 1.
    /// Значения глобальных времён берутся из настроек проекта при расчёте.
    /// </summary>
    public int PulseStepIncrement { get; set; } = (int)ScenarioPulseStepIncrement.ActionHoldPlusOne;

    /// <summary>Ускорение пульса по календарю при прогоне (1, 10, 50, 100). 1 — обычная скорость.</summary>
    public int RunPulseTimingCoefficient { get; set; } = 1;

    /// <summary>Копия записи реестра.</summary>
    public ScenarioHeader Clone()
    {
      return new ScenarioHeader
      {
        Id = Id,
        Title = Title,
        Description = Description,
        DateText = DateText,
        InitialHomeostasisValues = InitialHomeostasisValues ?? "",
        PreRunTargetStage = PreRunTargetStage,
        PreRunClearAgentData = PreRunClearAgentData,
        PreRunNormalHomeostasisState = PreRunNormalHomeostasisState,
        ScenarioObservationMode = ScenarioObservationMode,
        ScenarioAuthoritativeRecording = ScenarioAuthoritativeRecording,
        PulseStepIncrement = PulseStepIncrement,
        RunPulseTimingCoefficient = RunPulseTimingCoefficient
      };
    }
  }

  /// <summary>Полный сценарий: шапка и упорядоченные строки шагов.</summary>
  public sealed class ScenarioDocument
  {
    /// <summary>Версия заголовка реестра сценариев.</summary>
    public const int FormatVersion = 1;

    /// <summary>Версия файла строк сценария (шаг + пульс + …; v4 — ожидаемые логи; v5 — META без полей группы; v6 — код зрительного канала в строке шага).</summary>
    public const int LinesFileFormatVersion = 6;

    /// <summary>Метаданные сценария (id, название, дата).</summary>
    public ScenarioHeader Header { get; set; } = new ScenarioHeader();
    /// <summary>Строки шагов в порядке выполнения.</summary>
    public List<ScenarioLineRow> Lines { get; set; } = new List<ScenarioLineRow>();

    /// <summary>Галки «не проверять столбец» для таблицы ожидаемых логов.</summary>
    public ScenarioLogExpectationColumnSkips LogExpectationColumnSkips { get; set; } =
        new ScenarioLogExpectationColumnSkips();

    /// <summary>Ожидаемые значения колонок лога по шагам (по одной строке на шаг сценария).</summary>
    public List<ScenarioLogExpectationRow> LogExpectations { get; set; } = new List<ScenarioLogExpectationRow>();

    /// <summary>Копия документа со всеми строками.</summary>
    public ScenarioDocument Clone()
    {
      return new ScenarioDocument
      {
        Header = Header?.Clone() ?? new ScenarioHeader(),
        Lines = Lines?.Select(l => l.Clone()).ToList() ?? new List<ScenarioLineRow>(),
        LogExpectationColumnSkips = LogExpectationColumnSkips?.Clone() ?? new ScenarioLogExpectationColumnSkips(),
        LogExpectations = LogExpectations?.Select(e => e.Clone()).ToList() ?? new List<ScenarioLogExpectationRow>()
      };
    }
  }

  /// <summary>Сериализация начальных значений параметров гомеостаза в строку сценария.</summary>
  public static class ScenarioHomeostasisValuesFormat
  {
    /// <summary>Сохраняет словарь в строку «id=value» через «;» (инвариантная культура).</summary>
    public static string Serialize(IReadOnlyDictionary<int, float> values)
    {
      if (values == null || values.Count == 0)
        return "";
      return string.Join(";", values.OrderBy(kv => kv.Key).Select(kv =>
          $"{kv.Key.ToString(CultureInfo.InvariantCulture)}={kv.Value.ToString("G", CultureInfo.InvariantCulture)}"));
    }

    /// <summary>Разбирает строку в словарь id → значение.</summary>
    public static Dictionary<int, float> Parse(string s)
    {
      var d = new Dictionary<int, float>();
      if (string.IsNullOrWhiteSpace(s))
        return d;
      foreach (var part in s.Split(';'))
      {
        var t = part.Trim();
        if (t.Length == 0)
          continue;
        int eq = t.IndexOf('=');
        if (eq <= 0)
          continue;
        if (!int.TryParse(t.Substring(0, eq).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
          continue;
        if (!float.TryParse(t.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
          continue;
        d[id] = v;
      }
      return d;
    }
  }
}
