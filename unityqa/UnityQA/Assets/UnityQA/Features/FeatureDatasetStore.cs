// -----------------------------------------------------------------------------
// UnityQA — FeatureDatasetStore.cs                               (M4 Slice B)
//
// PURPOSE
//   Storage conventions for the dataset — BOTH artifacts, one class (the
//   established one-store-per-document pattern):
//     dataset.json  — full document incl. statistics (machine-of-record)
//     features.csv  — flat rows for external ML tooling (pandas/sheets); the
//                     bridge out of Unity that Milestone 5 will actually load.
//
// CSV RULES (deterministic by construction)
//   Column order = fixed identity columns + FeatureDatasetBuilder.Selectors
//   order — the SAME table that orders statistics, so the two can never
//   drift. Invariant culture, floats "0.####". A feature whose source group
//   is unavailable for a row is an EMPTY cell (reads as NaN in pandas), never
//   a fake zero. Strings are quoted only when they contain a comma/quote.
// -----------------------------------------------------------------------------

using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace UnityQA.Features
{
    /// <summary>Save/Load dataset.json and export features.csv at a Sessions root.</summary>
    public static class FeatureDatasetStore
    {
        public const string JsonFileName = "dataset.json";
        public const string CsvFileName = "features.csv";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static string SaveJson(FeatureDataset dataset, string sessionsRoot)
        {
            Directory.CreateDirectory(sessionsRoot);
            string path = Path.Combine(sessionsRoot, JsonFileName);
            File.WriteAllText(path, JsonUtility.ToJson(dataset, prettyPrint: true));
            return path;
        }

        public static FeatureDataset LoadJson(string sessionsRoot)
        {
            string path = Path.Combine(sessionsRoot, JsonFileName);
            if (!File.Exists(path)) return null;
            try { return JsonUtility.FromJson<FeatureDataset>(File.ReadAllText(path)); }
            catch (Exception) { return null; }
        }

        public static string SaveCsv(FeatureDataset dataset, string sessionsRoot)
        {
            Directory.CreateDirectory(sessionsRoot);
            string path = Path.Combine(sessionsRoot, CsvFileName);
            File.WriteAllText(path, BuildCsv(dataset), new UTF8Encoding(false));
            return path;
        }

        /// <summary>CSV text for a dataset — public and pure so tests pin the exact format.</summary>
        public static string BuildCsv(FeatureDataset dataset)
        {
            var sb = new StringBuilder(4096);

            sb.Append("sessionId,folderName,level,sessionStatus,validationVerdict");
            foreach (FeatureSelector s in FeatureDatasetBuilder.Selectors)
                sb.Append(',').Append(s.Name);
            sb.Append('\n');

            foreach (SessionFeatures row in dataset.rows)
            {
                sb.Append(Escape(row.sessionId)).Append(',')
                  .Append(Escape(row.sessionFolderName)).Append(',')
                  .Append(Escape(row.level)).Append(',')
                  .Append(Escape(row.sessionStatus)).Append(',')
                  .Append(Escape(row.validationAvailable ? row.validationVerdict : ""));

                foreach (FeatureSelector s in FeatureDatasetBuilder.Selectors)
                {
                    sb.Append(',');
                    if (s.Available(row))
                        sb.Append(s.Get(row).ToString("0.####", Inv));
                    // else: empty cell — honest missingness, not a fake zero
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (value.IndexOf(',') < 0 && value.IndexOf('"') < 0 && value.IndexOf('\n') < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
