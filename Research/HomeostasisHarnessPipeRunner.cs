using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using ISIDA.Common;
using ISIDA.Gomeostas;
using Newtonsoft.Json;
using static ISIDA.Gomeostas.GomeostasSystem;

namespace ISIDA.Research
{
  /// <summary>Результат разбора многострочного ввода (без запуска калькулятора).</summary>
  public sealed class ResearchHarnessPipeParseOutcome
  {
    /// <summary>true, если нет блокирующих ошибок и есть хотя бы одна строка данных.</summary>
    public bool Success { get; set; }
    /// <summary>Сообщения, из-за которых разбор нельзя продолжить.</summary>
    public List<string> BlockingErrors { get; } = new List<string>();
    /// <summary>Неблокирующие замечания (округление и т.п.).</summary>
    public List<string> Warnings { get; } = new List<string>();
    /// <summary>Успешно разобранные строки сценария.</summary>
    public List<ResearchHarnessPipePreparedRow> Rows { get; } = new List<ResearchHarnessPipePreparedRow>();
  }

  /// <summary>Одно сравниваемое значение выхода (ожидание из строки vs факт калькулятора).</summary>
  public sealed class ResearchHarnessPipeOutputSlot
  {
    /// <summary>Создаёт слот сравнения для одной выходной колонки.</summary>
    /// <param name="label">Подпись (как в <see cref="ResearchHarnessResultColumn.Label"/>).</param>
    /// <param name="kind">Тип значения для разбора и сравнения.</param>
    /// <param name="expectedText">Текст ожидания из строки сценария.</param>
    public ResearchHarnessPipeOutputSlot(string label, HarnessValueKind kind, string expectedText)
    {
      Label = label ?? "";
      Kind = kind;
      ExpectedText = expectedText ?? "";
    }

    /// <summary>Подпись выходной колонки.</summary>
    public string Label { get; }
    /// <summary>Тип ожидаемого значения.</summary>
    public HarnessValueKind Kind { get; }
    /// <summary>Ожидаемое значение в текстовом виде из сценария.</summary>
    public string ExpectedText { get; }
    /// <summary>Фактическое значение после вызова калькулятора (текст для отчёта).</summary>
    public string ActualText { get; set; } = "";
    /// <summary>true, если ожидание и факт совпали по правилам типа.</summary>
    public bool Match { get; set; }
  }

  /// <summary>Одна строка прогона после разбора и приведения типов.</summary>
  public sealed class ResearchHarnessPipePreparedRow
  {
    /// <summary>Номер строки в исходном файле/тексте (1-based).</summary>
    public int SourceLineNumber;
    /// <summary>Все ячейки строки после разбиения по |.</summary>
    public string[] RawCells;
    /// <summary>Внутренний идентификатор кейса для отчёта.</summary>
    public string CaseId;
    /// <summary>true, если все <see cref="OutputSlots"/> совпали.</summary>
    public bool Match;
    /// <summary>Слоты выходов с ожиданием, фактом и флагом совпадения.</summary>
    public List<ResearchHarnessPipeOutputSlot> OutputSlots { get; } = new List<ResearchHarnessPipeOutputSlot>();

    internal int CriticalParamId;
    internal float CriticalCur;
    internal float CriticalPrev;
    internal float HarmfulValue;
    internal int ParamWeight;
    internal int ParamNorma;
    internal int ParamSpeed;
    internal bool ParamVital;
    internal float ParamCritMin;
    internal float ParamCritMax;

    internal int ExternalImpactValue;

    internal int OpFocusId;
    internal AppGlobalState.HomeostasisState OpOverallBefore;
    internal AppGlobalState.HomeostasisState OpOverallAfter;
    internal int OpP1Id;
    internal float OpP1Before;
    internal float OpP1Cur;
    internal int OpP1Weight;
    internal int OpP1Norma;
    internal int OpP1Speed;
    internal bool OpP1Vital;
    internal float OpP1Cmin;
    internal float OpP1Cmax;
    internal int OpP2Id;
    internal float OpP2Before;
    internal float OpP2Cur;
    internal int OpP2Weight;
    internal int OpP2Norma;
    internal int OpP2Speed;
    internal bool OpP2Vital;
    internal float OpP2Cmin;
    internal float OpP2Cmax;

    internal int DomDynamicTime;
    internal float DomDifSensor;
    internal string DomBaseStyleIdsRaw = "";
    internal string DomStyleActivationsRaw = "";

    internal void RecomputeMatch()
    {
      Match = OutputSlots.Count > 0 && OutputSlots.All(s => s.Match);
    }
  }

  /// <summary>Итог записи артефактов прогона.</summary>
  public sealed class ResearchHarnessPipeRunOutcome
  {
    /// <summary>true, если прогон завершён без фатальной ошибки.</summary>
    public bool Success;
    /// <summary>Текст ошибки при <see cref="Success"/> == false.</summary>
    public string ErrorMessage;
    /// <summary>Каталог прогона с артефактами.</summary>
    public string OutputDirectory;
    /// <summary>Число обработанных строк сценария.</summary>
    public int RowCount;
    /// <summary>Число строк, где хотя бы один выход не совпал с ожиданием.</summary>
    public int MismatchCount;
    /// <summary>Длительность прогона, мс.</summary>
    public long ElapsedMs;
    /// <summary>Полный путь к сгенерированному report.html.</summary>
    public string ReportHtmlPath;
  }

  /// <summary>Краткий манифест pipe-прогона (manifest.json).</summary>
  public sealed class PipeHarnessManifest
  {
    /// <summary>Версия схемы манифеста.</summary>
    public string schema_version = "pipe-2";
    /// <summary>Идентификатор прогона.</summary>
    public string harness_id;
    /// <summary>Число строк в прогоне.</summary>
    public int row_count;
    /// <summary>Число строк с расхождением (NO).</summary>
    public int mismatch_count;
    /// <summary>Длительность прогона, мс.</summary>
    public long elapsed_ms;
    /// <summary>Полный путь к исходному файлу со строками pipe.</summary>
    public string input_pipe_file;
    /// <summary>Полный путь к results.csv.</summary>
    public string results_csv;
    /// <summary>Полный путь к report.html.</summary>
    public string report_html;
  }

  /// <summary>Парсинг строк «P1|P2|…|ожидания выходов», валидация и пакетный вызов калькулятора.</summary>
  public static class ResearchHarnessPipeRunner
  {
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private const float FloatCompareEpsilon = 1e-5f;

