// -----------------------------------------------------------------------------
// UnityQA — OracleRegistry.cs                                    (M5 Slice B)
//
// PURPOSE
//   Explicit, ordered oracle registration. No reflection, no attribute
//   scanning, no dependency injection (per spec): an oracle exists in a run
//   because a line of code registered it — which makes the set of active
//   rules reviewable in a diff, and execution order a documented fact
//   (registration order) instead of an accident of type discovery.
// -----------------------------------------------------------------------------

using System.Collections.Generic;
using UnityEngine;

namespace UnityQA.Oracles
{
    /// <summary>Ordered oracle collection with enable/disable control.</summary>
    public sealed class OracleRegistry
    {
        private readonly List<IQualityOracle> oracles = new List<IQualityOracle>();

        /// <summary>Registration order — which IS execution order.</summary>
        public IReadOnlyList<IQualityOracle> Oracles => oracles;

        public int Count => oracles.Count;

        public int EnabledCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < oracles.Count; i++) if (oracles[i].Enabled) n++;
                return n;
            }
        }

        /// <summary>Register an oracle. Duplicate names are rejected (false) —
        /// two rules with one name would make reports ambiguous.</summary>
        public bool Register(IQualityOracle oracle)
        {
            if (oracle == null || string.IsNullOrEmpty(oracle.Name)) return false;
            if (TryGet(oracle.Name, out _))
            {
                Debug.LogWarning($"[UnityQA] Oracle '{oracle.Name}' already registered — ignored.");
                return false;
            }
            oracles.Add(oracle);
            return true;
        }

        public bool TryGet(string name, out IQualityOracle oracle)
        {
            for (int i = 0; i < oracles.Count; i++)
            {
                if (oracles[i].Name == name) { oracle = oracles[i]; return true; }
            }
            oracle = null;
            return false;
        }

        /// <summary>Enable/disable by name. False if no such oracle.</summary>
        public bool SetEnabled(string name, bool enabled)
        {
            if (!TryGet(name, out IQualityOracle oracle)) return false;
            oracle.Enabled = enabled;
            return true;
        }
    }
}
