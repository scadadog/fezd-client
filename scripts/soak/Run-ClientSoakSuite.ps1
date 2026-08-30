<#
.SYNOPSIS
  Overnight single-client FEZD soak suite driven by fezd-client against a live gateway.

.DESCRIPTION
  Simulates one client hitting the gateway with successive deploy --simulator jobs:
    1) Steady Monte Carlo (random inter-job gaps, small/large project rotation)
    2) Interrupt + recover (cancel mid-session, then full prove deploy)
    3) Cancel storm (N mid-cancels, then M full deploys)

  Writes suite.json, trials.csv, trials.jsonl, summary.txt under an out-dir stamp.

  Prerequisite: fezd-server serve is running on the gateway. Projects must be
  reachable from the machine running this script (uploaded each trial).

.EXAMPLE
  # From dist/performance-testing — auto-finds ../fezd-client + ../nextera-remote-client.fezd.env
  .\Run-ClientSoakSuite.ps1 -AppPassword '***' -Family M340 -Phases Steady

.EXAMPLE
  .\Run-ClientSoakSuite.ps1 -Family All -Phases All -AppPassword '***'

.EXAMPLE
  .\Run-ClientSoakSuite.ps1 -WhatIf
#>
[CmdletBinding()]
param(
    [string] $Connection = "",

    [string] $ProjectsFile = "",

    [string] $ClientPath = "",

    [string] $OutRoot = "",

    [string] $AppPassword = "",

    [ValidateSet("All", "M340", "M580")]
    [string] $Family = "All",

    [ValidateSet("All", "Steady", "Interrupt", "Storm")]
    [string] $Phases = "All",

    [int] $Seed = 42,

    [double] $DelayMinSec = 1,

    [double] $DelayMaxSec = 120,

    [int] $SteadyTrials = 60,

    [int] $SteadyDurationSec = 28800,

    [int] $InterruptCycles = 20,

    [int] $StormCancels = 10,

    [int] $StormProve = 5,

    [int] $MaxFailures = 5,

    [switch] $SkipHealth,

    [switch] $Strict,

    [switch] $WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Resolve paths / client binary
# ---------------------------------------------------------------------------

$ScriptDir = $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ScriptDir)) {
    $ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
}

function Resolve-FezdClient {
    param([string] $Hint)
    if ($Hint -and (Test-Path -LiteralPath $Hint)) {
        return (Resolve-Path -LiteralPath $Hint).Path
    }
    $parent = Split-Path $ScriptDir -Parent
    $candidates = @(
        (Join-Path $ScriptDir "fezd-client.exe"),
        (Join-Path $ScriptDir "fezd-client"),
        (Join-Path $parent "fezd-client.exe"),
        (Join-Path $parent "fezd-client"),
        (Join-Path $parent "dist\fezd-client.exe"),
        (Join-Path $parent "dist\fezd-client"),
        (Join-Path $parent "..\fezd-client.exe"),
        (Join-Path $parent "..\fezd-client")
    )
    foreach ($c in $candidates) {
        $full = [System.IO.Path]::GetFullPath($c)
        if (Test-Path -LiteralPath $full) { return $full }
    }
    $cmd = Get-Command fezd-client -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $cmd = Get-Command fezd-client.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    throw "fezd-client binary not found. Pass -ClientPath or place it in dist/ (parent of this script)."
}

function Resolve-ConnectionFile {
    param([string] $Hint)
    if ($Hint -and (Test-Path -LiteralPath $Hint)) {
        return (Resolve-Path -LiteralPath $Hint).Path
    }
    $parent = Split-Path $ScriptDir -Parent
    $candidates = @(
        $Hint,
        (Join-Path $ScriptDir "nextera-remote-client.fezd.env"),
        (Join-Path $ScriptDir "*.fezd.env"),
        (Join-Path $parent "nextera-remote-client.fezd.env"),
        (Join-Path $parent "dist\nextera-remote-client.fezd.env")
    )
    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        if ($c -like "*`**") {
            $hits = @(Get-Item -Path $c -ErrorAction SilentlyContinue)
            if ($hits.Count -ge 1) { return $hits[0].FullName }
            continue
        }
        $full = [System.IO.Path]::GetFullPath($c)
        if (Test-Path -LiteralPath $full) { return $full }
    }
    throw "Connection file not found. Pass -Connection or place a *.fezd.env in dist/."
}

function Resolve-ProjectPath {
    param([string] $RawPath, [string] $BaseDir)
    if ([string]::IsNullOrWhiteSpace($RawPath)) { return $null }
    if ([System.IO.Path]::IsPathRooted($RawPath) -and (Test-Path -LiteralPath $RawPath)) {
        return (Resolve-Path -LiteralPath $RawPath).Path
    }
    $fromBase = [System.IO.Path]::GetFullPath((Join-Path $BaseDir $RawPath))
    if (Test-Path -LiteralPath $fromBase) { return $fromBase }
    $fromScript = [System.IO.Path]::GetFullPath((Join-Path $ScriptDir $RawPath))
    if (Test-Path -LiteralPath $fromScript) { return $fromScript }
    $fromParent = [System.IO.Path]::GetFullPath((Join-Path (Split-Path $ScriptDir -Parent) $RawPath))
    if (Test-Path -LiteralPath $fromParent) { return $fromParent }
    return $fromBase
}

