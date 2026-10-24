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
| Machine | **AMD9800X3D** (since 24 October 2026; the earlier work was on the AMD7970X) |
| Repo | `C:\Source\Claude\LowPolyCharGen` (GitHub `peterbobwalton/LowPolyCharGen`, branch `main`) |
| Unreal test project | `C:\Unreal Projects\5.8\LowPolyCharTest` (UE 5.8.3, C++: `LpcgDisplaySoldier`, `LpcgSoldierCharacter`, `LpcgGameMode`) |
| Display level | `/Game/LowPolyCharGen/Maps/Lvl_Characters` (built by `tools/unreal_character_level.py`) |
| Pack data (parts as JSON/PNG) | `packdata/` — **git-ignored, licensed, never commit**. Made by `tools/pack2json.py`, extras by `tools/pack_extras.py` |
| Pack builder (scratch project) | `C:\Temp\LpcgPackBuilder`; built pack in `C:\Temp\LowPolySoldiers_Pack` |
| Pack delivery | `Z:\Media\Models` (see below) |
| Pack contents report | https://claude.ai/artifact/CWY3561gwqZocDCvnahGDF |
| Scratchpad | `C:\Temp` |

- **Packs:** never modify the pack originals in the game projects (UxVTest1, HeroesOfBenghazi). Test in LowPolyCharTest.
- **Editors:** several Unreal editors are often open (DroneWarriors, MechAnt…). Always target `editor="LowPolyCharTest"`.
- **Level scripts:** never destroy a level's own actors (WorldSettings, brushes): they are rooted, and destroying
  them crashed the editor before. Destroy actors one by one through the editor actor subsystem. Everything
  `unreal_character_level.py` spawns is tagged `LpcgShowcase`; a rebuild also clears untagged characters and labels
  (SkeletalMeshActor, LpcgDisplaySoldier, TextRenderActor) left by other scripts.
- **Z:\Media\Models:** overwrites and deletes there are blocked. Deliver each pack build under a new dated name.
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

## Where the pack is delivered

`Z:\Media\Models`:

