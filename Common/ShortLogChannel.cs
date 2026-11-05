using System;
using System.Collections.Generic;
using System.Text;

namespace ISIDA.Common
{
  /// <summary>
  /// Токен сокращённого лога — результат одного обращения к
  /// <see cref="M:ISIDA.Common.ShortLogChannel.Write(System.String)"/>
  /// (или <see cref="M:ISIDA.Common.ShortLogChannel.WriteBlock(System.String,System.String[])"/>).
  /// Номер пульса добавляется движком автоматически, клиент его не формирует.
  /// </summary>
  public sealed class ShortLogToken
  {
    /// <summary>Отступ строк блока внутри токена (две пробела, как в блоках «СТИМУЛ/ОТВЕТ»).</summary>
    public const string LineIndent = "  ";

    /// <summary>Время создания токена движком.</summary>
    public TimeSpan Time { get; }

    /// <summary>
    /// Текст сообщения. Может содержать переносы строк — токен остаётся единым блоком.
    /// </summary>
    public string Text { get; }

    /// <summary>Идентификатор сессии, в которой создан токен.</summary>
    public int SessionId { get; }

    /// <summary>Номер глобального пульса на момент создания токена (0 — пульсация ещё не шла).</summary>
    public int Pulse { get; }

    /// <summary>
    /// Создаёт токен сокращённого лога.
    /// </summary>
    /// <param name="time">Время создания, проставленное движком.</param>
    /// <param name="text">Текст сообщения.</param>
    /// <param name="sessionId">Идентификатор сессии.</param>
    /// <param name="pulse">Номер глобального пульса.</param>
    public ShortLogToken(TimeSpan time, string text, int sessionId, int pulse)
    {
      Time = time;
      Text = text ?? string.Empty;
      SessionId = sessionId;
      Pulse = pulse;
    }

    /// <summary>
    /// Готовое для отображения представление токена: номер пульса и текст блока.
    /// </summary>
    /// <returns>Текст вида «№ пульса: ххх» + перевод строки + содержимое токена.</returns>
    public override string ToString()
    {
      return $"№ пульса: {Pulse:D3}\n" + Text;
    }
  }

  /// <summary>
  /// Канал сокращённого (семантического) лога движка: один вызов записи — один токен
  /// с автоматически проставленным временем <c>hh:mm:ss</c>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Сокращённый лог формируется <b>в движке</b>: места кода, которым есть что сказать
  /// оператору, вызывают <see cref="M:ISIDA.Common.ShortLogChannel.Write(System.String)"/>
  /// напрямую — по аналогии с <see cref="Logger"/>, но без класса/метода/номера строки.
  /// Клиент (AIStudio, Velum) только читает: подписывается на <see cref="OnToken"/> и
  /// получает токены в реальном времени.
  /// </para>
  /// <para>
  /// Границы сессии заданы пульсацией: <see cref="StartSession"/> вызывается при включении
  /// пульсации (<see cref="GlobalTimer.Start"/>), <see cref="StopSession"/> — при выключении
  /// (<see cref="GlobalTimer.Stop"/> и аварийные остановки). Одна сессия = один цикл «вкл/выкл
  /// пульсации»; при включении клиентидёт новый <c>SessionId</c>, и клиент очищает свой вывод.
  /// </para>
  /// <para>
  /// Потокобезопасность: состояние защищено <c>lock</c>; события подписчикам всегда отправляются
  /// <b>вне</b> блокировки, чтобы обработчик, вызвавший запись, не мог образовать взаимную блокировку.
  /// Исключения подписчиков гасятся — сбой UI не должен останавливать пульс.
  /// </para>
  /// </remarks>
  public static class ShortLogChannel
  {
    #region События

    /// <summary>
    /// Новый токен сокращённого лога. Вызывается на потоке автора записи (обычно поток пульса).
    /// </summary>
    public static event Action<ShortLogToken> OnToken;

    /// <summary>
    /// Начало сессии: параметр — новый <c>SessionId</c>. Клиенту следует очистить свой вывод.
    /// </summary>
    public static event Action<int> OnSessionStarted;

    /// <summary>
    /// Завершение сессии: идентификатор и все токены сессии в порядке записи.
    /// </summary>
    public static event Action<int, ShortLogToken[]> OnSessionCompleted;