function Get-UtcStamp {
    return (Get-Date).ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'")
}

function Escape-Csv([string] $value) {
    if ($null -eq $value) { return "" }
    if ($value -match '[,"\r\n]') {
        return '"' + ($value -replace '"', '""') + '"'
    }
    return $value
}

function Escape-JsonString([string] $value) {
    if ($null -eq $value) { return "" }
    return ($value -replace '\\', '\\' -replace '"', '\"' -replace "`n", '\n' -replace "`r", '\r')
}

function Write-JsonLine {
    param([hashtable] $Obj, [string] $Path)
    $parts = New-Object System.Collections.Generic.List[string]
    foreach ($key in $Obj.Keys) {
        $v = $Obj[$key]
        if ($null -eq $v) {
            $parts.Add("`"$key`":null")
        }
        elseif ($v -is [bool]) {
            $parts.Add("`"$key`":$($v.ToString().ToLowerInvariant())")
        }
        elseif ($v -is [int] -or $v -is [long] -or $v -is [double] -or $v -is [decimal]) {
            $parts.Add("`"$key`":$([string]::Format([Globalization.CultureInfo]::InvariantCulture, '{0}', $v))")
        }
        else {
            $parts.Add("`"$key`":`"$(Escape-JsonString ([string]$v))`"")
        }
    }
    Add-Content -LiteralPath $Path -Value ("{" + ($parts -join ",") + "}") -Encoding UTF8
}

function New-GapSec {
    param([Random] $Rng, [double] $Min, [double] $Max, [int] $Trial)
    # Stratified thirds (matches fezd-server soak sample=stratified spirit).
    $span = [Math]::Max(0.0, $Max - $Min)
    $bucket = ($Trial - 1) % 3
    $lo = $Min + ($span * $bucket / 3.0)
    $hi = $Min + ($span * ($bucket + 1) / 3.0)
    if ($hi -le $lo) { return [Math]::Round($Min, 3) }
    $gap = $lo + ($Rng.NextDouble() * ($hi - $lo))
    $stratum = @("early", "mid", "late")[$bucket]
    return @{ GapSec = [Math]::Round($gap, 3); Stratum = $stratum }
}

function Get-Percentile {
    param([long[]] $Sorted, [double] $P)
    if (-not $Sorted -or $Sorted.Count -eq 0) { return 0 }
    $i = [int][Math]::Round(($Sorted.Count - 1) * $P)
    if ($i -lt 0) { $i = 0 }
    if ($i -ge $Sorted.Count) { $i = $Sorted.Count - 1 }
    return $Sorted[$i]
}

function Invoke-Fezd {
    param(
        [string] $Client,
        [string[]] $Arguments,
        [int] $TimeoutSec = 0
    )
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Client
    $psi.Arguments = ($Arguments | ForEach-Object {
        if ($_ -match '\s') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ }
    }) -join " "
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true

    $proc = New-Object System.Diagnostics.Process
    $proc.StartInfo = $psi
    $stdout = New-Object System.Text.StringBuilder
    $stderr = New-Object System.Text.StringBuilder
    $outHandler = {
        if (-not [string]::IsNullOrEmpty($EventArgs.Data)) {
            [void]$Event.MessageData.AppendLine($EventArgs.Data)
        }
    }
    $outEvent = Register-ObjectEvent -InputObject $proc -EventName OutputDataReceived -Action $outHandler -MessageData $stdout
    $errEvent = Register-ObjectEvent -InputObject $proc -EventName ErrorDataReceived -Action $outHandler -MessageData $stderr

    [void]$proc.Start()
    $proc.BeginOutputReadLine()
    $proc.BeginErrorReadLine()

    if ($TimeoutSec -gt 0) {
        if (-not $proc.WaitForExit($TimeoutSec * 1000)) {
            try { $proc.Kill() } catch { }
            Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
            Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue
            throw "fezd-client timed out after ${TimeoutSec}s: $($psi.Arguments)"
        }
    }
    else {
        $proc.WaitForExit()
    }

    # Drain async handlers
    Start-Sleep -Milliseconds 200
    Unregister-Event -SourceIdentifier $outEvent.Name -ErrorAction SilentlyContinue
    Unregister-Event -SourceIdentifier $errEvent.Name -ErrorAction SilentlyContinue

    return [pscustomobject]@{
        ExitCode = $proc.ExitCode
        StdOut   = $stdout.ToString()
        StdErr   = $stderr.ToString()
        Combined = ($stdout.ToString() + "`n" + $stderr.ToString())
    }
}

