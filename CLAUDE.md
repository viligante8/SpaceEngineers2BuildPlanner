# Space Engineers 2 Mod Development Guide

## Mission

Build Space Engineers 2 mods that are correct against the actual VRAGE3 engine — not against
plausible-sounding recollections of how Space Engineers 1 worked.

SE2 modding is new, sparsely documented, and structurally different from SE1. Most of what exists
publicly is prose that lags the engine. The authoritative sources are on this machine, and they are
the ones to use.

Correctness takes priority over speed.

Verification takes priority over assumptions.

Evidence takes priority over intuition.

---

# Core Principles

- Treat every claim about SE2 as untrusted until checked against the local SDK.
- Treat every assumption as a hypothesis requiring evidence.
- Prefer investigation over guessing.
- Prefer grepping the vanilla corpus over reasoning alone.
- Never confuse "how SE1 did it" with "how SE2 does it."
- Never confuse "the model remembers this" with "this is documented."
- **Never invent a GUID, a field name, a `$Type` string, or an enum value.** These are the single
  highest-risk fabrication category in this project — they look plausible, they are trivially
  checkable, and a wrong one fails at load time with an unhelpful error. See "Unknown Information."

---

# Canonical References

Three references, all reachable from this machine. Unlike a mature game's wiki-first stack, **the
authority here is inverted: the local binaries and the vanilla data outrank all prose.**

## 1. Game assembly XML documentation — the schema authority

`F:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2\*.xml`

Every shipped DLL has a paired `.xml` of C# doc comments — **~42 MB across 86 files**. This is
generated from the engine source, so it cannot drift from the engine the way prose can.

The big ones:

| File | Size | Covers |
|------|------|--------|
| `Game2.Simulation.xml` | 11 MB | Blocks, recipes, grids, gameplay definitions |
| `Game2.Client.xml` | 6.7 MB | Client-side/render/UI definitions |
| `VRage.Library.xml` | 3.6 MB | Core types, math, `Base6Directions`, etc. |
| `VRage.Render.xml` | 2.4 MB | Materials, particles, models |
| `VRage.Voxels.xml` | 2.3 MB | Planets, flora, voxel materials |
| `VRage.Core.xml` | 2.3 MB | Project/content pipeline plumbing |

It defines:

- every `…ObjectBuilder` field name and its meaning
- field-level semantics, units, and formulas
- which fields are optional
- cross-references between types (`<see cref="T:…"/>`)

Look up a field with:

```bash
grep -A5 'CubeBlockDefinitionObjectBuilder.Fragility' \
  "/f/SteamLibrary/steamapps/common/SpaceEngineers2/Game2/Game2.Simulation.xml"
```

These docs answer questions the vanilla corpus structurally cannot — a field vanilla never sets
still appears here, with its meaning. **When the vanilla data is silent, this is the source that
still has an answer.**

Doc comments can be terse or absent for a given member. Absent documentation is not permission to
guess the semantics — it is an "Unknown Information" case.

## 2. The vanilla definition corpus — the usage authority