    #endregion

    #region Состояние

    /// <summary>Блокировка состояния канала (счётчик сессий, текущая сессия, её токены).</summary>
    private static readonly object _lock = new object();

    /// <summary>Счётчик сессий; идентификаторы монотонно возрастают от 1.</summary>
    private static int _sessionIdCounter;

    /// <summary>Идентификатор активной сессии; 0 — сессия не активна, запись игнорируется.</summary>
    private static int _currentSessionId;

    /// <summary>Токены активной сессии в порядке записи.</summary>
    private static List<ShortLogToken> _currentSessionTokens = new List<ShortLogToken>();

    /// <summary>
    /// Буфер записей текущего пульса: все <see cref="Write(string)"/>/<see cref="WriteBlock"/>
    /// накапливаются здесь и объединяются в один токен на пульс при <see cref="FlushPulse"/>
    /// (по образцу буферизации <see cref="ResearchLogger"/>). Каждая запись хранит свой номер
    /// пульса, поэтому при запаздывании сброса подряд идущие записи одного пульса всё равно
    /// группируются корректно, а соседние пульсы не склеиваются.
    /// </summary>
    private static List<(int Pulse, string Text)> _pulseBuffer = new List<(int Pulse, string Text)>();

    #endregion

    #region Запись

    /// <summary>
    /// Записывает один токен сокращённого лога. Время проставляется движком.
    /// </summary>
    /// <param name="text">Текст сообщения; переносы строк допустимы — всё уйдёт одним блоком.</param>
    /// <remarks>
    /// Вызов вне активной сессии (пульсация не запущена) молча игнорируется: сокращённый лог
    /// живёт только внутри цикла «вкл/выкл пульсации».
    /// </remarks>
    /// <example>
    /// <code>
    /// ShortLogChannel.Write("Путь M (стадия 2)");
    /// </code>
    /// </example>
    public static void Write(string text)
    {
      Emit(text ?? string.Empty);
    }

    /// <summary>
    /// Записывает один токен с форматированием текста.
    /// </summary>
    /// <param name="format">Форматная строка (<see cref="string.Format(string, object[])"/>).</param>
    /// <param name="args">Аргументы форматирования.</param>
    /// <example>
    /// <code>
    /// ShortLogChannel.Write("Автоматизм: ID {0}, {1} (вес {2})", id, name, weight);
    /// </code>
    /// </example>
    public static void Write(string format, params object[] args)
    {
      if (format == null)
        return;

      string text;
      try
      {
        text = (args == null || args.Length == 0) ? format : string.Format(format, args);
      }
      catch (FormatException ex)
      {
        // Ошибка форматирования — дефект места записи, а не причина ронять пульс.
        Logger.Warning($"ShortLogChannel.Write: неверный формат «{format}»: {ex.Message}");
        return;
      }

      Emit(text);
    }

    /// <summary>
    /// Записывает блок «заголовок + строки» одним токеном: строки печатаются с отступом под заголовком.
    /// </summary>
    /// <param name="title">Заголовок блока (например «СТИМУЛ:»).</param>
    /// <param name="lines">Строки содержимого; пустые строки пропускаются.</param>
    /// <remarks>
    /// Нужен, чтобы семантически единый эпизод (стимул и ответ на него) не разъезжался на несколько
    /// токенов: вызов всегда даёт ровно один токен с одним временем.
    /// </remarks>
    /// <example>
    /// <code>
    /// ShortLogChannel.WriteBlock("СТИМУЛ:", "Узел дерева автоматизмов: ID 15");
    /// ShortLogChannel.WriteBlock("ОТВЕТ:", "Автоматизм: ID 3, Экспорт DXF (вес 2)");
    /// </code>
    /// </example>
    public static void WriteBlock(string title, params string[] lines)
    {
      var sb = new StringBuilder();
      if (!string.IsNullOrWhiteSpace(title))
        sb.Append(title.Trim());

      if (lines != null)
      {
        foreach (var line in lines)
        {
          if (string.IsNullOrEmpty(line))
            continue;
          if (sb.Length > 0)
            sb.Append('\n');
          sb.Append(ShortLogToken.LineIndent).Append(line.Trim());
        }
      }

      Emit(sb.ToString());
    }

