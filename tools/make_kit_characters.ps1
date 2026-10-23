# Exports the pack-kit comparison characters (Body=Pack) to out\kit. Needs the pack data extracted to
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
    "LPCG_Kit_Rifleman"        = "PackBody=TSA_Body_A PackLegs=TSA_Legs_B PackHead=TSA_Head_B1"
    "LPCG_Kit_Heavy"           = "PackBody=TSA_Body_D PackLegs=TSA_Legs_C PackHead=TSA_Head_D2 Build=1"
    "LPCG_Kit_Scout_Slim"      = "PackBody=TSA_Body_J PackLegs=TSA_Legs_A PackHead=TSA_Head_H1 Build=0"
    "LPCG_Kit_Tee_Slim"        = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Build=0"
    "LPCG_Kit_Tee"             = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1"
    "LPCG_Kit_Tee_Heavy"       = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Build=1"
    "LPCG_Kit_Female_Tee"      = "PackBody=TSA_Body_G PackLegs=TSA_Legs_A PackHead=TSA_Head_A1 Gender=Female"
    "LPCG_Kit_Female_Rifleman" = "PackBody=TSA_Body_E PackLegs=TSA_Legs_B PackHead=TSA_Head_B1 Gender=Female PackEquipment=TSA_light_backpack"
    "LPCG_Kit_Female_Slim"     = "PackBody=TSA_Body_K PackLegs=TSA_Legs_A PackHead=TSA_Head_K1 Gender=Female Build=0.25"
}

foreach ($name in $characters.Keys) {
    $cliArgs = @("--out", (Join-Path $OutDir "$name.fbx"), "--set", "Name=$name", "--set", "Body=Pack")
    foreach ($pair in $characters[$name].Split(" ", [StringSplitOptions]::RemoveEmptyEntries)) { $cliArgs += @("--set", $pair) }
    & $cli @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export of $name failed" }
}
