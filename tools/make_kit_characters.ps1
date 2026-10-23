# Exports the pack-kit comparison characters (Body=Pack) to out\kit, and the militia ones to out\kit_militia. Needs the pack data extracted to
# packdata\ first (tools/pack2json.py over FBX exports of the pack's skeletal meshes; never committed).
# Then, in the LowPolyCharTest editor's Python console:
#   py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "C:/source/Claude/LowPolyCharGen/out/kit" /Game/LowPolyCharGen/Kit
#   py "C:/source/Claude/LowPolyCharGen/tools/unreal_kit_lineup.py"
param([string] $OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "out\kit"))

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\src\LowPolyCharGen.Cli\LowPolyCharGen.Cli.csproj" -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$cli = "$root\src\LowPolyCharGen.Cli\bin\Release\net10.0\lowpolychargen.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

$characters = [ordered]@{
    "LPCG_Kit_Rifleman" = "PackBody=TSA_Body_A PackLegs=TSA_Legs_B PackHead=TSA_Head_B1 FacialHair=Stubble"
    "LPCG_Kit_Heavy" = "PackBody=TSA_Body_D PackLegs=TSA_Legs_C PackHead=TSA_Head_D2 Build=1 HeadShape=Heavy Nose=Broad SkinColor=#8A5A3C HairColor=#151010"
    "LPCG_Kit_Scout_Slim" = "PackBody=TSA_Body_J PackLegs=TSA_Legs_A PackHead=TSA_Head_H1 Build=0 HeadShape=Slim Nose=Roman FacialHair=Moustache HairColor=#2A1A12"
    "LPCG_Kit_Tee_Slim" = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Build=0 HeadShape=Slim SkinColor=#F0C8A8 HairColor=#C8A060 EyeColor=#4A6A8A FacialHair=Stubble"
    "LPCG_Kit_Tee" = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1"
    "LPCG_Kit_Tee_Heavy" = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Build=1 HeadShape=Square Nose=Broad SkinColor=#7A4A30 HairColor=#151010 FacialHair=Beard"
    "LPCG_Kit_Female_Tee" = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Gender=Female Hair=Long HairColor=#3A2416"
    "LPCG_Kit_Female_Rifleman" = "PackBody=TSA_Body_E PackLegs=TSA_Legs_B PackHead=TSA_Head_B1 Gender=Female PackEquipment=TSA_light_backpack Hair=Ponytail HairColor=#2A1A12"
    "LPCG_Kit_Female_Slim" = "PackBody=TSA_Body_K PackLegs=TSA_Legs_A PackHead=TSA_Head_K1 Gender=Female Build=0.25 Nose=Button Hair=Bun HairColor=#C89A58 SkinColor=#E8BC98 EyeColor=#4A6A8A"
    "LPCG_Kit_Tee_Urban" = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 RecolourClothes=true ShirtColor=#2E3A4A TrousersColor=#3A3A3C BootsColor=#1C1A18 GloveStyle=Bare FacialHair=Goatee SkinColor=#C89070 HairColor=#1E1612 Footwear=Shoes"
    "LPCG_Kit_Female_Bob" = "PackBody=TSA_Body_I PackLegs=TSA_Legs_C PackHead=TSA_Head_A1 Gender=Female Build=0.4 Hair=Bob HairColor=#D8B070 SkinColor=#EBC2A0 EyeColor=#3F6E9A"
}

foreach ($name in $characters.Keys) {
    $cliArgs = @("--out", (Join-Path $OutDir "$name.fbx"), "--set", "Name=$name", "--set", "Body=Pack")
    foreach ($pair in $characters[$name].Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) }
    & $cli @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export of $name failed" }
}

$militia = [ordered]@{
    "LPCG_Mil_Turban_Thobe" = "PackBody=TSA_Body_H PackLegs=MIL_Mesh_Legs_A Hat=Turban Legs=Thobe FacialHair=BushyBeard ShirtColor=#E8E4DA HatColor=#2A2A2E SkinColor=#B07850 HairColor=#1A1410"
    "LPCG_Mil_Keffiyeh_Thobe" = "PackBody=TSA_Body_H PackLegs=MIL_Mesh_Legs_A Hat=Keffiyeh Legs=Thobe FacialHair=Beard ShirtColor=#9A8C74 HatColor=#E8E4DA AccentColor=#9A2A26 SkinColor=#A87048 HairColor=#1A1410 Footwear=Shoes"
    "LPCG_Mil_Shemagh" = "PackBody=MIL_Mesh_Body_B PackLegs=MIL_Mesh_Legs_C FaceCover=Shemagh AccentColor=#C8B898"
    "LPCG_Mil_Balaclava" = "PackBody=MIL_Mesh_Body_D PackLegs=MIL_Mesh_Legs_B FaceCover=Balaclava"
    "LPCG_Mil_Beanie" = "PackBody=MIL_Mesh_Body_A PackLegs=MIL_Mesh_Legs_D PackHead=MIL_Mesh_head_D FacialHair=Beard SkinColor=#B88058"
    "LPCG_Mil_Turban_Jeans" = "PackBody=MIL_Mesh_Body_E PackLegs=MIL_Mesh_Legs_B Hat=Turban FacialHair=BushyBeard HatColor=#D8D0C0 SkinColor=#9A6440 HairColor=#151010 Footwear=Trainers BootsColor=#D8D8D0"
    "LPCG_Mil_Thobe_Grey" = "PackBody=TSA_Body_H PackLegs=MIL_Mesh_Legs_A Legs=Thobe Hat=Turban FacialHair=Beard ShirtColor=#7A766C HatColor=#1C1C1E SkinColor=#C08A60 HairColor=#2A1A12 Build=0.8"
    "LPCG_Mil_Shemagh_Black" = "PackBody=MIL_Mesh_Body_F PackLegs=MIL_Mesh_Legs_A FaceCover=Shemagh AccentColor=#202024 MilitiaTexture=T_Militias_B"
}
$MilitiaDir = Join-Path (Split-Path $OutDir -Parent) "kit_militia"
New-Item -ItemType Directory -Force $MilitiaDir | Out-Null
foreach ($name in $militia.Keys) {
    $cliArgs = @("--out", (Join-Path $MilitiaDir "$name.fbx"), "--set", "Name=$name", "--set", "Body=Pack")
    foreach ($pair in $militia[$name].Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) }
    & $cli @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export of $name failed" }
}
