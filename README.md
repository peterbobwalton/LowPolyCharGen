# Low Poly Character Generator

A WPF tool that builds low-poly characters procedurally and exports them as rigged, skinned
**FBX** files in a **T-pose**, with a painted texture for the face, clothing and gear. Two rigs:

* **Toon Soldiers** (default): the chunky proportions of the *Toon Soldiers Armies* pack on that pack's
  own skeleton (`Characters_Skeleton`, a 3ds Max Biped), so the generated characters import onto it
  and play all of the pack's animations and weapon attach points.
* **UE5 mannequin**: realistic proportions on a skeleton that matches `SK_Mannequin`.

![screenshot](docs/screenshot.png)

## Running

```
dotnet run --project src/LowPolyCharGen.App
```

Requires the .NET 10 SDK on Windows. There are no NuGet dependencies.

Pick the options on the left, check the result in the 3D preview and press **Export FBX…**.
Four files are written: `<name>.fbx`, `<name>_Diffuse.png` (the clean texture the FBX references),
`<name>_Grime.png` (the grime layer: RGB = dirt, lit like the texture; A = its coverage at full
grime) and `<name>.json` (the options, a preset). Blend the layer in a material as
`lerp(Diffuse.rgb, Grime.rgb, Grime.a * amount)`; the app's preview shows it baked at the Grime slider.

| Group     | Options |
|-----------|---------|
| Character | name, grime, rig (Toon Soldiers or UE5 mannequin), edition (desert, snow, jungle, civilian, militia), gender, build (slim to heavy), skin tone |
| Head      | face cover (balaclava, shemagh); fatigue and stress; face shape (standard, square, round, slim, heavy), size, ears (normal, pixie, elephant, stick out), eye colour, hair (8 styles), facial hair (stubble, moustache, goatee, beard, bushy beard), hair colour |
| Torso     | T-shirt, long sleeve, rolled sleeves, tank top, jacket, hoodie, polo, check shirt; colour; camouflage (woodland, digital, chocolate chip) |
| Legs      | trousers, cargo, shorts, tall boots, skirt, jeans, thobe; boots or trainers; trouser and boot colours |
| Add-ons   | hat (helmet, cap, beret, boonie, beanie, keffiyeh, turban), eyewear, backpack (light, rucksack, hydration, radio, demolition, tools), vest (chest rig, plate carrier), belt (plain, pouches, utility with holster), gloves, scarf, knee and elbow pads; hat, gear and accent colours |
| Output    | texture size (512, 1024 by default, or 2048), flat or smooth shading, IK bones on/off (mannequin rig) |

The preview can show the skeleton and a few test poses (A-pose, relaxed, walk, action) so the skin
weights can be judged. Poses are preview only: the export is always the T-pose bind.
**Randomise** rolls a character; presets are saved and loaded as JSON.

## How the characters are made

**Proportions.** The mannequin-rig bodies are tuned against adult reference measurements, not by eye:
`lowpolychargen --measure` prints neck, chest, waist, hip and thigh sizes against the targets in
`Anthropometry.cs`, and `--check` fails if the plain man or woman drifts more than about 8% from them.
Men have a fit, natural build (chest only a little wider than the waist); women have their own height,
a two-form bust, a defined waist and fuller hips and seat. The jaw has a level underside before it turns
into the neck, so the chin stands clear of the throat. The Toon Soldiers rig keeps its own chunky trunk.

