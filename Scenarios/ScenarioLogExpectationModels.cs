using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ISIDA.Scenarios
{
  /// <summary>Галки «не проверять столбец» для ожидаемых логов (как в редакторе сценария).</summary>
  public sealed class ScenarioLogExpectationColumnSkips : INotifyPropertyChanged
  {
    private bool _skipState;
    private bool _skipStyle;
    private bool _skipTheme;
    private bool _skipTrigger;
    private bool _skipOrUm;
    private bool _skipDanger;
    private bool _skipVeryActual;
    private bool _skipGeneticReflex;
    private bool _skipConditionReflex;
    private bool _skipAutomatizm;
    private bool _skipReflexChain;
    private bool _skipAutomatizmChain;
    private bool _skipMainCycle;

    /// <summary>Не сравнивать столбец «Состояние» (ID базового состояния гомеостаза).</summary>
    public bool SkipState
    {
      get => _skipState;
      set { if (_skipState == value) return; _skipState = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Стиль» (ID образа стилей поведения).</summary>
    public bool SkipStyle
    {
      get => _skipStyle;
      set { if (_skipStyle == value) return; _skipStyle = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Тема» (тип темы мышления).</summary>
    public bool SkipTheme
    {
      get => _skipTheme;
      set { if (_skipTheme == value) return; _skipTheme = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Триггер» (ID пускового стимула).</summary>
    public bool SkipTrigger
    {
      get => _skipTrigger;
      set { if (_skipTrigger == value) return; _skipTrigger = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «ОР/УМ» (ориентировочный рефлекс или уровни мышления).</summary>
    public bool SkipOrUm
    {
      get => _skipOrUm;
      set { if (_skipOrUm == value) return; _skipOrUm = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Опасно» (признак опасной ситуации в информационной среде).</summary>
    public bool SkipDanger
    {
      get => _skipDanger;
      set { if (_skipDanger == value) return; _skipDanger = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Актуально» (признак актуальной ситуации в информационной среде).</summary>
    public bool SkipVeryActual
    {
      get => _skipVeryActual;
      set { if (_skipVeryActual == value) return; _skipVeryActual = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Б/у рефлекс» (безусловный рефлекс).</summary>
    public bool SkipGeneticReflex
    {
      get => _skipGeneticReflex;
      set { if (_skipGeneticReflex == value) return; _skipGeneticReflex = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Усл. рефлекс» (условный рефлекс).</summary>
    public bool SkipConditionReflex
    {
      get => _skipConditionReflex;
      set { if (_skipConditionReflex == value) return; _skipConditionReflex = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Автоматизм».</summary>
    public bool SkipAutomatizm
    {
      get => _skipAutomatizm;
      set { if (_skipAutomatizm == value) return; _skipAutomatizm = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Цепочка РФ» (цепочка рефлекса).</summary>
    public bool SkipReflexChain
    {
      get => _skipReflexChain;
      set { if (_skipReflexChain == value) return; _skipReflexChain = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Цепочка АВ» (цепочка автоматизма).</summary>
    public bool SkipAutomatizmChain
    {
      get => _skipAutomatizmChain;
      set { if (_skipAutomatizmChain == value) return; _skipAutomatizmChain = value; OnPropertyChanged(); }
    }

    /// <summary>Не сравнивать столбец «Цикл М» (главный цикл мышления).</summary>
    public bool SkipMainCycle
    {
      get => _skipMainCycle;
      set { if (_skipMainCycle == value) return; _skipMainCycle = value; OnPropertyChanged(); }
    }

    /// <summary>Создаёт независимую копию флагов столбцов.</summary>
    /// <returns>Новый экземпляр с теми же значениями галок.</returns>
    public ScenarioLogExpectationColumnSkips Clone()
    {
      return new ScenarioLogExpectationColumnSkips
      {
        SkipState = SkipState,
        SkipStyle = SkipStyle,
        SkipTheme = SkipTheme,
        SkipTrigger = SkipTrigger,
        SkipOrUm = SkipOrUm,
        SkipDanger = SkipDanger,
        SkipVeryActual = SkipVeryActual,
        SkipGeneticReflex = SkipGeneticReflex,
        SkipConditionReflex = SkipConditionReflex,
        SkipAutomatizm = SkipAutomatizm,
        SkipReflexChain = SkipReflexChain,
        SkipAutomatizmChain = SkipAutomatizmChain,
        SkipMainCycle = SkipMainCycle
      };
    }

    /// <summary>Событие изменения значения галки столбца.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }

  /// <summary>Одна строка ожидаемого «снимка» лога (колонки как в LiveLogs, без времени).</summary>
  public sealed class ScenarioLogExpectationRow : INotifyPropertyChanged
  {
    private int _stepIndex;
    private int _pulseWithinScenario;
    private string _stateText = "-";
    private string _styleText = "-";
    private string _themeText = "-";
    private string _triggerText = "-";
    private string _orUmText = "-";
    private string _dangerText = "-";
    private string _veryActualText = "-";
    private string _geneticReflexText = "-";
    private string _conditionReflexText = "-";
    private string _automatizmText = "-";
    private string _reflexChainText = "-";
    private string _automatizmChainText = "-";
    private string _mainCycleText = "-";

    /// <summary>Порядковый номер шага сценария (синхронизируется со строкой шагов).</summary>
    public int StepIndex
    {
      get => _stepIndex;
      set { if (_stepIndex == value) return; _stepIndex = value; OnPropertyChanged(); }
    }

    /// <summary>Номер пульса внутри прогона сценария (синхронизируется со строкой шагов).</summary>
    public int PulseWithinScenario
    {
      get => _pulseWithinScenario;
      set { if (_pulseWithinScenario == value) return; _pulseWithinScenario = value; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение «Состояние». «-» — в логе ожидается прочерк; пусто — не сравнивать.</summary>
    public string StateText
    {
      get => _stateText;
      set { if (_stateText == value) return; _stateText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Стиль».</summary>
    public string StyleText
    {
      get => _styleText;
      set { if (_styleText == value) return; _styleText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Тема».</summary>
    public string ThemeText
    {
      get => _themeText;
      set { if (_themeText == value) return; _themeText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Триггер».</summary>
    public string TriggerText
    {
      get => _triggerText;
      set { if (_triggerText == value) return; _triggerText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «ОР/УМ».</summary>
    public string OrUmText
    {
      get => _orUmText;
      set { if (_orUmText == value) return; _orUmText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Опасно» (0 или 1).</summary>
    public string DangerText
    {
      get => _dangerText;
      set { if (_dangerText == value) return; _dangerText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Актуально» (0 или 1, в отчёте как у «Опасно»).</summary>
    public string VeryActualText
    {
      get => _veryActualText;
      set { if (_veryActualText == value) return; _veryActualText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Б/у рефлекс».</summary>
    public string GeneticReflexText
    {
      get => _geneticReflexText;
      set { if (_geneticReflexText == value) return; _geneticReflexText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Усл. рефлекс». Несколько допустимых значений: «1|2» (в .dat внутри поля «|» как \|).</summary>
    public string ConditionReflexText
    {
      get => _conditionReflexText;
      set { if (_conditionReflexText == value) return; _conditionReflexText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Автоматизм».</summary>
    public string AutomatizmText
    {
      get => _automatizmText;
      set { if (_automatizmText == value) return; _automatizmText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Цепочка РФ».</summary>
    public string ReflexChainText
    {
      get => _reflexChainText;
      set { if (_reflexChainText == value) return; _reflexChainText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Цепочка АВ».</summary>
    public string AutomatizmChainText
    {
      get => _automatizmChainText;
      set { if (_automatizmChainText == value) return; _automatizmChainText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Ожидаемое значение столбца «Цикл М».</summary>
    public string MainCycleText
    {
      get => _mainCycleText;
      set { if (_mainCycleText == value) return; _mainCycleText = value ?? ""; OnPropertyChanged(); }
    }

    /// <summary>Глубокая копия строки ожиданий.</summary>
    /// <returns>Новый экземпляр с теми же текстовыми полями и номерами шага/пульса.</returns>
    public ScenarioLogExpectationRow Clone()
    {
      return new ScenarioLogExpectationRow
      {
        StepIndex = StepIndex,
        PulseWithinScenario = PulseWithinScenario,
        StateText = StateText ?? "",
        StyleText = StyleText ?? "",
        ThemeText = ThemeText ?? "",
        TriggerText = TriggerText ?? "",
        OrUmText = OrUmText ?? "",
        DangerText = DangerText ?? "",
        VeryActualText = VeryActualText ?? "",
        GeneticReflexText = GeneticReflexText ?? "",
        ConditionReflexText = ConditionReflexText ?? "",
        AutomatizmText = AutomatizmText ?? "",
        ReflexChainText = ReflexChainText ?? "",
        AutomatizmChainText = AutomatizmChainText ?? "",
        MainCycleText = MainCycleText ?? ""
      };
    }

    /// <summary>Событие изменения полей строки ожидания.</summary>
    public event PropertyChangedEventHandler PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
  }
}
