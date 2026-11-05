using ISIDA.Common;
using System;
using Xunit;

namespace Isida.Tests
{
  /// <summary>
  /// Тесты для канала сокращённого лога <see cref="ShortLogChannel"/>.
  /// Граница сессии = цикл «вкл/выкл пульсации», отдельного флага включения нет.
  /// </summary>
  public class ShortLogChannelTests
  {
    /// <summary>
    /// После <see cref="ShortLogChannel.Reset"/> канал «чист»: сессия не активна, токенов нет.
    /// </summary>
    [Fact]
    public void Reset_CleansAllState()
    {
      ShortLogChannel.StartSession();
      ShortLogChannel.Write("test");

      ShortLogChannel.Reset();

      Assert.Equal(0, ShortLogChannel.CurrentSessionId);
      Assert.Empty(ShortLogChannel.GetCurrentSessionTokens());
    }

    /// <summary>
    /// Запись вне активной сессии (пульсация не запущена) молча игнорируется.
    /// </summary>
    [Fact]
    public void Write_BeforeSession_IgnoresToken()
    {
      ShortLogChannel.Reset();

      ShortLogChannel.Write("outside");

      Assert.Empty(ShortLogChannel.GetCurrentSessionTokens());
    }

    /// <summary>
    /// Запись в активной сессии создаёт токен с авто-временем и номером пульса.
    /// </summary>
    [Fact]
    public void Write_InActiveSession_CreatesTokenWithTimeAndPulse()
    {
      ShortLogChannel.Reset();
      int sid = ShortLogChannel.StartSession();

      ShortLogChannel.Write("hello");

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal(sid, tokens[0].SessionId);
      Assert.Equal("hello", tokens[0].Text);
      // Время проставлено движком (не пустое).
      Assert.False(string.IsNullOrEmpty(tokens[0].Time.ToString(@"hh\:mm\:ss")));
    }

    /// <summary>
    /// Форматированный <c>Write</c> подставляет аргументы.
    /// </summary>
    [Fact]
    public void Write_WithFormat_FormatsCorrectly()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("ID {0}, {1} (вес {2})", 42, "DXF", 3);

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("ID 42, DXF (вес 3)", tokens[0].Text);
    }

    /// <summary>
    /// Один вызов форматированного <c>Write</c> = один токен.
    /// </summary>
    [Fact]
    public void Write_MultipleCalls_OneTokenEach()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("a");
      ShortLogChannel.Write("b");
      ShortLogChannel.Write("c");