**Realistic body.** With `Body = Realistic` (mannequin rig) the body is not lofted from sections
but taken from the MakeHuman base mesh, which is CC0: about 27,000 triangles with real anatomy. Its
shape targets set gender, build (weight and muscle) and ancestry, the way MakeHuman's macro sliders
do. The shaped mesh is fitted onto the mannequin skeleton in the T-pose: trunk, neck and head are moved
and stretched upright, the limbs turned onto the mannequin's bones and stretched to their length,
each vertex following its bones' moves by MakeHuman's own game_engine rig weights, which are then
handed over to the mannequin's bones. The result is decimated to `RealisticBody.BodyTriangles` (2,600)
with the face, hands and feet keeping more of their detail, and painted like the generated body:
clothes, landmarks (eyes, nose tip and mouth found on the mesh's profile) and gear sit on it because
the torso and head sections are measured off it. `tools/import_makehuman.py` rebuilds the asset
(`Assets/human_body.bin`) from a checkout of MakeHuman's MPFB2 data.

Clothing that stands off the skin is a shell over the dense body: trousers and shorts (hips and
legs, looser down the leg exactly as the generated garment is, less between the thighs), sleeves
(growing out of the painted shoulder), the whole loose top for a jacket or hoodie, and boot shafts.
Each shell is the body's surface pushed out by the garment's thickness, smoothed so it skims over
creases without sinking back, cut at its hem and decimated; the skin under it is dropped as hidden.
Boots and trainers are the generated ones.

**Faces and builds.** Men and women are clearly different, from MakeHuman's face and body targets
(`HumanBody.Face`): a man is lean and angular with a square, strong jaw and chin, flat cheeks, a
lower brow, and a V-shaped, muscular torso with broad shoulders; a woman is petite, with an oval face,
finer chin and nose, slightly larger eyes, fuller lips, higher brows, a medium bust, narrow shoulders,
a small waist and trim hips. `HeadShape` still picks square, round, slim or heavy, `Ancestry` (Mixed,
African, East Asian, European) leans the face and build, and `FaceSeed` varies the features within
the type, mirrored on both sides (Randomise picks both). The face keeps most of the decimation
budget and three times the texture density, the eyeballs are painted as eyes (iris and pupil round
the front of the ball, lid rims darkened along the real lids), and the painted nose shading gives way
to the modelled nose.

**Smooth shading, lips and symmetry.** The left half of the body is reduced and mirrored onto the right
(`Decimator.Options.OnMiddle` keeps the middle line on the mirror plane), so the two sides of the face
match exactly; the lips and nostrils keep the most triangles of all. The reduced mesh shades with the
dense body's normals (smoothed a little first, `Face.CornerNormals`), so it looks rounded instead of
faceted. The painted lips are measured off the modelled mouth (`LipProfile`: the face is rasterised as
a depth map and the line between the lips, and each lip's edge, found column by column), so the colour
sits exactly on the lips; women's corners of the mouth are lifted a little (`mouth-angles`).

**Hair.** On the realistic head the hair is a shell over the dense skull (`RealisticBody.BuildHair`):
thin at the hairline, so it grows out of the painted hair rather than sitting on the head like a
helmet, and fuller at the crown. A bob or long hair falls straight from the widest part of the skull
and clears the ears instead of following the head in under the jaw; the strands run down from the
crown. Under a hat the hair is painted only. Painted hair and beards carry on onto the neck, so their
edges do not follow the triangles.

**Fingers and hand poses.** The realistic body gets the UE5 mannequin's finger bones (a metacarpal
and three bones per finger, three for the thumb): Manny's names, hierarchy and bone orientations
(`MannequinFingers.cs`, generated by `tools/gen_mannequin_fingers.py` from `tools/manny_bones.json`,
a reference-pose dump of SK_Mannequin), placed on the realistic hand's own joints and weighted by
MakeHuman's finger weights. The mesh imports onto `SK_Mannequin` and its animations move the fingers.
`HandPose` (Relaxed, Open, Closed, Fist) bends the fingers into the bind pose before decimation, the
finger bones turning with them; `lowpolychargen --out soldier.fbx --set Body=Realistic --hand-poses`
writes one file per pose.

**Hands.** On the mannequin rig the hands are modelled, not generated: `tools/make_hand.py` builds a
dense, realistic left hand (tapered fingers with full rounded tips, knuckles, palm pads, a relaxed
thumb) as a distance field meshed with marching cubes, about 174,000 triangles. Look at it or touch
it up in Blender, then `lowpolychargen --make-hand hand_dense.obj` reduces it to the 6,000-triangle
template in `src/LowPolyCharGen.Core/Assets/hand_l.obj`. Each character's hands are decimated from
that template to `BodyBuilder.HandTriangles` (440) by the generator's own decimator (`Decimator.cs`,
quadric edge collapse: triangles go where the surface curves), placed on the wrist and stitched to
the arm. The painter adds nails from landmarks stored in the template. Toon characters keep the
pack's block fist.

