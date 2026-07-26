// -----------------------------------------------------------------------------
// UnityQA — QATelemetrySampler.cs                       (M2 design §7, D-010)
//
// PURPOSE
//   Turns continuous gameplay state into PlayerSample events at the frequency
//   configured in QAConfig (never hardcoded), and relays the adapter's edge
//   events (jump takeoff, landing) into the same pipeline. Everything flows
//   Player → adapter → sampler → QARunner.Emit → bus → QALogger → JsonlSink —
//   nothing bypasses the event system (Slice C ground rule).
//
// WHAT A SAMPLE CONTAINS (schema addition, EVENT-SCHEMA.md §3, type 30)
//   envelope pos = player position; payload = vx, vy, g (grounded 0/1),
//   mx (move input), facing (-1/+1, last non-zero direction), state
//   ("idle" | "run" | "rise" | "fall" — derived, see DeriveState).
//
// LIFECYCLE
//   Subscribes to the bus in Start (same reasoning as QALogger — the bus is
//   born in QARunner.Awake). Sampling runs ONLY while a session is active:
//   the coroutine starts on SessionStarted and stops on SessionEnded, and the
//   adapter's edge events are attached/detached on the same boundary — so no
//   "Emit with no active session" warnings can ever spam the Console.
//
// ALLOCATION HONESTY (design §11)
//   The payload dictionary is reused; QAEvent's defensive copy still allocates
//   one small dictionary per event. At 10 Hz that is ~10 tiny allocations per
//   second — measured in Slice D's profiler gate, and accepted consciously
//   over making QAEvent mutable (immutability is load-bearing, QAEvent.cs).
//   The state strings are interned constants: zero allocation per sample.
// -----------------------------------------------------------------------------

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace UnityQA.Core
{
    /// <summary>
    /// Periodic gameplay telemetry + edge-event relay. Scene setup: lives on
    /// the "[QA]" GameObject next to QARunner and an IGameAdapter component.
    /// </summary>
    [RequireComponent(typeof(QARunner))]
    public sealed class QATelemetrySampler : MonoBehaviour
    {
        // Interned state names — allocation-free, dataset-friendly.
        public const string StateIdle = "idle";
        public const string StateRun = "run";
        public const string StateRise = "rise";
        public const string StateFall = "fall";

        /// <summary>Velocity below this magnitude counts as "not moving" — keeps
        /// physics jitter from flickering idle/run in the dataset.</summary>
        public const float SpeedEpsilon = 0.01f;

        private QARunner runner;
        private IGameAdapter adapter;
        private Coroutine loop;
        private WaitForSeconds interval;
        private readonly Dictionary<string, object> payload = new Dictionary<string, object>(8);
        private int facing = 1; // +1 right, -1 left; persists through mx == 0

        private void Awake()
        {
            runner = GetComponent<QARunner>();
            adapter = GetComponent<IGameAdapter>(); // interface lookup — concrete type unknown here
            if (adapter == null)
                Debug.LogWarning("[UnityQA] QATelemetrySampler found no IGameAdapter on '[QA]' — " +
                                 "telemetry will be skipped. Add BenchGameAdapter (see docs/QA-SETUP.md).");
        }

        private void Start()
        {
            runner.Bus.Subscribe(OnEvent);
        }

        private void OnEvent(QAEvent e)
        {
            if (e.Type == QAEventType.SessionStarted) BeginSampling();
            else if (e.Type == QAEventType.SessionEnded) EndSampling();
        }

        private void BeginSampling()
        {
            if (adapter == null || loop != null) return;

            // Frequency comes from config at session start — never hardcoded.
            // (Changing telemetryHz mid-session applies to the NEXT session;
            // a session's config snapshot in session.json must stay truthful.)
            interval = new WaitForSeconds(1f / Mathf.Clamp(runner.Config.telemetryHz, 1, 50));
            facing = 1;

            adapter.JumpDetected += OnJump;
            adapter.LandedDetected += OnLanded;
            loop = StartCoroutine(SampleLoop());
        }

        private void EndSampling()
        {
            if (loop != null) { StopCoroutine(loop); loop = null; }
            if (adapter != null)
            {
                adapter.JumpDetected -= OnJump;
                adapter.LandedDetected -= OnLanded;
            }
        }

        private IEnumerator SampleLoop()
        {
            while (true)
            {
                // Yield FIRST: sampling must never run nested inside the
                // SessionStarted publish (StartCoroutine executes until the
                // first yield synchronously — sampling there would emit a
                // PlayerSample before some subscribers finished handling
                // SessionStarted, making delivery order depend on component
                // order — a bug class we exclude structurally).
                yield return interval; // cached — no per-tick allocation
                EmitSample();
            }
        }

        private void EmitSample()
        {
            if (!adapter.IsValid) return;

            Vector2 vel = adapter.PlayerVelocity;
            float mx = adapter.MoveInput;
            if (mx > SpeedEpsilon) facing = 1;
            else if (mx < -SpeedEpsilon) facing = -1;

            payload.Clear();
            payload["vx"] = vel.x;
            payload["vy"] = vel.y;
            payload["g"] = adapter.IsGrounded ? 1 : 0;
            payload["mx"] = mx;
            payload["facing"] = facing;
            payload["state"] = DeriveState(vel, adapter.IsGrounded);

            runner.Emit(QAEventType.PlayerSample, adapter.PlayerPosition, payload);
        }

        private void OnJump()
        {
            payload.Clear();
            runner.Emit(QAEventType.JumpExecuted, adapter.PlayerPosition, payload);
        }

        private void OnLanded(float fallSpeed)
        {
            payload.Clear();
            payload["fallSpeed"] = fallSpeed;
            runner.Emit(QAEventType.Landed, adapter.PlayerPosition, payload);
        }

        /// <summary>
        /// Movement-state classification — public and static so EditMode tests
        /// pin the mapping without a scene (the derivation IS the contract;
        /// future ML labels depend on it staying stable).
        /// Airborne wins over horizontal: rising/falling describe the physics
        /// situation that matters to QA (a "falling" player near a pit is the
        /// interesting fact, not that they also drift sideways).
        /// </summary>
        public static string DeriveState(Vector2 velocity, bool grounded)
        {
            if (!grounded)
                return velocity.y > SpeedEpsilon ? StateRise : StateFall;
            return Mathf.Abs(velocity.x) > SpeedEpsilon ? StateRun : StateIdle;
        }

        private void OnDisable()
        {
            EndSampling();
            if (runner != null && runner.Bus != null)
                runner.Bus.Unsubscribe(OnEvent);
        }
    }
}
