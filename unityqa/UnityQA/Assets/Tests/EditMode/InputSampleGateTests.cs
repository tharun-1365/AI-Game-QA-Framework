// -----------------------------------------------------------------------------
// UnityQA Tests — InputSampleGateTests.cs                   (M2 Slice D tests)
//
// Pins the emit-decision contract: change detection, keyframe cadence, and
// the no-spam guarantee. Pure logic, no scene — which is exactly why the gate
// was extracted from the recorder.
// -----------------------------------------------------------------------------

using NUnit.Framework;
using UnityQA.Core;

namespace UnityQA.Tests
{
    public sealed class InputSampleGateTests
    {
        private static PlayerInputState S(int h, bool jump) =>
            new PlayerInputState { horizontal = h, jumpHeld = jump };

        [Test]
        public void UnchangedInput_BeforeKeyframe_DoesNotEmit()
        {
            bool emit = InputSampleGate.ShouldEmit(S(1, false), S(1, false),
                stepsSinceKeyframe: 10, keyframeEverySteps: 250, out bool kf);
            Assert.IsFalse(emit, "constant input must not spam");
            Assert.IsFalse(kf);
        }

        [Test]
        public void HorizontalChange_Emits_NotAsKeyframe()
        {
            bool emit = InputSampleGate.ShouldEmit(S(0, false), S(1, false), 10, 250, out bool kf);
            Assert.IsTrue(emit);
            Assert.IsFalse(kf, "a change emission is not a keyframe");
        }

        [Test]
        public void JumpHeldChange_Emits_BothDirections()
        {
            Assert.IsTrue(InputSampleGate.ShouldEmit(S(0, false), S(0, true), 10, 250, out _),
                "press (held false→true) must emit");
            Assert.IsTrue(InputSampleGate.ShouldEmit(S(0, true), S(0, false), 10, 250, out _),
                "release (held true→false) must emit");
        }

        [Test]
        public void KeyframeDue_Emits_EvenWithoutChange()
        {
            bool emit = InputSampleGate.ShouldEmit(S(-1, true), S(-1, true), 250, 250, out bool kf);
            Assert.IsTrue(emit);
            Assert.IsTrue(kf);
        }

        [Test]
        public void KeyframeBoundary_IsAtOrPast_NotBefore()
        {
            InputSampleGate.ShouldEmit(S(0, false), S(0, false), 249, 250, out bool before);
            InputSampleGate.ShouldEmit(S(0, false), S(0, false), 250, 250, out bool at);
            Assert.IsFalse(before);
            Assert.IsTrue(at);
        }

        [Test]
        public void ChangeAndKeyframeTogether_EmitsOnce_FlaggedKeyframe()
        {
            bool emit = InputSampleGate.ShouldEmit(S(0, false), S(1, false), 300, 250, out bool kf);
            Assert.IsTrue(emit);
            Assert.IsTrue(kf, "when both apply, the record is a keyframe (full state either way)");
        }

        [Test]
        public void MisconfiguredCadence_DegradesToEveryStep_NeverThrows()
        {
            // Same guard philosophy as JsonlSink's flushEveryN: degenerate, don't throw.
            bool emit = InputSampleGate.ShouldEmit(S(0, false), S(0, false), 1, 0, out bool kf);
            Assert.IsTrue(emit);
            Assert.IsTrue(kf);
        }
    }
}