**Mesh.** Hips, torso, neck, head, arms, hands and legs are one continuous surface (about 800
vertices), so shoulders and hips deform without gaps. Only the left half is modelled; it is mirrored
and welded along the centre line. The head is a sculpted grid whose jaw line is a hard edge over a
separate under-jaw surface; each face shape is a different set of proportions for that grid. Ears
are small shell-shaped meshes. Clothing changes the mesh only where it changes the silhouette
(loose trouser legs, cuffs, hems, a skirt); gear, hats and longer hair are separate pieces on top.

**Texture.** Everything else is paint. The atlas (at most 2048 x 2048) is generated, not drawn:
each texel knows where it lies on the character, and a procedural painter decides its colour from
that position - eyes, brows, lips and stubble on the face; necklines, plackets, pockets, belts,
seams, folds and camouflage on the clothes; laces and soles on the boots; flaps and webbing on the
gear. Because the painter works from the model position rather than the UV layout, features line
up across UV seams and follow whatever shape the options produce. Soft shading under the chin,
arms and gear is baked in from ambient occlusion.

The painter lives in `src/LowPolyCharGen.Core/Texturing/Painter*.cs`; to change how something
looks, that is the place.

## Toon Soldiers rig

The character is built on the mannequin T-pose as usual and then reshaped (`ToonRig.cs`): every
mannequin bone gets an affine map that thickens it in a frame aligned with the bone, turns it onto
the matching pack bone and moves its joint onto the pack's joint; vertices move by the weighted blend
of their bones' maps, so clothes, gear, hair and hats follow. The proportions (wide trunk, thick
limbs, 1.5x head, block fists, tall boots) are the constants at the top of `ToonRig.cs`; measured
against the pack's soldiers they agree within a centimetre or two. The skin weights are then moved
onto the pack's bones (the five spine bones become `Bip001-Spine`, both neck bones `Bip001-Neck`).

The exported skeleton is the pack's reference pose exactly (`ToonSoldierData.cs`, generated by
`tools/gen_toon_data.py` from `tools/toon_soldiers_skeleton.json`, a dump taken in the editor):
24 bones, `Bip001` root at the pelvis with the Biped's 1.8 scale, `WeaponContainer`,
`WeaponContainer_L` and `Backpack_container`. Imported meshes match the pack's reference pose to
0.0001 cm. Unreal's importer reports "some invalid bind poses" for this rig (it does for any root
with a scale) and rebinds from the time-zero pose, which is the same pose, so it is harmless.

**Uniform editions.** `Edition` = Desert (khaki, sand/brown three-colour camo, tan boots), Snow
(white with pale and mid grey camo, kept cool rather than warm) or Jungle (greens with brown and
near-black). Picking one sets the clothing, hat, gear, boot and accent colours and switches the
camouflage on (all still editable) and gives the camouflage that theatre's colours; `Custom` keeps
your own colours. On the command line: `--set Edition=Snow`. `Civilian` instead swaps military kit
for civvies (no camo or vest, jeans, trainers, a hoodie instead of a jacket) and Randomise then
picks civilian outfits and colours. `Militia` (styled on the Toon Soldiers Militia pack) dresses irregular
forces: camouflage on the trousers only (`CamoCoverage`) in urban greys, a shemagh face wrap,
surplus chest rigs and boots; Randomise then mixes dark civvies, balaclavas or shemaghs, beanies,
berets and old helmets. `FaceCover` = Balaclava (knit, eye slit, no hair) or Shemagh (wrapped over
the nose and mouth, with a matching scarf) works with any edition; its colour is `AccentColor`.
Traditional dress: `Legs = Thobe` (ankle-length robe in the shirt's cloth), `Hat = Keffiyeh` (headcloth
in the shemagh pattern, draped to the shoulders, black agal) or `Turban`; `FacialHair = BushyBeard`
is a real beard mesh. The militia randomiser dresses about a third of the men this way.

`Camouflage = ChocolateChip` is the 1991 US six-colour desert pattern (tan, khaki and brown blobs with
black and white rocks) in its own fixed colours, whatever the edition. `Backpack = Demolition` straps
explosive blocks (in `AccentColor`) and a det-cord coil to a small pack; `Tools` is a rucksack with an
entrenching tool and a pick. `tools/make_desert_storm.ps1` exports the OpDesertStorm cast with them.

