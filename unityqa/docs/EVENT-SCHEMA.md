# EVENT-SCHEMA — UnityQA Event Model (FR-1.7, SRS §14)

**Status:** PLACEHOLDER — schema v1 is specified in SRS-M1 §14 and will be
frozen here when the event system is implemented (instrumentation milestone).
This document is the input specification for Module 6 (report generator) and
Module 7 (analysis) — once frozen, changes require a MODULES.md decision entry.

## Envelope (every event, JSONL — one JSON object per line)

| Field | Type | Required | Meaning |
|---|---|---|---|
| sid | string | yes | Session ID (`yyyyMMdd-HHmmss-<suffix>`) |
| seq | long | yes | Strictly increasing, no gaps within a session (NFR-1.5) |
| t | float | yes | Session time, seconds |
| frame | int | yes | `Time.frameCount` at emission |
| type | string | yes | One of the event types below |
| pos | {x,y} | when spatial | World position |
| payload | object | yes (may be {}) | Flat, string-keyed, type-specific |

## Event types (v1 — to be frozen)

`SessionStarted` · `SessionEnded` · `PlayerSpawned` · `PlayerDied` ·
`PlayerPositionSampled` · `TokenCollected` · `TriggerFired` · `BoundsExited` ·
`ExpectedTriggersSummary` · `AdapterWarning`

Per-type payload contracts: TBD at implementation, one subsection each.

## Session metadata (`session.json`)

`schemaVersion`, session ID, level name, start timestamp, full QAConfig
snapshot, Unity version, app version. Rationale: a log must be interpretable
without opening the project (SRS §14).
