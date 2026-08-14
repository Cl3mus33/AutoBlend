# AutoBlend

A standalone tool for Skyrim Special Edition that scans your load order for landscape texture
variants and patches the meshes that use them to alpha-blend instead of alpha-test — the same
kind of fix mods like Vanaheimr ship by hand for their own textures, generalized to your whole
load order.

## What it does

Point AutoBlend at your merged load order (via Mod Organizer 2, Vortex, or a plain Data folder)
and it will:

- Scan every `*/landscape/` texture path (vanilla, DLC, and mod-added) for a `statics` or
  `blending` subfolder — the convention several landscape texture mods already use to signal
  "this texture wants alpha blending, not alpha testing".
- Find every mesh that references a matching texture (via loaded plugins' Alternate Textures, and
  by scanning the meshes themselves for their baked-in diffuse path).
- Bake the detected diffuse texture path directly into each mesh's embedded texture slot, and flip
  its `NiAlphaProperty` from alpha-testing to alpha-blending — without touching any other flag bit
  or restructuring the mesh.
- Generate a dedicated output plugin with derived TextureSets and Alternate Texture overrides,
  deriving from whichever mod's texture set a shape already carries (rather than vanilla) when one
  exists — so a shape another mod has already retextured keeps that mod's normal/other maps.
- Run fully offline against your files — it never touches the running game.

## Two shells, one patch engine

All of the actual scanning/patching logic lives in **`src/AutoBlend.Core`** (C#, built on
[Mutagen](https://github.com/Mutagen-Modding/Mutagen) and
[niflysharp](https://github.com/Aetherinox/niflysharp)). Two front-ends drive it:

- **`native/`** — a native wxWidgets shell, forked from [AutoSeasons](https://github.com/Kesta-Dev/AutoSeasons)'
  own build (CMake + vcpkg + wxWidgets), calling into `AutoBlend.Core` through a
  [DNNE](https://github.com/dotnet/dnne)-exported `src/AutoBlend.NativeExport` assembly. This is
  the primary, actively-developed shell.
- **`src/AutoBlend.App`** — a WPF desktop shell (.NET 9), kept for platforms/workflows where the
  native build toolchain isn't convenient.

Both shells read and write the same `%APPDATA%\AutoBlend\settings.json`, so switching between them
mid-modlist keeps your settings.

## Installation

1. Download the latest release and install it like any other mod (MO2: as a regular mod; Vortex:
   extract into a mod folder).
2. Point it at your game install / merged load order, your mod manager (if any), and an output
   folder.
3. Run it, then enable the generated output plugin in your mod manager.

Supports Skyrim SE and Skyrim LE.

## Building from source

### Native shell (`native/`)

Requirements:
- Windows, Visual Studio 2022 (MSVC toolchain) or the standalone Build Tools
- [vcpkg](https://github.com/microsoft/vcpkg) (manifest mode; dependencies are pulled automatically)
- .NET 9 SDK (for the Mutagen/niflysharp-based patch backend)
- CMake 3.31+, Ninja

```bash
git clone https://github.com/<your-username>/AutoBlend.git
cd AutoBlend/native
cmake -B buildRelease -S . -G Ninja -DCMAKE_TOOLCHAIN_FILE=<path-to-vcpkg>/scripts/buildsystems/vcpkg.cmake -DCMAKE_BUILD_TYPE=RelWithDebInfo
cmake --build buildRelease
```

`src/AutoBlend.Core` and `src/AutoBlend.NativeExport` are built and published automatically as
part of this same CMake build (via DNNE) — no separate `dotnet build` step is needed. Unlike some
DNNE-based tools, this project passes settings/progress across the native boundary as plain JSON
rather than FlatBuffers: the whole patch pipeline is one coarse-grained call (`start_patch_run`,
polled via `get_progress`), so there's no FlatBuffers schema compiler dependency. The built
executable and its runtime dependencies (`AutoBlend_dotnetlib/`, the self-contained .NET runtime)
end up in `native/buildRelease/bin/`.

**Deploying as an MO2 tool**: the executable must be copied into a mod folder MO2 already knows
about (a plain, unregistered folder under `mods/` won't participate in MO2's virtual filesystem
merge, which `AutoBlend_dotnetlib` resolution depends on) — see `native/AutoBlend/src/main.cpp`'s
`configureDotnetLibDirectory()` for how that resolution works.

### WPF shell (`src/`)

```bash
cd src
dotnet build AutoBlend.sln
```

## Credits

- Native shell architecture (wxWidgets + DNNE-bridged .NET patch backend) adapted from
  [AutoSeasons](https://github.com/Kesta-Dev/AutoSeasons), which is itself built on
  [PGPatcher](https://github.com/hakasapl/PGPatcher) by hakasapl.
- [nifly](https://github.com/ousnius/nifly) by ousnius, and its C# binding
  [niflysharp](https://github.com/Aetherinox/niflysharp), for NIF file handling.
- [Mutagen](https://github.com/Mutagen-Modding/Mutagen) for reading and writing Bethesda plugin
  files.

## License

GPLv3 — see [LICENSE](LICENSE). `native/` is derived from AutoSeasons, itself derived from
PGPatcher, both GPLv3-licensed.