function Get-SessionIdFromLog([string] $Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    $m = [regex]::Match($Text, 'Session\s+([A-Za-z0-9\-]+)\s+accepted')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Get-ServerVersionFromHealth([string] $Text) {
    $m = [regex]::Match($Text, 'server version\s*:\s*(\S+)')
    if ($m.Success) { return $m.Groups[1].Value }
    return $null
}

function Get-ClientVersion([string] $Client) {
    try {
        $r = & $Client --version 2>&1 | Out-String
        $m = [regex]::Match($r, 'FEZD\s+(\S+)')
        if ($m.Success) { return $m.Groups[1].Value }
        return ($r.Trim() -split "`n")[0]
    }
    catch {
        return "unknown"
    }
}

# ---------------------------------------------------------------------------
# Bootstrap
# ---------------------------------------------------------------------------

$Client = Resolve-FezdClient -Hint $ClientPath
$Connection = Resolve-ConnectionFile -Hint $Connection

if ([string]::IsNullOrWhiteSpace($ProjectsFile)) {
    $ProjectsFile = Join-Path $ScriptDir "projects.manifest.json"
}
if (-not (Test-Path -LiteralPath $ProjectsFile)) {
    $example = Join-Path $ScriptDir "projects.manifest.json.example"
    throw "Projects file not found: $ProjectsFile`nCopy $example to projects.manifest.json and set real .zef paths."
}
$ProjectsFile = (Resolve-Path -LiteralPath $ProjectsFile).Path
$ProjectsBaseDir = Split-Path -Parent $ProjectsFile

if ([string]::IsNullOrWhiteSpace($OutRoot)) {
    $OutRoot = Join-Path $ScriptDir "suite-runs"
}
if (-not (Test-Path -LiteralPath $OutRoot)) {
    New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
}
$OutRoot = (Resolve-Path -LiteralPath $OutRoot).Path

if ([string]::IsNullOrWhiteSpace($AppPassword)) {
    $AppPassword = $env:FEZD_APP_PASSWORD
}

$stamp = Get-UtcStamp
$SuiteDir = Join-Path $OutRoot "suite-$stamp"
New-Item -ItemType Directory -Force -Path $SuiteDir | Out-Null

$allProjects = Get-Content -LiteralPath $ProjectsFile -Raw -Encoding UTF8 | ConvertFrom-Json
if (-not $allProjects) { throw "Empty projects manifest: $ProjectsFile" }

$projects = @()
foreach ($p in $allProjects) {
    if ($Family -ne "All" -and $p.family -ne $Family) { continue }
    if (-not $p.path) { throw "Project entry missing path: $($p | ConvertTo-Json -Compress)" }
    $resolved = Resolve-ProjectPath -RawPath ([string]$p.path) -BaseDir $ProjectsBaseDir
    if (-not $resolved -or -not (Test-Path -LiteralPath $resolved)) {
        throw "Project file not found: $($p.path) (label=$($p.label)) resolved=$resolved"
    }
    $fi = Get-Item -LiteralPath $resolved
    $projects += [pscustomobject]@{
        Label             = [string]$p.label
        Family            = [string]$p.family
        SizeClass         = [string]$p.sizeClass
        Path              = $fi.FullName
        Bytes             = [long]$fi.Length
        SizeMb            = [Math]::Round($fi.Length / 1MB, 4)
        ExpectedDeploySec = if ($p.expectedDeploySec) { [int]$p.expectedDeploySec } else { 120 }
        CancelAtSec       = if ($p.cancelAtSec) { [int]$p.cancelAtSec } else { [int]([Math]::Max(15, ($p.expectedDeploySec * 0.4))) }
    }
}
if ($projects.Count -eq 0) {
    throw "No projects matched Family=$Family in $ProjectsFile"
}

$clientVersion = Get-ClientVersion -Client $Client
$serverVersion = $null

Write-Host "FEZD client soak suite"
Write-Host "  client      : $Client ($clientVersion)"
Write-Host "  connection  : $Connection"
Write-Host "  out-dir     : $SuiteDir"
Write-Host "  family      : $Family"
Write-Host "  phases      : $Phases"
Write-Host "  projects    : $($projects.Count)"
foreach ($p in $projects) {
    Write-Host ("    - {0} [{1}/{2}] {3:N3} MB  cancelAt={4}s  {5}" -f `
        $p.Label, $p.Family, $p.SizeClass, $p.SizeMb, $p.CancelAtSec, $p.Path)
}

if ($WhatIf) {
    Write-Host "WhatIf: exiting before health / phases."
    return
}

if (-not $SkipHealth) {
    Write-Host "Running health..."
    $health = Invoke-Fezd -Client $Client -Arguments @("health", "--connection", $Connection) -TimeoutSec 60
    Write-Host $health.Combined
    if ($health.ExitCode -ne 0) {
        throw "Gateway health failed (exit $($health.ExitCode)). Start fezd-server serve on the gateway."
    }
    $serverVersion = Get-ServerVersionFromHealth $health.Combined
}

$suiteMeta = [ordered]@{
    stamp           = $stamp
    startedUtc      = (Get-Date).ToUniversalTime().ToString("o")
    clientPath      = $Client
    clientVersion   = $clientVersion
    serverVersion   = $serverVersion
    connection      = $Connection
    family          = $Family
    phases          = $Phases
    seed            = $Seed
    delayMinSec     = $DelayMinSec
    delayMaxSec     = $DelayMaxSec
    steadyTrials    = $SteadyTrials
    steadyDurationSec = $SteadyDurationSec
    interruptCycles = $InterruptCycles
    stormCancels    = $StormCancels
    stormProve      = $StormProve
    maxFailures     = $MaxFailures
    projects        = @($projects | ForEach-Object {
        [ordered]@{
            label = $_.Label; family = $_.Family; sizeClass = $_.SizeClass
            path = $_.Path; bytes = $_.Bytes; sizeMb = $_.SizeMb
            expectedDeploySec = $_.ExpectedDeploySec; cancelAtSec = $_.CancelAtSec
        }
    })
    phaseResults    = @()
}
$suiteMeta | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $SuiteDir "suite.json") -Encoding UTF8

$rng = New-Object Random $Seed
$globalTrial = 0
$csvHeader = "trial,utc,phase,trialKind,family,sizeClass,seed,stratum,gapSec,projectPath,projectLabel,projectBytes,projectSizeMb,sessionId,cancelAtSec,interrupted,automationMs,pass,exitCode,detail,clientVersion,serverVersion"

function Ensure-PhaseDir([string] $Name) {
    $dir = Join-Path $SuiteDir $Name
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $csv = Join-Path $dir "trials.csv"
    $jsonl = Join-Path $dir "trials.jsonl"
    Set-Content -LiteralPath $csv -Value $csvHeader -Encoding UTF8
    if (Test-Path -LiteralPath $jsonl) { Remove-Item -LiteralPath $jsonl -Force }
    return [pscustomobject]@{ Dir = $dir; Csv = $csv; Jsonl = $jsonl; Trials = New-Object System.Collections.Generic.List[object] }
}

function Append-Trial {
    param($PhaseState, [hashtable] $Rec)
    $line = @(
        $Rec.trial
        Escape-Csv $Rec.utc
        Escape-Csv $Rec.phase
        Escape-Csv $Rec.trialKind
        Escape-Csv $Rec.family
        Escape-Csv $Rec.sizeClass
        $Rec.seed
        Escape-Csv $Rec.stratum
        $Rec.gapSec
        Escape-Csv $Rec.projectPath
        Escape-Csv $Rec.projectLabel
        $Rec.projectBytes
        $Rec.projectSizeMb
        Escape-Csv $Rec.sessionId
        $Rec.cancelAtSec
        ($(if ($Rec.interrupted) { "true" } else { "false" }))
        $Rec.automationMs
        ($(if ($Rec.pass) { "true" } else { "false" }))
        $Rec.exitCode
        Escape-Csv $Rec.detail
        Escape-Csv $Rec.clientVersion
        Escape-Csv $Rec.serverVersion
    ) -join ","
    Add-Content -LiteralPath $PhaseState.Csv -Value $line -Encoding UTF8
    Write-JsonLine -Obj $Rec -Path $PhaseState.Jsonl
    [void]$PhaseState.Trials.Add([pscustomobject]$Rec)
    $status = if ($Rec.pass) { "PASS" } else { "FAIL" }
    Write-Host ("  [{0}] trial={1} kind={2} label={3} gap={4}s auto={5}ms {6} {7}" -f `
        $Rec.phase, $Rec.trial, $Rec.trialKind, $Rec.projectLabel, $Rec.gapSec, $Rec.automationMs, $status, $Rec.detail)
}

function Write-PhaseSummary {
    param($PhaseState, [string] $PhaseName)
    $trials = @($PhaseState.Trials)
    $pass = @($trials | Where-Object { $_.pass }).Count
    $sb = New-Object System.Text.StringBuilder
    [void]$sb.AppendLine("FEZD client soak phase summary")
    [void]$sb.AppendLine("phase: $PhaseName")
    [void]$sb.AppendLine("out-dir: $($PhaseState.Dir)")
    [void]$sb.AppendLine("clientVersion: $clientVersion")
    [void]$sb.AppendLine("serverVersion: $serverVersion")
    [void]$sb.AppendLine("seed: $Seed")
    [void]$sb.AppendLine(("trials: {0}" -f $trials.Count))
    [void]$sb.AppendLine(("pass: {0}/{1} ({2:0.#}%)" -f $pass, $trials.Count, $(if ($trials.Count -eq 0) { 0 } else { 100.0 * $pass / $trials.Count })))
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("By trialKind:")
    foreach ($g in ($trials | Group-Object trialKind | Sort-Object Name)) {
        $rows = @($g.Group)
        $gp = @($rows | Where-Object { $_.pass }).Count
        $ms = @($rows | ForEach-Object { [long]$_.automationMs } | Sort-Object)
        [void]$sb.AppendLine(("  {0}: n={1} pass={2}/{1} ({3:0.#}%) automationMs p50={4} p95={5}" -f `
            $g.Name, $rows.Count, $gp, $(if ($rows.Count -eq 0) { 0 } else { 100.0 * $gp / $rows.Count }),
            (Get-Percentile $ms 0.50), (Get-Percentile $ms 0.95)))
    }
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("By project label:")
    foreach ($g in ($trials | Group-Object projectLabel | Sort-Object Name)) {
        $rows = @($g.Group)
        $gp = @($rows | Where-Object { $_.pass }).Count
        $ms = @($rows | ForEach-Object { [long]$_.automationMs } | Sort-Object)
        [void]$sb.AppendLine(("  {0}: n={1} pass={2}/{1} ({3:0.#}%) automationMs p50={4} p95={5}" -f `
            $g.Name, $rows.Count, $gp, $(if ($rows.Count -eq 0) { 0 } else { 100.0 * $gp / $rows.Count }),
            (Get-Percentile $ms 0.50), (Get-Percentile $ms 0.95)))
    }
    $summaryPath = Join-Path $PhaseState.Dir "summary.txt"
    Set-Content -LiteralPath $summaryPath -Value $sb.ToString() -Encoding UTF8
    return [pscustomobject]@{
        Phase = $PhaseName
        Trials = $trials.Count
        Pass = $pass
        PassPct = $(if ($trials.Count -eq 0) { 0 } else { [Math]::Round(100.0 * $pass / $trials.Count, 1) })
        SummaryPath = $summaryPath
    }
}

function New-BaseRecord {
    param($Project, [string] $Phase, [string] $TrialKind, [double] $GapSec, [string] $Stratum)
    $script:globalTrial++
    return @{
        trial          = $script:globalTrial
        utc            = (Get-Date).ToUniversalTime().ToString("o")
        phase          = $Phase
        trialKind      = $TrialKind
        family         = $Project.Family
        sizeClass      = $Project.SizeClass
        seed           = $Seed
        stratum        = $Stratum
        gapSec         = $GapSec
        projectPath    = $Project.Path
        projectLabel   = $Project.Label
        projectBytes   = $Project.Bytes
        projectSizeMb  = $Project.SizeMb
        sessionId      = ""
        cancelAtSec    = 0
        interrupted    = $false
        automationMs   = 0
        pass           = $false
        exitCode       = -1
        detail         = ""
        clientVersion  = $clientVersion
        serverVersion  = $serverVersion
    }
}

function Build-DeployArgs([string] $ZefPath) {
    $args = @(
        "deploy", $ZefPath,
        "--connection", $Connection,
        "--simulator", "--run", "--force"
    )
    if (-not [string]::IsNullOrWhiteSpace($AppPassword)) {
        $args += @("--app-password", $AppPassword)
    }
    return $args
}

function Invoke-SteadyDeploy {
    param($Project, [string] $Phase, [double] $GapSec, [string] $Stratum, $PhaseState)
    $rec = New-BaseRecord -Project $Project -Phase $Phase -TrialKind "steady" -GapSec $GapSec -Stratum $Stratum
    $timeout = [Math]::Max(300, $Project.ExpectedDeploySec * 4)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $r = Invoke-Fezd -Client $Client -Arguments (Build-DeployArgs $Project.Path) -TimeoutSec $timeout
        $sw.Stop()
        $rec.automationMs = $sw.ElapsedMilliseconds
        $rec.sessionId = Get-SessionIdFromLog $r.Combined
        $rec.exitCode = $r.ExitCode
        $rec.pass = ($r.ExitCode -eq 0)
        $rec.detail = if ($rec.pass) { "deploy-sim OK" } else {
            $tail = ($r.Combined -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3) -join " | "
            if ([string]::IsNullOrWhiteSpace($tail)) { "deploy failed exit=$($r.ExitCode)" } else { $tail.Trim() }
        }
    }
    catch {
        $sw.Stop()
        $rec.automationMs = $sw.ElapsedMilliseconds
        $rec.pass = $false
        $rec.exitCode = -1
        $rec.detail = $_.Exception.Message
    }
    Append-Trial -PhaseState $PhaseState -Rec $rec
    return $rec
}

