# Builds the CLI, exports characters and renders a Blender contact sheet of them.
#
#   .\preview.ps1 -Out sheet.png -Sets "Hat=Helmet,Backpack=Rucksack", "Gender=Female,Hair=Bob" [-Pose] [-Views front,back]
#
# Each entry of -Sets is one character: a comma separated list of Property=Value overrides
# ("" for the default character).
param(
    [Parameter(Mandatory)] [string] $Out,
    [string[]] $Sets = @(""),
    [string] $Views = "front,three_quarter,side,back",
    [string] $Size = "560x600",
    [switch] $Pose,
    [switch] $Head,
    [switch] $Report,
    [string] $Blender = "C:\Program Files\Blender Foundation\Blender 5.2\blender.exe"
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$work = Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Out))) "fbx"
New-Item -ItemType Directory -Force $work | Out-Null

dotnet build "$root\src\LowPolyCharGen.Cli\LowPolyCharGen.Cli.csproj" -nologo -v q | Select-String -Pattern "error|Warn.*[1-9] Warning" | ForEach-Object { $_.Line }
if ($LASTEXITCODE -ne 0) { throw "build failed" }

$files = @()
$i = 0
foreach ($set in $Sets) {
    $i++
    $file = Join-Path $work ("char{0:00}.fbx" -f $i)
    $cliArgs = @("--out", $file)
    foreach ($pair in ($set -split "," | Where-Object { $_ })) { $cliArgs += @("--set", $pair.Trim()) }
    & "$root\src\LowPolyCharGen.Cli\bin\Debug\net10.0\lowpolychargen.exe" @cliArgs
    if ($LASTEXITCODE -ne 0) { throw "export failed" }
    $files += $file
}

$blenderArgs = @("-b", "--factory-startup", "--python", "$PSScriptRoot\blender_preview.py", "--", $Out) + $files + @("--views", $Views, "--size", $Size)
if ($Pose) { $blenderArgs += "--pose" }
if ($Head) { $blenderArgs += "--head" }
if ($Report) { $blenderArgs += "--report" }
Remove-Item $Out -ErrorAction SilentlyContinue   # so the check below sees this run's sheet
$ErrorActionPreference = "Continue"
& $Blender @blenderArgs 2>&1 | ForEach-Object { "$_" } |
    Where-Object { $_ -match "^(===|ARMATURE|MESH|   |wrote|Traceback|.*Error)" -and $_ -notmatch "Saved:" }
if (-not (Test-Path $Out)) { throw "Blender did not write $Out" }
