# Security

Build Planner is a **plugin**, not a data mod. The launch option tells the game to load a DLL, which
then runs with the same access to your machine as the game itself. It patches engine methods at
runtime and reads private engine fields by reflection. That is a lot of trust to ask for a keybind
mod, and it is true of every code mod, from anyone.

**Do not take my word for it that it is harmless, and do not make a habit of trusting random
uploads.** Everything on this page is a claim until you check it, so here is how to check it.

Three routes, in increasing order of effort:

1. **Read the DLL.** Any .NET assembly decompiles back to readable C#. Open `BuildPlanner.dll` in
   [ILSpy](https://github.com/icsharpcode/ILSpy) — free, no install needed for the portable build —
   and read what it actually does. It is about 7,000 lines and the parts that touch your machine at
   all are a few dozen.
2. **Read the source.** It is this repository, at the tag matching your download. Same code, laid
   out for humans rather than reconstructed by a decompiler.
3. **Build it yourself** and run that instead of my download. Step 3 below.

## What it does and does not do

| | |
|---|---|
| Network connections | **None.** No socket, no HTTP, no DNS. |
| Starting processes | **None.** |
| Registry | **None.** |
| Native calls in `BuildPlanner.dll` | **None.** MonoMod has them - see below. |
| Files it reads | Its own folder only: the optional `quiet`, `trace-input` and `queries.txt`. |
| Files it writes | Its own log, plus your control bindings (below). |
| Bundled binaries | Unmodified MonoMod and Mono.Cecil from NuGet. Hashes below. |

### What it changes outside its own folder, deliberately

1. **Your control bindings.** The nine actions appear in Options -> Controls -> Building and are
   saved like any other binding, in
   `%APPDATA%\SpaceEngineers2\AppData\EngineOptions\CustomizedControlsOptionsPart`.
2. **One cleanup of that file, once per launch.** Versions before 1.0.0 wrote bindings with an
   all-zero action ID. Those are removed at startup
   (`BuildPlannerBinding.PurgeOrphanedCustomizations`). The game cannot resolve an all-zero ID to
   any action, so nothing that works is removed - every real binding has a real ID.
3. **Your world save** - inventory contents and assembler queues, exactly as the controls describe.

Beyond those three it touches nothing: it does not open your save files directly, read your
screenshots, or go anywhere else on your machine.

## Verify it yourself

### 1. Check the bundled dependencies are the real ones

```powershell
Expand-Archive BuildPlanner-*.zip -DestinationPath .\bp
Get-FileHash .\bp\*.dll -Algorithm SHA256 | Format-Table Hash,Path -AutoSize
```

Every DLL except `BuildPlanner.dll` must be one of these:

```
831DCA77470D85CB6FFBEA3072DAA7A3DF5B7C9FCFD9C3F43674A9BE99D4BFCF  Mono.Cecil.dll
28CB367972BDC1CD43E4006306AF2FD96D37F4ED4B239EE90E1DC7237A93AF7F  Mono.Cecil.Mdb.dll
A332332633FBCB20E8D50E49B4DB7BD1557721417122CF0C5F4C42F2332391D0  Mono.Cecil.Pdb.dll
BF992F3DCE364EBCC3200FA7832EF07E20B4E2DBC3A8A6213CE44E3D239DB984  Mono.Cecil.Rocks.dll
AC3F32BFD44AAB83ABF71ABDFF6DDE548D57B7C0F8A1FE6D8964E348B4EEAFB1  MonoMod.Backports.dll
EA64FD108F9FFF734E00D5E6D744CA9B8DBE6C0F388854212EFAC661315EA90C  MonoMod.Core.dll
DDA580D518F2CB732188478B2EE9AA92AC94F31DD5B0A500B9D9604EB340202D  MonoMod.Iced.dll
FDD0E3538340FD78B8F521E62B8CAC1EBB7683AC1F27F6AAEDFC1044B14BF4BB  MonoMod.ILHelpers.dll
558E6D2DA32C3CB6895B52D1F51DDCDD7FDCCDB29EBBD0B5DB739304BE864BC7  MonoMod.RuntimeDetour.dll
28DA8241F93E16A04B0E113C75D490415705F06CC7F699C6DBB00C70C56E90B1  MonoMod.Utils.dll
```

Do not trust that table either - rebuild it from nuget.org:

```powershell
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/monomod.runtimedetour/25.3.3/monomod.runtimedetour.25.3.3.nupkg
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/monomod.core/1.3.3/monomod.core.1.3.3.nupkg
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/monomod.utils/25.0.11/monomod.utils.25.0.11.nupkg
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/monomod.backports/1.1.2/monomod.backports.1.1.2.nupkg
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/monomod.ilhelpers/1.1.0/monomod.ilhelpers.1.1.0.nupkg
curl.exe -sLO https://api.nuget.org/v3-flatcontainer/mono.cecil/0.11.6/mono.cecil.0.11.6.nupkg

dotnet nuget verify *.nupkg --all
```

Each must report a valid repository signature from `CN=NuGet.org Repository by Microsoft`. Then
unzip each and hash the DLLs, and compare. Note which framework folder each ships from:

| Package | Folder |
|---|---|
| MonoMod.RuntimeDetour, MonoMod.Core (which also contains MonoMod.Iced), MonoMod.Utils | `lib\net9.0\` |
| MonoMod.Backports, MonoMod.ILHelpers | `lib\net8.0\` - neither ships a net9.0 build |
| Mono.Cecil and its three siblings | `lib\netstandard2.0\` |

> The `sha512` values in `BuildPlanner.deps.json` will **not** match the `.nupkg.sha512` files in
> your NuGet cache. That is expected: deps.json records NuGet's *content* hash, while
> `.nupkg.sha512` hashes the *signed package file*. `dotnet nuget verify` prints the content hash -
> that is the one matching deps.json.

### 2. Check `BuildPlanner.dll` makes no network, process, registry or native calls

Ask the binary rather than reading 7,000 lines of C#. ILSpy, dnSpy or `ildasm` all work:

```powershell
ilspycmd -r . .\bp\BuildPlanner.dll |
  Select-String 'System\.Net|Diagnostics\.Process|Microsoft\.Win32|DllImport|LoadLibrary|GetProcAddress'
```

Expect **zero matches**. The only I/O types the assembly references anywhere are `System.IO.File`,
`Directory`, `Path` and `DirectoryInfo` - the last being the discarded return type of
`Directory.CreateDirectory`, which creating the log folder needs. Its P/Invoke count is **0**.

### 3. Check the DLL was built from this source

Builds are deterministic (`Deterministic` and `PathMap` in `BuildPlanner/BuildPlanner.csproj`), so
the same commit rebuilds to the same bytes:

```powershell
git clone https://github.com/viligante8/SpaceEngineers2BuildPlanner
cd SpaceEngineers2BuildPlanner
git checkout v1.0.3          # the tag matching the release you downloaded
.\packaging\package.ps1 -Version 1.0.3
```

Use the version you actually downloaded; `git tag` lists the tags that exist.

Hash the resulting `BuildPlanner.dll` and compare it with the one in the release.

**This needs Space Engineers 2 installed** - the plugin compiles against Keen's shipped assemblies,
which are not redistributable, which is also why there is no CI (see `RELEASING.md`). It is the only
end-to-end check, and it is the one a reviewer without the game cannot perform.

If the hashes differ, check your .NET SDK version and your Space Engineers 2 version before assuming
the worst. Both feed the compiler, and a different SE2 build can change referenced assembly
versions. A mismatch is a reason to open an issue, not proof of tampering.

### 4. Watch what it actually touches

Run [Process Monitor](https://learn.microsoft.com/sysinternals/downloads/procmon) filtered to
`SpaceEngineers2.exe`, with and without the launch option. The difference should be the
`BuildPlanner\` folder and the game's own options file. Add a network filter and there should be no
difference at all.

## Why your antivirus may complain

MonoMod patches methods in memory, which needs `VirtualAlloc`, `VirtualProtect`,
`FlushInstructionCache`, `LoadLibraryW` and `GetProcAddress`. Making a page writable, rewriting
code, and making it executable again is also what an injector does, so heuristic scanners sometimes
flag it.

`BuildPlanner.dll` contains **zero** native imports. All of them are in `MonoMod.Core.dll` and
`MonoMod.Utils.dll`, which step 1 proves are the unmodified upstream MIT libraries used by most
.NET game mods.

The release is not Authenticode-signed - a code-signing certificate costs more than this hobby
project is worth - so SmartScreen may warn as well. Verify by hash instead.

## The diagnostic query file

If `%APPDATA%\SpaceEngineers2\BuildPlanner\queries.txt` exists, pressing the diagnose key
(`SHIFT+ALT+CTRL+N`) reads it, walks the dotted paths in it against live game state, and writes what
it finds to the log. It is a debugging aid, and it does nothing unless you create the file.

Its limits, all enforced in `BuildPlanner/src/Diagnostics.cs`:

- **Read-only.** There is no `SetValue`, and no call to a method with arguments, anywhere in it.
- **Three fixed roots** - `tool`, `planner`, `clientsession`. Anything else throws.
- **Instance members only.** The reflection flags deliberately omit `BindingFlags.Static`, so there
  is no route from a game object into the .NET runtime, the filesystem, or anything outside the
  reachable game object graph.

Two honest caveats:

- Navigating to a property runs that property's getter, and getters can have side effects.
- A deep query can produce a very large log and freeze the game while it writes. Depth is capped
  at 4; that is still a lot.

**Treat `queries.txt` as executable input.** Do not paste one from someone you do not trust, for the
same reason you would not paste a console command. And because the log then holds whatever the query
asked for, skim it before attaching it to a bug report. By default the log holds only the plugin's
own actions: block names, item counts, component lists.

## Reporting a problem

Open an issue at https://github.com/viligante8/SpaceEngineers2BuildPlanner/issues
