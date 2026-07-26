# UnityQA — AI-Powered Automated Game QA Framework for Unity

> **Status: Module 1, Milestone 1** — project scaffold + BenchGame foundation.
> An autonomous QA framework that explores 2D platformer levels, detects
> gameplay bugs (soft locks, unreachable areas, missing triggers, out-of-bounds),
> collects evidence, and generates professional bug reports.

*(README is a stub during early milestones; it grows a GIF and architecture
diagram at each milestone per the design doc. Full documentation lives in
[`docs/`](docs/): [Design](docs/DESIGN.md) · [SRS Module 1](docs/SRS-Module1.md) ·
[GUT Spec](docs/GUT-SPEC.md) · [Benchmark](docs/BENCHMARK.md) ·
[Build Log](docs/MODULES.md).)*

## Layout

- `UnityQA/` — Unity 6.3 LTS project (framework in `Assets/UnityQA/`, game
  under test in `Assets/TestGame/`)
- `docs/` — design document, SRS, specifications, build log
- `reports/` — sample generated QA session outputs (later milestones)

## Quick start

1. Unity Hub → **Add** → select the `UnityQA/` folder → open with **Unity 6000.3.x LTS**
   (if Hub offers to upgrade to your installed 6.3 patch, accept).
2. First load takes a minute (package import + script compile). On first load the
   `LevelBaselineBuilder` automatically opens `Level_Baseline`, paints the level
   geometry, and saves the scene — watch the Console for `[BenchGame]` messages.
3. Press **Play**. Move with **A/D** or **←/→**, jump with **Space**.

If the level ever looks empty or broken: menu **BenchGame ▸ Rebuild
Level_Baseline From Scratch** regenerates the entire scene from code.

## Requirements

Unity 6.3 LTS (6000.3.x) · Git with LFS (`git lfs install` before cloning)
