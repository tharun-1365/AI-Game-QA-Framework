// -----------------------------------------------------------------------------
// UnityQA — QAInputRecorder.cs                                   (M2 Slice D)
//
// PURPOSE
//   Records what the player ATTEMPTS: InputSample events on every input
//   change plus periodic keyframes (QAConfig.inputKeyframeEverySteps — never
//   hardcoded). Mandated flow honored end to end:
//   PlayerController2D → BenchGameAdapter → QAInputRecorder → QARunner →
//   QAEventBus → QALogger → JsonlSink. Nothing bypasses the bus.
//
// EVENT DISCIPLINE (no spam by construction)
//   InputSampleGate (pure, unit-tested) decides emission: changed OR keyframe
//   due. Constant input produces ONLY keyframes; jump press/release are
//   changes of jumpHeld, so edges ride the change path with derived
//   jumpPressed / jumpReleased flags. First record of every session is a
//   keyframe (emitted on the first Update AFTER SessionStarted — never nested
//   inside the SessionStarted publish, same reentrancy discipline as the
//   telemetry sampler).
//
// TIME DOMAINS (subtle, viva-worthy)
//   Input is FRAME-domain (read in Update, where GetButton* semantics live);
//   the keyframe cadence and the payload's `step` are FIXED-STEP-domain
//   (counted in FixedUpdate), because replay and cross-referencing with
//   physics need the frame-rate-independent clock (FR-1.19). This class
//   straddles both on purpose and documents it.
//
// ALLOCATION NOTES
//   Payload dictionary reused; bool/int payload values come from cached boxes
//   (no per-emission boxing); no LINQ, no per-frame strings. QAEvent's
//   defensive dictionary copy per emission remains the accepted, documented
//   trade (see QATelemetrySampler header) — and emissions here are sparse by
//   design.
// -----------------------------------------------------------------------------

using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>
    /// Attempted-input recorder. Scene setup: on the "[QA]" GameObject next to
    /// QARunner and the game adapter (which must implement IPlayerInputObserver).
    /// </summary>
    [RequireComponent(typeof(QARunner))]
    public sealed class QAInputRecorder : MonoBehaviour
    {
        // Cached boxes — payload values without per-emission allocations.
        private static readonly object BoxTrue = true;
        private static readonly object BoxFalse = false;
        private static readonly object[] BoxH = { -1, 0, 1 }; // index: horizontal + 1

        private QARunner runner;
        private IPlayerInputObserver observer;

        private readonly System.Collections.Generic.Dictionary<string, object> payload =
            new System.Collections.Generic.Dictionary<string, object>(8);

        private bool active;
        private bool pendingInitialKeyframe;
        private PlayerInputState previous;
        private long stepCount;          // fixed steps since session start
        private int stepsSinceKeyframe;  // fixed steps since last keyframe

        private void Awake()
        {
            runner = GetComponent<QARunner>();
            observer = GetComponent<IPlayerInputObserver>();
            if (observer == null)
                Debug.LogWarning("[UnityQA] QAInputRecorder found no IPlayerInputObserver on '[QA]' — " +
                                 "input capture will be skipped. Is the game adapter attached?");
        }

        private void Start()
        {
            runner.Bus.Subscribe(OnEvent);
        }

        private void OnEvent(QAEvent e)
        {
            if (e.Type == QAEventType.SessionStarted)
            {
                stepCount = 0;
                stepsSinceKeyframe = 0;
                pendingInitialKeyframe = true; // emitted on the next Update, outside this publish
                active = true;
            }
            else if (e.Type == QAEventType.SessionEnded)
            {
                active = false;
            }
        }

        private void FixedUpdate()
        {
            if (!active) return;
            stepCount++;
            stepsSinceKeyframe++;
        }

        private void Update()
        {
            if (!active || observer == null) return;
            if (!observer.TryGetInputState(out PlayerInputState current)) return;

            if (pendingInitialKeyframe)
            {
                // Session's first input record: a keyframe, with no edges
                // (there is no meaningful "previous" before the session).
                pendingInitialKeyframe = false;
                previous = current;
                Emit(current, jumpPressed: false, jumpReleased: false, isKeyframe: true);
                stepsSinceKeyframe = 0;
                return;
            }

            if (InputSampleGate.ShouldEmit(previous, current, stepsSinceKeyframe,
                                           runner.Config.inputKeyframeEverySteps, out bool isKeyframe))
            {
                Emit(current,
                     jumpPressed: current.jumpHeld && !previous.jumpHeld,
                     jumpReleased: !current.jumpHeld && previous.jumpHeld,
                     isKeyframe: isKeyframe);
                if (isKeyframe) stepsSinceKeyframe = 0;
                previous = current;
            }
        }

        private void Emit(in PlayerInputState s, bool jumpPressed, bool jumpReleased, bool isKeyframe)
        {
            payload.Clear();
            payload["step"] = stepCount;                       // fixed-step clock: the replay join key
            payload["horizontal"] = BoxH[s.horizontal + 1];
            payload["jumpPressed"] = jumpPressed ? BoxTrue : BoxFalse;
            payload["jumpReleased"] = jumpReleased ? BoxTrue : BoxFalse;
            payload["jumpHeld"] = s.jumpHeld ? BoxTrue : BoxFalse;
            payload["keyframe"] = isKeyframe ? BoxTrue : BoxFalse;

            // No position: input is not spatial — where the player WAS is
            // telemetry's fact (avoid redundancy, Slice D ground rule).
            runner.Emit(QAEventType.InputSample, null, payload);
        }

        private void OnDisable()
        {
            active = false;
            if (runner != null && runner.Bus != null)
                runner.Bus.Unsubscribe(OnEvent);
        }
    }
}
