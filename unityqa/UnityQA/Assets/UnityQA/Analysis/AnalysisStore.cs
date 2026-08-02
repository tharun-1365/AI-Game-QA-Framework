// -----------------------------------------------------------------------------
// UnityQA — AnalysisStore.cs                                     (M5 Slice A)
//
// PURPOSE
//   Storage convention for analysis.json at the Sessions root — the fourth
//   collection-level document (catalog.json, dataset.json, features.csv,
//   analysis.json). Same one-store-per-document pattern as every sibling.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace UnityQA.Analysis
{
    /// <summary>Save/Load analysis.json at a Sessions root.</summary>
    public static class AnalysisStore
    {
        public const string FileName = "analysis.json";

        public static string Save(DatasetAnalysis analysis, string sessionsRoot)
        {
            Directory.CreateDirectory(sessionsRoot);
            string path = Path.Combine(sessionsRoot, FileName);
            File.WriteAllText(path, JsonUtility.ToJson(analysis, prettyPrint: true));
            return path;
        }

        public static DatasetAnalysis Load(string sessionsRoot)
        {
            string path = Path.Combine(sessionsRoot, FileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<DatasetAnalysis>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }
    }
}
