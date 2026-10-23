# LowPolyCharGen – handover (pack kit work, October 2026)

This document records where the Toon Soldiers **pack kit** work stands, how the pieces fit together, and what is
open. Read it before carrying on. The older procedural generator (`Body` = Standard/Toon/…) is unchanged and
documented in `README.md`.

## Goal (from Pete)

- Use the Toon Soldiers packs (Armies and Militia) as the basis for design, but improve on them. Add more head, torso,
  leg, boot, hand and glove variations, including women.
- Characters must be Nanite-optimised for high performance.
- Eyes may be toon style, but they must be the right shape.

## Environment and rules

| Thing | Where |
|---|---|
| Repo | `C:\Source\Claude\LowPolyCharGen` (GitHub `peterbobwalton/LowPolyCharGen`, branch `main`) |
| Unreal test project | `C:\Unreal Projects\5.8\LowPolyCharTest` (UE 5.8.3, C++: `LpcgDisplaySoldier`, `LpcgSoldierCharacter`, `LpcgGameMode`) |
| Display level | `/Game/LowPolyCharGen/Maps/Lvl_Characters` (built by `tools/unreal_character_level.py`) |
| Pack data (parts as JSON/PNG) | `packdata/` — **git-ignored, licensed, never commit**. Made by `tools/pack2json.py`, extras by `tools/pack_extras.py` |
| Scratchpad | `C:\Temp` |

- **Packs:** never modify the pack originals in the game projects (UxVTest1, HeroesOfBenghazi). Test in LowPolyCharTest.
- **Editors:** several Unreal editors are often open (DroneWarriors, MechAnt…). Always target `editor="LowPolyCharTest"`.
- **Level scripts:** never destroy a level's own actors. Only remove tagged actors (`LpcgShowcase`); destroying
  rooted actors crashed the editor before.
- **Build and git:** build and run from Visual Studio 2026 through the VS MCP bridge. Do commits and pushes yourself
  with the git MCP tool.
- **OneDrive is off limits.**

## Pack kit — how it works (`src/LowPolyCharGen.Core/Kit/`)

`Body = Pack` routes `CharacterGenerator` to `KitBuilder.Build`. This assembles pack parts (`PackHead`, `PackBody`,
`PackLegs` and `PackEquipment`, a comma list) on the pack's own 24-bone `Characters_Skeleton`. The skeleton is
never changed, so every pack animation still works.

- **KitMorph:** smooth per-segment space warp for build (slim/heavy) and the female shape (with bust vertices
  inserted).
