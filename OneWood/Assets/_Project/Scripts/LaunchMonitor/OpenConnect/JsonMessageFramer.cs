using System.Collections.Generic;
using System.Text;

namespace OneWood.LaunchMonitor.OpenConnect
{
    /// <summary>
    /// Splits a TCP text stream into complete top-level JSON objects. Open Connect
    /// clients may send objects newline-delimited, back-to-back, or split across
    /// packets, so framing is done by tracking brace depth (ignoring braces inside strings).
    /// </summary>
    public sealed class JsonMessageFramer
    {
        public const int DefaultMaxMessageLength = 64 * 1024;

        readonly int _maxMessageLength;
        readonly StringBuilder _current = new StringBuilder();
        readonly StringBuilder _junk = new StringBuilder();
        int _depth;
        bool _inString;
        bool _escaped;

        public JsonMessageFramer(int maxMessageLength = DefaultMaxMessageLength)
        {
            _maxMessageLength = maxMessageLength;
        }

        /// <summary>True while a partial object is buffered.</summary>
        public bool HasPartialMessage => _depth > 0;

        /// <summary>
        /// Consumes a chunk of text. Each completed object is added to <paramref name="messages"/>;
        /// discarded text and oversize messages are described in <paramref name="errors"/>.
        /// </summary>
        public void Push(string chunk, ICollection<string> messages, ICollection<string> errors = null)
        {
            foreach (char c in chunk)
            {
                if (_depth == 0)
                {
                    if (c == '{')
                    {
                        FlushJunk(errors);
                        _depth = 1;
                        _current.Append(c);
                    }
                    else if (!char.IsWhiteSpace(c))
                    {
                        _junk.Append(c);
                    }
                    continue;
                }

                _current.Append(c);
                if (_current.Length > _maxMessageLength)
                {
                    errors?.Add($"Discarded message longer than {_maxMessageLength} characters.");
                    Reset();
                    continue;
                }

                if (_inString)
                {
                    if (_escaped) _escaped = false;
                    else if (c == '\\') _escaped = true;
                    else if (c == '"') _inString = false;
                }
                else if (c == '"')
                {
                    _inString = true;
                }
                else if (c == '{' || c == '[')
                {
                    _depth++;
                }
                else if (c == '}' || c == ']')
                {
                    _depth--;
                    if (_depth == 0)
                    {
                        messages.Add(_current.ToString());
                        _current.Clear();
                    }
                }
            }
            FlushJunk(errors);
        }

        public void Reset()
        {
            _current.Clear();
            _depth = 0;
            _inString = false;
            _escaped = false;
        }

        void FlushJunk(ICollection<string> errors)
        {
            if (_junk.Length == 0) return;
            const int preview = 40;
            string text = _junk.Length > preview ? _junk.ToString(0, preview) + "…" : _junk.ToString();
            errors?.Add($"Discarded {_junk.Length} unexpected character(s) outside a JSON object: \"{text}\"");
            _junk.Clear();
        }
    }
}
