#Requires -Version 5.1
<#
.SYNOPSIS
  Run Aqsat backend + frontend in persistent windows (survives closing this terminal).
.DESCRIPTION
  Starts the API (http://localhost:5027) and the Vite dev server
  (http://127.0.0.1:5173) each in its own PowerShell window (independent of
  this terminal, so both keep running after it closes). Each window writes its
  transcript to .logs/backend.log and .logs/frontend.log, and this script waits
  for each port to answer before returning. Re-running is safe: an already-open
  window with the same title is reused instead of starting a duplicate.
.EXAMPLE
  ./run-dev.ps1
  ./run-dev.ps1 -NoFrontend
  ./run-dev.ps1 -NoBackend
  ./run-dev.ps1 -OpenBrowser
#>
[CmdletBinding()]
param(
  [switch]$NoBackend,
  [switch]$NoFrontend,
  [switch]$OpenBrowser,
  [string]$BackendUrl = 'http://localhost:5027',
  [string]$FrontendUrl = 'http://127.0.0.1:5173',
  [int]$WaitSeconds = 150
)

$ErrorActionPreference = 'Stop'

$RootDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogDir = Join-Path $RootDir '.logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$BackendLog = Join-Path $LogDir 'backend.log'
$FrontendLog = Join-Path $LogDir 'frontend.log'

$BackendPort = ([uri]$BackendUrl).Port
$FrontendPort = ([uri]$FrontendUrl).Port

function Test-PortOpen([string]$Computer, [int]$Port) {
  $client = New-Object Net.Sockets.TcpClient
  try {
    $iar = $client.BeginConnect($Computer, $Port, $null, $null)
    $ok = $iar.AsyncWaitHandle.WaitOne(1000)
    return ($ok -and $client.Connected)
  } catch { return $false } finally { $client.Close() }
}

function Wait-ForPort([string]$Name, [string]$Computer, [int]$Port, [int]$TimeoutSec) {
  $deadline = (Get-Date).AddSeconds($TimeoutSec)
  while ((Get-Date) -lt $deadline) {
    if (Test-PortOpen $Computer $Port) {
      Write-Host "$Name is up on ${Computer}:${Port}."
      return
    }
    Start-Sleep -Seconds 2
  }
  throw "$Name did not answer on ${Computer}:${Port} within ${TimeoutSec}s."
}

function Get-UserSecret([string]$Name) {
  $prefix = $Name + ' = '
  $lines = dotnet user-secrets list --project (Join-Path $RootDir 'src/Aqsat.Api')
  foreach ($line in $lines) {
    if ($line.StartsWith($prefix)) { return $line.Substring($prefix.Length).Trim() }
  }
  throw "user-secret '$Name' not found. Set it once, e.g.: dotnet user-secrets set `"$Name`" `"<value>`" --project src/Aqsat.Api"
}

function Start-PersistentWindow([string]$Title, [string]$Inner, [string]$LogFile, [string]$WorkDir, [hashtable]$ExtraEnv = $null) {
  $procs = Get-CimInstance Win32_Process -Filter "Name='powershell.exe'" -ErrorAction SilentlyContinue |
    Where-Object { $_.CommandLine -like ('*' + $Title + '*') }
  $first = $procs | Select-Object -First 1
  if ($first) {
    Write-Host "Already running: $Title (pid $($first.ProcessId)) - reusing it."
    return
  }
  if ([string]::IsNullOrWhiteSpace($WorkDir)) { $WorkDir = $RootDir }
  # The child starts in System32 by default, so pin its working directory and
  # use Start-Transcript (absolute path) for the log file. Stop any leftover
  # transcript first so a stale lock never blocks logging.
  $envStamp = '$env:ASPNETCORE_ENVIRONMENT=''Development''; '
  if ($ExtraEnv) {
    foreach ($kv in $ExtraEnv.GetEnumerator()) {
      # Values are secrets: single-quote them so nothing inside expands.
      $envStamp += ('$env:' + $kv.Key + '=''' + ($kv.Value -replace "'", "''") + '''; ')
    }
  }
  $wrapped = '$Host.UI.RawUI.WindowTitle = ''' + $Title + '''; [Console]::OutputEncoding = [Text.Encoding]::UTF8; try { Stop-Transcript | Out-Null } catch {}; Start-Transcript -Path ''' + $LogFile + ''' -Force | Out-Null; ' + $envStamp + $Inner
  $psi = New-Object Diagnostics.ProcessStartInfo
  $psi.FileName = 'powershell'
  $psi.Arguments = '-NoExit -Command "' + $wrapped.Replace('"', '""') + '"'
  $psi.WorkingDirectory = $WorkDir
  $psi.UseShellExecute = $true
  [Diagnostics.Process]::Start($psi) | Out-Null
  Write-Host "Started: $Title (log: $LogFile)"
}

if (-not $NoBackend) {
  # ASPNETCORE_ENVIRONMENT=Development is stamped into the child window (only
  # Development loads user-secrets), and the values are passed explicitly as
  # __-style env vars as well: relying on dotnet run's implicit user-secrets
  # resolution inside the spawned window proved unreliable, and these four are
  # exactly what the API refuses to start without.
  $backendEnv = @{
    'ConnectionStrings__Default'  = (Get-UserSecret 'ConnectionStrings:Default')
    'Jwt__Key'                    = (Get-UserSecret 'Jwt:Key')
    'Encryption__NationalIdKey'   = (Get-UserSecret 'Encryption:NationalIdKey')
    'Platform__BootstrapSecret'   = (Get-UserSecret 'Platform:BootstrapSecret')
  }
  $backendInner = 'dotnet run --project src/Aqsat.Api --urls ' + $BackendUrl + ' --no-launch-profile'
  Start-PersistentWindow 'Aqsat-Backend' $backendInner $BackendLog $RootDir $backendEnv
  Wait-ForPort 'Backend' 'localhost' $BackendPort $WaitSeconds
  Write-Host "  health: ${BackendUrl}/health | swagger: ${BackendUrl}/swagger"
}

if (-not $NoFrontend) {
  $webDir = Join-Path $RootDir 'src/aqsat-web'
  if (-not (Test-Path (Join-Path $webDir 'node_modules'))) {
    Write-Host 'node_modules missing - running npm install first (one time)...'
    Push-Location $webDir
    try { npm install } finally { Pop-Location }
  }
  $frontendInner = 'npm run dev -- --port ' + $FrontendPort + ' --host 127.0.0.1'
  Start-PersistentWindow 'Aqsat-Frontend' $frontendInner $FrontendLog $webDir
  Wait-ForPort 'Frontend' '127.0.0.1' $FrontendPort $WaitSeconds
}

Write-Host ''
Write-Host 'Done. Open the app at:'
Write-Host "  $FrontendUrl"
if ($OpenBrowser) { Start-Process $FrontendUrl }