**Weathering** (painted, 0..1): `Grime` (dust and smudges everywhere, dirt in creases, mud up the
legs), `Fatigue` (dark rings and bags under the eyes, red lids, sallow skin, hollow cheeks) and
`Stress` (furrowed brow, forehead lines, deep nose-to-mouth folds, sweat).

The texture gets the pack's hand-painted finish on this rig: deeper baked occlusion, a strong top
light, broad brush-like value patches and muted warm colours.

## What is in the FBX (mannequin rig)

* Binary FBX 7.4, centimetres, Z-up, character facing -Y in the file. Unreal imports that as a
  character facing +Y with an untransformed `root` bone, the same layout as the UE mannequin.
* One skinned mesh (roughly 1,600 to 2,800 triangles) with at most 4 bone influences per vertex,
  explicit normals, one UV set, a bind pose and one material with the diffuse texture.
* Skeleton: `root, pelvis, spine_01..05, neck_01, neck_02, head, clavicle/upperarm/lowerarm/hand_l/r,
  thigh/calf/foot/ball_l/r` plus the optional `ik_foot_*` / `ik_hand_*` bones. Names, hierarchy,
  joint positions and bone orientations are the UE5 mannequin's (`SK_Mannequin`). Trunk and leg
  bones are identical to its reference pose; the arms are rotated out to a T-pose with the bone
  lengths kept. Finger and twist bones are not included: the modelled hands are rigid on `hand_l/r`,
  in a relaxed pose.
* Men are the mannequin's size: same joint positions, 181 cm tall at head size 1.0. Women get their own
  frame on the same bones (as Quinn shares Manny's skeleton): 168 cm tall with narrower shoulders, made by
  moving the joints (`BodyFrame.cs`). Pick `SK_Mannequin` on import and the animations still apply.

### Importing into Unreal

**Toon Soldiers rig:** run `tools/import_unreal.py` in the editor (Tools > Execute Python Script, or
`py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "D:/out" /Game/LowPolyCharGen` in the
Python console). For every FBX in the folder it imports `SK_<name>` onto
`/Game/Toon_Soldiers_Armies/Meshes/Characters_Skeleton`, its texture as `T_<name>`, makes
`MI_<name>` (a `M_Armies_Parent` instance using it) and assigns the pack's physics asset. Any pack
animation, Blueprint or weapon socket then works with the new mesh.

**Mannequin rig:** import the FBX as a skeletal mesh. Either let Unreal create a new skeleton, or pick `SK_Mannequin`
as the skeleton to share the mannequin's animations (the bones are a subset of that skeleton, so
it binds without changes). Leave "Use T0 as ref pose" off. With "Import textures" on, the texture
and a material are created automatically.

## Command line

`src/LowPolyCharGen.Cli` exports without the UI:

```
lowpolychargen --out soldier.fbx --set Hat=Helmet --set Backpack=Rucksack --set Camouflage=Woodland
lowpolychargen --out soldier.fbx --preset soldier.json
lowpolychargen --random 20 --out-dir out --seed 7
lowpolychargen --variants out        # one FBX per option value
lowpolychargen --check               # validates both rigs, body proportions and a few hundred generated meshes
lowpolychargen --measure             # body measurements of the plain man and woman against adult reference sizes
lowpolychargen --make-hand out/hand_dense.obj   # rebuild the hand template from tools/make_hand.py's dense hand
lowpolychargen --pose-test poses.json --out-dir poses --set Hat=Boonie
                                     # skins a Toon Soldiers character with bone poses dumped from
                                     # the pack's animations in Unreal and writes one OBJ per frame
```

Use `--set Rig=Mannequin` for the mannequin rig.

`--set` takes any property of `CharacterSpec` (`Property=Value`, colours as `#RRGGBB`).

## Code layout