    /// <summary>
    /// Кладёт текст в буфер текущего пульса под блокировкой; рассылка откладывается до
    /// <see cref="FlushPulse"/> (или завершения сессии), чтобы все события одного пульса
    /// ушли оператору одним токеном-блоком.
    /// </summary>
    /// <param name="text">Готовый текст записи.</param>
    private static void Emit(string text)
    {
      lock (_lock)
      {
        if (_currentSessionId == 0)
          return;

        _pulseBuffer.Add((GlobalTimer.GlobalPulsCount, text ?? string.Empty));
      }
    }

    /// <summary>
    /// Сбрасывает буфер пульса: группирует подряд идущие записи с одинаковым номером пульса
    /// в один токен, дописывает токены в сессию и рассылает их подписчикам вне блокировки.
    /// </summary>
    /// <remarks>
    /// Вызывается в конце обработки пульса (<see cref="GlobalTimer"/> в <c>finally</c>
    /// <c>ProcessAgentPulse</c>), а также при завершении/старте сессии, чтобы не потерять хвост.
    /// Пустой буфер — ничего не делает (на пульсе без событий токен не создаётся).
    /// </remarks>
    public static void FlushPulse()
    {
      List<ShortLogToken> created;

      lock (_lock)
        created = FlushBufferLocked();

      if (created == null)
        return;

      foreach (var token in created)
        RaiseToken(token);
    }

    /// <summary>
    /// Преобразует содержимое буфера в токены (группировка по пульсу), дописывает их в сессию.
    /// Вызывать только под <c>lock (_lock)</c>.
    /// </summary>
    /// <returns>Созданные токены для рассылки; <c>null</c>, если буфер пуст или сессии нет
    /// (в этом случае буфер просто очищается).</returns>
    private static List<ShortLogToken> FlushBufferLocked()
    {
      if (_pulseBuffer.Count == 0)
        return null;

      if (_currentSessionId == 0)
      {
        _pulseBuffer.Clear();
        return null;
      }

      var created = new List<ShortLogToken>();
      int i = 0;
      while (i < _pulseBuffer.Count)
      {
        int pulse = _pulseBuffer[i].Pulse;
        var sb = new StringBuilder();
        while (i < _pulseBuffer.Count && _pulseBuffer[i].Pulse == pulse)
        {
          if (sb.Length > 0)
            sb.Append('\n');
          sb.Append(_pulseBuffer[i].Text);
          i++;
        }

        var token = new ShortLogToken(
            DateTime.Now.TimeOfDay,
            sb.ToString(),
            _currentSessionId,
            pulse);

        _currentSessionTokens.Add(token);
        created.Add(token);
      }

      _pulseBuffer.Clear();
      return created;
    }

    #endregion

    #region Сессии

    /// <summary>
    /// Начинает новую сессию сокращённого лога (включение пульсации).
    /// </summary>
    /// <returns>Идентификатор новой сессии.</returns>
    /// <remarks>
    /// Незавершённая предыдущая сессия (например после аварийной остановки пульса) закрывается
    /// первой, чтобы её токены не смешались с новой.
    /// </remarks>
    public static int StartSession()
    {
      int previousSessionId = 0;
      ShortLogToken[] previousTokens = null;
      int newSessionId;

      // Буфер на входе обычно пуст: все пути остановки пульса (Stop, SafeStopWithAgentDeath,
      // SafeStopWithError) зовут StopSession(), сбрасывающий буфер, а Reset() его очищает.
      // Если же хвост остался (например, повторный Start без Stop), он группируется и уходит
      // в закрываемую сессию через OnSessionCompleted — но БЕЗ live-рассылки OnToken:
      // StartSession вызывается из GlobalTimer.Start() под lock (_timerLock), а подписчик UI
      // может войти в тот же _timerLock обратно — прямая дорога к взаимной блокировке.
      lock (_lock)
      {
        FlushBufferLocked();
        if (_currentSessionId != 0)
        {
          previousSessionId = _currentSessionId;
          previousTokens = _currentSessionTokens.ToArray();
        }

        newSessionId = ++_sessionIdCounter;
        _currentSessionId = newSessionId;
        _currentSessionTokens = new List<ShortLogToken>();
      }

      if (previousTokens != null)
        RaiseSessionCompleted(previousSessionId, previousTokens);

      RaiseSessionStarted(newSessionId);
      return newSessionId;
    }