function Read-LogTail([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return "" }
    try {
        return Get-Content -LiteralPath $Path -Raw -ErrorAction SilentlyContinue
    }
    catch {
        return ""
    }
}

function Invoke-InterruptCycle {
    param(
        $Project,
        [string] $Phase,
        [double] $GapSec,
        [string] $Stratum,
        $PhaseState,
        [switch] $SkipRecover,
        [string] $InterruptKind = "interrupt"
    )
    $cancelAt = [int]$Project.CancelAtSec
    $timeout = [Math]::Max(300, $Project.ExpectedDeploySec * 4)

    # --- interrupt trial ---
    $irec = New-BaseRecord -Project $Project -Phase $Phase -TrialKind $InterruptKind -GapSec $GapSec -Stratum $Stratum
    $irec.cancelAtSec = $cancelAt
    $irec.interrupted = $true

    $logPath = Join-Path $PhaseState.Dir ("interrupt-{0}.log" -f $irec.trial)
    $outPath = Join-Path $PhaseState.Dir ("interrupt-{0}.stdout.log" -f $irec.trial)
    $errPath = Join-Path $PhaseState.Dir ("interrupt-{0}.stderr.log" -f $irec.trial)
    foreach ($f in @($outPath, $errPath, $logPath)) {
        if (Test-Path -LiteralPath $f) { Remove-Item -LiteralPath $f -Force }
    }

    $argList = Build-DeployArgs $Project.Path
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $proc = Start-Process -FilePath $Client -ArgumentList $argList `
        -RedirectStandardOutput $outPath -RedirectStandardError $errPath `
        -PassThru -NoNewWindow -WindowStyle Hidden

    $sessionId = $null
    $deadline = (Get-Date).AddSeconds($timeout)
    $cancelSent = $false
    $cancelAtDeadline = (Get-Date).AddSeconds($cancelAt)
    $lastLogLen = 0

    try {
        while (-not $proc.HasExited) {
            $combined = (Read-LogTail $outPath) + "`n" + (Read-LogTail $errPath)
            if ($combined.Length -gt $lastLogLen) {
                $newChunk = $combined.Substring($lastLogLen)
                foreach ($line in ($newChunk -split "`n")) {
                    if ($line.Trim()) { Write-Host "    $($line.TrimEnd())" }
                }
                $lastLogLen = $combined.Length
            }

            if (-not $sessionId) {
                $sessionId = Get-SessionIdFromLog $combined
                if ($sessionId) { $irec.sessionId = $sessionId }
            }

            if (-not $cancelSent -and $sessionId -and (Get-Date) -ge $cancelAtDeadline) {
                Write-Host "    -> cancel session $sessionId (after ~${cancelAt}s)"
                try {
                    $cr = Invoke-Fezd -Client $Client -Arguments @(
                        "cancel", $sessionId, "--connection", $Connection
                    ) -TimeoutSec 60
                    Add-Content -LiteralPath $logPath -Value $cr.Combined -Encoding UTF8
                    $cancelSent = $true
                }
                catch {
                    Add-Content -LiteralPath $logPath -Value ("cancel error: " + $_.Exception.Message) -Encoding UTF8
                    $cancelSent = $true
                }
            }

            if (-not $cancelSent -and -not $sessionId -and (Get-Date) -ge $cancelAtDeadline.AddSeconds(45)) {
                $irec.detail = "No session id observed before cancel window; waiting for deploy exit"
                break
            }

            if ((Get-Date) -gt $deadline) {
                try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
                break
            }

            Start-Sleep -Milliseconds 400
        }

        if (-not $proc.HasExited) {
            try { $proc.WaitForExit(120000) } catch { }
        }
    }
    finally {
        $sw.Stop()
        $combinedFinal = (Read-LogTail $outPath) + "`n" + (Read-LogTail $errPath)
        Set-Content -LiteralPath $logPath -Value $combinedFinal -Encoding UTF8
        if (-not $proc.HasExited) {
            try { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    }

    if (-not $irec.sessionId) {
        $irec.sessionId = Get-SessionIdFromLog (Read-LogTail $logPath)
    }
    $irec.automationMs = $sw.ElapsedMilliseconds
    try { $irec.exitCode = $proc.ExitCode } catch { $irec.exitCode = -1 }

    # Interrupt "pass" means cancel was requested.
    if ($cancelSent) {
        $irec.pass = $true
        $irec.detail = "cancel requested session=$($irec.sessionId) exit=$($irec.exitCode)"
    }
    elseif ($proc.HasExited -and $irec.exitCode -eq 0) {
        $irec.pass = $false
        $irec.detail = "deploy finished before cancel window (too fast for cancelAtSec=$cancelAt)"
    }
    else {
        $irec.pass = $false
        $irec.detail = "cancel not sent; exit=$($irec.exitCode)"
    }
    Append-Trial -PhaseState $PhaseState -Rec $irec

    if ($SkipRecover) { return @{ Interrupt = $irec; Recover = $null } }

    # Brief settle after cancel before recovery prove
    Start-Sleep -Seconds 5

    $rrec = New-BaseRecord -Project $Project -Phase $Phase -TrialKind "recover" -GapSec 5 -Stratum "recover"
    $rsw = [Diagnostics.Stopwatch]::StartNew()
    try {
        $rr = Invoke-Fezd -Client $Client -Arguments (Build-DeployArgs $Project.Path) -TimeoutSec $timeout
        $rsw.Stop()
        $rrec.automationMs = $rsw.ElapsedMilliseconds
        $rrec.sessionId = Get-SessionIdFromLog $rr.Combined
        $rrec.exitCode = $rr.ExitCode
        $rrec.pass = ($rr.ExitCode -eq 0)
        $rrec.detail = if ($rrec.pass) { "recover deploy OK after cancel" } else {
            $tail = ($rr.Combined -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3) -join " | "
            if ([string]::IsNullOrWhiteSpace($tail)) { "recover failed exit=$($rr.ExitCode)" } else { $tail.Trim() }
        }
    }
    catch {
        $rsw.Stop()
        $rrec.automationMs = $rsw.ElapsedMilliseconds
        $rrec.pass = $false
        $rrec.exitCode = -1
        $rrec.detail = $_.Exception.Message
    }
    Append-Trial -PhaseState $PhaseState -Rec $rrec
    return @{ Interrupt = $irec; Recover = $rrec }
}

function Wait-Gap([double] $GapSec, [string] $Label) {
    if ($GapSec -le 0) { return }
    Write-Host ("  waiting gap={0:0.##}s before {1}..." -f $GapSec, $Label)
    Start-Sleep -Seconds $GapSec
}

function Run-SteadyPhase {
    param([string] $PhaseName, $FamilyProjects)
    Write-Host ""
    Write-Host "=== PHASE $PhaseName (steady Monte Carlo) ==="
    $state = Ensure-PhaseDir $PhaseName
    $campaignSw = [Diagnostics.Stopwatch]::StartNew()
    $consecutiveFails = 0
    $cursor = 0

    for ($t = 1; $t -le $SteadyTrials; $t++) {
        if ($campaignSw.Elapsed.TotalSeconds -ge $SteadyDurationSec) {
            Write-Host "Duration cap reached ($SteadyDurationSec s)."
            break
        }
        $project = $FamilyProjects[$cursor % $FamilyProjects.Count]
        $cursor++
        $gapInfo = New-GapSec -Rng $rng -Min $DelayMinSec -Max $DelayMaxSec -Trial $t
        if ($t -gt 1 -or $gapInfo.GapSec -gt 0) {
            Wait-Gap -GapSec $gapInfo.GapSec -Label "trial $t"
        }
        $rec = Invoke-SteadyDeploy -Project $project -Phase $PhaseName -GapSec $gapInfo.GapSec -Stratum $gapInfo.Stratum -PhaseState $state
        if ($rec.pass) { $consecutiveFails = 0 }
        else {
            $consecutiveFails++
            if ($MaxFailures -gt 0 -and $consecutiveFails -ge $MaxFailures) {
                Write-Host "Stopping after $consecutiveFails consecutive failures (--MaxFailures $MaxFailures)."
                break
            }
        }
    }
    return Write-PhaseSummary -PhaseState $state -PhaseName $PhaseName
}

function Run-InterruptPhase {
    param([string] $PhaseName, $FamilyProjects)
    Write-Host ""
    Write-Host "=== PHASE $PhaseName (interrupt + recover) ==="
    $state = Ensure-PhaseDir $PhaseName
    $cursor = 0
    $consecutiveRecoverFails = 0

    for ($t = 1; $t -le $InterruptCycles; $t++) {
        $project = $FamilyProjects[$cursor % $FamilyProjects.Count]
        $cursor++
        $gapInfo = New-GapSec -Rng $rng -Min ([Math]::Max(5, $DelayMinSec)) -Max ([Math]::Max(30, $DelayMaxSec / 2)) -Trial $t
        Wait-Gap -GapSec $gapInfo.GapSec -Label "interrupt cycle $t"
        $result = Invoke-InterruptCycle -Project $project -Phase $PhaseName -GapSec $gapInfo.GapSec -Stratum $gapInfo.Stratum -PhaseState $state
        if ($result.Recover -and $result.Recover.pass) { $consecutiveRecoverFails = 0 }
        elseif ($result.Recover) {
            $consecutiveRecoverFails++
            if ($MaxFailures -gt 0 -and $consecutiveRecoverFails -ge $MaxFailures) {
                Write-Host "Stopping interrupt phase after $consecutiveRecoverFails consecutive recover failures."
                break
            }
        }
    }
    return Write-PhaseSummary -PhaseState $state -PhaseName $PhaseName
}

function Run-StormPhase {
    param([string] $PhaseName, $FamilyProjects)
    Write-Host ""
    Write-Host "=== PHASE $PhaseName (cancel storm + prove) ==="
    $state = Ensure-PhaseDir $PhaseName
    $project = $FamilyProjects[0]

    for ($t = 1; $t -le $StormCancels; $t++) {
        Wait-Gap -GapSec 5 -Label "storm cancel $t"
        [void](Invoke-InterruptCycle -Project $project -Phase $PhaseName -GapSec 5 -Stratum "storm" -PhaseState $state -SkipRecover -InterruptKind "storm-cancel")
    }

    $proveFails = 0
    for ($t = 1; $t -le $StormProve; $t++) {
        Wait-Gap -GapSec 10 -Label "storm prove $t"
        # Reuse deploy path but record as storm-prove
        $rec = New-BaseRecord -Project $project -Phase $PhaseName -TrialKind "storm-prove" -GapSec 10 -Stratum "prove"
        $timeout = [Math]::Max(300, $project.ExpectedDeploySec * 4)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        try {
            $r = Invoke-Fezd -Client $Client -Arguments (Build-DeployArgs $project.Path) -TimeoutSec $timeout
            $sw.Stop()
            $rec.automationMs = $sw.ElapsedMilliseconds
            $rec.sessionId = Get-SessionIdFromLog $r.Combined
            $rec.exitCode = $r.ExitCode
            $rec.pass = ($r.ExitCode -eq 0)
            $rec.detail = if ($rec.pass) { "storm-prove OK" } else {
                $tail = ($r.Combined -split "`n" | Where-Object { $_.Trim() } | Select-Object -Last 3) -join " | "
                if ([string]::IsNullOrWhiteSpace($tail)) { "storm-prove failed exit=$($r.ExitCode)" } else { $tail.Trim() }
            }
        }
        catch {
            $sw.Stop()
            $rec.automationMs = $sw.ElapsedMilliseconds
            $rec.pass = $false
            $rec.exitCode = -1
            $rec.detail = $_.Exception.Message
        }
        Append-Trial -PhaseState $state -Rec $rec
        if (-not $rec.pass) { $proveFails++ }
    }

    $summary = Write-PhaseSummary -PhaseState $state -PhaseName $PhaseName
    Write-Host "Storm prove failures: $proveFails / $StormProve"
    return $summary
}

# ---------------------------------------------------------------------------
# Run phases
# ---------------------------------------------------------------------------

$phaseResults = @()
$wantSteady = ($Phases -eq "All" -or $Phases -eq "Steady")
$wantInterrupt = ($Phases -eq "All" -or $Phases -eq "Interrupt")
$wantStorm = ($Phases -eq "All" -or $Phases -eq "Storm")

$familiesToRun = @()
if ($Family -eq "All") {
    $familiesToRun = @($projects | Select-Object -ExpandProperty Family -Unique)
}
else {
    $familiesToRun = @($Family)
}

foreach ($fam in $familiesToRun) {
    $famProjects = @($projects | Where-Object { $_.Family -eq $fam })
    if ($famProjects.Count -eq 0) { continue }
    $famKey = $fam.ToLowerInvariant()

    if ($wantSteady) {
        $pr = Run-SteadyPhase -PhaseName "01-steady-$famKey" -FamilyProjects $famProjects
        $phaseResults += $pr
        if ($Strict -and $pr.Pass -lt $pr.Trials) { throw "Strict: steady phase failed for $fam" }
    }
    if ($wantInterrupt) {
        $pr = Run-InterruptPhase -PhaseName "03-interrupt-$famKey" -FamilyProjects $famProjects
        $phaseResults += $pr
        if ($Strict -and $pr.Pass -lt $pr.Trials) { throw "Strict: interrupt phase failed for $fam" }
    }
    if ($wantStorm) {
        $pr = Run-StormPhase -PhaseName "05-cancel-storm-$famKey" -FamilyProjects $famProjects
        $phaseResults += $pr
        if ($Strict -and $pr.Pass -lt $pr.Trials) { throw "Strict: storm phase failed for $fam" }
    }
}

# Suite rollup
$suiteMeta.finishedUtc = (Get-Date).ToUniversalTime().ToString("o")
$suiteMeta.phaseResults = @($phaseResults | ForEach-Object {
    [ordered]@{ phase = $_.Phase; trials = $_.Trials; pass = $_.Pass; passPct = $_.PassPct }
})
$suiteMeta | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $SuiteDir "suite.json") -Encoding UTF8

$totalTrials = ($phaseResults | Measure-Object -Property Trials -Sum).Sum
$totalPass = ($phaseResults | Measure-Object -Property Pass -Sum).Sum
$rollup = New-Object System.Text.StringBuilder
[void]$rollup.AppendLine("FEZD client soak suite summary")
[void]$rollup.AppendLine("out-dir: $SuiteDir")
[void]$rollup.AppendLine("clientVersion: $clientVersion")
[void]$rollup.AppendLine("serverVersion: $serverVersion")
[void]$rollup.AppendLine("seed: $Seed")
[void]$rollup.AppendLine(("total: {0}/{1} pass ({2:0.#}%)" -f $totalPass, $totalTrials, $(if ($totalTrials -eq 0) { 0 } else { 100.0 * $totalPass / $totalTrials })))
[void]$rollup.AppendLine()
foreach ($pr in $phaseResults) {
    [void]$rollup.AppendLine(("  {0}: {1}/{2} ({3}%)" -f $pr.Phase, $pr.Pass, $pr.Trials, $pr.PassPct))
}
Set-Content -LiteralPath (Join-Path $SuiteDir "suite-summary.txt") -Value $rollup.ToString() -Encoding UTF8

Write-Host ""
Write-Host "Suite finished: $SuiteDir"
Write-Host $rollup.ToString()

if ($totalPass -lt $totalTrials) { exit 1 }
exit 0
