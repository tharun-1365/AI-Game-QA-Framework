// -----------------------------------------------------------------------------
// UnityQA — QAEvent.cs                                    (EVENT-SCHEMA.md §3)
//
// PURPOSE
//   One immutable record of one discrete observation — the single currency
//   every UnityQA layer trades in (SRS §8).
//
// WHY IMMUTABLE
//   Many consumers (logger, overlay, future detectors) receive the SAME event
//   instance from the bus. If any consumer could mutate it, history would
//   depend on subscriber order — a bug class we delete by construction:
//   readonly fields, no setters, payload copied at creation.
//
// DESIGN NOTES
//   - Position is nullable: lifecycle events have no location; forcing (0,0)
//     would poison future spatial analysis with fake origins.
//   - Payload is Dictionary<string, object>, kept FLAT (schema rule §6).
//     Events are sparse (a few per second at most), so this allocation is
//     nowhere near any hot path — the dense streams don't use QAEvent at all.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>
    /// Immutable discrete observation. Constructed only via QARunner.Emit
    /// (which stamps identity, sequence, and time) — everyone else just reads.
    /// </summary>
    public sealed class QAEvent
    {
        /// <summary>Session UUID (amendment A2).</summary>
        public readonly string Sid;

        /// <summary>Strictly increasing within a session, no gaps (NFR-1.5).</summary>
        public readonly long Seq;

        /// <summary>Seconds since session start.</summary>
        public readonly float T;

        /// <summary>Time.frameCount at emission.</summary>
        public readonly int Frame;

        public readonly QAEventType Type;

        /// <summary>World position, when the event is spatial; null otherwise.</summary>
        public readonly Vector2? Pos;

        /// <summary>Flat, type-specific extras (EVENT-SCHEMA.md §3 table). Never null.</summary>
        public readonly IReadOnlyDictionary<string, object> Payload;

        private static readonly Dictionary<string, object> Empty = new Dictionary<string, object>();

        public QAEvent(string sid, long seq, float t, int frame,
                       QAEventType type, Vector2? pos,
                       IDictionary<string, object> payload)
        {
            Sid = sid;
            Seq = seq;
            T = t;
            Frame = frame;
            Type = type;
            Pos = pos;
            // Defensive copy: the emitter may reuse its dictionary; we must not
            // share mutable state with it.
            Payload = payload == null || payload.Count == 0
                ? Empty
                : new Dictionary<string, object>(payload);
        }

        /// <summary>Human-readable one-liner (Console mirror, overlay).</summary>
        public override string ToString()
        {
            string where = Pos.HasValue ? $" @({Pos.Value.x:F2},{Pos.Value.y:F2})" : "";
            return $"[{T:F2}s #{Seq}] {Type}{where}";
        }
    }
}