- **HeadMorph:** female jaw/chin/nose; head shapes Square/Slim/Round/Heavy; `Nose` = Pack/Small/Button/Broad/Roman
  (moves the pack face's known nose vertices).
- **FacePainter:** a per-character 1024 face texture (material slot 1). Layout:
  - the face tile at the top (v 0.5–1);
  - the headwear cloth region (keffiyeh check, turban wraps) at v 0–0.5;
  - the hair, hat and garment strips at u 0.8–1.

  Male: facial hair. Female: a clean repaint (soft modelling, thin brows, lashes, lips). Almond toon eyes. Skin and
  hair recolour.
- **HairPieces:** toy-style LongHair/Bob shells, Ponytail/Bun.
- **KitPieces:** Turban, Keffiyeh (+ agal), ThobeSkirt.
- **KitHands:** `GloveStyle` Bare/Tan/Dark.
- **Low footwear:** Shoes and Trainers.
- **RecolourClothes:** recolours the clothes.
- **Material slots:** 0 = Armies texture (kit MI), 1 = face, 2 = Militia texture.
- **Per-vertex data in UV1–3**, because Nanite skinned meshes drop vertex colours and GPU skinning allows at most
  4 UV sets: skin tint and cloth recolour. `M_LPCG_Kit` decodes it with custom HLSL (pow 2.2 on the sRGB ratio).
  `KitPreview` reproduces this on the CPU for the WPF app and the CLI's `--preview`.
- **Face covers:** `FaceCover` Balaclava/Shemagh pick Militia heads. A turban or keffiyeh needs a bare head (and
  swaps one in).

**WPF app:** the PACK KIT card fills its lists from `PackLibrary`. Launch profiles in
`src/LowPolyCharGen.App/Properties/launchSettings.json`.

**Import:** `tools/import_unreal.py <fbx folder> <dest>`. Nanite on, vertex colour replace, slots mapped by name
suffix, materials from `M_LPCG_Kit`.

## Done this session (latest commits first)

- `dc5e544` **Stand-alone content pack export.**
  - `tools/pack_export_stage.py` runs in LowPolyCharTest. It copies the 44 rebuilt characters, the 450 pack
    animations, 78 weapons and all their dependencies into a scratch content-only project,
    `C:\Temp\LpcgPackBuilder`.
  - `tools/pack_export_build.py` runs in that project. It moves everything under `/Game/LowPolySoldiers` with the
    references fixed, checks there are no outside references (result: 0), and copies the folder to
    `C:\Temp\LowPolySoldiers_Pack`, with a README. A zip is at `C:\Temp\LowPolySoldiers_Pack_UE5.8.zip` (88 MB).
  - Layout: `Characters/{Armies,SpecOps,Militia}` (+ `Faces/`), `Skeleton/SKEL_Soldier`, `Animations/`,
    `Weapons/{Armies,Militia}`, `Materials/`, `Textures/`.
  - Why a scratch project: Python can't re-point a mesh's or animation's skeleton (read-only). `rename_assets` does
    fix references, so the assets are moved inside a throw-away copy instead.
  - Pete cancelled committing the built pack into the repo; it contains the licensed pack assets. **Do not commit
    it** unless he asks again and confirms the repo is private (Git LFS 3.7.1 is installed on his PC if he does).
- `0da4d75` **Digital camo:** `tools/digital_camo.py`.
  - It repaints the Toon Soldiers UE5 pack's uniform (`/Game/Toon_Soldiers_UE5`). The cloth is found as the
    difference between two colourways, then repainted with a blocky camo that keeps the painted shading.
  - Palettes: olive, woodland, desert, urban.
  - Made `T_Soldiers_OliveDigital` and `MI_Soldiers_OliveDigital` in `/Game/LowPolyCharGen/ToonSoldiersUE5`. The
    pack's meshes are untouched.
  - A preview of the 12 UE5 prebuilts (blue and camo, tagged `LpcgUE5Camo`) stands at x ≈ −2500 in
    Lvl_Characters.
  - Offered, not done: copies of those 12 meshes with the camo set as their default material.
- `fc9635f` **The pack's 44 prebuilts rebuilt with the kit.**
  - `tools/pack_prebuilts.txt` lists each prebuilt's parts, matched from the prebuilt meshes by position + UV.
  - `tools/pack_extras.py` extracts the prebuilt-only geometry into `packdata` as `MIL_Extra_GasMask`,
    `_Sunglasses`, `_Pouch` and `_Pouch_B`.
  - `tools/make_pack_prebuilts.ps1` exports them to `out/kit_pack`. They are imported to
    `/Game/LowPolyCharGen/Kit/Pack` as `SK_LPCG_Pack_*`.
  - In Lvl_Characters, each row of prebuilts is followed by a row of their kit rebuilds.
  - Militia01's mesh sits about (−0.45, 0, −1.83) off the parts. It matched after that offset: head_K, Body_C,
    Legs_C, equip_B.
- Earlier this session (`d46cf34` and before):
  - Women's faces, noses, long hair and bobs, gloves and recolouring, low footwear.
  - Militia: turban, keffiyeh, thobe, beards, shemagh and balaclava.
  - The display level, with weapon-specific idles (the RPG on the shoulder).
  - Crowd test: kit with Nanite 19.9 ms vs 20.3 ms without; 1000 characters, editor-bound.
  - The WPF app's Pack kit options and preview.

## Rebuild everything from scratch

1. Pack data:
   - Export the pack's skeletal meshes to FBX, then run `py tools/pack2json.py`. This fills `packdata/`.
   - Then run `py tools/pack_extras.py`.
2. Export the characters:
   - `tools/make_kit_characters.ps1` writes `out/kit` and `out/kit_militia`.
   - `tools/make_pack_prebuilts.ps1` writes `out/kit_pack`.
3. In the LowPolyCharTest editor:
   - `py tools/import_unreal.py <out folder> /Game/LowPolyCharGen/Kit[/Pack]`
   - Then `py tools/unreal_character_level.py`.
4. Stand-alone pack:
   - Run `py tools/pack_export_stage.py` in LowPolyCharTest.
   - Open `C:\Temp\LpcgPackBuilder\LpcgPackBuilder.uproject`.
   - Run `py tools/pack_export_build.py` there.

## Open / possible next steps

- The UE5 pack (`/Game/Toon_Soldiers_UE5`) is not part of the kit. Its parts (`TS_Head_*`, `TS_Body_*`) use their
  own skeleton and 2048 painted textures. To add it: `pack2json.py` over its parts, then a third part family in
  `KitBuilder`.
- Camo copies of the 12 UE5 prebuilts, and adding them to the stand-alone pack (as a faction), if wanted.
- Add the kit's custom characters (the 19 in `/Game/LowPolyCharGen/Kit`) to the stand-alone pack, if wanted.
- In the WPF app, hide the non-pack options when Body = Pack.
- Get a performance figure from a standalone (packaged) build, not the editor.
- Known quirks:
  - The bridge's `exec_python` treats code whose text ends like a `.py` path as a file path. Avoid `.py'` near the
    end of inline code.
  - Large `device_commit_files` uploads (about 28 MB) failed. Keep them to about 18 MB or less.
