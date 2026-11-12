#requires -Version 7.0
param([string]$Archive, [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/qa/package-boot'))
$ErrorActionPreference = 'Stop'
if (-not $Archive) {
    $packages = @(Get-ChildItem -Path (Join-Path $PSScriptRoot '../artifacts/packages/*-win-x64.zip'))
    if ($packages.Count -ne 1) { throw 'Specify -Archive when more than one package exists.' }
    $Archive = $packages[0].FullName
}
$Archive = (Resolve-Path -LiteralPath $Archive).Path
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new QA directory; existing data is not overwritten.' }
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$root = (Resolve-Path -LiteralPath $OutputDirectory).Path
Expand-Archive -LiteralPath $Archive -DestinationPath $root
function Check-Launch([string]$name) {
    $result = Join-Path $root "data/$name.json"
    $processInfo = [Diagnostics.ProcessStartInfo]::new((Join-Path $root 'GrabForge.exe'))
    $processInfo.UseShellExecute = $false; $processInfo.CreateNoWindow = $true
    $processInfo.WorkingDirectory = [IO.Path]::GetTempPath()
    $processInfo.Environment['DOTNET_ROOT'] = Join-Path $root 'not-installed'
    $processInfo.Environment['DOTNET_ROOT_X64'] = Join-Path $root 'not-installed'
    $processInfo.ArgumentList.Add('--self-check'); $processInfo.ArgumentList.Add($result)
    $process = [Diagnostics.Process]::Start($processInfo)
    try {
        if (-not $process.WaitForExit(45000)) { $process.Kill($true); throw 'Packaged startup timed out.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $result)) { throw "Packaged startup failed ($($process.ExitCode))." }
    } finally { $process.Dispose() }
    $report = Get-Content -LiteralPath $result -Raw | ConvertFrom-Json
    foreach ($pair in @(@($report.Root,$root), @($report.BaseDirectory,(Join-Path $root 'app')), @($report.RuntimeDirectory,(Join-Path $root 'app')), @($report.Tools,(Join-Path $root 'data/tools')), @($report.Updater,(Join-Path $root 'app/updater')))) {
        if ([IO.Path]::TrimEndingDirectorySeparator($pair[0]) -ne [IO.Path]::TrimEndingDirectorySeparator($pair[1])) { throw "Unexpected portable path: $($pair[0])" }
    }
    $files = @(Get-ChildItem -LiteralPath $root -File)
    if ($files.Count -ne 1 -or $files[0].Name -ne 'GrabForge.exe') { throw 'Root files were not tidied.' }
    if (Test-Path -LiteralPath (Join-Path $root 'app/data')) { throw 'Profile was placed inside runtime files.' }
    return $report
}
$report = Check-Launch first
[IO.File]::WriteAllText((Join-Path $root 'data/preservation.txt'), 'keep portable data')
$null = Check-Launch second
if ((Get-Content -LiteralPath (Join-Path $root 'data/preservation.txt') -Raw) -ne 'keep portable data') { throw 'Restart changed user data.' }
"PASS: packaged native host v$($report.Version), private runtime, portable paths, clean root and restart preservation"
