param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Push-Location $projectRoot
try {
    & $dotnet build src/GrabForge.Desktop/GrabForge.Desktop.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }
    & $dotnet build src/GrabForge.Updater/GrabForge.Updater.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Updater build failed.' }
    & $dotnet test tests/GrabForge.Tests/GrabForge.Tests.csproj -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    if ($Publish) {
        $out = Join-Path $projectRoot "artifacts\GrabForge-v$version-win-x64"
        $stage = Join-Path $projectRoot ('artifacts\.publish-' + [guid]::NewGuid().ToString('N'))
        & $dotnet publish src/GrabForge.Desktop/GrabForge.Desktop.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $stage --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
        & $dotnet publish src/GrabForge.Updater/GrabForge.Updater.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $stage 'updater') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
        foreach ($name in @('README.md','LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $stage }
        $licenseFolder = Join-Path $stage 'licenses'
        New-Item -ItemType Directory -Path $licenseFolder -Force | Out-Null
        foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
            $runtimeNotice = Join-Path (Split-Path -Parent $dotnet) $name
            if (-not (Test-Path -LiteralPath $runtimeNotice)) { throw "Missing .NET distribution notice: $name" }
            Copy-Item -LiteralPath $runtimeNotice -Destination (Join-Path $licenseFolder "DOTNET-$name")
        }
        @{ Product = 'GrabForge'; Version = $version; Runtime = 'win-x64' } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $stage 'release.json') -Encoding utf8
        $zip = "$out.zip"
        $packageItems = Get-ChildItem -LiteralPath $stage -Force | Where-Object { $_.Name -notin @('data', 'tools') -and -not $_.Name.StartsWith('.') }
        Compress-Archive -LiteralPath @($packageItems.FullName) -DestinationPath $zip -Force
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $audit = [System.IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($entry in $audit.Entries) {
                $top = $entry.FullName.Replace('\', '/').Split('/')[0]
                if ($top -in @('data', 'tools') -or $top.StartsWith('.')) { throw 'Release ZIP includes local data or tools.' }
            }
            if (-not ($audit.Entries | Where-Object { $_.FullName -eq 'GrabForge.exe' })) { throw 'Release ZIP is missing GrabForge.exe at root.' }
        } finally { $audit.Dispose() }
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [System.IO.File]::WriteAllText("$zip.sha256", "$hash  $([System.IO.Path]::GetFileName($zip))`n")
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        foreach ($item in $packageItems) { Copy-Item -LiteralPath $item.FullName -Destination $out -Recurse -Force }
        $verifiedStage = [System.IO.Path]::GetFullPath($stage)
        $artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $verifiedStage.StartsWith($artifactRoot, [System.StringComparison]::OrdinalIgnoreCase) -or -not ([System.IO.Path]::GetFileName($verifiedStage)).StartsWith('.publish-')) { throw 'Unexpected staging directory.' }
        Remove-Item -LiteralPath $verifiedStage -Recurse -Force
        Write-Host "Release artifact: $zip"
    }
} finally { Pop-Location }
