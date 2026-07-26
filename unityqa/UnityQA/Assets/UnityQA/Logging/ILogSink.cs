// -----------------------------------------------------------------------------
// UnityQA — ILogSink.cs                                       (M2 design §4, §9)
//
// PURPOSE
//   The contract every event consumer that "writes somewhere" fulfills.
//   QALogger fans each QAEvent out to a set of ILogSinks; it neither knows
//   nor cares whether a sink targets the Console, a file, or (Module 5)
//   screenshots and evidence bundles. Adding an output = adding a sink;
//   QALogger never changes. This is the Open/Closed seam of the logging layer.
//
// LIFECYCLE CONTRACT
//   Open  — once, when a session starts (folder exists by then).
//   Write — once per event, in bus order, between Open and Close.
//   Flush — push buffered data to durable storage; may be called at any time.
//   Close — once; after it, no further Writes arrive. Must be idempotent-safe
//           (a defensive double-Close must not throw — crash paths call it).
// -----------------------------------------------------------------------------

using UnityQA.Core;

namespace UnityQA.Logging
{
    /// <summary>Destination for discrete events during a session.</summary>
    public interface ILogSink
    {
        /// <param name="session">Identity/metadata of the starting session.</param>
        /// <param name="sessionFolder">Absolute path of this session's folder.</param>
        void Open(QASessionInfo session, string sessionFolder);

        void Write(QAEvent e);

        void Flush();

        void Close();
    }
}
