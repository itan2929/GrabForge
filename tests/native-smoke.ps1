#requires -Version 7.0
param(
    [Parameter(Mandatory)][string]$Driver,
    [Parameter(Mandatory)][int]$TargetPid,
    [Parameter(Mandatory)][long]$WindowId,
    [Parameter(Mandatory)][string]$Socket,
    [Parameter(Mandatory)][string]$DemoUrl,
    [string]$Session = 'grabforge-smoke',
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\native-smoke')
)
$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$OutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
function Call-Cua([string]$tool, [hashtable]$arguments) {
    $arguments.pid = $TargetPid; $arguments.window_id = $WindowId; $arguments.session = $Session
    $result = $arguments | ConvertTo-Json -Compress | & $Driver call $tool --socket $Socket | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0 -or $result.isError) { throw ($result | ConvertTo-Json -Depth 4) }
    return $result
}
function Observe([string]$name = 'state') {
    $script:state = Call-Cua 'get_window_state' @{ screenshot_out_file = (Join-Path $OutputDirectory "$name.png") }
    return $script:state.tree_markdown
}
function Act([string]$tool, [string]$pattern, [string]$value = '') {
    $tree = Observe
    $line = @($tree -split "`n" | Where-Object { $_ -match $pattern })[0]
    if ($line -notmatch '\[(\d+)\]') { throw "No actionable match: $pattern" }
    $arguments = @{ element_token = ($script:state.snapshot_id + ':' + $Matches[1]) }
    if ($tool -eq 'set_value') { $arguments.value = $value }
    $null = Call-Cua $tool $arguments
    return Observe
}
function Click-Id([string]$id) { return Act 'click' ('id=' + [regex]::Escape($id) + ' ') }
function Set-Id([string]$id, [string]$value) { return Act 'set_value' ('id=' + [regex]::Escape($id) + ' ') $value }
function Require([bool]$condition, [string]$message) { if (-not $condition) { throw $message }; Write-Output "PASS: $message" }
function Wait-Text([string]$pattern) {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    do { $tree = Observe; if ($tree -match $pattern) { return $tree }; Start-Sleep -Milliseconds 300 } while ([DateTime]::UtcNow -lt $deadline)
    throw "Timed out waiting for $pattern"
}

$null = Set-Id 'Urls' $DemoUrl
$null = Click-Id 'Analyze'
$tree = Wait-Text 'GrabForge demo'
Require ($tree -match '解析完了') 'native local-media analysis'
$null = Click-Id 'ModeAudio'
$null = Click-Id 'ConvertAudio'
$tree = Set-Id 'OutputTemplate' '%(title)s.%(ext)s'
$tree = Wait-Text '\.mp3'
Require ($tree -match '\.mp3') 'live final-extension preview'
$null = Click-Id 'EffectsTab'
$null = Click-Id 'Compressor'
$null = Click-Id 'Normalize'
$null = Click-Id 'NormalizePreset2'
$null = Click-Id 'CompressorAdvanced'
$null = Set-Id 'Ratio' '3.5'
$tree = Click-Id 'NormalizeAdvanced'
Require ($tree -match 'value="3.5" id=Ratio' -and $tree -match 'value="-14" id=TargetLufs') 'independent effect details and simple-preset synchronization'
$null = Observe 'effects'
$null = Click-Id 'MetadataTab'
$null = Observe 'metadata'
$null = Click-Id 'AdvancedTab'
$null = Click-Id 'NetworkTab'
$null = Observe 'network'
$null = Click-Id 'AcquisitionTab'
$null = Observe 'acquisition'
$null = Click-Id 'OtherTab'
$null = Observe 'other'
$null = Act 'click' 'ListItem "ツール・更新".*actions'
$null = Observe 'tools'
$null = Act 'click' 'ListItem "プリセット".*actions'
$name = 'Smoke ' + [DateTime]::UtcNow.ToString('HHmmss')
$null = Set-Id 'PresetName' $name
$null = Click-Id 'SavePreset'
$tree = Wait-Text ('ListItem "' + $name + '"')
Require ($tree -match [regex]::Escape($name)) 'native preset save'
$null = Act 'click' ('ListItem "' + $name + '".*actions')
$tree = Click-Id 'ApplyPreset'
Require ($tree -match 'id=ConvertAudio') 'preset apply returns to configured download view'
$null = Click-Id 'EffectsTab'
$null = Click-Id 'CompressorAdvanced'
$tree = Click-Id 'NormalizeAdvanced'
Require ($tree -match 'value="3.5" id=Ratio' -and $tree -match 'value="-14" id=TargetLufs') 'effect settings survive preset round-trip'
$null = Observe 'readme'
$tree = Observe
$windows = @{pid=$TargetPid;session=$Session} | ConvertTo-Json -Compress | & $Driver call list_windows --socket $Socket | ConvertFrom-Json
$bounds = @($windows.windows | Where-Object window_id -eq $WindowId)[0].bounds
$null = Call-Cua 'set_window_frame' @{x=$bounds.x;y=$bounds.y;width=1040;height=640}
$null = Observe 'minimum'
$resized = @{pid=$TargetPid;session=$Session} | ConvertTo-Json -Compress | & $Driver call list_windows --socket $Socket | ConvertFrom-Json
$frame = @($resized.windows | Where-Object window_id -eq $WindowId)[0].bounds
foreach ($id in @('Ratio','TargetLufs','ResetCompressor','ResetNormalize')) {
    $line = @($state.tree_markdown -split "`n" | Where-Object { $_ -match ('id=' + $id + ' ') })[0]
    if ($line -notmatch '\[(\d+)\]') { throw "Missing minimum-size control: $id" }
    $element = @($state.elements | Where-Object element_index -eq ([int]$Matches[1]))[0]
    Require ($element.frame.w -gt 0 -and $element.frame.h -gt 0 -and $element.frame.y + $element.frame.h -le $frame.y + $frame.height) "minimum-size control visible: $id"
}
$null = Call-Cua 'set_window_frame' @{x=$bounds.x;y=$bounds.y;width=$bounds.width;height=$bounds.height}
$null = Observe
$null = Click-Id 'AddQueue'
$null = Act 'click' 'ListItem "キュー・履歴".*actions'
$tree = Wait-Text 'Complete'
Require ($tree -match 'Complete') 'download and audio processing through native UI'
$null = Observe 'queue'
Write-Output "Screenshots: $OutputDirectory"
