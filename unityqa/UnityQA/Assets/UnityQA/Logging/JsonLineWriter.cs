// -----------------------------------------------------------------------------
// UnityQA — JsonLineWriter.cs                                  (M2 design §9-11)
//
// PURPOSE
//   Builds the JSON lines of EVENT-SCHEMA.md by hand over one reused
//   StringBuilder: event lines (§3) and stream header lines (§1, amendment A1).
//
// WHY HAND-BUILT JSON (design §11)
//   The dense streams of Slice C will produce tens of lines per second for
//   minutes; a general-purpose serializer allocates objects on every call.
//   This writer allocates only the final string. The cost of hand-building is
//   escaping bugs — so escaping is centralized in ONE method and unit-tested,
//   and the schema's shapes are simple by design (flat fields, one nesting
//   level for pos/payload).
//
// FORMAT RULES ENFORCED HERE (schema §6)
//   Invariant culture always (a German OS writes "1,5" for 1.5f with default
//   ToString — that would corrupt every file; the culture test pins this).
//   Floats: "0.###" — max 3 decimals, trailing zeros trimmed.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>
    /// Allocation-light builder for schema v1 JSONL lines. NOT thread-safe by
    /// design (documented single-main-thread constraint, SRS §12) — one
    /// instance per consumer, reused line after line.
    /// </summary>
    public sealed class JsonLineWriter
    {
        private const string FloatFormat = "0.###";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly StringBuilder sb = new StringBuilder(256);

        // ---------------------------------------------------------------- lines

        /// <summary>Stream header line — first line of every .jsonl (A1/A2).</summary>
        public string HeaderLine(string stream, QASessionInfo session)
        {
            sb.Length = 0;
            sb.Append("{\"header\":1,\"schemaVersion\":").Append(QASessionInfo.SchemaVersion)
              .Append(",\"stream\":\"").Append(stream)
              .Append("\",\"sessionId\":\"").Append(session.SessionId).Append("\"}");
            return sb.ToString();
        }

        /// <summary>Event envelope line per schema §3.</summary>
        public string EventLine(QAEvent e)
        {
            sb.Length = 0;
            sb.Append("{\"sid\":\"").Append(e.Sid)
              .Append("\",\"seq\":").Append(e.Seq)
              .Append(",\"t\":").Append(e.T.ToString(FloatFormat, Inv))
              .Append(",\"frame\":").Append(e.Frame)
              .Append(",\"type\":\"").Append(e.Type.ToString()).Append('"');

            if (e.Pos.HasValue)
            {
                sb.Append(",\"pos\":{\"x\":").Append(e.Pos.Value.x.ToString(FloatFormat, Inv))
                  .Append(",\"y\":").Append(e.Pos.Value.y.ToString(FloatFormat, Inv))
                  .Append('}');
            }

            sb.Append(",\"payload\":{");
            bool first = true;
            foreach (KeyValuePair<string, object> kv in e.Payload)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(Escape(kv.Key)).Append("\":");
                AppendValue(kv.Value);
            }
            sb.Append("}}");
            return sb.ToString();
        }

        // -------------------------------------------------------------- values

        /// <summary>
        /// Serialize one payload value. Recognized primitives keep their JSON
        /// type; anything exotic degrades to an escaped string — a QA log must
        /// never throw over an unexpected payload (observer must not crash
        /// the observed).
        /// </summary>
        private void AppendValue(object value)
        {
            switch (value)
            {
                case null: sb.Append("null"); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(Inv)); break;
                case long l: sb.Append(l.ToString(Inv)); break;
                case float f: sb.Append(f.ToString(FloatFormat, Inv)); break;
                case double d: sb.Append(d.ToString(FloatFormat, Inv)); break;
                case string s: sb.Append('"').Append(Escape(s)).Append('"'); break;
                default: sb.Append('"').Append(Escape(value.ToString())).Append('"'); break;
            }
        }

        /// <summary>
        /// Minimal JSON string escaping: backslash, quote, and control chars.
        /// Centralized so it exists exactly once and is tested exactly once.
        /// </summary>
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;

            // Fast path: most strings (event types, object names) need nothing.
            bool clean = true;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '"' || c == '\\' || c < ' ') { clean = false; break; }
            }
            if (clean) return s;

            var e = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': e.Append("\\\""); break;
                    case '\\': e.Append("\\\\"); break;
                    case '\n': e.Append("\\n"); break;
                    case '\r': e.Append("\\r"); break;
                    case '\t': e.Append("\\t"); break;
                    default:
                        if (c < ' ') e.Append("\\u").Append(((int)c).ToString("x4", Inv));
                        else e.Append(c);
                        break;
                }
            }
            return e.ToString();
        }
    }
}
