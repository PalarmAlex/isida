using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using ISIDA.Actions;
using ISIDA.Common;
using ISIDA.Gomeostas;
using ISIDA.Reflexes;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Регресс: у автоматизма в логе появляется отдельная колонка «ТриггерАВ» — ID узла дерева
  /// автоматизмов, по которому он сматчился.
  ///
  /// Исторически в единственную колонку «Триггер» при запуске автоматизма попадал PerceptionImage
  /// предъявления (<see cref="AppGlobalState.LastTriggerStimulusID"/>). Для рефлексов это корректно
  /// (у усл. рефлекса Level3 = ID образа-триггера), но у автоматизмов PerceptionImage-предъявление
  /// триггером не является: их триггер — ветка <c>AutomatizmTree</c>. В отчётах Velum это выглядело
  /// как «автоматизм запустил образ, в котором только цвет».
  ///
  /// Колонка заводится отдельной, а не переопределением «Триггер»: ID образов восприятия и ID узлов
  /// дерева — разные пространства имён с независимыми счётчиками, и в одном поле их неразличимо.
  /// </summary>
  /// <remarks>
  /// Наблюдение идёт через CSV-файл: <c>WriteCsvLine</c> пишет все ключи словаря записи, и именно
  /// CSV/JSONL — источник данных для отчётов Velum. Канал <see cref="ILogWriter"/> (память UI)
  /// сознательно не расширяется — его контракт потребляет внешний адаптер.
  /// </remarks>
  [Collection("EngineIntegration")]
  public sealed class AutomatizmTriggerColumnTests : EngineIntegrationTestBase
  {
    public AutomatizmTriggerColumnTests(EngineFixture engine) : base(engine)
    {
    }

    private static ResearchLogger CreateCsvLogger(string tempDir)
    {
      return new ResearchLogger(
          GomeostasSystem.Instance,
          PerceptionImagesSystem.Instance,
          ReflexesActivator.Instance,
          AdaptiveActionsSystem.Instance,
          logsDirectory: tempDir,
          logFileName: "AutomatizmTriggerColumnTests",
          format: ResearchLogger.LogFormat.Csv,
          clearOnStart: true,
          enabled: true);
    }

    /// <summary>
    /// Гасит триггерный образ активатора: <c>GetCurrentTriggerImageID</c> отдаёт
    /// <c>ActiveGlobalCurTriggerStimulusID</c> в приоритете над <c>LastTriggerStimulusID</c>,
    /// а это рантаймовое состояние <see cref="EngineFixture.ResetToCleanState"/> не сбрасывает —
    /// остаток чужого теста дал бы в «Триггер» не тот ID.
    /// </summary>
    private static void ClearActivatorTriggerId()
    {
      var field = typeof(ReflexesActivator).GetField(
          "_activeGlobalCurTriggerStimulusID", BindingFlags.Instance | BindingFlags.NonPublic);
      Assert.NotNull(field);
      field.SetValue(ReflexesActivator.Instance, 0);
    }

    /// <summary>
    /// При активном автоматизме в CSV попадает «ТриггерАВ» = ID узла дерева, а «Триггер»
    /// остаётся PerceptionImage-предъявлением (обе колонки различимы).
    /// </summary>
    [Fact]
    public void ActiveAutomatizm_WritesAutomatizmTreeNodeIdAsTriggerAv()
    {
      string tempDir = PrepareTempDir();
      try
      {
        // PerceptionImage-предъявление: только зрительный канал (тот самый «пустой» цветной образ
        // из отчёта Velum) — он НЕ является триггером автоматизма.
        int perceptionImageId = NewImage(color: 1);
        int automatizmNodeId = 777;
        int automatizmId = 42;

        AppGlobalState.LastTriggerStimulusID = perceptionImageId;
        AppGlobalState.AutomatizmNodeId = automatizmNodeId;

        var logger = CreateCsvLogger(tempDir);
        try
        {
          ClearActivatorTriggerId();
          int pulse = Engine.CurrentPulse + 10;
          // Как PsychicSystem на пульсе активации: узел + ID автоматизма + пульс запуска.
          AppGlobalState.UpdateAutomatizmInfo(automatizmId, pulse);

          logger.LogSystemState(pulse);
          logger.Flush();
        }
        finally
        {
          logger.Dispose();
        }

        var (headers, rows) = ReadCsv(tempDir);
        Assert.Contains("ТриггерАВ", headers);
        Assert.Contains("Триггер", headers);

        int triggerAvCol = Array.IndexOf(headers, "ТриггерАВ");
        int triggerCol = Array.IndexOf(headers, "Триггер");
        int automatizmCol = Array.IndexOf(headers, "Автоматизм");

        Assert.NotEmpty(rows);
        var row = rows[rows.Count - 1];
        Assert.Equal(automatizmId.ToString(), row[automatizmCol]);
        Assert.Equal(automatizmNodeId.ToString(), row[triggerAvCol]);
        // «Триггер» не переопределён: там по-прежнему образ предъявления.
        Assert.Equal(perceptionImageId.ToString(), row[triggerCol]);
      }
      finally
      {
        AppGlobalState.AutomatizmNodeId = 0;
        AppGlobalState.LastTriggerStimulusID = 0;
        AppGlobalState.ResetAutomatizmInfo();
        TryDeleteDir(tempDir);
      }
    }

    /// <summary>
    /// Без активного автоматизма колонка пустая: значение узла не «течёт» в строки рефлексов
    /// (иначе клиент не отличил бы триггер автоматизма от триггера рефлекса).
    /// </summary>
    [Fact]
    public void NoAutomatizm_TriggerAvStaysEmpty()
    {
      string tempDir = PrepareTempDir();
      try
      {
        // Остаток узла от предыдущего запуска не должен попасть в «ТриггерАВ» строки без автоматизма.
        AppGlobalState.AutomatizmNodeId = 555;
        // Стимул-предъявление есть (строка рефлекса): «Триггер» заполнен, «ТриггерАВ» — пуст.
        int perceptionImageId = NewImage(color: 1);
        AppGlobalState.LastTriggerStimulusID = perceptionImageId;

        var logger = CreateCsvLogger(tempDir);
        try
        {
          ClearActivatorTriggerId();
          int pulse = Engine.CurrentPulse + 10;
          AppGlobalState.ResetAutomatizmInfo();

          logger.LogSystemState(pulse);
          logger.Flush();
        }
        finally
        {
          logger.Dispose();
        }

        var (headers, rows) = ReadCsv(tempDir);
        Assert.Contains("ТриггерАВ", headers);
        int triggerAvCol = Array.IndexOf(headers, "ТриггерАВ");

        Assert.NotEmpty(rows);
        foreach (var row in rows)
          Assert.Equal("", row[triggerAvCol]);
      }
      finally
      {
        AppGlobalState.AutomatizmNodeId = 0;
        AppGlobalState.LastTriggerStimulusID = 0;
        AppGlobalState.ResetAutomatizmInfo();
        TryDeleteDir(tempDir);
      }
    }

    private static (string[] headers, List<string[]> rows) ReadCsv(string tempDir)
    {
      string csvPath = Path.Combine(tempDir, "AutomatizmTriggerColumnTests.csv");
      Assert.True(File.Exists(csvPath), "CSV-лог не создан: " + csvPath);

      var lines = File.ReadAllLines(csvPath, Encoding.UTF8).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
      Assert.NotEmpty(lines);

      var headers = SplitCsvLine(lines[0]);
      var rows = new List<string[]>();
      for (int i = 1; i < lines.Count; i++)
      {
        var cells = SplitCsvLine(lines[i]);
        // Строки «_Parameters»/«_Styles» пишутся в другие файлы — здесь только симбионтная строка.
        Assert.Equal(headers.Length, cells.Length);
        rows.Add(cells);
      }
      return (headers, rows);
    }

    /// <summary>Разбор строки CSV с учётом кавычек (как пишет <c>EscapeCsvValue</c>).</summary>
    private static string[] SplitCsvLine(string line)
    {
      var cells = new List<string>();
      var sb = new StringBuilder();
      bool inQuotes = false;
      for (int i = 0; i < line.Length; i++)
      {
        char c = line[i];
        if (inQuotes)
        {
          if (c == '"')
          {
            if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
            else inQuotes = false;
          }
          else sb.Append(c);
        }
        else if (c == '"') inQuotes = true;
        else if (c == ';') { cells.Add(sb.ToString()); sb.Clear(); }
        else sb.Append(c);
      }
      cells.Add(sb.ToString());
      return cells.ToArray();
    }

    private static string PrepareTempDir()
    {
      string tempDir = Path.Combine(Path.GetTempPath(), "isida_triggerv_" + Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(tempDir);
      return tempDir;
    }

    private static void TryDeleteDir(string dir)
    {
      try { if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true); } catch { }
    }
  }
}
