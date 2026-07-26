// -----------------------------------------------------------------------------
// UnityQA Tests — QAEventBusTests.cs                        (M2 test plan §12)
//
// Verifies the two guarantees the bus advertises (delivery + exception
// isolation) and the reentrancy case that breaks naive implementations.
// Pure C# — no scene, runs in milliseconds (why the bus is not a MonoBehaviour).
// -----------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityQA.Core;

namespace UnityQA.Tests
{
    public sealed class QAEventBusTests
    {
        private static QAEvent MakeEvent(long seq = 0) =>
            new QAEvent("test-sid", seq, 0f, 0, QAEventType.SessionStarted, null, null);

        [Test]
        public void Publish_ReachesAllSubscribers()
        {
            var bus = new QAEventBus();
            int a = 0, b = 0;
            bus.Subscribe(_ => a++);
            bus.Subscribe(_ => b++);

            bus.Publish(MakeEvent());

            Assert.AreEqual(1, a);
            Assert.AreEqual(1, b);
        }

        [Test]
        public void Unsubscribe_StopsDelivery()
        {
            var bus = new QAEventBus();
            int calls = 0;
            Action<QAEvent> handler = _ => calls++;
            bus.Subscribe(handler);
            bus.Publish(MakeEvent());
            bus.Unsubscribe(handler);
            bus.Publish(MakeEvent());

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void ThrowingSubscriber_DoesNotBreakOthers()
        {
            var bus = new QAEventBus();
            int survivor = 0;
            bus.Subscribe(_ => throw new InvalidOperationException("deliberate test explosion"));
            bus.Subscribe(_ => survivor++);

            // The bus reports the exception via Debug.LogException; tell the
            // test runner an error log is expected so it doesn't fail the test.
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("deliberate test explosion"));

            bus.Publish(MakeEvent());

            Assert.AreEqual(1, survivor, "subscriber after the throwing one must still be called");
        }

        [Test]
        public void UnsubscribingDuringPublish_IsSafe_AndTakesEffectNextPublish()
        {
            var bus = new QAEventBus();
            int calls = 0;
            Action<QAEvent> selfRemover = null;
            selfRemover = _ => { calls++; bus.Unsubscribe(selfRemover); };
            bus.Subscribe(selfRemover);

            bus.Publish(MakeEvent()); // must not throw (snapshot iteration)
            bus.Publish(MakeEvent()); // removed by now

            Assert.AreEqual(1, calls);
        }

        [Test]
        public void SubscribingDuringPublish_DoesNotReceiveCurrentEvent()
        {
            var bus = new QAEventBus();
            int lateCalls = 0;
            bus.Subscribe(_ => bus.Subscribe(__ => lateCalls++));

            bus.Publish(MakeEvent());
            Assert.AreEqual(0, lateCalls, "late subscriber must not see the in-flight event");

            bus.Publish(MakeEvent());
            Assert.AreEqual(1, lateCalls, "but must see the next one");
        }
    }
}
