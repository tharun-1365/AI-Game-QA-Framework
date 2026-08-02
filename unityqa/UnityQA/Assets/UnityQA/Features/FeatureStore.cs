// -----------------------------------------------------------------------------
// UnityQA — FeatureStore.cs                                      (M4 Slice A)
//
// PURPOSE
//   Owns the features.json convention — where it lives (inside its session's
//   folder, completing the artifact set: session.json, events.jsonl,
//   replay.json, validation.json, features.json) and how it is written
//   (pretty JsonUtility; D-009 lineage, zero packages). Same shape as
//   ReplayFileStore on purpose: one storage-convention class per document.
// -----------------------------------------------------------------------------

using System;
using System.IO;
using UnityEngine;

namespace UnityQA.Features
{
    /// <summary>Save/Load features.json inside a session folder.</summary>
    public static class FeatureStore
    {
        public const string FileName = "features.json";

        /// <returns>Absolute path of the written file.</returns>
        public static string Save(SessionFeatures features, string sessionFolder)
        {
            Directory.CreateDirectory(sessionFolder);
            string path = Path.Combine(sessionFolder, FileName);
            File.WriteAllText(path, JsonUtility.ToJson(features, prettyPrint: true));
            return path;
        }

        /// <summary>Load previously extracted features; null if absent/corrupt (never throws).</summary>
        public static SessionFeatures Load(string sessionFolder)
        {
            string path = Path.Combine(sessionFolder, FileName);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonUtility.FromJson<SessionFeatures>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
