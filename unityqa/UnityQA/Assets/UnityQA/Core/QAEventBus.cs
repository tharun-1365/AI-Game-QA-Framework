// -----------------------------------------------------------------------------
// UnityQA — QAEventBus.cs                                          (SRS §5 D1)
//
// PURPOSE
//   Minimal synchronous publish/subscribe: producers call Publish, consumers
//   Subscribe. The seam every current and future module plugs into.
//
// WHY THIS SIMPLE (Rule 8, line budget ~50)
//   No reflection, no attributes, no threading, no message queues. A list of
//   delegates and a loop. Constraints documented instead of engineered away:
//   synchronous + main-thread only, so handlers may safely use the Unity API.
//
// TWO NON-OBVIOUS GUARANTEES (both unit-tested)
//   1. Exception isolation: a throwing subscriber is logged and skipped; it
//      cannot break delivery to the others. The logger must never die because
//      the overlay had a bug, and vice versa.
//   2. Reentrancy safety: subscribing/unsubscribing DURING a publish is legal.
//      We iterate a snapshot array, so mutation of the live list never
//      invalidates the loop. Copy cost is negligible at our subscriber counts.
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityQA.Core
{
    /// <summary>
    /// Synchronous, main-thread pub/sub for QAEvents. Owned by QARunner;
    /// plain C# (not a MonoBehaviour) so it is unit-testable without a scene.
    /// </summary>
    public sealed class QAEventBus
    {
        private readonly List<Action<QAEvent>> subscribers = new List<Action<QAEvent>>();

        /// <summary>Snapshot cache; rebuilt only when the subscriber list changes.</summary>
        private Action<QAEvent>[] snapshot = Array.Empty<Action<QAEvent>>();
        private bool dirty;

        public int SubscriberCount => subscribers.Count;

        public void Subscribe(Action<QAEvent> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            subscribers.Add(handler);
            dirty = true;
        }

        public void Unsubscribe(Action<QAEvent> handler)
        {
            if (subscribers.Remove(handler))
                dirty = true;
        }

        /// <summary>
        /// Deliver to all current subscribers. A throwing subscriber is
        /// reported via Debug.LogException and skipped — never fatal.
        /// </summary>
        public void Publish(QAEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            if (dirty)
            {
                snapshot = subscribers.ToArray();
                dirty = false;
            }

            var current = snapshot; // local ref: immune to mid-loop rebuilds
            for (int i = 0; i < current.Length; i++)
            {
                try
                {
                    current[i](e);
                }
                catch (Exception ex)
                {
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }
    }
}