```
src/LowPolyCharGen.Core     generator and exporter, no UI dependencies
  CharacterSpec.cs            every option; bindable, JSON-serialisable, randomiser
  Skeleton.cs                 mannequin T-pose and Toon Soldiers skeletons (MannequinData.cs, ToonSoldierData.cs)
  ToonRig.cs                  reshapes a built character onto the Toon Soldiers skeleton and proportions
  MeshBuilder.cs              skinned primitives, UV charts and atlas packing, mirroring/welding, normals
  Profile.cs                  torso and head volumes that can be meshed and queried
  Skinning.cs                 weight functions (rigid, bone chains with cross-fades, blends)
  BodyFrame.cs                gives a character its own height and shoulder width on the mannequin bones
  Anthropometry.cs            measures a mesh (neck, chest, waist, hips, thigh) against adult reference sizes
  Decimator.cs                quadric edge-collapse decimator (dense model -> triangle budget)
  Assets/hand_l.obj           the hand template (from tools/make_hand.py via --make-hand)
  Parts/BodyBuilder.cs        the welded body: hips, torso, neck, arms, hands, legs, boots
  Parts/HandTemplate.cs       loads, decimates (cached) and describes the modelled hand
  Parts/HumanBody.cs          the MakeHuman base mesh: shape targets, fitting onto the mannequin skeleton
  Parts/RealisticBody.cs      decimates the fitted body into the character mesh, slots and UV patches
  Assets/human_body.bin       MakeHuman base mesh, targets and rig weights (CC0), from tools/import_makehuman.py
  Parts/HeadBuilder.cs        skull and face shapes, ears, hair, hats, eyewear
  Parts/GearBuilder.cs        vests, backpacks, belt kit
  Texturing/Painter*.cs       the procedural painter (face, clothes, materials)
  Texturing/TextureBaker.cs   rasterises the mesh into the atlas; PNG encoder
  Texturing/AmbientOcclusion.cs   baked soft shading
  Fbx/                        FBX node tree, binary 7.4 writer, exporter
  Posing.cs                   linear blend skinning for the preview poses
src/LowPolyCharGen.App      WPF front end (WPF 3D preview, colour pickers, presets)
src/LowPolyCharGen.Cli      headless exporter and consistency checks
tools/                      Blender contact-sheet renderer, skeleton data generators, Unreal import script
unreal/LowPolyCharTest      UE 5.8 C++ test project: native locomotion anim instance, playable soldier, lineup (see its README)
```

Model space everywhere is right-handed, centimetres: **+X = character's left, -Y = front, +Z = up**.

### Adding things

* **An option value:** add it to the enum in `CharacterSpec.cs` (it shows up in the UI
  automatically), then handle it in the matching builder and, if it is paint, in the painter.
* **Geometry:** primitives take a `Slot` (what the surface is made of) and a weight function, e.g.
  `mesh.AddBox(centre, halfExtents, Slot.Hat, c.HeadRigid)`. Left/right pairs go inside
  `mesh.Mirrored(m => ...)`. Things worn on the body sample its surface (`c.Torso.FrontY(z, x)`,
  `c.Head.Surface(...)`, `leg.RadiusU(z)`) so they follow gender and build. UVs are automatic.
* **Paint:** each body `Slot` has a method in the painter that receives the texel's position and
  normal. Landmarks such as eye height, hem and boot-top heights come from `PaintContext`.

Run `lowpolychargen --check` afterwards, and `tools/preview.ps1` to look at the result.

### Visual checks

`tools/preview.ps1` exports characters with the CLI, imports the FBX files into Blender
(headless) and renders a contact sheet, which also proves the files load in a second importer:

```
.\tools\preview.ps1 -Out sheet.png -Sets "Hat=Helmet,Backpack=Rucksack", "Gender=Female,Hair=Bob" -Pose
.\tools\preview.ps1 -Out heads.png -Sets "HeadShape=Square,FacialHair=Beard" -Head
```

`tools/gen_mannequin_data.py` regenerates `MannequinData.cs` from a reference-pose dump of the
mannequin taken in the Unreal Editor.

## Credits

The realistic body uses the MakeHuman base mesh (hm08), its shape targets and the game_engine rig
weights, released as CC0 by Data Collection AB, Joel Palmius and Jonas Hauquier
(http://www.makehumancommunity.org, https://github.com/makehumancommunity/mpfb2).
