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
      ShortLogChannel.FlushPulse();

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
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("ID 42, DXF (вес 3)", tokens[0].Text);
    }

    /// <summary>
    /// Несколько записей на одном пульсе объединяются в один токен.
    /// </summary>
    [Fact]
    public void Write_MultipleCallsOnSamePulse_GroupedIntoOneToken()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("a");
      ShortLogChannel.Write("b");
      ShortLogChannel.Write("c");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("a\nb\nc", tokens[0].Text);
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
      ShortLogChannel.FlushPulse();
      ShortLogChannel.WriteBlock("ОТВЕТ:", "Автоматизм: ID 3, Экспорт DXF (вес 2)");
      ShortLogChannel.FlushPulse();

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
      ShortLogChannel.FlushPulse();

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

      // Первая сессия закрыта событием со своим токеном (FlushPulse вызван в StartSession).
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
    /// На одном пульсе все записи группируются в один токен.
    /// </summary>
    [Fact]
    public void StopSession_ReturnsTokensAndResets()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("a");
      ShortLogChannel.Write("b");

      var tokens = ShortLogChannel.StopSession();

      // Все записи на одном пульсе → один объединённый токен.
      Assert.Single(tokens);
      Assert.Equal("a\nb", tokens[0].Text);
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
    /// Событие <c>OnToken</c> получает каждый токен после <c>FlushPulse</c>.
    /// </summary>
    [Fact]
    public void OnToken_FiresOnWrite()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogToken captured = null;
      ShortLogChannel.OnToken += t => captured = t;

      ShortLogChannel.Write("event test");
      ShortLogChannel.FlushPulse();

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
      // Все записи на одном пульсе → один объединённый токен.
      Assert.Single(capturedTokens);
      Assert.Equal("tok1\ntok2", capturedTokens[0].Text);
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
      ShortLogChannel.FlushPulse();

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

      var ex = Record.Exception(() => {
        ShortLogChannel.Write("safe");
        ShortLogChannel.FlushPulse();
      });

      Assert.Null(ex);
      // Токен всё равно записан в сессию, несмотря на сбой подписчика.
      Assert.Single(ShortLogChannel.GetCurrentSessionTokens());
    }

    /// <summary>
    /// Идентичный предыдущему блок на следующем сбросе НЕ создаёт новый токен, а увеличивает
    /// <see cref="ShortLogToken.RepeatCount"/> последнего (сжатие повторов).
    /// </summary>
    [Fact]
    public void FlushPulse_IdenticalBlockOnNextFlush_CollapsedIntoRepeat()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.WriteBlock("Б/У РЕФЛЕКС:", "ID 1, действий=1");
      ShortLogChannel.FlushPulse();
      ShortLogChannel.WriteBlock("Б/У РЕФЛЕКС:", "ID 1, действий=1");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal(2, tokens[0].RepeatCount);
    }

    /// <summary>
    /// Отличный от последнего блок остаётся отдельным токеном (сжатие трогает только повторы).
    /// </summary>
    [Fact]
    public void FlushPulse_DifferentBlocks_NotCollapsed()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("a");
      ShortLogChannel.FlushPulse();
      ShortLogChannel.Write("b");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Equal(2, tokens.Length);
      Assert.Equal(1, tokens[0].RepeatCount);
      Assert.Equal(1, tokens[1].RepeatCount);
    }

    /// <summary>
    /// Блок, разделённый иным токеном (A, B, A), НЕ схлопывается в повтор: сравнение идёт только
    /// с последним токеном сессии, иначе терялась бы не-подряд идущая последовательность.
    /// </summary>
    [Fact]
    public void FlushPulse_SameBlockAfterDifferentBlock_NotCollapsed()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("A");
      ShortLogChannel.FlushPulse();
      ShortLogChannel.Write("B");
      ShortLogChannel.FlushPulse();
      ShortLogChannel.Write("A");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Equal(3, tokens.Length);
      Assert.All(tokens, t => Assert.Equal(1, t.RepeatCount));
    }

    /// <summary>
    /// При повторе рассылается <see cref="ShortLogChannel.OnTokenUpdated"/> (тот же объект),
    /// а не новый <see cref="ShortLogChannel.OnToken"/>.
    /// </summary>
    [Fact]
    public void FlushPulse_Repeat_FiresOnTokenUpdated()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      int createdCount = 0;
      ShortLogToken updated = null;
      ShortLogChannel.OnToken += t => createdCount++;
      ShortLogChannel.OnTokenUpdated += t => updated = t;

      ShortLogChannel.Write("повтор");
      ShortLogChannel.FlushPulse();
      ShortLogChannel.Write("повтор");
      ShortLogChannel.FlushPulse();

      Assert.Equal(1, createdCount);
      Assert.NotNull(updated);
      Assert.Equal(2, updated.RepeatCount);
    }

    /// <summary>
    /// <c>ToString</c> токена с повторами добавляет «×N» к номеру пульса.
    /// </summary>
    [Fact]
    public void Token_ToString_ShowsRepeatCount()
    {
      var token = new ShortLogToken(new TimeSpan(13, 16, 22), "блок", 1, 5);
      token.MarkRepeated(new TimeSpan(13, 17, 0), 18);

      string s = token.ToString();

      Assert.Contains("№ пульса: 018 ×2", s);
      Assert.Contains("блок", s);
    }

    /// <summary>
    /// <c>Write(text, join)</c> дописывает запись в конец ПРЕДЫДУЩЕЙ того же пульса через
    /// заданный разделитель вместо перевода строки (разделитель задаёт, как ТЕКУЩАЯ запись
    /// соединяется с предыдущей; <c>null</c> — с новой строки).
    /// </summary>
    [Fact]
    public void Write_WithJoinSeparator_AppendsToPreviousLine()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      // Многострочный блок «АВТОМАТИЗМ СОЗДАН:» — одна запись (с переводом строки внутри).
      ShortLogChannel.WriteBlock("АВТОМАТИЗМ СОЗДАН:", "ID 2, ActionsImage=5, полезность=0, ветка=14");
      // Дописать к концу предыдущей записи через "; " → целевой формат оператора.
      ShortLogChannel.Write("Эхо - Автоматизм", "; ");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal(
          "АВТОМАТИЗМ СОЗДАН:\n  ID 2, ActionsImage=5, полезность=0, ветка=14; Эхо - Автоматизм",
          tokens[0].Text);
    }

    /// <summary>
    /// Разделитель игнорируется, если запись первая на пульсе (нет предыдущей).
    /// </summary>
    [Fact]
    public void Write_WithJoinSeparator_FirstRecord_IsNormal()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("первая запись", "; ");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Single(tokens);
      Assert.Equal("первая запись", tokens[0].Text);
    }

    /// <summary>
    /// Разделитель применяется только в пределах одного пульса; разные пульсы — с новой строки.
    /// </summary>
    [Fact]
    public void Write_WithJoinSeparator_CrossPulse_Resets()
    {
      ShortLogChannel.Reset();
      ShortLogChannel.StartSession();

      ShortLogChannel.Write("pulse A");
      ShortLogChannel.FlushPulse();

      ShortLogChannel.Write("pulse B", "; ");
      ShortLogChannel.FlushPulse();

      var tokens = ShortLogChannel.GetCurrentSessionTokens();
      Assert.Equal(2, tokens.Length);
      Assert.Equal("pulse A", tokens[0].Text);
      Assert.Equal("pulse B", tokens[1].Text);
    }
  }
}
