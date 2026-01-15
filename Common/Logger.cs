using ISIDA.Common;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace ISIDA.Common
{
  /// <summary>
  /// Уровни логирования
  /// </summary>
  public enum LogLevel
  {
    /// <summary>Отладочная информация (только в отладочной сборке)</summary>
    Debug,
    /// <summary>Информационное сообщение</summary>
    Info,
    /// <summary>Предупреждение</summary>
    Warning,
    /// <summary>Ошибка</summary>
    Error
  }

  /// <summary>
  /// Статический класс для централизованного логирования с автоматическим определением контекста
  /// </summary>
  /// <remarks>
  /// Автоматически определяет класс, метод и строку кода, откуда был вызван метод логирования.
  /// </remarks>
  public static class Logger
  {
    private static readonly object _lock = new object();

    /// <summary>
    /// Основной метод логирования
    /// </summary>
    /// <param name="level">Уровень логирования</param>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <example>
    /// <code>
    /// Logger.Log(LogLevel.Info, "Сообщение");
    /// </code>
    /// </example>
    public static void Log(LogLevel level, string message,
                          [CallerFilePath] string filePath = "",
                          [CallerMemberName] string memberName = "",
                          [CallerLineNumber] int lineNumber = 0)
    {
      string className = Path.GetFileNameWithoutExtension(filePath);
      string timestamp = DateTime.Now.ToString("HH:mm:ss.fff");

      string logMessage = $"{timestamp} [{level}] [{className}.{memberName}:{lineNumber}] {message}";

      lock (_lock)
      {
        System.Diagnostics.Debug.WriteLine(logMessage);

        if (level == LogLevel.Error)
        {
          FileValidator.LogError(logMessage);
        }
      }
    }

    /// <summary>
    /// Логирование информационного сообщения
    /// </summary>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <example>
    /// <code>
    /// Logger.Info("Метод выполнен успешно");
    /// </code>
    /// </example>
    public static void Info(string message,
                           [CallerFilePath] string filePath = "",
                           [CallerMemberName] string memberName = "",
                           [CallerLineNumber] int lineNumber = 0)
    {
      Log(LogLevel.Info, message, filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Логирование предупреждения
    /// </summary>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <example>
    /// <code>
    /// Logger.Warning("Необычное поведение системы");
    /// </code>
    /// </example>
    public static void Warning(string message,
                              [CallerFilePath] string filePath = "",
                              [CallerMemberName] string memberName = "",
                              [CallerLineNumber] int lineNumber = 0)
    {
      Log(LogLevel.Warning, message, filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Логирование ошибки
    /// </summary>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <remarks>
    /// В дополнение к выводу в Debug, ошибки также записываются через FileValidator.LogError()
    /// </remarks>
    /// <example>
    /// <code>
    /// Logger.Error("Произошла критическая ошибка");
    /// </code>
    /// </example>
    public static void Error(string message,
                            [CallerFilePath] string filePath = "",
                            [CallerMemberName] string memberName = "",
                            [CallerLineNumber] int lineNumber = 0)
    {
      Log(LogLevel.Error, message, filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Логирование отладочной информации (только в отладочной сборке)
    /// </summary>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <remarks>
    /// Метод активен только при компиляции в конфигурации DEBUG.
    /// В релизных сборках вызовы этого метода игнорируются.
    /// </remarks>
    /// <example>
    /// <code>
    /// Logger.DebugLog("Значение переменной: {value}");
    /// </code>
    /// </example>
    public static void DebugLog(string message,
                              [CallerFilePath] string filePath = "",
                              [CallerMemberName] string memberName = "",
                              [CallerLineNumber] int lineNumber = 0)
    {
#if DEBUG
      Log(LogLevel.Debug, message, filePath, memberName, lineNumber);
#endif
    }

    /// <summary>
    /// Логирование ошибки с исключением
    /// </summary>
    /// <param name="message">Сообщение для логирования</param>
    /// <param name="exception">Исключение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <example>
    /// <code>
    /// try { ... }
    /// catch (Exception ex) {
    ///     Logger.Error("Ошибка обработки", ex);
    /// }
    /// </code>
    /// </example>
    public static void Error(string message, Exception exception,
                            [CallerFilePath] string filePath = "",
                            [CallerMemberName] string memberName = "",
                            [CallerLineNumber] int lineNumber = 0)
    {
      string fullMessage = $"{message}: {exception?.GetType().Name} - {exception?.Message}";
      Error(fullMessage, filePath, memberName, lineNumber);
    }

    /// <summary>
    /// Логирование исключения (упрощенный метод)
    /// </summary>
    /// <param name="exception">Исключение для логирования</param>
    /// <param name="filePath">Путь к файлу (заполняется автоматически компилятором)</param>
    /// <param name="memberName">Имя метода (заполняется автоматически компилятором)</param>
    /// <param name="lineNumber">Номер строки (заполняется автоматически компилятором)</param>
    /// <example>
    /// <code>
    /// try { ... }
    /// catch (Exception ex) {
    ///     Logger.Error(ex);
    /// }
    /// </code>
    /// </example>
    public static void Error(Exception exception,
                            [CallerFilePath] string filePath = "",
                            [CallerMemberName] string memberName = "",
                            [CallerLineNumber] int lineNumber = 0)
    {
      string message = $"Исключение: {exception?.GetType().Name} - {exception?.Message}";
      Error(message, filePath, memberName, lineNumber);
    }
  }
}