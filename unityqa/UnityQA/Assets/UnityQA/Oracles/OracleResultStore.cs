// -----------------------------------------------------------------------------
// UnityQA — OracleResultStore.cs                                 (M5 Slice B)
//
// PURPOSE
//   Storage convention for oracle-results.json at the Sessions root — the
//   fifth collection-level document. Same one-store-per-document pattern as
//   every sibling (catalog, dataset, csv, analysis).
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace UnityQA.Oracles
{
    /// <summary>Save/Load oracle-results.json at a Sessions root.</summary>
    public static class OracleResultStore
    {
        public const string FileName = "oracle-results.json";

        public static string Save(OracleRunResults results, string sessionsRoot)
        {
            Directory.CreateDirectory(sessionsRoot);
            string path = Path.Combine(sessionsRoot, FileName);
            File.WriteAllText(path, JsonUtility.ToJson(results, prettyPrint: true));
            return path;
        }

        public static OracleRunResults Load(string sessionsRoot)
        {
            string path = Path.Combine(sessionsRoot, FileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<OracleRunResults>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }
    }
}
