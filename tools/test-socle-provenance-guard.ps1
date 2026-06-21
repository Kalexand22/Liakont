#!/usr/bin/env pwsh
# Self-test for tools/socle-provenance-check.ps1 (RDL09). Builds a throwaway git repo with a fake
# vendored tree (one true Stratum file, one marked Liakont addition), copies the real guard into it,
# and asserts the guard's behaviour across the scenarios that matter for provenance integrity:
#   1. -Generate excludes marker files (baseline pins only the Stratum file)
#   2. clean tree -> exit 0
#   3. editing a Liakont addition -> exit 0 (no false drift)            [false-positive closed]
#   4. editing the true Stratum file, NOT consigned -> exit 2           [false-negative preserved]
#   5. same edit, consigned in the doc block -> exit 0
#   6. a PINNED Stratum file that gains a marker -> exit 2 (tamper, NOT consignable)   [P2 hardening]
# Exit 0 = all scenarios passed; non-zero = a regression in the guard.
$ErrorActionPreference = 'Stop'
$guard = Join-Path $PSScriptRoot 'socle-provenance-check.ps1'
$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ("socle-prov-selftest-" + [System.Guid]::NewGuid().ToString('N'))

function Fail($msg) { Write-Host "SELF-TEST FAIL: $msg" -ForegroundColor Red; exit 1 }
function Invoke-Guard([string]$g, [switch]$Gen) {
    if ($Gen) { & powershell -NoProfile -ExecutionPolicy Bypass -File $g -Generate | Out-Null }
    else { & powershell -NoProfile -ExecutionPolicy Bypass -File $g | Out-Null }
    return $LASTEXITCODE
}

try {
    New-Item -ItemType Directory -Path (Join-Path $tmp 'tools') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tmp 'docs/architecture') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $tmp 'src/Common/Abstractions') -Force | Out-Null
    Copy-Item $guard (Join-Path $tmp 'tools/socle-provenance-check.ps1')

    $stratum = Join-Path $tmp 'src/Common/Abstractions/StratumThing.cs'
    $added   = Join-Path $tmp 'src/Common/Abstractions/AddedThing.cs'
    Set-Content -LiteralPath $stratum -Value "namespace Stratum.Common.Abstractions;`npublic class StratumThing {}" -NoNewline
    Set-Content -LiteralPath $added -Value "// Liakont addition (self-test): not part of the original Stratum vendoring.`nnamespace Stratum.Common.Abstractions;" -NoNewline

    $prov = Join-Path $tmp 'docs/architecture/provenance-socle-stratum.md'
    $blockEmpty = "# provenance test`n<!-- SOCLE-CONSIGNED-DRIFT:START -->`n<!-- SOCLE-CONSIGNED-DRIFT:END -->`n"
    Set-Content -LiteralPath $prov -Value $blockEmpty

    $g = Join-Path $tmp 'tools/socle-provenance-check.ps1'
    Push-Location $tmp
    try {
        # git writes autocrlf warnings to stderr; under Stop mode that would throw. Use Continue
        # locally for all native git calls whose stderr is intentionally discarded.
        $eaPrev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git init -q 2>$null; & git add -A 2>$null
        $ErrorActionPreference = $eaPrev

        if ((Invoke-Guard $g -Gen) -ne 0) { Fail "generate exit non-zero" }
        $base = Get-Content (Join-Path $tmp 'tools/socle-baseline.sha1') -Raw
        if ($base -match 'AddedThing\.cs') { Fail "baseline pins a Liakont addition (AddedThing.cs)" }
        if ($base -notmatch 'StratumThing\.cs') { Fail "baseline missing the Stratum file" }

        if ((Invoke-Guard $g) -ne 0) { Fail "clean tree not exit 0" }

        Add-Content -LiteralPath $added -Value "// touched"
        $eaPrev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git add -A 2>$null
        $ErrorActionPreference = $eaPrev
        if ((Invoke-Guard $g) -ne 0) { Fail "editing a Liakont addition flagged drift (expected 0)" }

        Add-Content -LiteralPath $stratum -Value "// drift"
        $eaPrev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git add -A 2>$null
        $ErrorActionPreference = $eaPrev
        if ((Invoke-Guard $g) -ne 2) { Fail "unconsigned Stratum drift not caught (expected 2)" }

        Set-Content -LiteralPath $prov -Value "# provenance test`n<!-- SOCLE-CONSIGNED-DRIFT:START -->`nsrc/Common/Abstractions/StratumThing.cs`n<!-- SOCLE-CONSIGNED-DRIFT:END -->`n"
        if ((Invoke-Guard $g) -ne 0) { Fail "consigned Stratum drift not accepted (expected 0)" }

        Set-Content -LiteralPath $stratum -Value "// Liakont addition (tamper): pretend this Stratum file is an addition`nnamespace Stratum.Common.Abstractions;" -NoNewline
        $eaPrev = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & git add -A 2>$null
        $ErrorActionPreference = $eaPrev
        if ((Invoke-Guard $g) -ne 2) { Fail "pinned file gaining a marker not caught as tamper (expected 2)" }
    }
    finally { Pop-Location }

    Write-Host "socle-provenance self-test: 6/6 scenarios OK" -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path $tmp) { try { [System.IO.Directory]::Delete($tmp, $true) } catch {} }
}
