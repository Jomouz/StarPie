[CmdletBinding()]
param(
    [string]$PluginDirectory = 'H:\starpie-official-worktrees\qr-scanner\src\StarPie.Plugin.QrScanner\bin\Release\net8.0-windows',
    [switch]$PrepareOnly
)

$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$hostDirectory = Join-Path $repository 'WinPieGestures/bin/Release/net8.0-windows10.0.19041.0'
$executable = Join-Path $hostDirectory 'StarPie.exe'
$PluginDirectory = [IO.Path]::GetFullPath($PluginDirectory)
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build the candidate host first.' }
$required = @('StarPie.Plugin.QrScanner.dll', 'zxing.dll', 'plugin.json', 'THIRD-PARTY-NOTICES.txt', 'ZXing-LICENSE.txt')
foreach ($name in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $PluginDirectory $name))) { throw "Missing plugin payload: $name" }
}
if (Test-Path -LiteralPath (Join-Path $PluginDirectory 'StarPie.Plugin.Abstractions.dll')) { throw 'Plugin output must not carry the SDK.' }

# A new profile on every run; never mutate the user's installed configuration or registry.
$profile = Join-Path $repository ('scratch/qr-evidence/acceptance-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $profile | Out-Null
function New-CandidateStartInfo([string]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $executable
    $info.WorkingDirectory = $hostDirectory
    $info.Arguments = $Arguments
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.EnvironmentVariables['LOCALAPPDATA'] = $profile
    return $info
}

# Let the actual candidate produce a complete default config through its headless CLI.
$initial = [Diagnostics.Process]::Start((New-CandidateStartInfo '--test-instance --plugin-paths'))
if (-not $initial.WaitForExit(15000)) { throw 'Candidate profile initialization has not exited; no GUI was started.' }
if ($initial.ExitCode -ne 0) { throw "Candidate profile initialization failed: $($initial.ExitCode)" }
$initial.Dispose()
$configPath = Join-Path $profile 'StarPie/config.json'
$config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json
$config.AutoCheckUpdate = $false
$config.Plugins.HasPromptedOfficialPluginsOnboarding = $true
$config.Plugins.EnablePluginSystem = $true
$config.Plugins.PortableMode = $false
[IO.File]::WriteAllText($configPath, ($config | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))

$manifest = Get-Content -LiteralPath (Join-Path $PluginDirectory 'plugin.json') -Raw | ConvertFrom-Json
$managedRoot = Join-Path $profile 'StarPie/plugin-data'
$installed = Join-Path $managedRoot $manifest.id
New-Item -ItemType Directory -Path $installed -Force | Out-Null
foreach ($name in $required) { Copy-Item -LiteralPath (Join-Path $PluginDirectory $name) -Destination $installed }
$registry = @{
    registryVersion = 1
    entries = @(@{
        id = $manifest.id; name = $manifest.name; version = $manifest.version
        description = $manifest.description; author = $manifest.author; license = $manifest.license
        installPath = $manifest.id; enabled = $true; preload = $false; official = $true
        source = 'UserSelectedFolder'; capabilitiesAck = @($manifest.capabilities)
        entrySha256 = (Get-FileHash -LiteralPath (Join-Path $installed $manifest.assembly) -Algorithm SHA256).Hash
    })
}
[IO.File]::WriteAllText((Join-Path $managedRoot 'registry.json'), ($registry | ConvertTo-Json -Depth 100), [Text.UTF8Encoding]::new($false))
Write-Host "Candidate profile: $profile"
Write-Host 'This candidate requests ScreenCapture, Ui, Clipboard and Process. Copy/open require a button click.'
Write-Host 'In gesture actions choose Plugin action > QR scanner > Scan a selected region / Scan near the pointer.'
if (-not $PrepareOnly) {
    # Explicit manual invocation of this script starts the visible acceptance app.
    $process = [Diagnostics.Process]::Start((New-CandidateStartInfo '--test-instance'))
    Write-Host "Candidate PID: $($process.Id). Exit via its tray when finished."
    $process.Dispose()
}
