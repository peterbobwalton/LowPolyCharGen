# LowPolyCharTest

UE 5.8 C++ test project for LowPolyCharGen characters on the Toon Soldiers skeleton. Only the code,
config and project file are in git; the content is assembled locally because the Toon Soldiers
pack is licensed Marketplace content and the template content ships with the engine.

## C++ (Source/LowPolyCharTest)

| Class | What it does |
|---|---|
| `ULpcgLocomotionAnimInstance` | Native locomotion: idle/walk/run blend by ground speed plus a standing/crouched blend, evaluated in the anim proxy (no Animation Blueprint, no anim graph). Walk and run share a gait phase. |
| `ALpcgSoldierCharacter` | Playable third-person soldier: generated mesh, weapon in the pack's `WeaponContainer` socket, template input (WASD/mouse/Space) plus code-made Sprint (Shift) and Crouch (C/Ctrl). |
| `ALpcgGameMode` | Default pawn = `ALpcgSoldierCharacter`; the project's and the map's game mode. |
| `ALpcgDisplaySoldier` | Lineup actor: loops a clip or the C++ locomotion blend at a fixed speed, animates in the editor viewport. |

`CharacterScale` (default 1.0, the packs' size) scales a character in the world. It lives on the
mesh component, not in the FBX: the pack's animations key every bone's transform including the
Biped root's scale, so a size baked into the mesh would be undone. Weapons scale with the character
(the packs' proportions), and the locomotion blend divides the ground speed by the scale so a
bigger or smaller stride does not slide.

**No foot sliding.** Each gait clip carries the speed its planted foot moves at, measured in the
editor (walk 111, run 369, sprint 478, crouch walk 107 cm/s; the pack's Root_Motion versions
under-travel, e.g. run 302). The soldier moves at exactly those speeds times `CharacterScale`, the
blend cross-fades between neighbouring gaits and scales the playback rate outside them.
`tools/unreal_footslip_test.py` drives the soldier in Play-in-Editor and measures planted-foot slip;
what is left equals the clips' own toe roll (walk ~28, run ~50 cm/s).

**Weapons** go on the packs' `WeaponSocket_R` socket (right hand), which both the Armies and
Militia skeletons define with a 0.6 scale: with the Biped's 1.8 bone scale that gives the guns
their intended size. Attaching to the bare `WeaponContainer` bone makes every gun 1.8x too big.
Militia use the Militia pack's own weapons (AK-47, AKSU, PKM, RPG-7, SVD, pistols...), which are
authored for the same socket.

**Crowds.** Built for hundreds of soldiers on screen. Each character is one draw call (one material),
24 bones with at most 4 weights per vertex, and 1024 textures by default. `tools/import_unreal.py` adds two
reduced LODs (about half and a quarter of the triangles; `--lods=1` for none). Both soldier classes turn on
update rate optimisation (distant soldiers animate at a lower rate) and only evaluate a pose when rendered.
`CharacterScale` goes on the capsule, so crouching keeps a scaled soldier's capsule size.

**Grime layer.** `tools/import_unreal.py` builds `M_LPCG_Character` (once): BaseColor =
`lerp(Texture, GrimeTexture.rgb, GrimeTexture.a * GrimeAmount)`, and gives each character a
material instance with its grime layer and `GrimeAmount` from its exported JSON. At runtime
`ALpcgSoldierCharacter::SetGrime(float)` (Blueprint-callable) blends it in or out through a dynamic
material instance; `ALpcgDisplaySoldier` has a `Grime` property for the same.

## Setting up the content

1. Copy `Engine/Templates/TP_ThirdPersonBP/Content/*` into `Content/`, and each of
   `Engine/Templates/TemplateResources/High/{LevelPrototyping,Characters,Input}/Content/*` into
   `Content/<PackName>/` (they must keep their mount folder, e.g. `Content/Input/Actions`).
2. Copy `Toon_Soldiers_Armies` into `Content/` (from a project that owns it, e.g. UxVTest1).
3. Optional: copy the `UnrealMcpBridge` plugin into `Plugins/` and add it to the `.uproject`.
4. Generate project files, build `LowPolyCharTest` (Development Editor) in Visual Studio.
5. Export characters with LowPolyCharGen and import them with
   `../../tools/import_unreal.py <folder> /Game/LowPolyCharGen/Characters`.
6. `../../tools/unreal_lineup.py` places the showcase lineup and checks Play-in-Editor.

If another project's editor has Live Coding running, Unreal Build Tool refuses to build any project
on the same engine; add `-NoHotReloadFromIDE` to the NMake build command lines in
`Intermediate/ProjectFiles/LowPolyCharTest.vcxproj` (it is lost when project files are regenerated).