| Item | What |
|---|---|
| `LowPolySoldiers_Pack_UE5.8_2026-10-24_legsfix.zip` (166 MB) | **Latest.** 121 characters in 10 factions, with the legs/boots fix |
| `LowPolySoldiers_Pack_UE5.8_2026-10-24.zip` (172 MB) | 24 Oct, before the fix: the officers have no legs, the Taliban no legs or shoes |
| `LowPolySoldiers_Pack_UE5.8.zip` (88 MB) | 23 Oct, the first pack (44 rebuilt prebuilts); kept for reference |
| `LowPolySoldiers_Pack\` (folder) | **Not updated:** the 13:33 build from before the fix. It can't be overwritten; use the legsfix zip, or copy the fixed `C:\Temp\LowPolySoldiers_Pack` to a new dated folder |

## Done on the AMD9800X3D (24 October 2026, newest first)

- **86 strays removed from Lvl_Characters** (Pete confirmed). There were 62 part sheets at x = −8000 (tagged
  `LpcgHeadSheet`) and the 24 UE5 previews at x ≈ −2100 to −2500, now covered by the UE5 and UE5Camo rows. The level
  holds only its 204 row, floor and light actors. Backup: `C:\Temp\Lvl_Characters_20261024_backup.umap`.
- **Legless officers and bootless Taliban fixed** (`b585259`).
  - `KitBuilder` treated any body weighted to a thigh as a UE5 body with its own legs, and dropped PackLegs. The
    long jackets (`TSA_Body_H`, `MIL_Mesh_Body_F/G`) weight their hems to the thighs, so the factions' 3 officers
    lost their legs and 7 Taliban their legs and shoes. Now only a body weighted to the feet counts.
  - **Stronger `--check`:** it now builds every row of `pack_prebuilts.txt` and `pack_factions.txt` and fails a
    mesh that doesn't reach the ground. It passes on 659 characters; without the fix it flags 18.
  - `KitMorph` keeps the soles on the ground (the reshape pushed 7 characters about 1 mm under it).
  - Re-imported the 15 affected characters, rebuilt the pack and delivered it as the legsfix zip.
- **Level grouped by faction** (`e840637`): rows grouped by faction, each with a large row label at its left end.
- **Every kit character in the stand-alone pack** (`7079599`): 121 characters in 10 faction folders (Armies,
  SpecOps, Militia, UE5, UE5Camo, DesertStorm, Taliban, UKArmy, UKSF, Custom). The README is generated by
  `pack_export_build.py`.
  - The staging step clears the scratch project first, and the build waits for the asset registry.
  - The build re-points the animations' and skeleton's preview meshes into the pack. One editor-only soft reference
    remains: the physics asset's preview mesh, which Python can't reach.
- **Factions** (`tools/pack_factions.txt`, exported by `make_pack_prebuilts.ps1` with `pack_prebuilts.txt`):
  - DesertStorm (10): US in chocolate chip (`T_gulfwar_A`) with PASGT helmets, and UK in desert DPM (`T_desert_uk`).
    Both wear tan desert boots.
  - Taliban (8): turbans, shalwar kameez (thobe), beards and chest rigs, with AK, RPG, PKM and SVD.
  - UKArmy (8): MTP (`T_multicam`), Mk7-style helmets, plate carriers, L85.
  - UKSF (8): multicam mixed with plain tops (`Recolour=Top`), FAST helmets, ball caps with headsets, beards,
    shemaghs, suppressed carbines.
  - The seventh field of the list is the display weapon in Lvl_Characters (one row per faction).
- **`Recolour`** (flags Top, Trousers, Boots): recolours single garments. `RecolourClothes` still recolours all of them.
  Hands are now left out of the shirt recolour.
- **The UE5 pack's 12 prebuilts rebuilt** (`2860586`, `pack_prebuilts.txt`): faction `UE5` (blue) and `UE5Camo`
  (olive digital). The sixth field of the list holds more settings. Lvl_Characters gets three rows: originals,
  rebuilt, camo.
- **UE5 pack in the kit** (`2860586`).
  - The Toon Soldiers UE5 pack's parts (`TS_Head_*`, `TS_Body_*`) are now a third part family. They use the same
    Bip001 bones and face mesh as Armies, on a skeleton about 1.18 times larger, so they go straight onto the shared
    skeleton. Their bodies include the legs down to the feet, so `PackLegs` is ignored for them.
  - Material slot 3 (`_UE5`) takes `UE5Texture` (`T_Soldiers_*`: the pack's 9 colours, plus `T_Soldiers_OliveDigital`).
  - Slot 4 (`_Equipment`) holds `T_Equipment`, for `TS_Equip_FlamerTank` (SK_Pyro's tank, extracted by `pack_extras.py`).
  - Packdata: `pack2json.py packdata/fbx_ue5 packdata` (parts) and `pack2json.py packdata/fbx_ue5_prebuilt packdata TS_`
    (prebuilts, prefixed `TS_SK_`). The textures and all 30 Armies camos are exported as PNG into `packdata/`.
## Done earlier on the AMD7970X (latest commits first)

- `dc5e544` **Stand-alone content pack export.**
  - `tools/pack_export_stage.py` runs in LowPolyCharTest. It copies the 44 rebuilt characters, the 450 pack
    animations, 78 weapons and all their dependencies into a scratch content-only project,
    `C:\Temp\LpcgPackBuilder`.
  - `tools/pack_export_build.py` runs in that project. It moves everything under `/Game/LowPolySoldiers` with the
    references fixed, checks there are no outside references (result: 0), and copies the folder to
    `C:\Temp\LowPolySoldiers_Pack`, with a README.
  - Layout: `Characters/<faction>` (+ `Faces/`), `Skeleton/SKEL_Soldier`, `Animations/`,
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
  - Its preview in Lvl_Characters has since been removed; the UE5Camo kit rebuilds replace it.
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
   - UE5 pack: export its parts to `packdata/fbx_ue5` and its prebuilts to `packdata/fbx_ue5_prebuilt`, run
     `pack2json.py` (with the `TS_` prefix for the prebuilts), and export its textures as PNG into `packdata/`.
2. Export the characters:
   - `tools/make_kit_characters.ps1` writes `out/kit` and `out/kit_militia`.
   - `tools/make_pack_prebuilts.ps1` writes `out/kit_pack`: the prebuilts and the factions. `-Only "UKSF_*"` exports
     only the matching ones.
3. In the LowPolyCharTest editor:
   - `py tools/import_unreal.py <out folder> /Game/LowPolyCharGen/Kit[/Pack]`
   - Then `py tools/unreal_character_level.py`.
4. Stand-alone pack:
   - Run `py tools/pack_export_stage.py` in LowPolyCharTest.
   - Open `C:\Temp\LpcgPackBuilder\LpcgPackBuilder.uproject`.
   - Run `py tools/pack_export_build.py` there. It runs longer than the bridge's request timeout: wait for the editor
     to answer again, then look for the `pack: ... outside references: 0` line in LogPython.
   - Zip `C:\Temp\LowPolySoldiers_Pack` and copy it to `Z:\Media\Models` under a new dated name.
5. Run `lowpolychargen --check` after any generator change: it must pass on every character.

## Open / possible next steps

- More factions, proposed:
  - Iraqi Army and Republican Guard 1991 (the Desert Storm opposition).
  - Soviet forces in Afghanistan in the 1980s (`T_russian_green`).
  - Russian Spetsnaz (`T_spetsnaz_*`).
  - US Navy SEALs (`T_urban_seal`, multicam).
  - Cold War BAOR British (`T_woodlands_uk`).
  - Arctic troops (`T_winter_*`, `T_digital_snow`).
- The UE5 pack's own weapons (`SM_weapon_*` in `/Game/Toon_Soldiers_UE5`) are scaled for its larger skeleton. They
  aren't in the stand-alone pack.
- In the WPF app, hide the non-pack options when Body = Pack.
- Get a performance figure from a standalone (packaged) build, not the editor.
- The pack's physics asset still has an editor-only soft reference to a preview mesh outside the pack (Python can't
  reach it). It may need a C++ editor utility, or fixing by hand in the scratch project.
- `Z:\Media\Models\LowPolySoldiers_Pack` (folder) predates the legs fix (see above).
- Known quirks:
  - The bridge's `exec_python` treats code whose text ends like a `.py` path as a file path. Avoid `.py'` near the
    end of inline code.
  - Large `device_commit_files` uploads (about 28 MB) failed. Keep them to about 18 MB or less.
