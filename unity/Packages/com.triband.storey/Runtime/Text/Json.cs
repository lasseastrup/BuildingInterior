#nullable enable
// A JSON reader and writer small enough to own (ported from com.triband.road-contracts).
//
// The contract assembly references nothing, and that includes a JSON library.
// Unity's JsonUtility would cost the engine reference this assembly exists not
// to have, cannot express a null string, and does not promise to read a double
// back to the bits it was written from. Newtonsoft would be a package
// dependency on both sides of the seam. What a building file needs is a
// parser for objects, arrays, strings, numbers, booleans and null, and a writer
// for the same, and that is a couple of hundred lines.
//
// Numbers are parsed with double.Parse in the invariant culture, which .NET
// Core 3.0 and later make correctly rounded: the shortest round-trip decimal
// JavaScript's JSON.stringify writes reads back to the identical double. That
// is the property the parity fixtures depend on, and JsonTests asserts it on a
// few awkward values.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Triband.Storey.Text
{
    /// <summary>
    /// Parsed JSON as plain objects: <c>Dictionary&lt;string, object?&gt;</c> for an
    /// object, <c>List&lt;object?&gt;</c> for an array, <c>string</c>, <c>double</c>,
    /// <c>bool</c>, or <c>null</c>.
    /// </summary>
    public static class Json
    {
        public static object? Parse(string text)
        {
            var reader = new Reader(text);
            reader.SkipSpace();
            object? value = reader.Value();
            reader.SkipSpace();
            if (!reader.AtEnd) throw reader.Error("trailing characters");
            return value;
        }

        public static string Write(object? value)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value);
            return sb.ToString();
        }

        // --- reading ---------------------------------------------------------

        sealed class Reader
        {
            readonly string s;
            int i;

            public Reader(string text)
            {
                s = text;
                i = 0;
            }

            public bool AtEnd => i >= s.Length;

            public Exception Error(string what) => new FormatException($"JSON: {what} at offset {i}");

            public void SkipSpace()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') i++;
                    else break;
                }
            }

            public object? Value()
            {
                if (AtEnd) throw Error("unexpected end");
                char c = s[i];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    default:
                        if (c == '-' || (c >= '0' && c <= '9')) return Number();
                        throw Error($"unexpected '{c}'");
                }
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error($"expected {word}");
                i += word.Length;
            }

            Dictionary<string, object?> Object()
            {
                var result = new Dictionary<string, object?>();
                i++; // {
                SkipSpace();
                if (i < s.Length && s[i] == '}')
                {
                    i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || s[i] != '"') throw Error("expected a key");
                    string key = String();
                    SkipSpace();
                    if (AtEnd || s[i] != ':') throw Error("expected ':'");
                    i++;
                    SkipSpace();
                    result[key] = Value();
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end in object");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == '}')
                    {
                        i++;
                        return result;
                    }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object?> Array()
            {
                var result = new List<object?>();
                i++; // [
                SkipSpace();
                if (i < s.Length && s[i] == ']')
                {
                    i++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    result.Add(Value());
                    SkipSpace();
                    if (AtEnd) throw Error("unexpected end in array");
                    if (s[i] == ',')
                    {
                        i++;
                        continue;
                    }
                    if (s[i] == ']')
                    {
                        i++;
                        return result;
                    }
                    throw Error("expected ',' or ']'");
                }
            }

            string String()
            {
                i++; // opening quote
                // Fast path: no escapes.
                int start = i;
                while (i < s.Length && s[i] != '"' && s[i] != '\\') i++;
                if (AtEnd) throw Error("unterminated string");
                if (s[i] == '"')
                {
                    string plain = s.Substring(start, i - start);
                    i++;
                    return plain;
                }

                var sb = new StringBuilder();
                sb.Append(s, start, i - start);
                while (true)
                {
                    if (AtEnd) throw Error("unterminated string");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\')
                    {
                        sb.Append(c);
                        continue;
                    }
                    if (AtEnd) throw Error("unterminated escape");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Error("short \\u escape");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: throw Error($"bad escape '\\{e}'");
                    }
                }
            }

            double Number()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                if (i < s.Length && s[i] == '.')
                {
                    i++;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
                {
                    i++;
                    if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
                    while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
                }
                string token = s.Substring(start, i - start);
                if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    throw Error($"bad number '{token}'");
                }
                return value;
            }
        }

        // --- writing ---------------------------------------------------------

        static void WriteValue(StringBuilder sb, object? value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case string str: WriteString(sb, str); break;
                case double d: WriteNumber(sb, d); break;
                case float f: WriteNumber(sb, f); break;
                case int n: sb.Append(n.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object?> obj:
                {
                    sb.Append('{');
                    bool first = true;
                    foreach (var pair in obj)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, pair.Key);
                        sb.Append(':');
                        WriteValue(sb, pair.Value);
                    }
                    sb.Append('}');
                    break;
                }
                case IEnumerable<object?> list:
                {
                    sb.Append('[');
                    bool first = true;
                    foreach (var item in list)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteValue(sb, item);
                    }
                    sb.Append(']');
                    break;
                }
                default:
                    throw new ArgumentException($"JSON: cannot write a {value.GetType().Name}");
            }
        }

        /// <summary>
        /// A double the way JSON.stringify writes one: the shortest decimal that
        /// reads back to the same bits, -0 as 0, and no NaN or infinity.
        /// </summary>
        static void WriteNumber(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("JSON: cannot write NaN or infinity");
            if (d == 0) { sb.Append('0'); return; }
            // "R" is shortest-round-trip on .NET Core 3.0+. It may use exponent
            // notation ("1E-07") where JavaScript would not ("1e-7"); the value
            // is identical, and the parity tests compare values, not text.
            sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
        }

        static void WriteString(StringBuilder sb, string str)
        {
            sb.Append('"');
            foreach (char c in str)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