`F:\SteamLibrary\steamapps\common\Space Engineers 2 - Mod SDK\GameData\Vanilla\`

~26,000 `.def` and ~9,100 `.partialdef` files: every block, material, particle, prefab and
recipe Keen ships. This is not documentation *about* SE2 content — it *is* SE2's content.

It defines, by demonstration:

- which fields are actually set in practice, and to what
- idiomatic structure for each definition type
- how definitions reference each other
- which `$Type` strings and bundle versions are current

The XML docs say what a field *means*; this corpus says what a *working* value looks like. Use both.

Find every real example of a type:

```bash
V="/f/SteamLibrary/steamapps/common/Space Engineers 2 - Mod SDK/GameData/Vanilla"
grep -rl "CubeBlockDefinitionObjectBuilder" "$V/Assets" --include="*.def" --include="*.partialdef"
```

Enumerate all definition types actually in use:

```bash
grep -rh '"\$Type"' "$V/Content" --include="*.def" | sed 's/.*: *"//;s/",\?$//' | sort | uniq -c | sort -rn
```

**Also here:** `GameData/Engine/` (engine-level definitions and shaders) and
`GameData/Vanilla/Worlds/`.

## 3. Official prose — Steam guide, wiki, Keen Discord

- Steam: "Space Engineers 2 | Guide: Modding Overview" —
  https://steamcommunity.com/sharedfiles/filedetails/?id=3484180972
- Official wiki: https://spaceengineers2.wiki.gg/wiki/Modding (plus `/Tutorials`,
  `/Reference`, `/Tools` subpages)
- Keen Discord / support.keenswh.com announcements

This tier explains *intent and workflow* — what the editor is for, what the pipeline stages are,
why the system is shaped this way. It is genuinely useful for orientation and for questions the data
cannot answer ("is scripting supported yet?").

**But it is thin, it lags the build, and it is the least authoritative tier.** As of now the wiki
modding page is essentially a link hub with two videos — not a schema reference. Never implement from
this tier alone. If it disagrees with the SDK, the SDK is right, and see the Disagreement Policy for
what to do before acting on that.

---

# Relationship Between References

Expected: the XML docs describe a field, the vanilla corpus shows it in use, and the prose describes
the workflow around it. All three should be consistent.

When any two disagree:

STOP. Do not implement, and do not move on to another part of the task.

Then:

1. Identify the cause. The usual explanations, in rough order of likelihood:
   - **The prose is stale.** SE2 is under active development; guides lag builds.
   - **Version skew** — see "Bundle versions" below. Compare `$Bundles` values before concluding
     anything.
   - **You compared source to build output** — `Assets/` vs `Content/`. See "Assets vs Content";
     this is not a real disagreement.
   - **Not the same thing.** Definition names repeat across contexts (client vs server, `…Definition`
     vs `…DefinitionObjectBuilder`). Confirm identity before calling it a conflict.
   - The engine genuinely changed.
2. Only then decide.

**A confirmed conflict between the assemblies/vanilla data and any prose source resolves to the
binaries — the code is the engine.** But "confirmed" means ruling out the four explanations above
first, not just noticing that two texts differ.

**Two things that are NOT a disagreement to adjudicate:**

- An `Assets/` source file differing from its `Content/` output. That is the build working.
- A field documented in XML but never used in vanilla. That is a real, unused-by-Keen field, not a
  contradiction — though it *is* untested ground, so treat it as such and say so.

---

# Evidence Hierarchy

Use evidence in this order:

1. **Game assembly XML docs** (`SpaceEngineers2\Game2\*.xml`) — schema truth
2. **Vanilla `Assets/` corpus** (Mod SDK) — usage truth, and the source-of-record form
3. **Vanilla `Content/` corpus** — build-output truth; useful for seeing the resolved result
4. **Empirical: load the mod in the editor / launch the game and observe** — the final arbiter for
   behavior. If a claim can be settled by loading it, that beats further reading.
5. Official Steam guide / wiki / Keen announcements — workflow and intent
6. Community sources (forum posts, other people's mods, YouTube) — hints only, never authority
7. Existing files in this repo
8. Model knowledge — **lowest. Especially for SE1 recollections, which are actively misleading here.**

The assemblies define what the engine accepts.

The vanilla corpus defines what actually works.

Prose defines what Keen meant.

Loading it defines what really happens.

---

# The VRAGE3 Data Model

The things that must be understood before touching any file. All of the below was verified directly
against the local SDK.

## References are GUIDs; folder structure carries no meaning

The Steam guide's headline difference from SE1: *"folder structure and naming conventions do not
matter."* This is literally true. Every asset has a GUID, and definitions refer to one another by
GUID, never by path or name.

A block definition looks like:

```json
{
  "$Bundles": { "Game2": "2.0.1.2879", "System.Runtime": "1.0.0.0", "VRage": "2.0.1.2879" },
  "$Type": "Game2:Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockDefinitionObjectBuilder",
  "$Value": {
    "Guid": "bb73a590-48d2-4396-a6cb-9a2586a37902",
    "UIData": { "Name": "CatwalkCorner", "Icon": "{G}4f4d28fa-357a-4a83-9073-70c19989a495" },
    "PlacerDefinition": "c4ac668f-bc10-4e39-81f7-ccbfb69bd4d3",
    "Recipe": "4c14352e-b147-436e-a647-098614072375",
    "BlockKind": "c173b738-5369-4b59-b14d-28b0d9e146b1",
    "PCU": 5
  }
}
```

Consequences that matter:

- **Organize files however is clearest.** The engine does not care. Humans do — be consistent anyway.
- **Renaming or moving a file does not break references.** Changing a GUID breaks everything.
- **A bare GUID string in a field is a reference.** `"{G}<guid>"` is the same thing in a context that
  also accepts inline values (note `Icon` above).
- **To understand a definition you must resolve its GUIDs.** Reading one file tells you almost
  nothing. Follow the references — this is the SE2 analogue of tracing a full execution path.

Resolve a GUID to its defining file:

```bash
V="/f/SteamLibrary/steamapps/common/Space Engineers 2 - Mod SDK/GameData/Vanilla"
grep -rl "c4ac668f-bc10-4e39-81f7-ccbfb69bd4d3" "$V/Assets"
```

GUIDs are globally unique across the corpus (verified: 3,000/3,000 distinct in a sample of
`Assets/Blocks`). **Always generate new GUIDs with a real generator — never hand-edit an existing one
into a "new" value.** A GUID collision is a silent, extremely confusing failure.

## `Assets/` is source; `Content/` is build output — never edit `Content/`

Both trees mirror each other. They are not alternatives.

| `Assets/` (source, editable) | `Content/` (generated, do not edit) |
|---|---|
| `.partialdef` | `.def` (resolved) |
| `.def` | `.def` (copied/processed) |
| `.png` | `.dds`, `.comptex` |
| `.fbx` / `.gltf` | `.vrm` |
| `*.meta` (tracks AssetID + hashes) | — |

Each `Assets/` file has a sibling `.meta` recording its `AssetID`, a `FileHash`, and the processor
that produced its output (e.g. `"Identifier": "P_PARTIAL_DEF"`).

**Editing anything in `Content/` is editing build output.** It will be silently overwritten on the
next build, or — worse — its newer timestamp will make the pipeline skip regenerating it, so a stale
or hand-edited file persists and produces mystery failures that no source diff explains. If something
behaves inexplicably, suspect a stale `Content/` before suspecting the engine.

When reading vanilla to learn the format, **read `Assets/`** — that is the form you will be authoring.
Read `Content/` only to see what a partial resolved *into*.

## Partial definitions are the primary modding primitive

To change vanilla content you do not copy and edit it — you author a `PartialDefinitionDiff` against
its GUID:

```json
{
  "$Type": "VRage:Keen.VRage.ContentPipeline.Definitions.PartialDefinitionDiff",
  "$Value": {
    "BaseDefinition": "8c3076e5-3ede-4e2d-824f-2f95a64b5d57",
    "PartialDefinitionKind": "Copy",
    "Manipulator": { "Guid": "0a72640d-...", "ServerComposite": "f9a58ffb-..." },
    "DefinitionType": "VRage:Keen.VRage.Multiplayer.Data.PrefabBindingDefinition",
    "PriorityOverride": false
  }
}
```

- `BaseDefinition` — GUID of what you are deriving from
- `PartialDefinitionKind` — `Copy` makes a new definition from the base (the guide's "partial copy").
  **Before using any other value here, confirm the enum's members from the XML docs.** Do not guess.
- `Manipulator` — the fields you are overriding
- `DefinitionType` — note this is the `…Definition`, not the `…ObjectBuilder`
- `PriorityOverride` — check the XML docs before setting this true

Prefer a partial over a full copy: it inherits later vanilla fixes instead of freezing a snapshot.

## Bundle versions

Every def carries `$Bundles` pinning the assembly versions it was authored against.

**There is live version skew on this machine**, and it is exactly the kind of thing that produces a
confusing "disagreement":

- Vanilla block defs: `Game2`/`VRage` `2.0.1.2879`
- Some vanilla defs: `2.0.1.1811`
- The scaffolded mod's `.vrgproj`: `VRage 1.5.0.3092`
- `Vanilla.vrgproj`: `VRage 0.12.9.606`

Copy `$Bundles` from a *current* vanilla def of the same type rather than inventing values or
copying from an old file. When a field seems to not exist, check whether you are reading a def from
an older bundle before concluding anything.

## Project structure

A mod is a `.vrgproj` (JSON, `VRageProjectData`) with `"Type": "Mod"`, plus `Assets/`, generated
`Content/`, and `ModProject.modhub-metadata`.

Existing scaffold on this machine:
`%USERPROFILE%\Documents\SpaceEngineers2\Mods\New SE2 Mod\`

Key fields — `ProjectID.Guid` (this mod's identity), `ProjectDependencies` (Vanilla is
`56dacb76-e25e-468a-9432-21964a9a0569`, matching `Vanilla.vrgproj`'s own `ProjectID`), and
`DefinitionSetNames` (mod scaffold: `["World"]`; vanilla: `["World", "Core"]`).

`.hashescache.json`, `.referencescache.json`, `.startupblobscache.json` and `Content/` are all
generated — never hand-edit, and never treat as source of truth.

## Current scope of SE2 modding

Data modding: models, textures, animations, block definitions, particles, materials, voxels.

**Script/code mods are not supported yet** — Keen has stated deeper scripting is planned later in
Early Access. If a task appears to require custom C# behavior, say so rather than inventing an
extension point. Re-check this against tier 3 before relying on it; it is the kind of fact that
changes between builds.

---

# Local Paths

```
SDK   /f/SteamLibrary/steamapps/common/Space Engineers 2 - Mod SDK
GAME  /f/SteamLibrary/steamapps/common/SpaceEngineers2
XML   $GAME/Game2/*.xml
V     $SDK/GameData/Vanilla
MODS  $USERPROFILE/Documents/SpaceEngineers2/Mods
```

Also in the SDK: `Editor/` (Avalonia-based mod editor), `ExampleMod1/` (raw art source only — FBX,
PNG, LODs; **no `.vrgproj` or defs**, so it is an art-pipeline reference, not a project template),
`BlenderMaterialLibrary/SE2Decal&Material.zip`.

Note these are on `F:`, outside this repo. Paths use forward slashes and `/f/` under the Bash tool;
PowerShell needs `F:\...`. Quote them — they contain spaces.

**Recursive greps over the whole corpus are slow** — ~50k files on an external drive, easily over two
minutes. Scope to a subdirectory (`$V/Assets/Blocks/Catwalks`) when the location is roughly known, and
run whole-tree searches in the background rather than blocking on them. `$GAME/Game2/*.xml` is
non-recursive and fast — prefer it for schema questions.

---

# Specification First

Before authoring or changing any definition:

1. Find the definition type in the **XML docs**; read the field summaries.
2. Find **real vanilla examples** of that type in `Assets/`.
3. **Resolve the GUID references** in those examples — the referenced definitions are usually where
   the actual behavior lives.
4. Read the relevant Steam guide / wiki section for workflow context.
5. Identify edge cases and unknowns.
6. Confirm the sources agree.
7. Only then author.

Never author a definition from memory. Never invent a field name to see if it works.

---

# Verification

There is no unit-test harness here — this is data, not code. Verification is therefore *manual and
mandatory*, not optional because it is inconvenient.

Before calling anything done:

- **Every GUID reference resolves** to a definition that actually exists. Grep each one.
- **Every `$Type` string** appears verbatim somewhere in vanilla or the XML docs.
- **Every field name** is confirmed against the XML docs for that exact type.
- **`$Bundles`** matches a current vanilla def of the same type.
- **The mod builds** in the editor without processing errors.
- **The result loads in-game and behaves as intended** — this is the real test. A def that parses is
  not a def that works.

When something fails, prefer a minimal reproduction — one definition, one changed field — over
changing several things and guessing which mattered.

Record what was verified and how. A claim of "verified" with no method behind it is worth nothing.

## Bug fixes

When something breaks and you fix it, write down the root cause and the evidence for it — not just
the change. If investigating one broken reference reveals sibling cases (the same mistake in adjacent
definitions, the same stale GUID copied elsewhere), fix and note *each*, rather than the one that was
reported. Assume there are more instances until you have grepped and confirmed otherwise.

---

# Assumptions

Whenever you think:

- "probably"
- "likely"
- "usually"
- "I think"
- "it should"
- "in SE1 this was…"

STOP.

That last one is the most dangerous in this project. SE1 knowledge is *not* transferable to VRAGE3 —
different data model, different references, different pipeline. Recalling SE1 confidently is a
failure mode, not a shortcut.

Replace assumptions with a grep, a doc lookup, or a load test.

---

# Unknown Information

Never fabricate:

- GUIDs
- field names
- `$Type` / bundle strings
- enum values (`PartialDefinitionKind`, directions, categories…)
- numeric values or units
- file-format details
- engine capabilities or limits
- editor features

If it cannot be verified:

State exactly what is unknown.

State how it could be verified (which XML file, which vanilla path, which editor action).

Leave a TODO.

Never invent behavior. **An honest "I could not confirm this field exists" is a correct answer; a
plausible invented field name is not.**

---

# Disagreement Policy

When references disagree, after ruling out the four benign explanations in "Relationship Between
References":

**STOP.** Do not implement. Do not pick whichever seems more authoritative and continue on it.

Then:

1. Document the disagreement — exact files, exact lines, exact versions.
2. Collect evidence from all available tiers.
3. Check bundle versions on both sides.
4. Determine the reason.
5. Report it, and continue only once it is understood.

The binaries outrank the prose. That resolves most conflicts — but the point is to *understand* the
conflict, not to invoke a tiebreak rule and move on. An unexplained conflict often means the mental
model is wrong somewhere else too.

---

# Bias Against Confirmation

Do not stop after finding one piece of supporting evidence.

Ask:

- Is this definition referenced from somewhere that overrides it?
- Is there a partial def modifying this base elsewhere?
- Is there a client-side *and* a server-side variant? (`_Client`, `_Server`, `_ClientComposition`,
  `_ServerComposition` are pervasive in vanilla — finding one and stopping is the single most likely
  way to miss half the picture.)
- Is this handled by the content pipeline rather than the definition?
- Am I reading a stale `Content/` file instead of the `Assets/` source?
- Is there a newer vanilla example using different fields?

Assume the first file found is incomplete until proven otherwise. In a GUID-referenced graph, the
file you found is rarely the whole story.

---

# Agent Stop Conditions

STOP and report instead of continuing when:

- references genuinely disagree after the benign explanations are ruled out
- a field, type, or enum value cannot be confirmed to exist
- a GUID reference cannot be resolved
- behavior depends on undocumented engine internals
- the task appears to require script/code modding
- the complete reference graph cannot be resolved

Never "pick the most likely answer" for a GUID, a field name, or an enum value.

Report: what was being attempted, what could not be confirmed, which sources were checked, what the
candidate explanations are, and what would settle it.

---

# Definition of Done

- The definition type is understood from the XML docs.
- Real vanilla examples were consulted.
- All GUID references resolve.
- All field names and `$Type` strings are verified.
- `$Bundles` versions are current and consistent.
- The mod builds without processing errors.
- **The change was loaded and observed in-game.**
- Non-obvious decisions are documented with their evidence.
- No undocumented assumptions remain.
- Nothing in `Content/` was hand-edited.

Parsing is never sufficient. Building is never sufficient. Confidence comes from loading it and
looking at it.

---

# Documentation

When a non-obvious mechanic is worked out, write it down: the mechanic, the source (file + line or
GUID), edge cases, and why the implementation is correct.

Most of what gets learned here is not written down anywhere public. **This file, and notes alongside
it, are the project's memory** — assume the next session has none of today's context, and that no
external source will fill the gap.

When a durable fact about the SDK, the pipeline, or the data model is confirmed, add it to this file.
It is meant to grow.

---

# Useful Commands

```bash
SDK="/f/SteamLibrary/steamapps/common/Space Engineers 2 - Mod SDK"
GAME="/f/SteamLibrary/steamapps/common/SpaceEngineers2"
V="$SDK/GameData/Vanilla"

# What does this field mean?
grep -A6 'CubeBlockDefinitionObjectBuilder.PCU' "$GAME/Game2/Game2.Simulation.xml"

# All members of a type
grep -B1 -A6 'CubeBlockDefinitionObjectBuilder\.' "$GAME/Game2/Game2.Simulation.xml" | less

# Which type contains a field name?
grep -rh 'F:Keen\..*\.Fragility"' "$GAME/Game2/"*.xml

# Real examples of a definition type (source form)
grep -rl "PowerableBlockDefinitionObjectBuilder" "$V/Assets" --include="*.def" --include="*.partialdef"

# What defines this GUID / who references it?
grep -rl "bb73a590-48d2-4396-a6cb-9a2586a37902" "$V/Assets"

# Every definition type in use, by frequency
grep -rh '"\$Type"' "$V/Content" --include="*.def" | sed 's/.*: *"//;s/",\?$//' | sort | uniq -c | sort -rn

# Current bundle versions for a type
grep -A4 '"\$Bundles"' "$V/Assets/Blocks/Antennas/Antennas/1250/Antenna1250.partialdef"

# New GUID
python -c "import uuid; print(uuid.uuid4())"
```

---

# Repo Conventions

This project is a git repository published at
`https://github.com/viligante8/SpaceEngineers2BuildPlanner`, MIT licensed (`LICENSE`).

It holds one deliverable: the **Build Planner** plugin (`BuildPlanner/`), with unit tests in
`BuildPlanner.Tests/`, engine findings in `notes/`, manual test steps in `notes/in-game-tests.md`,
and release instructions in `RELEASING.md`. `README.md` at the root is the public front page.

**Never put an absolute path containing a user name into a tracked file or a shipped binary.** A
released `.pdb` and an un-mapped DLL both published this machine's user name to every downloader
once already; `<PathMap>` in the csproj and a guard in `scripts/package.ps1` now prevent it, and the
docs use `%USERPROFILE%` or a generic `C:\SE2Mods\` example.

**Space Engineers 2 has no multiplayer.** Do not write "unverified in multiplayer", "on a dedicated
server", or "in single player" — the first two describe something that does not exist, and the third
implies a contrast with it. The client/server split inside the engine is real and important
(`notes/client-server-split.md`), but both halves run in-process, always.

Do not commit, publish to the Workshop, or otherwise push anything outward without being asked.

**No AI attribution, ever, anywhere in this repo.** No `Co-Authored-By: Claude`, no "Generated with
Claude Code", no mention of AI in commit messages, code comments, or docs. This overrides the default
Claude Code commit footer. Write commits as the author would.

---

# Do Not Substitute Inference For An Available Source

When a source *could* be obtained but is not currently at hand, **get it** — do not reason about
what it probably contains.

Concretely, when something is not on disk:

- Ask the user to subscribe/download/install it, and wait.
- Say plainly which file would settle the question.
- Do **not** write "almost certainly", "the technique is likely", or "it's probably a data mod"
  about an artifact that could simply be read.

This failure is subtle because the inference often *sounds* well-supported — it is built on real
evidence from the binaries. It is still a guess, and it still violates "Evidence takes priority over
intuition." A wrong guess here is worse than no answer, because it is delivered with the same
confidence as a verified fact.

The same applies to hitting a tooling wall: a timed-out grep, a missing binary (`strings`), a failed
reflection load, a decompile that is too slow. **A tool failing is not evidence about the subject.**
Narrow the scope, try another tool, or ask — but never convert "I could not check" into "it is
probably X."

**Reference implementations outrank reasoning about them.** One working example — a shipping mod's
source, a vanilla def — settles in seconds what an hour of decompiling only approximates. When one
exists, read it first.

*Confirmed by example:* the SE2 Programmable Block mod (Steam Workshop 3679814146, in SE1's app
`1133870`) ships `PBPatch_source/PBPatchPlugin.cs` and its `.csproj`. Reading it gave the exact
plugin-loading mechanism — `SpaceEngineers2.exe -loadScripts -plugins:path\to\Plugin.dll`,
`Keen.VRage.Core.Plugins.IPlugin`, `PluginHost.OnBeforeEngineInstantiated`, MonoMod
`RuntimeDetour.Hook` — none of which was reachable by inference.
---

# Debugging Runtime Code: Log Every Branch, Verify The Object First

These rules were paid for during the Build Planner plugin. Each cost multiple game restarts.

## A silent code path is a broken code path

**Every early `return` in a handler must say why it returned** — not just error paths, the
"nothing to do" paths too.

The failure mode is specific and expensive: a keypress produced *no log line at all*, which looks
identical to "the key never reached my code". Several restarts went into hunting an input-routing
bug (context eviction, layer conflicts, key contention) before enabling the engine's own input trace
proved the key had been dispatching correctly the entire time. The handler was running, hitting
`if (destination == null) return;`, and vanishing without a word.

```csharp
// WRONG - indistinguishable from "never called"
if (destination == null) return;

// RIGHT
if (destination == null) { _notifier.Warning("could not find your inventory"); return; }
```

Silence is the most misleading signal in this project because it is consistent with *every*
hypothesis. Make it impossible.

## Verify the object before fixing what you do on it

When a lookup returns null, **dump what you are actually holding before theorising about why the
lookup failed.**

Concretely: `TryGet<InventoryComponent>()` on the "character" returned null. The plausible cause —
correct in general — is that a character carries three `InventoryComponent`s bound to tag slots
(`Inventory`, `ConsumableInventory`, `DatapadInventory`), so an untagged lookup cannot disambiguate.
That fix was implemented, deployed, and changed nothing, because the real problem was that the entity
was not the character at all.

The tell was already in the log and was not acted on: `no BlockPlacerEntityComponent on character`
had been printing on *every* right-click. Two components that must exist on a player character, both
absent, is one bug — the wrong entity — not two independent lookup bugs.

Before fixing a failed lookup, log the target's identity and component list:

```csharp
Log.Write($"  debug: no InventoryComponent on '{DescribeEntity(entity)}'");
```

`Entity.DebugName` and `Entity.Components` (an `ImmutableArray<Component>`) are both public.

**Two failed lookups on the same object are evidence about the object, not about the lookups.**

## Use the engine's own diagnostics before building a theory

`ActionProcessorDebugObject.DetailedInputLog` makes `GameInputProcessorComponent` log every control
it consumes or discards, and why, into the game log:

```
[Input][#1721]: Consuming input Keyboard::N in layer #36
[Input][#1721]: Control Keyboard::N : BuildPlannerWithdraw activated with state Start.
[Input][#2244]: Discard candidate control Keyboard::Escape, input Keyboard::Escape already consumed.
```

That output settled in one test what several rounds of decompiling and inference had got wrong.
Reach for engine-side tracing early — the engine knows what it did.

## Do not put a plugin log where the game prunes it

`%APPDATA%\SpaceEngineers2\Temp\Logs\` is **cleared on startup**. A log written there is destroyed by
the next launch, including the log of the run being diagnosed. Use a sibling directory
(`%APPDATA%\SpaceEngineers2\BuildPlanner\`) so history survives, which also lets the user run several
tests before anything is read.

## Reflection lookups: expect ambiguity

`GetMethod`/`GetProperty` by name throws `AmbiguousMatchException` when a member is overloaded or
redeclared in a derived class. Both bit this project:

- `GameEntityExtensions.GetSession` — non-generic `GetSession(Entity)` plus generic
  `GetSession<T>(Entity)`. Filter with `!IsGenericMethodDefinition` and an exact parameter match.
- `GameInputProcessorComponent.DebugObject` — redeclared with a covariant return type. Walk the type
  hierarchy with `BindingFlags.DeclaredOnly` and take the most-derived.

Prefer a direct compiled call whenever the type is public — the compiler checks it and it cannot
drift silently.

---

# SE2 Code Mods Via Plugins — Confirmed Working

**"Current scope of SE2 modding" above is out of date on one point.** In-game scripting (the
programmable-block sandbox) is still unfinished, but **arbitrary C# runs today via the plugin
system**, verified end to end on this machine.

```
SpaceEngineers2.exe -plugins:C:\path\to\YourPlugin.dll
```

- Entry point: implement `Keen.VRage.Core.Plugins.IPlugin`. `PluginHost` instantiates via
  `Activator.CreateInstance(pluginType, this)`, falling back to a parameterless constructor — provide
  both.
- Lifecycle: `PluginHost.OnBeforeEngineInstantiated(EngineBuilder)` and `OnBeforeProjectsLoaded`.
- Patching: MonoMod `RuntimeDetour` — `new Hook(methodInfo, replacementDelegate)`.
- csproj: `net9.0`, `<EnableDynamicLoading>true`, `<Reference>` each `Game2\*.dll` with
  `<Private>false</Private>`.
- `-loadScripts` is **not** required for a plugin; that flag is for in-game scripting.

## You cannot register a new Component type from a plugin

`EngineBuilder.Add<MyComponent>()` calls `RuntimeComponentInfo.For(type)`, resolving through
`MetadataManager.GetActiveContext()`. That context is built once from the **entry assembly**
(`MetadataManager.InitializeWithEntryAssembly`); a dynamically loaded plugin assembly is not in it,
so the lookup returns null and `Add` throws `NullReferenceException` inside `CreateIfNeeded`.

Attach to a component the engine already knows: detour a suitable existing method (for input work,
`InputGameComponent.Init`) and hang the behaviour off that.

## Input system facts

- **One context per named layer.** `GameInputProcessorComponent.ActivateContext` deactivates whatever
  occupies a named layer and takes its place. Reusing a vanilla `InputContextDefinition` (which
  carries a `Layer`) makes mod and game evict each other — the symptom is a handler that binds
  without error and never fires. Construct a **layer-less** context instead:
  `new InputContextDefinition(actions)` appends to `_activeContexts` and coexists.
- **An input is consumed once per frame.** `DisambiguatingControlActivationFilter` logs
  "Discard candidate control ..., input already consumed"; `ProcessActionsPerContext` assigns each
  control to exactly one context. Sharing a key with a vanilla action is a race, not coexistence —
  pick an unbound key. (`Mouse::Middle` is bound in vanilla to `ToolTertiary`, `PaintBlock` and
  `ToggleGridFollowing`; `Keyboard::N` has zero references.)
- **`ControlCustomizationEngineComponent` owns the mapping.** It keeps `_baseMappings` and rebuilds
  the processor mapping from it whenever custom binds change, silently discarding anything added
  straight to the processor. The game log shows this as `228 Mapping added` followed by
  `227 Mapping added`. Hook its `SetMapping` and inject into the mapping it is about to publish.
- **Appearing in Options -> Controls:** `ControlCustomizationViewModel` builds from
  `mapping.ControlsPerAction`, drops actions whose `Category` is null or the hidden category, and
  orders groups by `ActionCategoryConfiguration.OrderedControlCategories`. Give the action a `Name`
  (`StringId`) and a real `Category` — vanilla "BuildingControls" is
  `480bde0d-9a98-48fb-bffb-40cc0e156c30`. Both are `private set`; set the backing fields
  (`<Name>k__BackingField`, `<Category>k__BackingField`) by reflection.
- **Timing matters:** the controls menu is populated during startup, *before*
  `InputGameComponent.Init`. An action created at attach time is too late to appear — create it on
  demand inside the `SetMapping` hook.

## Reaching the world session from outside a session

A plugin component lives on the **engine** entity, whose scene is the app-level `GameCoreScene`, not
a `Session`. `Entity.GetSession()` there throws `InvalidCastException: Unable to cast GameCoreScene
to Session`. Use the route `McpServerComponent` uses:

```csharp
var scene = hostEntity.Scene?.UserObject as GameCoreScene;
var session = scene?.GameClient?.Get<WorldSessionComponent>()?.OwnedSession;
```

Resolve per call — it is null at the main menu and changes with each world load.

## Mod content-cache validation

A mod's `content/contentcache.json` records each file's expected length. If a shipped `.def` does not
match, the engine logs `[Content Validation]: Failed to validate ... Differing file length (expected
N, got M)`, shows **"Loading Failed. Corrupted files have been found."**, and refuses to mount those
defs. The rest of the mod still loads, so the symptom is a block that never appears. Seen in a
published Workshop mod whose author edited defs after generating the cache.

## Entity component lookup

`Entity.TryGet<T>(StringId tag = default)` takes an optional tag. Entities carrying several
components of the same type bind them to **tag slots** in their `EntityCompositeDefinition` — the
character has `Inventory`, `ConsumableInventory` and `DatapadInventory`. Pass the tag when the
composite defines one.

---

# Testing Policy

**Integration failures dominate here, and unit tests cannot see them.** Every bug in the Build
Planner plugin — wrong entity, input-context eviction, mapping reset, tag slots, silent returns —
was a fact about the live engine. All would have passed a green test suite.

So the split is:

- **Pure logic gets unit tests** — component-total merging, multipliers, outcome classification,
  modifier resolution. These are regression-proofing for refactors, and they run without the game.
- **Everything touching the engine gets a logged, reproducible in-game check.** The log *is* the test
  output. Keep every branch reported (see "A silent code path is a broken code path") so a run can be
  read after the fact instead of re-run under observation.

Do not let a passing suite stand in for loading the mod. "Definition of Done" still requires
observing the change in-game.
