using System;
using System.Collections.Generic;
using System.Text;
using Fubar.Diff.Core.Json;

namespace Fubar.Diff.Infrastructure.Json;

/// <summary>
/// A JSON parser that records the line and column of every value.
///
/// Hand-written rather than wrapping <c>System.Text.Json</c>: <c>Utf8JsonReader</c> exposes byte
/// offsets but not per-node line and column, and the semantic diff needs line ranges to highlight
/// itself inside a text editor. Reconstructing lines from byte offsets afterwards would mean scanning
/// the document a second time and handling multi-byte characters anyway, so parsing directly over
/// <c>char</c> is both simpler and exact.
///
/// **Iterative, with an explicit stack.** Recursive descent is shorter, but nesting depth is
/// attacker-controlled - a few thousand open brackets would overflow the stack and take the process
/// down, and parser abuse is in scope per SECURITY.md. An explicit stack turns that into a clean
/// error at <see cref="MaxDepth"/>.
/// </summary>
public sealed class JsonAstParser : IJsonParser
{
    /// <summary>
    /// Nesting limit. Far beyond any hand-written or machine-generated document, and low enough that
    /// the container stack cannot itself become a memory problem.
    /// </summary>
    public const int MaxDepth = 512;

    public JsonAstNode Parse(string text)
    {
        var cursor = new Cursor(text);
        var node = cursor.ParseDocument();

        return node;
    }

    public bool TryParse(string text, out JsonAstNode? node, out JsonParseException? error)
    {
        try
        {
            node = Parse(text);
            error = null;
            return true;
        }
        catch (JsonParseException ex)
        {
            node = null;
            error = ex;
            return false;
        }
    }

    /// <summary>
    /// Position tracking plus the parse itself. A helper class so the line/column bookkeeping lives in
    /// one place instead of being threaded through every method.
    /// </summary>
    private sealed class Cursor
    {
        private readonly string _text;
        private int _index;
        private int _line = 1;
        private int _column = 1;

        public Cursor(string text) => _text = text;

        private bool AtEnd => _index >= _text.Length;

        private char Current => _text[_index];

        private (int Line, int Column) Position => (_line, _column);

        private JsonParseException Error(string message) =>
            new(message, new SourceSpan(_line, _column, _line, _column));

        private SourceSpan SpanFrom((int Line, int Column) start) =>
            new(start.Line, start.Column, _line, _column);

        /// <summary>Advances one character, keeping line and column in step.</summary>
        private void Advance()
        {
            if (AtEnd)
            {
                return;
            }

            if (_text[_index] == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }

            _index++;
        }

        private void SkipWhitespace()
        {
            while (!AtEnd && Current is ' ' or '\t' or '\n' or '\r')
            {
                Advance();
            }
        }

        public JsonAstNode ParseDocument()
        {
            var node = ParseValue();

            SkipWhitespace();
            if (!AtEnd)
            {
                throw Error("unexpected trailing content after the top-level value");
            }

            return node;
        }

        /// <summary>
        /// Parses one value, descending into containers via an explicit stack.
        ///
        /// Two alternating phases: parse a value, then unwind - attaching the finished value to its
        /// parent and consuming separators until either another value is expected or the document is
        /// complete. Every loop iteration consumes at least one character, so it always terminates.
        /// </summary>
        private JsonAstNode ParseValue()
        {
            var stack = new Stack<Frame>();

            while (true)
            {
                var completed = ParseOneValueOrOpenContainer(stack);

                // The value phase returns null when it opened a container that still needs filling.
                if (completed is null)
                {
                    continue;
                }

                // Unwind phase.
                while (true)
                {
                    if (stack.Count == 0)
                    {
                        return completed;
                    }

                    var frame = stack.Peek();
                    frame.Add(completed);

                    SkipWhitespace();
                    if (AtEnd)
                    {
                        throw Error($"unexpected end of input, expected '{frame.Closer}'");
                    }

                    if (Current == ',')
                    {
                        Advance();
                        SkipWhitespace();

                        // A trailing comma before the closer is invalid JSON, but it is a common
                        // hand-editing slip and refusing to diff the file over it would be unhelpful.
                        if (!AtEnd && Current == frame.Closer)
                        {
                            Advance();
                            completed = frame.Close(SpanFrom(frame.Start));
                            stack.Pop();
                            continue;
                        }

                        if (frame.IsObject)
                        {
                            ParsePropertyName(frame);
                        }

                        break;
                    }

                    if (Current == frame.Closer)
                    {
                        Advance();
                        completed = frame.Close(SpanFrom(frame.Start));
                        stack.Pop();
                        continue;
                    }

                    throw Error($"expected ',' or '{frame.Closer}' but found '{Current}'");
                }
            }
        }

        /// <summary>
        /// Parses a scalar and returns it, or opens a container and returns null. An empty container
        /// closes immediately and is returned like a scalar.
        /// </summary>
        private JsonAstNode? ParseOneValueOrOpenContainer(Stack<Frame> stack)
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw Error("unexpected end of input, expected a value");
            }

            var start = Position;

            if (Current is not ('{' or '['))
            {
                return ParseScalar(start);
            }

            var isObject = Current == '{';
            Advance();

