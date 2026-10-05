# Exports the OpDesertStorm cast (1991, Kuwait) to out\desertstorm: the US specialist squad in
# chocolate-chip DCUs and Iraqi Army / Republican Guard infantry. Weapons are not part of the meshes;
# the game attaches them (suggested pairings in the comments).
# Then import with tools/import_unreal.py into the game's /Game/Characters/DesertStorm.
param([string] $OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "out\desertstorm"))

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\src\LowPolyCharGen.Cli\LowPolyCharGen.Cli.csproj" -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$cli = "$root\src\LowPolyCharGen.Cli\bin\Release\net10.0\lowpolychargen.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

$us = "Edition=Desert Camouflage=ChocolateChip Legs=Cargo"
$iraqi = "Edition=Custom Camouflage=None Legs=Trousers HairColor=#1C1714 BootsColor=#1E1C1A GearColor=#4A4630 ShirtColor=#5A5A3C TrousersColor=#55553A"
$guard = "Edition=Custom Camouflage=Woodland Legs=Cargo HairColor=#1C1714 BootsColor=#2A2420 GearColor=#5A5038 ShirtColor=#A0916A TrousersColor=#9A8B64"

$characters = [ordered]@{
    # US Army specialists (squad slots 1-4)
    "ODS_US_Commander"  = "$us Hat=Helmet Torso=Jacket Vest=ChestRig Belt=Pouches Backpack=Radio Eyewear=Sunglasses SkinColor=#E0B896 Hair=Buzz HeadShape=Square Grime=0.3"               # M16A2 / CAR-15
    "ODS_US_Sniper"     = "$us Hat=Boonie Torso=RolledSleeves Vest=None Belt=Pouches Backpack=Hydration Scarf=true Gloves=true SkinColor=#C49A78 Hair=Short FacialHair=Stubble Grime=0.4 Fatigue=0.3"   # M24 (bolt action)
    "ODS_US_Engineer"   = "$us Hat=Cap Torso=Jacket Vest=ChestRig Belt=Utility Backpack=Tools Eyewear=Goggles Gloves=true KneePads=true SkinColor=#6B4630 Hair=Buzz Build=0.7 Grime=0.5"      # M16A2
    "ODS_US_Demolition" = "$us Hat=Helmet Torso=LongSleeve Vest=ChestRig Belt=Pouches Backpack=Demolition AccentColor=#5E6038 Eyewear=Goggles KneePads=true ElbowPads=true SkinColor=#F0C8A8 Hair=Short Build=0.6 Grime=0.4"   # M16A2 / CAR-15
    # Iraqi Army infantry (olive drab)
    "ODS_IRQ_Rifleman_Beret"   = "$iraqi Hat=Beret HatColor=#2E3326 Torso=Jacket Vest=ChestRig Belt=Plain FacialHair=Moustache Hair=Short SkinColor=#A87B58 HeadShape=Standard Grime=0.3"      # AK-47 / AKM
    "ODS_IRQ_Rifleman_Helmet"  = "$iraqi Hat=Helmet HatColor=#4A4B36 Torso=LongSleeve Vest=ChestRig Belt=Pouches Backpack=Light FacialHair=Moustache Hair=Buzz SkinColor=#8C6446 HeadShape=Round Build=0.6 Grime=0.5 Fatigue=0.5"   # AKM
    "ODS_IRQ_Shemagh_Red"      = "$iraqi FaceCover=Shemagh AccentColor=#8E2B27 Torso=RolledSleeves Vest=ChestRig Belt=Plain Hair=Short SkinColor=#B88A64 Grime=0.4"                              # AK-47
    "ODS_IRQ_Shemagh_Helmet"   = "$iraqi Hat=Helmet HatColor=#4A4B36 FaceCover=Shemagh AccentColor=#2A2A2E Torso=Jacket Belt=Pouches FacialHair=Stubble SkinColor=#9A6E4E Grime=0.5 Stress=0.4"   # PKM / RPG-7
    # Republican Guard (tan camouflage, red beret)
    "ODS_IRQ_Guard_Beret"      = "$guard Hat=Beret HatColor=#8A1F1F Torso=Jacket Vest=PlateCarrier Belt=Pouches FacialHair=Moustache Hair=Short SkinColor=#A87B58 HeadShape=Square Build=0.6 Grime=0.2"   # AKM / SVD
    "ODS_IRQ_Guard_Helmet"     = "$guard Hat=Helmet HatColor=#8E8060 Torso=LongSleeve Vest=ChestRig Belt=Pouches Backpack=Light Eyewear=Goggles FacialHair=Moustache Hair=Buzz SkinColor=#8C6446 HeadShape=Heavy Build=0.8 Grime=0.4"   # AKM
}

foreach ($name in $characters.Keys) {
    $cliArgs = @("--out", (Join-Path $OutDir "$name.fbx"), "--set", "Name=$name")
    foreach ($pair in $characters[$name].Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) }
    & $cli @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export of $name failed" }
}
