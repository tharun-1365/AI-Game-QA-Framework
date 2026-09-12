// -----------------------------------------------------------------------------
// UnityQA.Adapters — ReplayDivergenceProbe.cs                    (DIAGNOSTIC)
//
// PURPOSE
//   Frame-exact state tracer for replay determinism debugging. Writes one CSV
//   row per rendered frame into the active session's folder, capturing every
//   quantity needed to locate the FIRST CAUSAL DIFFERENCE between an original
//   run and its replay — not merely the first position difference.
//
// WHY A SEPARATE COMPONENT AND NOT TELEMETRY
//   Telemetry is a 10 Hz behavioral log with a frozen schema (EVENT-SCHEMA.md)
//   and a dataset downstream. Divergence debugging needs ~60 Hz, needs private
//   controller latch state, and must NOT enter events.jsonl — putting it there
//   would change the telemetry schema, corrupt the dataset, and alter what the
//   oracles read. So this writes its own side-car file, reads only public
//   surfaces, emits nothing on the bus, and is deletable in one file when the
//   investigation closes. It is diagnostic apparatus, not part of the product.
//
// EXECUTION ORDER (load-bearing)
//   [DefaultExecutionOrder(1000)] puts both callbacks LAST:
//     Update      — after ReplayPlayer (-50) has pushed the frame and after
//                   PlayerController2D (0) has sampled it, so the row shows
//                   what the controller actually latched this frame.
//     FixedUpdate — after the controller's, so velocity/grounded are the
//                   post-step values that produced the motion.
//
// ALIGNMENT MODEL (how two traces are compared)
//   Recording run: replay frame N is captured on session-update N.
//   Replay run:    replay frame N is applied on whatever update it lands on,
//                  and the probe records that index directly.
//   So the traces are joined on REPLAY FRAME INDEX, which is the only key
//   that means the same thing on both sides. The original's frame index is
//   its session-update index; the replay's is read from ReplayPlayer. The
//   comparer prints the alignment it used so the join is auditable.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Text;
using BenchGame;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Adapters
{
    /// <summary>
    /// Per-frame determinism tracer. Add to "[QA]" next to QARunner while
    /// investigating replay divergence; remove when the investigation closes.
    /// Writes "divergence-trace.csv" into each session folder.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [RequireComponent(typeof(QARunner))]
    public sealed class ReplayDivergenceProbe : MonoBehaviour
    {
        public const string TraceFileName = "divergence-trace.csv";

        private const string Header =
            "update,fixed,t,replayFrame,replayT,inMoveX,inJumpDown,inJumpHeld," +
            "ctrlMoveInput,ctrlJumpRequested,grounded,px,py,vx,vy,jumps,runState";

        [Tooltip("Rows are buffered in memory and written once at session end.")]
        [SerializeField] private int initialCapacity = 8192;

        private QARunner runner;
        private ReplayPlayer replayPlayer;   // optional — null during a human run
        private PlayerController2D controller;
        private BenchGameAdapter adapter;
        private GameRun gameRun;

        private StringBuilder rows;
        private bool tracing;
        private float sessionStartTime;
        private int updateIndex;
        private int fixedIndex;
        private int jumpsThisFrame;

        private void Awake()
        {
            runner = GetComponent<QARunner>();
            replayPlayer = GetComponent<ReplayPlayer>();
            adapter = GetComponent<BenchGameAdapter>();
        }

        private void Start()
        {
            runner.Bus.Subscribe(OnEvent);
        }

        private void OnEvent(QAEvent e)
        {
            if (e.Type == QAEventType.SessionStarted) Begin();
            else if (e.Type == QAEventType.SessionEnded) EndAndWrite();
        }

        private void Begin()
        {
            controller = FindFirstObjectByType<PlayerController2D>();
            gameRun = FindFirstObjectByType<GameRun>();
            if (controller == null)
            {
                Debug.LogWarning("[UnityQA] DivergenceProbe: no PlayerController2D — trace skipped.");
                return;
            }

            rows = new StringBuilder(initialCapacity * 96);
            rows.Append(Header).Append('\n');

            sessionStartTime = Time.time;
            updateIndex = 0;
            fixedIndex = 0;
            jumpsThisFrame = 0;
            tracing = true;

            if (adapter != null) adapter.JumpDetected += OnJump;
        }

        private void OnJump() => jumpsThisFrame++;

        private void FixedUpdate()
        {
            if (tracing) fixedIndex++;
        }

        private void Update()
        {
            if (!tracing || controller == null) return;

            // Which recorded frame drove this Update? ReplayPlayer increments
            // its cursor immediately after pushing, so the frame APPLIED this
            // Update is CurrentFrame - 1. During a human run there is no
            // player (or it is idle) and we fall back to the session-update
            // index, which is the recorder's frame numbering by construction.
            bool playing = replayPlayer != null && replayPlayer.IsPlaying;
            int replayFrame = playing ? replayPlayer.CurrentFrame - 1 : updateIndex;
            float replayT = playing ? replayPlayer.CurrentFrameTimestamp : -1f;

            IPlayerInputSource src = controller.InputSource;
            Vector2 p = controller.transform.position;
            Vector2 v = controller.Velocity;

            rows.Append(updateIndex).Append(',')
                .Append(fixedIndex).Append(',')
                .Append(F(Time.time - sessionStartTime)).Append(',')
                .Append(replayFrame).Append(',')
                .Append(F(replayT)).Append(',')
                .Append(F(src != null ? src.MoveX : 0f)).Append(',')
                .Append(src != null && src.JumpDown ? 1 : 0).Append(',')
                .Append(src != null && src.JumpHeld ? 1 : 0).Append(',')
                .Append(F(controller.MoveInput)).Append(',')
                .Append(controller.JumpRequested ? 1 : 0).Append(',')
                .Append(controller.IsGrounded ? 1 : 0).Append(',')
                .Append(F(p.x)).Append(',').Append(F(p.y)).Append(',')
                .Append(F(v.x)).Append(',').Append(F(v.y)).Append(',')
                .Append(jumpsThisFrame).Append(',')
                .Append(gameRun != null ? gameRun.State.ToString() : "n/a")
                .Append('\n');

            jumpsThisFrame = 0;
            updateIndex++;
        }

        private void EndAndWrite()
        {
            if (!tracing) return;
            tracing = false;
            if (adapter != null) adapter.JumpDetected -= OnJump;

            QASessionInfo session = runner.CurrentSession;
            if (session == null) return;

            string folder = Path.Combine(QALogger.SessionsRoot, session.FolderName);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, TraceFileName);
            File.WriteAllText(path, rows.ToString());
            Debug.Log($"[UnityQA] Divergence trace written — {updateIndex} frames → {path}");
            rows = null;
        }

        /// <summary>Invariant-culture, 4 decimals — same discipline as the log writers.</summary>
        private static string F(float f) => f.ToString("F4", CultureInfo.InvariantCulture);

        private void OnDisable()
        {
            if (runner != null && runner.Bus != null) runner.Bus.Unsubscribe(OnEvent);
        }

        // =====================================================================
        // COMPARER — finds the FIRST CAUSAL DIFFERENCE, not the first position
        // difference. Ordered by causality: a state can only be blamed once
        // every state that feeds it has been cleared.
        // =====================================================================

        private struct Row
        {
            public int update, fixedIdx, replayFrame, jumps;
            public float t, replayT, inMoveX, ctrlMoveInput, px, py, vx, vy;
            public bool inJumpDown, inJumpHeld, ctrlJumpRequested, grounded;
            public string runState;
        }

        [Tooltip("Original session folder (name under Sessions, or absolute).")]
        [SerializeField] private string compareOriginalFolder = "";

        [Tooltip("Validation session folder (name under Sessions, or absolute).")]
        [SerializeField] private string compareValidationFolder = "";

        [ContextMenu("Diagnose Replay Divergence")]
        public void DiagnoseDivergence()
        {
            string a = Resolve(compareOriginalFolder);
            string b = Resolve(compareValidationFolder);
            if (a == null || b == null) return;
            Debug.Log(Diagnose(a, b));
        }

        /// <summary>
        /// Pure over two trace files. Returns a human-readable report naming
        /// the first frame at which each state class first differs, in causal
        /// order, plus the verdict line.
        /// </summary>
        public static string Diagnose(string originalFolder, string validationFolder)
        {
            Row[] o = LoadTrace(Path.Combine(originalFolder, TraceFileName));
            Row[] r = LoadTrace(Path.Combine(validationFolder, TraceFileName));
            if (o == null || r == null)
                return "[UnityQA] Divergence diagnosis aborted — a trace file is missing. " +
                       "Add ReplayDivergenceProbe to [QA] and re-record BOTH runs.";

            var sb = new StringBuilder();
            sb.Append("[UnityQA] REPLAY DIVERGENCE DIAGNOSIS\n");
            sb.Append($"  original   : {o.Length} frames  ({originalFolder})\n");
            sb.Append($"  validation : {r.Length} frames  ({validationFolder})\n");
            sb.Append("  join key   : replay frame index (original: session-update index)\n\n");

            // Index the replay side by the recorded frame it applied.
            int maxFrame = 0;
            for (int i = 0; i < r.Length; i++) if (r[i].replayFrame > maxFrame) maxFrame = r[i].replayFrame;
            var byFrame = new int[maxFrame + 2];
            for (int i = 0; i < byFrame.Length; i++) byFrame[i] = -1;
            for (int i = 0; i < r.Length; i++)
            {
                int f = r[i].replayFrame;
                if (f >= 0 && f < byFrame.Length && byFrame[f] < 0) byFrame[f] = i; // first application wins
            }

            int firstInput = -1, firstGround = -1, firstLatch = -1, firstVel = -1, firstPos = -1;
            int firstFixedSkew = -1, firstStateSkew = -1;
            int compared = 0;

            for (int f = 0; f < o.Length && f < byFrame.Length; f++)
            {
                int j = byFrame[f];
                if (j < 0) continue;              // this frame was never applied — reported below
                Row A = o[f], B = r[j];
                compared++;

                if (firstInput < 0 && (Diff(A.inMoveX, B.inMoveX, 0.0001f)
                                       || A.inJumpDown != B.inJumpDown
                                       || A.inJumpHeld != B.inJumpHeld)) firstInput = f;

                if (firstFixedSkew < 0 && (A.fixedIdx - A.update) != (B.fixedIdx - B.update))
                    firstFixedSkew = f;

                if (firstLatch < 0 && (Diff(A.ctrlMoveInput, B.ctrlMoveInput, 0.0001f)
                                       || A.ctrlJumpRequested != B.ctrlJumpRequested)) firstLatch = f;

                if (firstGround < 0 && A.grounded != B.grounded) firstGround = f;
                if (firstStateSkew < 0 && A.runState != B.runState) firstStateSkew = f;
                if (firstVel < 0 && (Diff(A.vx, B.vx, 0.01f) || Diff(A.vy, B.vy, 0.01f))) firstVel = f;
                if (firstPos < 0 && (Diff(A.px, B.px, 0.01f) || Diff(A.py, B.py, 0.01f))) firstPos = f;
            }

            sb.Append($"  compared   : {compared} joined frames\n\n");
            sb.Append("  FIRST DIFFERENCE BY STATE CLASS (causal order — top-most is the cause)\n");
            Line(sb, "1. input applied            ", firstInput, o, byFrame, r);
            Line(sb, "2. controller latch state   ", firstLatch, o, byFrame, r);
            Line(sb, "3. Update/FixedUpdate skew  ", firstFixedSkew, o, byFrame, r);
            Line(sb, "4. grounded state           ", firstGround, o, byFrame, r);
            Line(sb, "5. GameRun state            ", firstStateSkew, o, byFrame, r);
            Line(sb, "6. rigidbody velocity       ", firstVel, o, byFrame, r);
            Line(sb, "7. position                 ", firstPos, o, byFrame, r);

            sb.Append('\n').Append(Verdict(firstInput, firstLatch, firstFixedSkew,
                                           firstGround, firstVel, firstPos));

            // Frames the replay never applied (dropped or truncated).
            int missing = 0;
            for (int f = 0; f < o.Length && f < byFrame.Length; f++) if (byFrame[f] < 0) missing++;
            if (missing > 0)
                sb.Append($"\n  NOTE: {missing} recorded frames were never applied during playback " +
                          "(playback ended early, or frames were skipped).");
            return sb.ToString();
        }

        private static string Verdict(int input, int latch, int skew, int ground, int vel, int pos)
        {
            if (input >= 0 && input <= latch.OrMax() && input <= pos.OrMax())
                return "  VERDICT: the INPUT STREAM itself differs first — playback is applying a " +
                       "different frame than was recorded (alignment/off-by-one), not a physics issue.";
            if (latch >= 0 && (pos < 0 || latch <= pos))
                return "  VERDICT: input matched but the CONTROLLER LATCH differed first — stale " +
                       "moveInput/jumpRequested carried into the run, or a frame was sampled with " +
                       "no source attached. Initial-condition defect, not frame-domain.";
            if (skew >= 0 && (pos < 0 || skew <= pos))
                return "  VERDICT: input and latch matched; the UPDATE/FIXEDUPDATE INTERLEAVING " +
                       "differs first — this IS the D-011 frame-domain limitation. Confirm by " +
                       "re-running at a different targetFrameRate: the divergence frame should move.";
            if (ground >= 0 && (pos < 0 || ground <= pos))
                return "  VERDICT: GROUNDED state differed before position — a collider/contact " +
                       "difference, not an input difference.";
            if (vel >= 0 && (pos < 0 || vel <= pos))
                return "  VERDICT: VELOCITY differed before position — physics applied the same " +
                       "input differently (step boundary or contact resolution).";
            if (pos >= 0)
                return "  VERDICT: position differed with no antecedent state difference — check " +
                       "initial conditions and any external teleport.";
            return "  VERDICT: no divergence detected in any traced state class.";
        }

        private static void Line(StringBuilder sb, string label, int frame,
                                 Row[] o, int[] byFrame, Row[] r)
        {
            if (frame < 0) { sb.Append("     ").Append(label).Append(": (never differs)\n"); return; }
            Row A = o[frame], B = r[byFrame[frame]];
            sb.Append("     ").Append(label).Append($": frame {frame}  t≈{A.t:F3}s\n");
            sb.Append($"          original  in={A.inMoveX:F2}/{(A.inJumpDown ? "D" : "-")}{(A.inJumpHeld ? "H" : "-")} " +
                      $"latch={A.ctrlMoveInput:F2}/{(A.ctrlJumpRequested ? "J" : "-")} g={(A.grounded ? 1 : 0)} " +
                      $"p=({A.px:F3},{A.py:F3}) v=({A.vx:F3},{A.vy:F3}) fx={A.fixedIdx} up={A.update}\n");
            sb.Append($"          replay    in={B.inMoveX:F2}/{(B.inJumpDown ? "D" : "-")}{(B.inJumpHeld ? "H" : "-")} " +
                      $"latch={B.ctrlMoveInput:F2}/{(B.ctrlJumpRequested ? "J" : "-")} g={(B.grounded ? 1 : 0)} " +
                      $"p=({B.px:F3},{B.py:F3}) v=({B.vx:F3},{B.vy:F3}) fx={B.fixedIdx} up={B.update}\n");
        }

        private static bool Diff(float a, float b, float tol) => Mathf.Abs(a - b) > tol;

        private static Row[] LoadTrace(string path)
        {
            if (!File.Exists(path)) { Debug.LogError($"[UnityQA] Trace not found: {path}"); return null; }
            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2) return Array.Empty<Row>();

            var list = new System.Collections.Generic.List<Row>(lines.Length);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] c = lines[i].Split(',');
                if (c.Length < 17) continue;
                list.Add(new Row
                {
                    update = I(c[0]), fixedIdx = I(c[1]), t = P(c[2]),
                    replayFrame = I(c[3]), replayT = P(c[4]),
                    inMoveX = P(c[5]), inJumpDown = c[6] == "1", inJumpHeld = c[7] == "1",
                    ctrlMoveInput = P(c[8]), ctrlJumpRequested = c[9] == "1",
                    grounded = c[10] == "1",
                    px = P(c[11]), py = P(c[12]), vx = P(c[13]), vy = P(c[14]),
                    jumps = I(c[15]), runState = c[16]
                });
            }
            return list.ToArray();
        }

        private static float P(string s) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float f) ? f : 0f;

        private static int I(string s) =>
            int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;

        private static string Resolve(string folder)
        {
            if (string.IsNullOrEmpty(folder))
            {
                Debug.LogError("[UnityQA] Set both compare folders on the probe before diagnosing.");
                return null;
            }
            string p = Path.IsPathRooted(folder) ? folder : Path.Combine(QALogger.SessionsRoot, folder);
            if (Directory.Exists(p)) return p;
            Debug.LogError($"[UnityQA] Folder not found: {p}");
            return null;
        }
    }

    internal static class DivergenceIntExtensions
    {
        /// <summary>-1 (never differs) sorts as "after everything" in comparisons.</summary>
        public static int OrMax(this int i) => i < 0 ? int.MaxValue : i;
    }
}
