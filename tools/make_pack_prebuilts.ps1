# Rebuilds the packs' prebuilt characters (SK_Armies_*, SK_SpecOps_*, SK_Militia*, and the UE5 pack's 12 in two
# colourways) with the kit: the same parts
# (tools/pack_prebuilts.txt, matched from the prebuilt meshes) and the default pack textures, with the kit's faces.
# Needs the pack data in packdata\ (tools/pack2json.py), plus the prebuilt-only pieces (gas mask, sunglasses,
# pouches) extracted with:  py tools/pack_extras.py
# Then, in the LowPolyCharTest editor's Python console:
#   py "C:/source/Claude/LowPolyCharGen/tools/import_unreal.py" "C:/source/Claude/LowPolyCharGen/out/kit_pack" /Game/LowPolyCharGen/Kit/Pack
#   py "C:/source/Claude/LowPolyCharGen/tools/unreal_character_level.py"
# -Lists: the character lists (pack_prebuilts.txt, the factions of pack_factions.txt); -Only: a name wildcard (DesertStorm_*).
param([string] $OutDir = (Join-Path (Split-Path $PSScriptRoot -Parent) "out\kit_pack"),
      [string[]] $Lists = @("pack_prebuilts.txt", "pack_factions.txt"), [string] $Only = "*")

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
dotnet build "$root\src\LowPolyCharGen.Cli\LowPolyCharGen.Cli.csproj" -c Release -nologo -v q | Out-Null
if ($LASTEXITCODE -ne 0) { throw "build failed" }
$cli = "$root\src\LowPolyCharGen.Cli\bin\Release\net10.0\lowpolychargen.exe"
New-Item -ItemType Directory -Force $OutDir | Out-Null

foreach ($line in $Lists | ForEach-Object { Get-Content (Join-Path $PSScriptRoot $_) }) {
    if ($line.StartsWith("#") -or -not $line.Trim()) { continue }
    $n, $head, $body, $legs, $equip, $more, $weapon = $line.Split("|")   # the weapon is for the display level only
    if ($n -notlike $Only) { continue }
    $name = "LPCG_Pack_$n"
    $cliArgs = @("--out", (Join-Path $OutDir "$name.fbx"), "--set", "Name=$name", "--set", "Body=Pack", "--set", "PackHead=$head",
        "--set", "PackBody=$body", "--set", "PackLegs=$legs", "--set", "PackEquipment=$equip")
    if ($more) { foreach ($pair in $more.Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) } }
    & $cli @cliArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "export failed: $name" }
    Write-Host $name
}