      Assert.Equal(3, ShortLogChannel.GetCurrentSessionTokens().Length);
    }

    /// <summary>
    /// <c>WriteBlock</c> формирует блок «заголовок + отступные строки» одним токеном.
    /// </summary>
    [Fact]
    public void WriteBlock_FormatsHeaderAndIndentedLines()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.WriteBlock("СТИМУЛ:", "Узел дерева автоматизмов: ID 15");
      ShortLogChannel.WriteBlock("ОТВЕТ:", "Автоматизм: ID 3, Экспорт DXF (вес 2)");

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Equal(2, tokens.Length);
      Assert.Equal("СТИМУЛ:\n  Узел дерева автоматизмов: ID 15", tokens[0].Text);
      Assert.Equal("ОТВЕТ:\n  Автоматизм: ID 3, Экспорт DXF (вес 2)", tokens[1].Text);
    }

    /// <summary>
    /// <c>WriteBlock</c> без строк даёт только заголовок.
    /// </summary>
    [Fact]
    public void WriteBlock_NoLines_HeaderOnly()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.WriteBlock("HEADER:");

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("HEADER:", tokens[0].Text);
    }

    /// <summary>
    /// Новая <c>StartSession</c> закрывает незавершённую предыдущую и выдаёт новый идентификатор.
    /// </summary>
    [Fact]
    public void StartSession_WithExistingSession_ClosesPreviousAndIncrementsId()
    {
      ShortLogChannel.Reset();

      int closedId = 0;
      ShortLogToken[] closedTokens = null;
      ShortLogChannel.OnSessionCompleted += (id, tokens) => { closedId = id; closedTokens = tokens; };

      int first = ShortLogChannel.StartSession();
      ShortLogChannel.Write("from-first");

      int second = ShortLogChannel.StartSession();

      // Первая сессия закрыта событием со своим токеном.
      Assert.Equal(first, closedId);
      Assert.NotNull(closedTokens);
      Assert.Single(closedTokens);
      Assert.Equal("from-first", closedTokens[0].Text);

      // Активна вторая, её токены пусты.
      Assert.Equal(second, ShortLogChannel.CurrentSessionId);
      Assert.True(second > first);
      Assert.Empty(ShortLogChannel.GetCurrentSessionTokens());
    }

    /// <summary>
    /// <c>StopSession</c> возвращает токены в порядке записи и деактивирует сессию.
    /// </summary>
    [Fact]
    public void StopSession_ReturnsTokensAndResets()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("a");
      ShortLogChannel.Write("b");

      var tokens = ShortLogChannel.StopSession();

      Assert.Equal(2, tokens.Length);
      Assert.Equal("a", tokens[0].Text);
      Assert.Equal("b", tokens[1].Text);
      Assert.Equal(0, ShortLogChannel.CurrentSessionId);
    }

    /// <summary>
    /// <c>StopSession</c> без активной сессии возвращает пустой массив.
    /// </summary>
    [Fact]
    public void StopSession_WithoutSession_ReturnsEmpty()
    {
      ShortLogChannel.Reset();

      Assert.Empty(ShortLogChannel.StopSession());
    }

    /// <summary>
    /// Событие <c>OnToken</c> получает каждый записанный токен.
    /// </summary>
    [Fact]
    public void OnToken_FiresOnWrite()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogToken captured = null;
      ShortLogChannel.OnToken += t => captured = t;

      ShortLogChannel.Write("event test");

      Assert.NotNull(captured);
      Assert.Equal("event test", captured.Text);
    }

    /// <summary>
    /// Событие <c>OnSessionStarted</c> получает новый идентификатор сессии.
    /// </summary>
    [Fact]
    public void OnSessionStarted_FiresWithNewId()
    {
      ShortLogChannel.Reset();

      int capturedId = -1;
      ShortLogChannel.OnSessionStarted += id => capturedId = id;

      int sid = ShortLogChannel.StartSession();

      Assert.Equal(sid, capturedId);
      Assert.Equal(sid, ShortLogChannel.CurrentSessionId);
    }

    /// <summary>
    /// Событие <c>OnSessionCompleted</c> получает все токены завершённой сессии.
    /// </summary>
    [Fact]
    public void OnSessionCompleted_FiresWithAllTokens()
    {
      ShortLogChannel.Reset();
      int sid = ShortLogChannel.StartSession();

      ShortLogChannel.Write("tok1");
      ShortLogChannel.Write("tok2");

      int capturedId = 0;
      ShortLogToken[] capturedTokens = null;
      ShortLogChannel.OnSessionCompleted += (id, tokens) => { capturedId = id; capturedTokens = tokens; };

      ShortLogChannel.StopSession();

      Assert.Equal(sid, capturedId);
      Assert.NotNull(capturedTokens);
      Assert.Equal(2, capturedTokens.Length);
      Assert.Equal("tok1", capturedTokens[0].Text);
      Assert.Equal("tok2", capturedTokens[1].Text);
    }

    /// <summary>
    /// Токен поддерживает многострочный текст (переносы сохраняются).
    /// </summary>
    [Fact]
    public void Write_MultilineText_Preserved()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("строка 1\nстрока 2");

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("строка 1\nстрока 2", tokens[0].Text);
    }

    /// <summary>
    /// <c>ToString</c> токена даёт номер пульса и текст блока.
    /// </summary>
    [Fact]
    public void Token_ToString_HasPulseLineAndText()
    {
      var token = new ShortLogToken(new TimeSpan(13, 16, 22), "test text", 1, 42);

      string s = token.ToString();

      Assert.Contains("№ пульса: 042", s);
      Assert.Contains("test text", s);
    }

    /// <summary>
    /// Исключение подписчика <c>OnToken</c> не должно «выстрелить» наружу (сбой UI не роняет пульс).
    /// </summary>
    [Fact]
    public void OnToken_SubscriberThrows_WriteDoesNotThrow()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.OnToken += t => throw new InvalidOperationException("subscriber fail");

      var ex = Record.Exception(() => ShortLogChannel.Write("safe"));

      Assert.Null(ex);
      // Токен всё равно записан в сессию, несмотря на сбой подписчика.
      Assert.Single(ShortLogChannel.GetCurrentSessionTokens());
    }
  }
}