            if (stack.Count >= MaxDepth)
            {
                throw Error($"nesting is deeper than the {MaxDepth} level limit");
            }

            var frame = new Frame(isObject, start);

            SkipWhitespace();
            if (AtEnd)
            {
                throw Error($"unexpected end of input, expected '{frame.Closer}'");
            }

            if (Current == frame.Closer)
            {
                Advance();
                return frame.Close(SpanFrom(start));
            }

            if (isObject)
            {
                ParsePropertyName(frame);
            }

            stack.Push(frame);
            return null;
        }

        private void ParsePropertyName(Frame frame)
        {
            SkipWhitespace();
            if (AtEnd || Current != '"')
            {
                throw Error("expected a property name in double quotes");
            }

            var start = Position;
            frame.PendingName = ParseStringLiteral();
            frame.PendingNameSpan = SpanFrom(start);

            SkipWhitespace();
            if (AtEnd || Current != ':')
            {
                throw Error("expected ':' after the property name");
            }

            Advance();
        }

        private JsonAstNode ParseScalar((int Line, int Column) start)
        {
            var begin = _index;

            switch (Current)
            {
                case '"':
                {
                    var value = ParseStringLiteral();
                    return new JsonAstScalar(JsonAstKind.String, _text[begin.._index], value, SpanFrom(start));
                }

                case 't':
                    Expect("true");
                    return new JsonAstScalar(JsonAstKind.Boolean, "true", null, SpanFrom(start));

                case 'f':
                    Expect("false");
                    return new JsonAstScalar(JsonAstKind.Boolean, "false", null, SpanFrom(start));

                case 'n':
                    Expect("null");
                    return new JsonAstScalar(JsonAstKind.Null, "null", null, SpanFrom(start));

                default:
                    return ParseNumber(start);
            }
        }

        private void Expect(string literal)
        {
            foreach (var expected in literal)
            {
                if (AtEnd || Current != expected)
                {
                    throw Error($"expected '{literal}'");
                }

                Advance();
            }
        }

        private JsonAstNode ParseNumber((int Line, int Column) start)
        {
            var begin = _index;

            if (!AtEnd && Current == '-')
            {
                Advance();
            }

            var digits = 0;
            while (!AtEnd && char.IsAsciiDigit(Current))
            {
                Advance();
                digits++;
            }

            if (digits == 0)
            {
                throw Error($"expected a value but found '{(AtEnd ? "end of input" : Current.ToString())}'");
            }

            if (!AtEnd && Current == '.')
            {
                Advance();
                while (!AtEnd && char.IsAsciiDigit(Current))
                {
                    Advance();
                }
            }

            if (!AtEnd && (Current == 'e' || Current == 'E'))
            {
                Advance();
                if (!AtEnd && (Current == '+' || Current == '-'))
                {
                    Advance();
                }

                while (!AtEnd && char.IsAsciiDigit(Current))
                {
                    Advance();
                }
            }

            return new JsonAstScalar(JsonAstKind.Number, _text[begin.._index], null, SpanFrom(start));
        }

        /// <summary>Reads a quoted string, resolving escapes. Assumes the opening quote is current.</summary>
        private string ParseStringLiteral()
        {
            Advance(); // opening quote

            var builder = new StringBuilder();

            while (true)
            {
                if (AtEnd)
                {
                    throw Error("unterminated string");
                }

                var c = Current;

                if (c == '"')
                {
                    Advance();
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    Advance();
                    continue;
                }

                Advance();
                if (AtEnd)
                {
                    throw Error("unterminated escape sequence");
                }

                switch (Current)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;

                    case 'u':
                        // Consumes its own characters, including the trailing one, so it skips the
                        // shared Advance below.
                        builder.Append(ParseUnicodeEscape());
                        continue;

                    default:
                        throw Error($"unrecognised escape '\\{Current}'");
                }

                Advance();
            }
        }

        private char ParseUnicodeEscape()
        {
            Advance(); // 'u'

            var value = 0;
            for (var i = 0; i < 4; i++)
            {
                if (AtEnd || !Uri.IsHexDigit(Current))
                {
                    throw Error("a \\u escape needs four hexadecimal digits");
                }

                value = (value * 16) + Convert.ToInt32(Current.ToString(), 16);
                Advance();
            }

            return (char)value;
        }

        /// <summary>One open container while parsing.</summary>
        private sealed class Frame
        {
            private readonly List<JsonAstNode> _items = [];
            private readonly List<JsonAstProperty> _properties = [];

            public Frame(bool isObject, (int Line, int Column) start)
            {
                IsObject = isObject;
                Start = start;
            }

            public bool IsObject { get; }

            public char Closer => IsObject ? '}' : ']';

            public (int Line, int Column) Start { get; }

            public string? PendingName { get; set; }

            public SourceSpan PendingNameSpan { get; set; }

            public void Add(JsonAstNode value)
            {
                if (IsObject)
                {
                    _properties.Add(new JsonAstProperty(PendingName ?? string.Empty, value, PendingNameSpan));
                    PendingName = null;
                }
                else
                {
                    _items.Add(value);
                }
            }

            public JsonAstNode Close(SourceSpan span) => IsObject
                ? new JsonAstObject(_properties, span)
                : new JsonAstArray(_items, span);
        }
    }
}
