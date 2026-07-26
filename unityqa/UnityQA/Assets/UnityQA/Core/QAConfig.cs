// -----------------------------------------------------------------------------
// UnityQA — QAConfig.cs                                     (SRS FR-1.12, §5 D3)
//
// PURPOSE
//   Every tunable knob of the framework in one committed asset. Zero magic
//   numbers in code; an experiment's settings are reviewable and versioned.
//
// WHY A SCRIPTABLEOBJECT
//   Inspector-editable without recompiling (FR-1.4 spirit), referenced by any
//   scene, diffable as text in Git, and snapshot-serialized into session.json
//   so every log knows the settings that produced it (reproducibility, §2.1).
//
// SLICE NOTE
//   Fields for Slices B–D (flush policy, rates, keyframes) are declared NOW so
//   the asset's shape — and its snapshot in session.json — is stable from the
//   first committed session. Declaring a knob is cheap; changing a frozen
//   schema is not. Slice A itself reads only the keys and consoleEvents.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>All framework tunables. Create via: Assets ▸ Create ▸ UnityQA ▸ Config.</summary>
    [CreateAssetMenu(fileName = "DefaultQAConfig", menuName = "UnityQA/Config")]
    public sealed class QAConfig : ScriptableObject
    {
        [Header("Session control")]
        [Tooltip("Starts/stops a QA session (FR-1.4).")]
        public KeyCode startStopKey = KeyCode.F9;

        [Tooltip("Shows/hides the debug overlay (FR-1.11). Used from Slice D.")]
        public KeyCode overlayKey = KeyCode.F10;

        [Header("Console mirror")]
        [Tooltip("Echo discrete events to the Unity Console with a [UnityQA] prefix (FR-1.10). Dense streams are never echoed.")]
        public bool consoleEvents = true;

        [Header("Recording rates — used from Slice C")]
        [Tooltip("Telemetry samples per second (design §7). 10 Hz default; 1–50.")]
        [Range(1, 50)] public int telemetryHz = 10;

        [Tooltip("Unconditional input keyframe every N fixed steps (design §8). 250 = 5 s at 50 Hz.")]
        public int inputKeyframeEverySteps = 250;

        [Header("File I/O — used from Slice B")]
        [Tooltip("Flush events.jsonl every N lines (crash-safety vs throughput, design §11).")]
        public int flushEveryNEvents = 25;

        [Tooltip("Flush dense streams (telemetry/inputs) every N seconds.")]
        public float denseFlushSeconds = 1f;
    }
}
