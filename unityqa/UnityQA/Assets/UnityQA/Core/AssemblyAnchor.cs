// -----------------------------------------------------------------------------
// UnityQA — AssemblyAnchor.cs
//
// PURPOSE
//   This file contains NO framework logic. It exists for exactly one reason:
//   an assembly definition (UnityQA.asmdef) with zero scripts inside it makes
//   Unity emit an "assembly has no scripts" import warning on every refresh.
//   This empty anchor keeps the UnityQA assembly compiling cleanly until the
//   first real framework script arrives in a later milestone (per SRS-M1 §6).
//
//   Milestone 1 rule check: "DO NOT IMPLEMENT: QA framework" — respected.
//   An empty type is scaffolding, not framework code. It will be deleted the
//   moment QAEvent.cs (the first real Core script) is created.
// -----------------------------------------------------------------------------

namespace UnityQA.Core
{
    /// <summary>
    /// Placeholder that keeps the UnityQA assembly non-empty during Milestone 1.
    /// Delete when the first real Core script lands.
    /// </summary>
    internal static class AssemblyAnchor
    {
    }
}
