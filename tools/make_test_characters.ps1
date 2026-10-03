# Exports the LowPolyCharTest lineup characters to out\characters (FBX, clean texture, grime layer, _Spec.json).
# Then, in the LowPolyCharTest editor's Python console:
#   py "C:/source/Claude/LowPolyCharGen/tools/lpct_refresh.py"
param([string] $OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "out\characters"))

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\src\LowPolyCharGen.Cli\LowPolyCharGen.Cli.csproj" -c Release -nologo -v q | Out-Null
$cli = "$root\src\LowPolyCharGen.Cli\bin\Release\net10.0\lowpolychargen.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

$characters = [ordered]@{
    "LPCG_Desert_Rifleman"   = "Edition=Desert Hat=Helmet Vest=PlateCarrier Belt=Pouches Torso=LongSleeve Legs=Cargo Backpack=Light"
    "LPCG_Desert_Scout"      = "Edition=Desert Hat=Boonie Vest=ChestRig Torso=RolledSleeves Legs=Cargo Backpack=Rucksack Gloves=true Scarf=true"
    "LPCG_Snow_Rifleman"     = "Edition=Snow Hat=Helmet Vest=PlateCarrier Belt=Pouches Torso=LongSleeve Legs=Cargo Backpack=Light"
    "LPCG_Snow_Scout"        = "Edition=Snow Hat=Boonie Vest=ChestRig Torso=RolledSleeves Legs=Cargo Backpack=Rucksack Gloves=true Scarf=true"
    "LPCG_Jungle_Rifleman"   = "Edition=Jungle Hat=Helmet Vest=PlateCarrier Belt=Pouches Torso=LongSleeve Legs=Cargo Backpack=Light"
    "LPCG_Jungle_Scout"      = "Edition=Jungle Hat=Boonie Vest=ChestRig Torso=RolledSleeves Legs=Cargo Backpack=Rucksack Gloves=true Scarf=true"
    "LPCG_Desert_Weathered"  = "Edition=Desert Hat=Helmet Vest=PlateCarrier Belt=Pouches Torso=LongSleeve Legs=Cargo FacialHair=Stubble Grime=0.8 Fatigue=0.8 Stress=0.8"
    "LPCG_Desert_Medic"      = "Edition=Desert Gender=Female Hair=Ponytail Hat=Cap Torso=TShirt Legs=Cargo Belt=Utility Backpack=Light"
    "LPCG_Jungle_Sniper"     = "Edition=Jungle Gender=Female Hair=Bun Hat=Boonie Torso=TankTop Legs=Cargo Belt=Pouches Gloves=true"
    "LPCG_Civilian_Hoodie"   = "Edition=Civilian Torso=Hoodie Legs=Jeans Hat=Beanie ShirtColor=#3E5F7A"
    "LPCG_Civilian_Tee"      = "Edition=Civilian Gender=Female Hair=Long Torso=TShirt Legs=Jeans ShirtColor=#C9A23A Backpack=Light"
    "LPCG_Militia_Balaclava" = "Edition=Militia FaceCover=Balaclava Torso=TShirt Vest=PlateCarrier Legs=Cargo ShirtColor=#3B4434 Grime=0.4 Fatigue=0.4"
    "LPCG_Militia_Shemagh"   = "Edition=Militia FaceCover=Shemagh Torso=LongSleeve Legs=Cargo Hat=Beanie ShirtColor=#5A4A3A GearColor=#4A4A44 Grime=0.5 Fatigue=0.5 Stress=0.3"
    "LPCG_Militia_Female"    = "Edition=Militia Gender=Female FaceCover=Shemagh AccentColor=#7A2A26 Torso=CheckShirt ShirtColor=#3E4A5A Legs=Jeans Hair=Ponytail Grime=0.3"
    "LPCG_Militia_Keffiyeh"  = "Edition=Militia Legs=Thobe Torso=LongSleeve Hat=Keffiyeh FacialHair=BushyBeard FaceCover=None ShirtColor=#E8E4DA AccentColor=#7A2A26 Camouflage=None Belt=None Vest=ChestRig Grime=0.4 Fatigue=0.5 Stress=0.4"
    "LPCG_Militia_Turban"    = "Edition=Militia Legs=Thobe Torso=LongSleeve Hat=Turban FacialHair=BushyBeard FaceCover=None ShirtColor=#8E8A80 HatColor=#1E1E20 Camouflage=None Belt=None Vest=None Grime=0.3"
}

foreach ($name in $characters.Keys) {
    $cliArgs = @("--out", (Join-Path $OutDir "$name.fbx"), "--set", "Name=$name")
    foreach ($pair in $characters[$name].Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) }
    & $cli @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export of $name failed" }
}