    /// <summary>Разбирает текст; при блокирующих ошибках <see cref="ResearchHarnessPipeParseOutcome.Success"/> = false.</summary>
    public static ResearchHarnessPipeParseOutcome Parse(
        ResearchHarnessPipeMethodInfo method,
        string multiLineText)
    {
      var outcome = new ResearchHarnessPipeParseOutcome();
      if (method == null)
      {
        outcome.BlockingErrors.Add("Не выбран метод.");
        return outcome;
      }

      var lines = (multiLineText ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
      int logicalLine = 0;
      for (int i = 0; i < lines.Length; i++)
      {
        var line = lines[i].Trim();
        if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
          continue;
        logicalLine++;
        var cells = SplitPipeRow(line);
        if (cells.Length != method.ColumnCount)
        {
          outcome.BlockingErrors.Add(
              $"Строка {logicalLine} (файл строка {i + 1}): ожидается {method.ColumnCount} колонок через «|», получено {cells.Length}.");
          continue;
        }

        try
        {
          if (method.HarnessId == HomeostasisHarnessIds.HasCriticalParameterChanges)
            ParseCriticalRow(cells, logicalLine, i + 1, method, outcome);
          else if (method.HarnessId == HomeostasisHarnessIds.AnyVitalHarmfulZone)
            ParseHarmfulRow(cells, logicalLine, i + 1, method, outcome);
          else if (method.HarnessId == HomeostasisHarnessIds.ExternalImpactCriticalFlags)
            ParseExternalFlagsRow(cells, logicalLine, i + 1, method, outcome);
          else if (method.HarnessId == HomeostasisHarnessIds.CalculateUrgencyFunction)
            ParseUrgencyRow(cells, logicalLine, i + 1, method, outcome);
          else if (method.HarnessId == HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment)
            ParseOperatorRow(cells, logicalLine, i + 1, method, outcome);
          else if (method.HarnessId == HomeostasisHarnessIds.DominantAndFinalStyles)
            ParseDominantRow(cells, logicalLine, i + 1, method, outcome);
          else
            outcome.BlockingErrors.Add("Неизвестный harness_id у метода.");
        }
        catch (FormatException ex)
        {
          outcome.BlockingErrors.Add($"Строка {logicalLine}: {ex.Message}");
        }
      }

      if (outcome.Rows.Count == 0 && outcome.BlockingErrors.Count == 0)
        outcome.BlockingErrors.Add("Нет ни одной строки данных (пустой ввод или только комментарии).");

      outcome.Success = outcome.BlockingErrors.Count == 0;
      return outcome;
    }

    /// <summary>
    /// Автогенерация строк сценария: значения в колонках «ожидание» заполняются результатом
    /// <see cref="HomeostasisCalculator"/> в момент генерации (регрессионный слепок / golden master).
    /// Такие строки при неизменном движке почти всегда дадут OK — это проверка самосогласованности
    /// и удобный шаблон сценария, а не независимое доказательство корректности формул; для последнего
    /// нужны эталоны из предметной области или ручные ожидания, не совпадающие с текущим выводом калькулятора.
    /// </summary>
    public static string BuildAutoScenarioText(ResearchHarnessPipeMethodInfo method, HomeostasisCalculator calculator)
    {
      if (method == null)
        return "# Автогенерация: метод не выбран.";
      if (calculator == null)
        return "# Автогенерация: калькулятор гомеостаза недоступен.";

      var sb = new StringBuilder();
      if (method.HarnessId == HomeostasisHarnessIds.HasCriticalParameterChanges)
        AppendAutoScenario_HasCritical(sb, calculator);
      else if (method.HarnessId == HomeostasisHarnessIds.AnyVitalHarmfulZone)
        AppendAutoScenario_AnyVitalHarmful(sb, calculator);
      else if (method.HarnessId == HomeostasisHarnessIds.ExternalImpactCriticalFlags)
        AppendAutoScenario_ExternalFlags(sb, calculator);
      else if (method.HarnessId == HomeostasisHarnessIds.CalculateUrgencyFunction)
        AppendAutoScenario_Urgency(sb, calculator);
      else if (method.HarnessId == HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment)
        AppendAutoScenario_Operator(sb, calculator);
      else if (method.HarnessId == HomeostasisHarnessIds.DominantAndFinalStyles)
        AppendAutoScenario_Dominant(sb, calculator);
      else
        sb.AppendLine("# Автогенерация: неизвестный harness_id.");

      return sb.ToString().TrimEnd() + "\n";
    }

    private static void AppendAutoScenario_HasCritical(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# Автоген: HasCriticalParameterChanges — Out1 = факт метода при генерации.");
      int id = 1;
      const int weight = 50;
      const float cmin = 0f;
      const float cmax = 100f;

      foreach (bool vital in new[] { false, true })
      {
        foreach (int speed in new[] { -10, 10 })
        {
          foreach (int norma in new[] { 40, 55 })
          {
            foreach (float prev in new[] { 42f, 58f })
            {
              float step = Math.Max(Math.Abs(speed) / 100f, 1e-4f);
              float curNoise = speed < 0 ? prev + step * 0.4f : prev - step * 0.4f;
              float curWorse = speed < 0 ? prev - (step + 0.25f) : prev + (step + 0.25f);
              float curBetter = speed < 0 ? prev + (step + 0.25f) : prev - (step + 0.25f);
              float curEq = prev;
              foreach (var cur in new[] { curNoise, curWorse, curBetter, curEq })
              {
                float c = Clamp(cur, 0f, 100f);
                float p = Clamp(prev, 0f, 100f);
                AppendCriticalLine(sb, calculator, ref id, c, p, weight, norma, speed, vital, cmin, cmax);
              }
            }
          }
        }
      }

      foreach (bool vital in new[] { false, true })
      {
        foreach (int speed in new[] { -2, -1, 1, 2 })
        {
          float prev = 50f;
          int norma = 50;
          float step = Math.Max(Math.Abs(speed) / 100f, 1e-4f);
          float curNoise = speed < 0 ? prev + step * 0.4f : prev - step * 0.4f;
          float curWorse = speed < 0 ? prev - (step + 0.2f) : prev + (step + 0.2f);
          foreach (var cur in new[] { curNoise, curWorse, prev })
            AppendCriticalLine(sb, calculator, ref id, Clamp(cur, 0f, 100f), prev, weight, norma, speed, vital, cmin, cmax);
        }
      }

      AppendCriticalLine(sb, calculator, ref id, 10f, 60f, 20, 50, -10, true, 5f, 95f);
      AppendCriticalLine(sb, calculator, ref id, 88f, 40f, 80, 50, 12, true, 0f, 100f);
    }

    private static void AppendAutoScenario_AnyVitalHarmful(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# Автоген: AnyVitalParameterInHarmfulZone — Out1 = факт метода при генерации.");
      int id = 1;
      const int weight = 50;
      const float cmin = 0f;
      const float cmax = 100f;

      foreach (bool vital in new[] { false, true })
      {
        foreach (int speed in new[] { -10, 1, 10 })
        {
          foreach (int norma in new[] { 35, 60 })
          {
            foreach (float rel in new[] { -15f, -0.5f, 0f, 0.5f, 15f })
            {
              float v = Clamp(norma + rel, 0f, 100f);
              AppendHarmfulLine(sb, calculator, ref id, v, weight, norma, speed, vital, cmin, cmax);
            }
          }
        }
      }

      AppendHarmfulLine(sb, calculator, ref id, 0.5f, 30, 50, -10, true, 0f, 100f);
      AppendHarmfulLine(sb, calculator, ref id, 99.5f, 30, 50, 10, true, 0f, 100f);
    }

    private static void AppendAutoScenario_ExternalFlags(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# Автоген: HasExternalCriticalImpact + IsExternalImpactCritical — ожидания = факт при генерации.");
      int id = 1;
      const int weight = 50;
      const float cmin = 0f;
      const float cmax = 100f;

      foreach (bool vital in new[] { false, true })
      {
        foreach (int speed in new[] { -12, -10, -5, 5, 10, 12 })
        {
          foreach (int norma in new[] { 45, 55 })
          {
            foreach (float val in new[] { 20f, 48f, 52f, 80f })
            {
              foreach (int impact in new[] { -12, -8, -6, -5, -4, 0, 4, 5, 6, 8, 12 })
              {
                float v = Clamp(val, 0f, 100f);
                AppendExternalLine(sb, calculator, ref id, v, weight, norma, speed, vital, cmin, cmax, impact);
              }
            }
          }
        }
      }
    }

    private static void AppendAutoScenario_Urgency(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# Автоген: CalculateUrgencyFunction — ожидание = факт при генерации (инвариантная культура).");
      int id = 1;
      const float cmin = 0f;
      const float cmax = 100f;

      foreach (bool vital in new[] { false, true })
      {
        foreach (int speed in new[] { -10, -3, 3, 10 })
        {
          foreach (int norma in new[] { 30, 50, 70 })
          {
            foreach (int weight in new[] { 20, 50, 100 })
            {
              foreach (float rel in new[] { -25f, -5f, 0f, 5f, 25f })
              {
                float v = Clamp(norma + rel, 0f, 100f);
                AppendUrgencyLine(sb, calculator, ref id, v, weight, norma, speed, vital, cmin, cmax);
              }
            }
          }
        }
      }
    }

    private static void AppendAutoScenario_Operator(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# Автоген: ComputeOperatorAutomatizmAssessment — один параметр, focus 0 или id, ожидание = факт.");
      int pid = 1;
      const int w = 50;
      const float cmin = 0f;
      const float cmax = 100f;

      foreach (AppGlobalState.HomeostasisState ob in new[]
               { AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well })
      {
        foreach (AppGlobalState.HomeostasisState oa in new[]
                 { AppGlobalState.HomeostasisState.Bad, AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Well })
        {
          foreach (int speed in new[] { -10, 10 })
          {
            int norma = 50;
            foreach (float before in new[] { 40f, 50f, 60f })
            {
              float step = Math.Max(Math.Abs(speed) / 100f, 1e-4f);
              foreach (float cur in new[]
                       {
                         before,
                         speed < 0 ? before - step * 2f : before + step * 2f,
                         speed < 0 ? before + step * 2f : before - step * 2f
                       })
              {
                foreach (int focus in new[] { 0, pid })
                {
                  float b = Clamp(before, 0f, 100f);
                  float c = Clamp(cur, 0f, 100f);
                  AppendOperatorLine(sb, calculator, focus, ob, oa, pid, b, c, w, norma, speed, true, cmin, cmax);
                }
              }
            }
          }
        }
      }

      AppendOperatorTwoParamLine(sb, calculator, 0,
          AppGlobalState.HomeostasisState.Normal, AppGlobalState.HomeostasisState.Normal,
          1, 50f, 45f, w, 50, -10, true, cmin, cmax,
          2, 50f, 55f, w, 50, -10, true, cmin, cmax);
    }

    private static void AppendOperatorLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        int focus,
        AppGlobalState.HomeostasisState ob,
        AppGlobalState.HomeostasisState oa,
        int p1id,
        float before1,
        float cur1,
        int w1,
        int n1,
        int sp1,
        bool vit1,
        float cmin1,
        float cmax1)
    {
      AppendOperatorTwoParamLine(sb, calculator, focus, ob, oa, p1id, before1, cur1, w1, n1, sp1, vit1, cmin1, cmax1,
          0, 0f, 0f, 50, 50, -1, false, 0f, 100f);
    }

    private static void AppendOperatorTwoParamLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        int focus,
        AppGlobalState.HomeostasisState ob,
        AppGlobalState.HomeostasisState oa,
        int p1id,
        float before1,
        float cur1,
        int w1,
        int n1,
        int sp1,
        bool vit1,
        float cmin1,
        float cmax1,
        int p2id,
        float before2,
        float cur2,
        int w2,
        int n2,
        int sp2,
        bool vit2,
        float cmin2,
        float cmax2)
    {
      var dict = new Dictionary<int, float>();
      dict[p1id] = before1;
      if (p2id != 0)
        dict[p2id] = before2;

      var list = new List<ParameterData>
      {
        new ParameterData(p1id, "P" + p1id, "", cur1, w1, n1, sp1, vit1, cmin1, cmax1)
      };
      if (p2id != 0)
        list.Add(new ParameterData(p2id, "P" + p2id, "", cur2, w2, n2, sp2, vit2, cmin2, cmax2));

      int exp = calculator.ComputeOperatorAutomatizmAssessment(dict, list, focus, ob, oa);
      sb.Append(focus.ToString(Inv)).Append('|')
          .Append(OverallToPipe(ob)).Append('|')
          .Append(OverallToPipe(oa)).Append('|')
          .Append(p1id.ToString(Inv)).Append('|')
          .Append(before1.ToString(Inv)).Append('|')
          .Append(cur1.ToString(Inv)).Append('|')
          .Append(w1.ToString(Inv)).Append('|')
          .Append(n1.ToString(Inv)).Append('|')
          .Append(sp1.ToString(Inv)).Append('|')
          .Append(vit1 ? "1" : "0").Append('|')
          .Append(cmin1.ToString(Inv)).Append('|')
          .Append(cmax1.ToString(Inv)).Append('|')
          .Append(p2id.ToString(Inv)).Append('|')
          .Append(before2.ToString(Inv)).Append('|')
          .Append(cur2.ToString(Inv)).Append('|')
          .Append(w2.ToString(Inv)).Append('|')
          .Append(n2.ToString(Inv)).Append('|')
          .Append(sp2.ToString(Inv)).Append('|')
          .Append(vit2 ? "1" : "0").Append('|')
          .Append(cmin2.ToString(Inv)).Append('|')
          .Append(cmax2.ToString(Inv)).Append('|')
          .Append(exp.ToString(Inv))
          .AppendLine();
    }

    private static int OverallToPipe(AppGlobalState.HomeostasisState s)
    {
      if (s == AppGlobalState.HomeostasisState.Bad) return -1;
      if (s == AppGlobalState.HomeostasisState.Well) return 1;
      return 0;
    }

