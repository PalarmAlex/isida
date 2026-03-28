using System;
using System.IO;
using System.Text;

namespace ISIDA.Scenarios
{
  /// <summary>
  /// Технический лог сценария оператора в ProgramData\ISIDA\Logs\ScenarioRunnerDebug.log
  /// (для сопоставления с AgentLogs.csv и отладки якоря/пульсов).
  /// </summary>
  public static class ScenarioRunnerDiagnostics
  {
    private static readonly object Sync = new object();

    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ISIDA", "Logs", "ScenarioRunnerDebug.log");

    /// <summary>Одна строка UTF-8 с меткой времени.</summary>
    public static void Write(string message)
    {
      try
      {
        var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)
            + " " + (message ?? "") + Environment.NewLine;
        lock (Sync)
        {
          var dir = Path.GetDirectoryName(LogPath);
          if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
          File.AppendAllText(LogPath, line, Encoding.UTF8);
        }
      }
      catch
      {
        // не мешаем прогону сценария
      }
    }
  }
}