    /// <summary>
    /// Завершает активную сессию (выключение пульсации) и возвращает её токены.
    /// </summary>
    /// <returns>Токены сессии в порядке записи; пустой массив, если активной сессии не было.</returns>
    public static ShortLogToken[] StopSession()
    {
      int sessionId;
      ShortLogToken[] tokens;

      // Хвост последнего пульса (события, написанные после последнего FlushPulse из пульса)
      // обязан попасть в завершаемую сессию, иначе оператор их не увидит.
      FlushPulse();

      lock (_lock)
      {
        if (_currentSessionId == 0)
          return new ShortLogToken[0];

        sessionId = _currentSessionId;
        tokens = _currentSessionTokens.ToArray();
        _currentSessionTokens = new List<ShortLogToken>();
        _currentSessionId = 0;
      }

      RaiseSessionCompleted(sessionId, tokens);
      return tokens;
    }

    /// <summary>
    /// Снимок токенов активной сессии (для отладочных выводов и тестов, без завершения сессии).
    /// </summary>
    /// <returns>Токены текущей сессии; пустой массив, если активной сессии нет.</returns>
    public static ShortLogToken[] GetCurrentSessionTokens()
    {
      lock (_lock)
      {
        if (_currentSessionId == 0)
          return new ShortLogToken[0];
        return _currentSessionTokens.ToArray();
      }
    }

    /// <summary>
    /// Идентификатор активной сессии; 0 — сессия не активна.
    /// </summary>
    public static int CurrentSessionId
    {
      get
      {
        lock (_lock)
          return _currentSessionId;
      }
    }

    /// <summary>
    /// Полностью сбрасывает канал: закрывает сессию без рассылки и снимает подписки клиентов.
    /// </summary>
    /// <remarks>
    /// Вызывается при выгрузке движка (<see cref="GlobalTimer.ClearSystems"/>), вместе со сбросом
    /// остальных статических подписок: пересозданный контекст требует повторной подписки клиента.
    /// </remarks>
    public static void Reset()
    {
      // Выгрузка движка: сессия закрывается без рассылки (подписчики вот-вот станут невалидны),
      // поэтому буфер пульса просто отбрасывается, а не сбрасывается через FlushPulse.
      lock (_lock)
      {
        OnToken = null;
        OnSessionStarted = null;
        OnSessionCompleted = null;
        _currentSessionId = 0;
        _currentSessionTokens = new List<ShortLogToken>();
        _pulseBuffer.Clear();
        _sessionIdCounter = 0;
      }
    }

    #endregion

    #region Рассылка событий

    /// <summary>
    /// Рассылает токен подписчикам, гася их исключения (сбой UI не должен останавливать пульс).
    /// </summary>
    /// <param name="token">Токен для рассылки.</param>
    private static void RaiseToken(ShortLogToken token)
    {
      try
      {
        OnToken?.Invoke(token);
      }
      catch (Exception ex)
      {
        Logger.Warning($"ShortLogChannel.OnToken: {ex.Message}");
      }
    }

    /// <summary>
    /// Рассылает событие начала сессии.
    /// </summary>
    /// <param name="sessionId">Идентификатор начатой сессии.</param>
    private static void RaiseSessionStarted(int sessionId)
    {
      try
      {
        OnSessionStarted?.Invoke(sessionId);
      }
      catch (Exception ex)
      {
        Logger.Warning($"ShortLogChannel.OnSessionStarted: {ex.Message}");
      }
    }

    /// <summary>
    /// Рассылает событие завершения сессии.
    /// </summary>
    /// <param name="sessionId">Идентификатор завершённой сессии.</param>
    /// <param name="tokens">Токены завершённой сессии.</param>
    private static void RaiseSessionCompleted(int sessionId, ShortLogToken[] tokens)
    {
      try
      {
        OnSessionCompleted?.Invoke(sessionId, tokens);
      }
      catch (Exception ex)
      {
        Logger.Warning($"ShortLogChannel.OnSessionCompleted: {ex.Message}");
      }
    }

    #endregion
  }
}