    private static void AppendExternalLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        ref int id,
        float val,
        int w,
        int norma,
        int speed,
        bool vital,
        float cmin,
        float cmax,
        int impact)
    {
      if (speed == 0)
        return;
      int pid = id++;
      string name = "P" + pid;
      var list = new List<ParameterData>
      {
        new ParameterData(pid, name, "", val, w, norma, speed, vital, cmin, cmax)
      };
      var dict = new Dictionary<int, int> { { pid, impact } };
      bool t = calculator.HasExternalCriticalImpact(dict, list);
      bool o = calculator.IsExternalImpactCritical(dict, list);
      sb.Append(pid.ToString(Inv)).Append('|')
          .Append(val.ToString(Inv)).Append('|')
          .Append(w.ToString(Inv)).Append('|')
          .Append(norma.ToString(Inv)).Append('|')
          .Append(speed.ToString(Inv)).Append('|')
          .Append(vital ? "1" : "0").Append('|')
          .Append(cmin.ToString(Inv)).Append('|')
          .Append(cmax.ToString(Inv)).Append('|')
          .Append(impact.ToString(Inv)).Append('|')
          .Append(t ? "1" : "0").Append('|')
          .Append(o ? "1" : "0")
          .AppendLine();
    }

    private static void AppendUrgencyLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        ref int id,
        float val,
        int w,
        int norma,
        int speed,
        bool vital,
        float cmin,
        float cmax)
    {
      if (speed == 0)
        return;
      int pid = id++;
      string name = "P" + pid;
      var p = new ParameterData(pid, name, "", val, w, norma, speed, vital, cmin, cmax);
      float u = calculator.CalculateUrgencyFunction(p);
      sb.Append(pid.ToString(Inv)).Append('|')
          .Append(val.ToString(Inv)).Append('|')
          .Append(w.ToString(Inv)).Append('|')
          .Append(norma.ToString(Inv)).Append('|')
          .Append(speed.ToString(Inv)).Append('|')
          .Append(vital ? "1" : "0").Append('|')
          .Append(cmin.ToString(Inv)).Append('|')
          .Append(cmax.ToString(Inv)).Append('|')
          .Append(u.ToString("G9", Inv))
          .AppendLine();
    }

    private static void AppendCriticalLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        ref int id,
        float cur,
        float prev,
        int w,
        int norma,
        int speed,
        bool vital,
        float cmin,
        float cmax)
    {
      int pid = id++;
      string name = "P" + pid;
      var curList = new List<ParameterData>
      {
        new ParameterData(pid, name, "", cur, w, norma, speed, vital, cmin, cmax)
      };
      var prevList = new List<ParameterData>
      {
        new ParameterData(pid, name, "", prev, w, norma, speed, vital, cmin, cmax)
      };
      int out1 = calculator.HasCriticalParameterChanges(curList, prevList) ? 1 : 0;
      sb.Append(pid.ToString(Inv)).Append('|')
          .Append(cur.ToString(Inv)).Append('|')
          .Append(prev.ToString(Inv)).Append('|')
          .Append(w.ToString(Inv)).Append('|')
          .Append(norma.ToString(Inv)).Append('|')
          .Append(speed.ToString(Inv)).Append('|')
          .Append(vital ? "1" : "0").Append('|')
          .Append(cmin.ToString(Inv)).Append('|')
          .Append(cmax.ToString(Inv)).Append('|')
          .Append(out1.ToString(Inv))
          .AppendLine();
    }

    private static void AppendHarmfulLine(
        StringBuilder sb,
        HomeostasisCalculator calculator,
        ref int id,
        float val,
        int w,
        int norma,
        int speed,
        bool vital,
        float cmin,
        float cmax)
    {
      int pid = id++;
      string name = "P" + pid;
      var list = new List<ParameterData>
      {
        new ParameterData(pid, name, "", val, w, norma, speed, vital, cmin, cmax)
      };
      int out1 = calculator.AnyVitalParameterInHarmfulZone(list) ? 1 : 0;
      sb.Append(pid.ToString(Inv)).Append('|')
          .Append(val.ToString(Inv)).Append('|')
          .Append(w.ToString(Inv)).Append('|')
          .Append(norma.ToString(Inv)).Append('|')
          .Append(speed.ToString(Inv)).Append('|')
          .Append(vital ? "1" : "0").Append('|')
          .Append(cmin.ToString(Inv)).Append('|')
          .Append(cmax.ToString(Inv)).Append('|')
          .Append(out1.ToString(Inv))
          .AppendLine();
    }

    private static float Clamp(float v, float lo, float hi)
    {
      if (v < lo) return lo;
      if (v > hi) return hi;
      return v;
    }

    /// <summary>Выполняет прогон по уже разобранным строкам и пишет артефакты в каталог.</summary>
    public static ResearchHarnessPipeRunOutcome Execute(
        HomeostasisCalculator calculator,
        ResearchHarnessPipeMethodInfo method,
        List<ResearchHarnessPipePreparedRow> rows,
        string outputDirectory,
        string originalInputText)
    {
      var result = new ResearchHarnessPipeRunOutcome();
      if (calculator == null)
      {
        result.ErrorMessage = "Калькулятор недоступен.";
        return result;
      }

      if (string.IsNullOrWhiteSpace(outputDirectory))
      {
        result.ErrorMessage = "Не задан каталог вывода.";
        return result;
      }

      Directory.CreateDirectory(outputDirectory);
      var sw = Stopwatch.StartNew();

      if (method.HarnessId == HomeostasisHarnessIds.DominantAndFinalStyles)
        GlobalTimer.SetGlobalPulseCountForHarness(10_000);

      foreach (var row in rows)
      {
        if (method.HarnessId == HomeostasisHarnessIds.HasCriticalParameterChanges)
          RunCritical(calculator, row);
        else if (method.HarnessId == HomeostasisHarnessIds.AnyVitalHarmfulZone)
          RunHarmful(calculator, row);
        else if (method.HarnessId == HomeostasisHarnessIds.ExternalImpactCriticalFlags)
          RunExternalFlags(calculator, row);
        else if (method.HarnessId == HomeostasisHarnessIds.CalculateUrgencyFunction)
          RunUrgency(calculator, row);
        else if (method.HarnessId == HomeostasisHarnessIds.ComputeOperatorAutomatizmAssessment)
          RunOperator(calculator, row);
        else if (method.HarnessId == HomeostasisHarnessIds.DominantAndFinalStyles)
          RunDominantAndFinal(calculator, row);
        else
        {
          result.ErrorMessage = "Неизвестный harness_id.";
          return result;
        }

        row.RecomputeMatch();
      }

      sw.Stop();

      var inputPath = Path.Combine(outputDirectory, "input_pipe.txt");
      File.WriteAllText(inputPath, originalInputText ?? "", Encoding.UTF8);

      var csvPath = Path.Combine(outputDirectory, "results.csv");
      WriteCsv(method, rows, csvPath);

      int mismatches = rows.Count(r => !r.Match);
      var manifest = new PipeHarnessManifest
      {
        harness_id = method.HarnessId,
        row_count = rows.Count,
        mismatch_count = mismatches,
        elapsed_ms = sw.ElapsedMilliseconds,
        input_pipe_file = Path.GetFullPath(inputPath),
        results_csv = Path.GetFullPath(csvPath),
        report_html = Path.GetFullPath(Path.Combine(outputDirectory, "report.html"))
      };
      File.WriteAllText(Path.Combine(outputDirectory, "manifest.json"),
          JsonConvert.SerializeObject(manifest, Formatting.Indented), Encoding.UTF8);

      var reportPath = manifest.report_html;
      ResearchHarnessPipeReportHtmlBuilder.WriteReport(method, rows, manifest, reportPath);

      result.Success = true;
      result.OutputDirectory = outputDirectory;
      result.RowCount = rows.Count;
      result.MismatchCount = mismatches;
      result.ElapsedMs = sw.ElapsedMilliseconds;
      result.ReportHtmlPath = reportPath;
      return result;
    }

    /// <summary>Разбор строки активаций стилей по зонам (как в данных параметра агента).</summary>
    /// <param name="data">Фрагмент вида «3:101,102;5:201» (зона:id через запятую; повтор).</param>
    /// <returns>Словарь зона → список id стилей (ключи 0…6).</returns>
    public static Dictionary<int, List<int>> ParseStyleActivationsHarness(string data)
    {
      var result = new Dictionary<int, List<int>>();
      for (int z = 0; z <= 6; z++)
        result[z] = new List<int>();
      if (string.IsNullOrWhiteSpace(data))
        return result;
      foreach (var statePart in data.Split(';'))
      {
        if (string.IsNullOrWhiteSpace(statePart))
          continue;
        var keyValue = statePart.Split(':');
        if (keyValue.Length == 2 && int.TryParse(keyValue[0].Trim(), NumberStyles.Integer, Inv, out int stateId))
        {
          var ids = keyValue[1].Split(',')
              .Where(s => !string.IsNullOrWhiteSpace(s))
              .Select(s => int.Parse(s.Trim(), NumberStyles.Integer, Inv))
              .ToList();
          if (result.ContainsKey(stateId))
            result[stateId] = ids;
        }
      }

      return result;
    }

    /// <param name="csv">Список целых id через запятую.</param>
    /// <returns>Упорядоченный появлением список id.</returns>
    public static List<int> ParseCommaStyleIds(string csv)
    {
      var r = new List<int>();
      if (string.IsNullOrWhiteSpace(csv))
        return r;
      foreach (var p in csv.Split(','))
      {
        if (int.TryParse(p.Trim(), NumberStyles.Integer, Inv, out int id))
          r.Add(id);
      }

      return r;
    }

    /// <param name="ids">Id стилей из справочника/сценария.</param>
    /// <returns>Минимальные объекты <see cref="BehaviorStyle"/> для вызова калькулятора.</returns>
    public static List<BehaviorStyle> BuildBaseStylesFromIds(IEnumerable<int> ids)
    {
      var list = new List<BehaviorStyle>();
      foreach (var id in ids)
        list.Add(new BehaviorStyle { Id = id, Name = "S" + id, Description = "" });
      return list;
    }

    private static void AppendAutoScenario_Dominant(StringBuilder sb, HomeostasisCalculator calculator)
    {
      sb.AppendLine("# FindDominantParameter + GetFinalActiveStyles; колонки ожидания = снимок калькулятора при генерации (см. BuildAutoScenarioText).");
      const string styles = "9101,9102,9103";
      const int weight = 55;
      const int norma = 50;
      const bool vital = true;
      const float cmin = 0f;
      const float cmax = 100f;
      const int pid = 1;
      const string act = "4:9101;5:9102;6:9103";

      foreach (int dyn in new[] { 3, 5, 10 })
      {
        foreach (float dif in new[] { 0f, 0.25f, 0.5f, 1f })
        {
          foreach (int speed in new[] { -10, 10 })
          {
            foreach (float val in new[] { 22f, 30f, 40f, 48f, 50f, 52f, 60f, 72f, 85f })
            {
              var p = new ParameterData(pid, "P" + pid, "", val, weight, norma, speed, vital, cmin, cmax);
              foreach (var kv in ParseStyleActivationsHarness(act))
                p.StyleActivations[kv.Key] = kv.Value.ToList();
              var plist = new List<ParameterData> { p };
              var baseStyles = BuildBaseStylesFromIds(ParseCommaStyleIds(styles));
              var (dom, zone, score) = calculator.FindDominantParameter(plist, dyn, dif);
              calculator.GetFinalActiveStyles(baseStyles, plist, dyn, dif);
              int did = dom?.Id ?? -1;
              sb.Append(dyn.ToString(Inv)).Append('|')
                  .Append(dif.ToString(Inv)).Append('|')
                  .Append(styles).Append('|')
                  .Append(pid.ToString(Inv)).Append('|')
                  .Append(val.ToString(Inv)).Append('|')
                  .Append(weight.ToString(Inv)).Append('|')
                  .Append(norma.ToString(Inv)).Append('|')
                  .Append(speed.ToString(Inv)).Append('|')
                  .Append(vital ? "1" : "0").Append('|')
                  .Append(cmin.ToString(Inv)).Append('|')
                  .Append(cmax.ToString(Inv)).Append('|')
                  .Append(act).Append('|')
                  .Append(did.ToString(Inv)).Append('|')
                  .Append(zone.ToString(Inv)).Append('|')
                  .Append(score.ToString("G9", Inv))
                  .AppendLine();
            }
          }
        }
      }
    }

    private static void ParseDominantRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int dyn = ParseIntStrict(cells[0], "P1 dynamic_time", logicalLine, outcome);
      float dif = ParseFloatStrict(cells[1], "P2 dif_sensor");
      string styles = (cells[2] ?? "").Trim();
      int pid = ParseIntStrict(cells[3], "P3 id параметра", logicalLine, outcome);
      float val = ParseFloatStrict(cells[4], "P4 значение");
      int weight = ParseIntWithOptionalFractionWarning(cells[5], "P5 вес", logicalLine, outcome);
      int norma = ParseIntWithOptionalFractionWarning(cells[6], "P6 норма", logicalLine, outcome);
      int speed = ParseIntWithOptionalFractionWarning(cells[7], "P7 скорость", logicalLine, outcome);
      if (speed == 0)
      {
        outcome.BlockingErrors.Add($"Строка {logicalLine}: скорость не может быть 0.");
        return;
      }

      bool vital = ParseBoolStrict(cells[8], "P8 жизненно важен");
      float cmin = ParseFloatStrict(cells[9], "P9 крит.мин");
      float cmax = ParseFloatStrict(cells[10], "P10 крит.макс");
      string act = (cells[11] ?? "").Trim();

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "dom:" + pid,
        Match = false,
        DomDynamicTime = dyn,
        DomDifSensor = dif,
        DomBaseStyleIdsRaw = styles,
        DomStyleActivationsRaw = act
      };
      AttachHarmfulPayload(row, pid, val, weight, norma, speed, vital, cmin, cmax);
      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static void RunDominantAndFinal(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      var styleIdList = ParseCommaStyleIds(row.DomBaseStyleIdsRaw);
      var baseStyles = BuildBaseStylesFromIds(styleIdList);
      string name = "P" + row.CriticalParamId;
      var p = new ParameterData(
          row.CriticalParamId, name, "", row.HarmfulValue,
          row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
          row.ParamCritMin, row.ParamCritMax);
      foreach (var kv in ParseStyleActivationsHarness(row.DomStyleActivationsRaw))
        p.StyleActivations[kv.Key] = kv.Value.ToList();

      var parameters = new List<ParameterData> { p };
      var (dom, zone, score) = calculator.FindDominantParameter(parameters, row.DomDynamicTime, row.DomDifSensor);
      calculator.GetFinalActiveStyles(baseStyles, parameters, row.DomDynamicTime, row.DomDifSensor);

      int actualId = dom?.Id ?? -1;
      ApplyIntToSlot(row, 0, actualId);
      ApplyIntToSlot(row, 1, zone);
      ApplyFloatToSlot(row, 2, score);
    }

    private static string[] SplitPipeRow(string line)
    {
      return line.Split('|');
    }

    private static void ParseCriticalRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int pid = ParseIntStrict(cells[0], "P1 (id параметра)", logicalLine, outcome);
      float cur = ParseFloatStrict(cells[1], "P2 (текущее значение)");
      float prev = ParseFloatStrict(cells[2], "P3 (предыдущее значение)");
      int weight = ParseIntWithOptionalFractionWarning(cells[3], "P4 (вес)", logicalLine, outcome);
      int norma = ParseIntWithOptionalFractionWarning(cells[4], "P5 (норма)", logicalLine, outcome);
      int speed = ParseIntWithOptionalFractionWarning(cells[5], "P6 (скорость)", logicalLine, outcome);
      if (speed == 0)
      {
        outcome.BlockingErrors.Add(
            $"Строка {logicalLine}: P6 (скорость) не может быть 0 — в модели ParameterData допустима только ненулевая скорость (отрицательная: дефицит, положительная: избыток).");
        return;
      }

      bool vital = ParseBoolStrict(cells[6], "P7 (жизненно важен)");
      float cmin = ParseFloatStrict(cells[7], "P8 (крит. мин)");
      float cmax = ParseFloatStrict(cells[8], "P9 (крит. макс)");

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "id=" + pid,
        Match = false
      };
      AttachCriticalPayload(row, pid, cur, prev, weight, norma, speed, vital, cmin, cmax);
      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static void ParseHarmfulRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int pid = ParseIntStrict(cells[0], "P1 (id параметра)", logicalLine, outcome);
      float val = ParseFloatStrict(cells[1], "P2 (значение)");
      int weight = ParseIntWithOptionalFractionWarning(cells[2], "P3 (вес)", logicalLine, outcome);
      int norma = ParseIntWithOptionalFractionWarning(cells[3], "P4 (норма)", logicalLine, outcome);
      int speed = ParseIntWithOptionalFractionWarning(cells[4], "P5 (скорость)", logicalLine, outcome);
      if (speed == 0)
      {
        outcome.BlockingErrors.Add(
            $"Строка {logicalLine}: P5 (скорость) не может быть 0 — в модели ParameterData допустима только ненулевая скорость (отрицательная: дефицит, положительная: избыток).");
        return;
      }

      bool vital = ParseBoolStrict(cells[5], "P6 (жизненно важен)");
      float cmin = ParseFloatStrict(cells[6], "P7 (крит. мин)");
      float cmax = ParseFloatStrict(cells[7], "P8 (крит. макс)");

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "id=" + pid,
        Match = false
      };
      AttachHarmfulPayload(row, pid, val, weight, norma, speed, vital, cmin, cmax);
      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static void ParseExternalFlagsRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int pid = ParseIntStrict(cells[0], "P1 (id параметра)", logicalLine, outcome);
      float val = ParseFloatStrict(cells[1], "P2 (значение)");
      int weight = ParseIntWithOptionalFractionWarning(cells[2], "P3 (вес)", logicalLine, outcome);
      int norma = ParseIntWithOptionalFractionWarning(cells[3], "P4 (норма)", logicalLine, outcome);
      int speed = ParseIntWithOptionalFractionWarning(cells[4], "P5 (скорость)", logicalLine, outcome);
      if (speed == 0)
      {
        outcome.BlockingErrors.Add($"Строка {logicalLine}: скорость не может быть 0.");
        return;
      }

      bool vital = ParseBoolStrict(cells[5], "P6 (жизненно важен)");
      float cmin = ParseFloatStrict(cells[6], "P7 (крит. мин)");
      float cmax = ParseFloatStrict(cells[7], "P8 (крит. макс)");
      int impact = ParseIntStrict(cells[8], "P9 (воздействие)", logicalLine, outcome);

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "id=" + pid,
        Match = false,
        ExternalImpactValue = impact
      };
      AttachHarmfulPayload(row, pid, val, weight, norma, speed, vital, cmin, cmax);
      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static void ParseUrgencyRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int pid = ParseIntStrict(cells[0], "P1 (id параметра)", logicalLine, outcome);
      float val = ParseFloatStrict(cells[1], "P2 (значение)");
      int weight = ParseIntWithOptionalFractionWarning(cells[2], "P3 (вес)", logicalLine, outcome);
      int norma = ParseIntWithOptionalFractionWarning(cells[3], "P4 (норма)", logicalLine, outcome);
      int speed = ParseIntWithOptionalFractionWarning(cells[4], "P5 (скорость)", logicalLine, outcome);
      if (speed == 0)
      {
        outcome.BlockingErrors.Add($"Строка {logicalLine}: скорость не может быть 0.");
        return;
      }

      bool vital = ParseBoolStrict(cells[5], "P6 (жизненно важен)");
      float cmin = ParseFloatStrict(cells[6], "P7 (крит. мин)");
      float cmax = ParseFloatStrict(cells[7], "P8 (крит. макс)");

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "id=" + pid,
        Match = false
      };
      AttachHarmfulPayload(row, pid, val, weight, norma, speed, vital, cmin, cmax);
      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static void ParseOperatorRow(
        string[] cells,
        int logicalLine,
        int fileLine,
        ResearchHarnessPipeMethodInfo method,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int focus = ParseIntStrict(cells[0], "P1 (focus_id)", logicalLine, outcome);
      var ob = ParseOverallState(cells[1], "P2 (overall_before)", logicalLine);
      var oa = ParseOverallState(cells[2], "P3 (overall_after)", logicalLine);

      int p1 = ParseIntStrict(cells[3], "P4 (p1 id)", logicalLine, outcome);
      float b1 = ParseFloatStrict(cells[4], "P5 (p1 до)");
      float c1 = ParseFloatStrict(cells[5], "P6 (p1 текущее)");
      int w1 = ParseIntWithOptionalFractionWarning(cells[6], "P7 (p1 вес)", logicalLine, outcome);
      int n1 = ParseIntWithOptionalFractionWarning(cells[7], "P8 (p1 норма)", logicalLine, outcome);
      int sp1 = ParseIntWithOptionalFractionWarning(cells[8], "P9 (p1 скорость)", logicalLine, outcome);
      if (sp1 == 0)
      {
        outcome.BlockingErrors.Add($"Строка {logicalLine}: p1 скорость не может быть 0.");
        return;
      }

      bool v1 = ParseBoolStrict(cells[9], "P10 (p1 жизненно важен)");
      float cmin1 = ParseFloatStrict(cells[10], "P11 (p1 крит.мин)");
      float cmax1 = ParseFloatStrict(cells[11], "P12 (p1 крит.макс)");

      int p2 = ParseIntStrict(cells[12], "P13 (p2 id)", logicalLine, outcome);
      float b2 = ParseFloatStrict(cells[13], "P14 (p2 до)");
      float c2 = ParseFloatStrict(cells[14], "P15 (p2 текущее)");
      int w2 = ParseIntWithOptionalFractionWarning(cells[15], "P16 (p2 вес)", logicalLine, outcome);
      int n2 = ParseIntWithOptionalFractionWarning(cells[16], "P17 (p2 норма)", logicalLine, outcome);
      int sp2 = ParseIntWithOptionalFractionWarning(cells[17], "P18 (p2 скорость)", logicalLine, outcome);
      bool v2 = ParseBoolStrict(cells[18], "P19 (p2 жизненно важен)");
      float cmin2 = ParseFloatStrict(cells[19], "P20 (p2 крит.мин)");
      float cmax2 = ParseFloatStrict(cells[20], "P21 (p2 крит.макс)");

      if (p2 != 0 && sp2 == 0)
      {
        outcome.BlockingErrors.Add($"Строка {logicalLine}: при ненулевом p2 id скорость p2 не может быть 0.");
        return;
      }

      var row = new ResearchHarnessPipePreparedRow
      {
        SourceLineNumber = fileLine,
        RawCells = (string[])cells.Clone(),
        CaseId = "op:" + p1 + (p2 != 0 ? "+" + p2 : ""),
        Match = false,
        OpFocusId = focus,
        OpOverallBefore = ob,
        OpOverallAfter = oa,
        OpP1Id = p1,
        OpP1Before = b1,
        OpP1Cur = c1,
        OpP1Weight = w1,
        OpP1Norma = n1,
        OpP1Speed = sp1,
        OpP1Vital = v1,
        OpP1Cmin = cmin1,
        OpP1Cmax = cmax1,
        OpP2Id = p2,
        OpP2Before = b2,
        OpP2Cur = c2,
        OpP2Weight = w2,
        OpP2Norma = n2,
        OpP2Speed = sp2,
        OpP2Vital = v2,
        OpP2Cmin = cmin2,
        OpP2Cmax = cmax2
      };

      AttachExpectedOutputs(row, method, cells, logicalLine, outcome);
      outcome.Rows.Add(row);
    }

    private static AppGlobalState.HomeostasisState ParseOverallState(string raw, string fieldName, int logicalLine)
    {
      raw = (raw ?? "").Trim();
      if (raw.Length == 0)
        throw new FormatException($"{fieldName}: пусто (нужно −1/0/1 или Bad/Normal/Well).");

      var low = raw.ToLowerInvariant();
      if (low == "bad" || low == "плохо")
        return AppGlobalState.HomeostasisState.Bad;
      if (low == "normal" || low == "норма" || low == "norm")
        return AppGlobalState.HomeostasisState.Normal;
      if (low == "well" || low == "хорошо")
        return AppGlobalState.HomeostasisState.Well;

      if (int.TryParse(raw, NumberStyles.Integer, Inv, out int v))
      {
        if (v == -1) return AppGlobalState.HomeostasisState.Bad;
        if (v == 0) return AppGlobalState.HomeostasisState.Normal;
        if (v == 1) return AppGlobalState.HomeostasisState.Well;
      }

      throw new FormatException($"{fieldName}: «{raw}» — ожидается −1, 0, 1 или Bad/Normal/Well.");
    }

    private static bool TryParseAssessmentInt(string raw, out int v, out string error)
    {
      v = 0;
      error = null;
      raw = (raw ?? "").Trim();
      if (!int.TryParse(raw, NumberStyles.Integer, Inv, out v))
      {
        error = "нужно целое −1, 0 или 1.";
        return false;
      }

      if (v < -1 || v > 1)
      {
        error = "допустимы только −1, 0 и 1.";
        return false;
      }

      error = null;
      return true;
    }

    private static void AttachExpectedOutputs(
        ResearchHarnessPipePreparedRow row,
        ResearchHarnessPipeMethodInfo method,
        string[] cells,
        int logicalLine,
        ResearchHarnessPipeParseOutcome outcome)
    {
      int ic = method.InputColumnCount;
      for (int si = 0; si < method.ResultSlotCount; si++)
      {
        var def = method.ResultColumns[si];
        string raw = (cells[ic + si] ?? "").Trim();
        string err;
        if (!TryValidateExpectedSlot(def.Kind, raw, out err))
        {
          outcome.BlockingErrors.Add($"Строка {logicalLine}, выход «{def.Label}»: {err}");
          return;
        }

        row.OutputSlots.Add(new ResearchHarnessPipeOutputSlot(def.Label, def.Kind, raw));
      }
    }

    private static bool TryValidateExpectedSlot(HarnessValueKind kind, string raw, out string error)
    {
      error = null;
      try
      {
        switch (kind)
        {
          case HarnessValueKind.Boolean:
            ParseBoolStrict(raw, "ожидание");
            return true;
          case HarnessValueKind.Int32:
            if (int.TryParse(raw, NumberStyles.Integer, Inv, out _))
              return true;
            error = "нужно целое число.";
            return false;
          case HarnessValueKind.TrinaryInt:
            return TryParseAssessmentInt(raw, out _, out error);
          case HarnessValueKind.Float:
            ParseFloatStrict(raw, "ожидание");
            return true;
          default:
            error = "неизвестный тип выхода.";
            return false;
        }
      }
      catch (FormatException ex)
      {
        error = ex.Message;
        return false;
      }
    }

    private static void AttachCriticalPayload(
        ResearchHarnessPipePreparedRow row,
        int pid, float cur, float prev, int weight, int norma, int speed, bool vital, float cmin, float cmax)
    {
      row.CriticalParamId = pid;
      row.CriticalCur = cur;
      row.CriticalPrev = prev;
      row.ParamWeight = weight;
      row.ParamNorma = norma;
      row.ParamSpeed = speed;
      row.ParamVital = vital;
      row.ParamCritMin = cmin;
      row.ParamCritMax = cmax;
    }

    private static void AttachHarmfulPayload(
        ResearchHarnessPipePreparedRow row,
        int pid, float val, int weight, int norma, int speed, bool vital, float cmin, float cmax)
    {
      row.CriticalParamId = pid;
      row.HarmfulValue = val;
      row.ParamWeight = weight;
      row.ParamNorma = norma;
      row.ParamSpeed = speed;
      row.ParamVital = vital;
      row.ParamCritMin = cmin;
      row.ParamCritMax = cmax;
    }

    private static void RunCritical(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      string name = "P" + row.CriticalParamId;
      var cur = new List<ParameterData>
      {
        new ParameterData(
            row.CriticalParamId, name, "", row.CriticalCur,
            row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
            row.ParamCritMin, row.ParamCritMax)
      };
      var prev = new List<ParameterData>
      {
        new ParameterData(
            row.CriticalParamId, name, "", row.CriticalPrev,
            row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
            row.ParamCritMin, row.ParamCritMax)
      };
      bool actual = calculator.HasCriticalParameterChanges(cur, prev);
      ApplyBoolToSlot(row, 0, actual);
    }

    private static void RunHarmful(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      string name = "P" + row.CriticalParamId;
      var list = new List<ParameterData>
      {
        new ParameterData(
            row.CriticalParamId, name, "", row.HarmfulValue,
            row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
            row.ParamCritMin, row.ParamCritMax)
      };
      bool actual = calculator.AnyVitalParameterInHarmfulZone(list);
      ApplyBoolToSlot(row, 0, actual);
    }

    private static void RunExternalFlags(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      string name = "P" + row.CriticalParamId;
      var list = new List<ParameterData>
      {
        new ParameterData(
            row.CriticalParamId, name, "", row.HarmfulValue,
            row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
            row.ParamCritMin, row.ParamCritMax)
      };
      var dict = new Dictionary<int, int> { { row.CriticalParamId, row.ExternalImpactValue } };
      bool t = calculator.HasExternalCriticalImpact(dict, list);
      bool o = calculator.IsExternalImpactCritical(dict, list);
      ApplyBoolToSlot(row, 0, t);
      ApplyBoolToSlot(row, 1, o);
    }

    private static void RunUrgency(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      string name = "P" + row.CriticalParamId;
      var p = new ParameterData(
          row.CriticalParamId, name, "", row.HarmfulValue,
          row.ParamWeight, row.ParamNorma, row.ParamSpeed, row.ParamVital,
          row.ParamCritMin, row.ParamCritMax);
      float actual = calculator.CalculateUrgencyFunction(p);
      ApplyFloatToSlot(row, 0, actual);
    }

    private static void RunOperator(HomeostasisCalculator calculator, ResearchHarnessPipePreparedRow row)
    {
      var dict = new Dictionary<int, float> { { row.OpP1Id, row.OpP1Before } };
      var currents = new List<ParameterData>
      {
        new ParameterData(row.OpP1Id, "P" + row.OpP1Id, "", row.OpP1Cur,
            row.OpP1Weight, row.OpP1Norma, row.OpP1Speed, row.OpP1Vital, row.OpP1Cmin, row.OpP1Cmax)
      };
      if (row.OpP2Id != 0)
      {
        dict[row.OpP2Id] = row.OpP2Before;
        currents.Add(new ParameterData(row.OpP2Id, "P" + row.OpP2Id, "", row.OpP2Cur,
            row.OpP2Weight, row.OpP2Norma, row.OpP2Speed, row.OpP2Vital, row.OpP2Cmin, row.OpP2Cmax));
      }

      int actual = calculator.ComputeOperatorAutomatizmAssessment(
          dict, currents, row.OpFocusId, row.OpOverallBefore, row.OpOverallAfter);
      ApplyIntToSlot(row, 0, actual);
    }

    private static void ApplyBoolToSlot(ResearchHarnessPipePreparedRow row, int index, bool actual)
    {
      var slot = row.OutputSlots[index];
      bool expected = ParseBoolStrict(slot.ExpectedText, slot.Label);
      slot.ActualText = actual ? "1" : "0";
      slot.Match = expected == actual;
    }

    private static void ApplyFloatToSlot(ResearchHarnessPipePreparedRow row, int index, float actual)
    {
      var slot = row.OutputSlots[index];
      float expected = ParseFloatStrict(slot.ExpectedText, slot.Label);
      slot.ActualText = actual.ToString("G9", Inv);
      slot.Match = Math.Abs(actual - expected) <= FloatCompareEpsilon + 1e-12f;
    }

    private static void ApplyIntToSlot(ResearchHarnessPipePreparedRow row, int index, int actual)
    {
      var slot = row.OutputSlots[index];
      if (slot.Kind == HarnessValueKind.TrinaryInt)
      {
        if (!TryParseAssessmentInt(slot.ExpectedText, out int expected, out _))
        {
          slot.ActualText = actual.ToString(Inv);
          slot.Match = false;
          return;
        }

        slot.ActualText = actual.ToString(Inv);
        slot.Match = expected == actual;
        return;
      }

      if (!int.TryParse(slot.ExpectedText.Trim(), NumberStyles.Integer, Inv, out int expectedPlain))
      {
        slot.ActualText = actual.ToString(Inv);
        slot.Match = false;
        return;
      }

      slot.ActualText = actual.ToString(Inv);
      slot.Match = expectedPlain == actual;
    }

    private static void WriteCsv(ResearchHarnessPipeMethodInfo method, List<ResearchHarnessPipePreparedRow> rows, string path)
    {
      var sb = new StringBuilder();
      var headers = new List<string>();
      foreach (var label in method.ColumnLabels)
        headers.Add(EscapeCsv(label));
      foreach (var rc in method.ResultColumns)
        headers.Add(EscapeCsv(rc.Label + " факт"));
      headers.Add("Итог строки");
      sb.AppendLine(string.Join(",", headers));

      foreach (var r in rows)
      {
        var cells = new List<string>();
        foreach (var c in r.RawCells)
          cells.Add(EscapeCsv(c));
        for (int i = 0; i < r.OutputSlots.Count; i++)
          cells.Add(EscapeCsv(r.OutputSlots[i].ActualText));
        cells.Add(EscapeCsv(r.Match ? "OK" : "NO"));
        sb.AppendLine(string.Join(",", cells));
      }

      File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string EscapeCsv(string s)
    {
      if (s == null) return "";
      bool need = s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
      if (!need) return s;
      return "\"" + s.Replace("\"", "\"\"") + "\"";
    }

    private static int ParseIntStrict(string raw, string fieldName, int logicalLine, ResearchHarnessPipeParseOutcome outcome)
    {
      raw = (raw ?? "").Trim();
      if (raw.Length == 0)
        throw new FormatException($"{fieldName}: пустое значение (нужно целое число).");

      if (int.TryParse(raw, NumberStyles.Integer, Inv, out int v))
        return v;

      if (double.TryParse(NormalizeNumber(raw), NumberStyles.Float, Inv, out double d))
      {
        double rounded = Math.Round(d);
        if (Math.Abs(d - rounded) < 1e-9)
        {
          outcome.Warnings.Add($"Строка {logicalLine}: {fieldName} — «{raw}» нецелое; для прогона будет использовано {(int)rounded}.");
          return (int)rounded;
        }
      }

      throw new FormatException($"{fieldName}: «{raw}» не распознано как целое число.");
    }

    private static int ParseIntWithOptionalFractionWarning(string raw, string fieldName, int logicalLine, ResearchHarnessPipeParseOutcome outcome)
    {
      return ParseIntStrict(raw, fieldName, logicalLine, outcome);
    }

    private static float ParseFloatStrict(string raw, string fieldName)
    {
      raw = (raw ?? "").Trim();
      if (raw.Length == 0)
        throw new FormatException($"{fieldName}: пустое значение.");

      if (double.TryParse(NormalizeNumber(raw), NumberStyles.Float, Inv, out double d))
        return (float)d;

      throw new FormatException($"{fieldName}: «{raw}» не число.");
    }

    private static string NormalizeNumber(string raw)
    {
      return raw.Replace(',', '.');
    }

    private static bool ParseBoolStrict(string raw, string fieldName)
    {
      raw = (raw ?? "").Trim();
      if (raw.Length == 0)
        throw new FormatException($"{fieldName}: пустое значение (ожидается 0/1, да/нет).");

      if (int.TryParse(raw, NumberStyles.Integer, Inv, out int iv))
      {
        if (iv == 0) return false;
        if (iv == 1) return true;
        throw new FormatException($"{fieldName}: для логического поля допустимы 0 или 1, не «{raw}».");
      }

      if (bool.TryParse(raw, out bool b))
        return b;

      var low = raw.ToLowerInvariant();
      if (low == "да" || low == "yes" || low == "y")
        return true;
      if (low == "нет" || low == "no" || low == "n")
        return false;

      throw new FormatException($"{fieldName}: «{raw}» не распознано как логическое значение (0, 1, да, нет, true, false).");
    }
  }
}
