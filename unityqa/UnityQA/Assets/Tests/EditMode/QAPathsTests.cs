// -----------------------------------------------------------------------------
// UnityQA Tests — QAPathsTests.cs                       (M5 Slice D, D-013)
//
// Pins the storage contract: every root lives under one project-local QAData
// tree (in the editor, where these tests run), the layout names are stable,
// and nothing lands inside Assets/ (Unity would import and .meta it).
// -----------------------------------------------------------------------------

using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityQA.Core;
using UnityQA.Logging;

namespace UnityQA.Tests
{
    public sealed class QAPathsTests
    {
        [Test]
        public void DataRoot_IsProjectLocal_InEditor()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Assert.AreEqual(Path.Combine(projectRoot, QAPaths.DataFolderName), QAPaths.DataRoot,
                "editor data root must be <project>/QAData — inspectable next to Assets/");
        }

        [Test]
        public void AllRoots_LiveUnderDataRoot_WithStableNames()
        {
            Assert.AreEqual(Path.Combine(QAPaths.DataRoot, "Sessions"), QAPaths.SessionsRoot);
            Assert.AreEqual(Path.Combine(QAPaths.DataRoot, "Datasets"), QAPaths.DatasetsRoot);
            Assert.AreEqual(Path.Combine(QAPaths.DataRoot, "Analysis"), QAPaths.AnalysisRoot);
            Assert.AreEqual(Path.Combine(QAPaths.DataRoot, "Reports"), QAPaths.ReportsRoot);
            Assert.AreEqual(Path.Combine(QAPaths.DataRoot, "Exports"), QAPaths.ExportsRoot);
        }

        [Test]
        public void DataRoot_IsNotInsideAssets()
        {
            string assets = Application.dataPath.Replace('\\', '/');
            string data = QAPaths.DataRoot.Replace('\\', '/');
            Assert.IsFalse(data.StartsWith(assets + "/"),
                "QAData inside Assets/ would be imported (and .meta'd) by Unity");
        }

        [Test]
        public void QALogger_SessionsRoot_IsTheQAPathsRoot()
        {
            // Every existing consumer resolves through QALogger.SessionsRoot;
            // this pins that the relocation reached all of them at once.
            Assert.AreEqual(QAPaths.SessionsRoot, QALogger.SessionsRoot);
        }
    }
}
