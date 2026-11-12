#requires -Version 7.0
param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -LiteralPath (Join-Path $projectRoot 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version
$localSdk = Join-Path $projectRoot '.tools/dotnet/dotnet.exe'
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
        $packageFolder = Join-Path $projectRoot 'artifacts/packages'
        $out = Join-Path $projectRoot "artifacts/publish/GrabForge-v$version-win-x64"
        $stage = Join-Path $projectRoot ('artifacts/work/publish-' + [guid]::NewGuid().ToString('N'))
        $app = Join-Path $stage 'app'
        New-Item -ItemType Directory -Path $packageFolder -Force | Out-Null
        & $dotnet publish src/GrabForge.Desktop/GrabForge.Desktop.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o $app --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Desktop publish failed.' }
        & $dotnet publish src/GrabForge.Updater/GrabForge.Updater.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o (Join-Path $app 'updater') --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Updater publish failed.' }
        $hostPack = Get-ChildItem -LiteralPath (Join-Path (Split-Path -Parent $dotnet) 'packs/Microsoft.NETCore.App.Host.win-x64') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        $template = Join-Path $hostPack.FullName 'runtimes/win-x64/native/apphost.exe'
        & $dotnet run -c Release --project build/GrabForge.Packaging -- $template (Join-Path $stage 'GrabForge.exe') (Join-Path $app 'GrabForge.dll')
        if ($LASTEXITCODE -ne 0) { throw 'Root executable creation failed.' }
        Remove-Item -LiteralPath (Join-Path $app 'GrabForge.exe') -Force
        $docs = Join-Path $app 'docs'
        New-Item -ItemType Directory -Path (Join-Path $docs 'assets'),(Join-Path $docs 'images'),(Join-Path $docs 'licenses') -Force | Out-Null
        foreach ($name in @('LICENSE','THIRD-PARTY-NOTICES.md','CHANGELOG.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $docs }
        foreach ($name in @('ACCEPTANCE.md','RELEASING.md','DESIGN.md')) { Copy-Item -LiteralPath (Join-Path $projectRoot "docs/$name") -Destination $docs }
        Copy-Item -LiteralPath (Join-Path $projectRoot 'assets/grabforge.png') -Destination (Join-Path $docs 'assets')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/images/app.png') -Destination (Join-Path $docs 'images')
        Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/help.html') -Destination (Join-Path $docs 'index.html')
        $readme = (Get-Content -LiteralPath (Join-Path $projectRoot 'README.md') -Raw).Replace('(docs/', '(').Replace('src="docs/', 'src="')
        [IO.File]::WriteAllText((Join-Path $docs 'README.md'), $readme)
        foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
            $notice = Join-Path (Split-Path -Parent $dotnet) $name
            if (-not (Test-Path -LiteralPath $notice)) { throw "Missing .NET distribution notice: $name" }
            Copy-Item -LiteralPath $notice -Destination (Join-Path $docs "licenses/DOTNET-$name")
        }
        $manifest = @{ Product = 'GrabForge'; Version = $version; Runtime = 'win-x64'; Layout = 2 } | ConvertTo-Json
        [IO.File]::WriteAllText((Join-Path $app 'release.json'), $manifest)
        # v0.2.1-v0.3.1 helpers read this at root. Startup relocates it into app/.
        [IO.File]::WriteAllText((Join-Path $stage 'release.json'), $manifest)
        $zip = Join-Path $packageFolder "GrabForge-v$version-win-x64.zip"
        Compress-Archive -LiteralPath (Join-Path $stage 'GrabForge.exe'),(Join-Path $stage 'release.json'),$app -DestinationPath $zip -Force
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        $audit = [IO.Compression.ZipFile]::OpenRead($zip)
        try {
            foreach ($entry in $audit.Entries) {
                $name = $entry.FullName.Replace('\', '/')
                if ($name -notin @('GrabForge.exe','release.json') -and -not $name.StartsWith('app/')) { throw "Unexpected root package item: $name" }
                if ($name -match '\.pdb$|(^|/)(data|tools|\.tools)/') { throw 'Release ZIP includes local data, tools or symbols.' }
            }
            foreach ($required in @('GrabForge.exe','app/GrabForge.dll','app/hostfxr.dll','app/release.json','app/updater/GrabForge.Updater.exe','app/docs/index.html')) {
                if (-not $audit.GetEntry($required)) { throw "Missing package entry: $required" }
            }
        } finally { $audit.Dispose() }
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))`n")
        if (Test-Path -LiteralPath $out) { throw 'Publish output already exists. Move the previous version to artifacts/qa before publishing again.' }
        New-Item -ItemType Directory -Path $out -Force | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $stage -Force) { Copy-Item -LiteralPath $item.FullName -Destination $out -Recurse -Force }
        $verifiedStage = [IO.Path]::GetFullPath($stage)
        $workRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/work')) + [IO.Path]::DirectorySeparatorChar
        if (-not $verifiedStage.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected staging directory.' }
        Remove-Item -LiteralPath $verifiedStage -Recurse -Force
        Write-Host "Release artifact: $zip"
    }
} finally { Pop-Location }
