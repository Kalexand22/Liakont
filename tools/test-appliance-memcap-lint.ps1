# Self-test du lint de plafond mémoire Keycloak appliance (RDF04).
#
# Prouve que tools/lint-appliance-keycloak-memcap.ps1 N'EST PAS pass-by-default : il PASSE sur le
# compose réel (plafond présent) et ÉCHOUE dès que le plafond disparaît (mem_limit retiré,
# MaxRAMPercentage retiré, ou les deux). Sans ce self-test, un lint qui renvoie toujours 0 serait un
# faux-vert. Logique pure (pas de dotnet, pas de Docker) → garde permanente en CI.
#
# Exit 0 = lint conforme. Exit 1 = le lint ne discrimine pas correctement (bug du lint).

$ErrorActionPreference = 'Stop'

$lint    = Join-Path $PSScriptRoot 'lint-appliance-keycloak-memcap.ps1'
$compose = Join-Path $PSScriptRoot '..\deploy\docker\appliance\docker-compose.yml'
$psExe   = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))    { Write-Host "[TEST-MEMCAP] lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $compose)) { Write-Host "[TEST-MEMCAP] compose introuvable : $compose" -ForegroundColor Red; exit 1 }

# Invoque le lint dans un process enfant (le `exit` du lint ne doit pas tuer ce self-test) et
# renvoie le code de sortie.
function Invoke-Lint([string]$path) {
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $lint -ComposePath $path *> $null
    return $LASTEXITCODE
}

$tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) ("memcap-lint-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null

$failures = @()
function Check([string]$label, [int]$got, [int]$want) {
    if ($got -ne $want) {
        $script:failures += "$label : exit $got attendu $want"
        Write-Host "[TEST-MEMCAP] FAIL  $label (exit $got, attendu $want)" -ForegroundColor Red
    } else {
        Write-Host "[TEST-MEMCAP] ok    $label (exit $got)" -ForegroundColor Green
    }
}

try {
    $orig = Get-Content -LiteralPath $compose

    # 1) Compose réel : plafond présent → le lint DOIT passer (exit 0).
    Check 'compose reel (plafond present)' (Invoke-Lint $compose) 0

    # 2) mem_limit retire → le lint DOIT echouer (exit 1).
    $noMemLimit = $orig | Where-Object { $_ -notmatch '^\s*mem_limit:\s' }
    $p2 = Join-Path $tmpDir 'no-mem-limit.yml'
    Set-Content -LiteralPath $p2 -Value $noMemLimit -Encoding utf8
    Check 'mem_limit retire' (Invoke-Lint $p2) 1

    # 3) MaxRAMPercentage retire → le lint DOIT echouer (exit 1).
    $noMaxRam = $orig | ForEach-Object { $_ -replace '\s*-XX:MaxRAMPercentage=\d+', '' }
    $p3 = Join-Path $tmpDir 'no-maxram.yml'
    Set-Content -LiteralPath $p3 -Value $noMaxRam -Encoding utf8
    Check 'MaxRAMPercentage retire' (Invoke-Lint $p3) 1

    # 4) Les deux gardes retirees → le lint DOIT echouer (exit 1).
    $noBoth = $noMemLimit | ForEach-Object { $_ -replace '\s*-XX:MaxRAMPercentage=\d+', '' }
    $p4 = Join-Path $tmpDir 'no-both.yml'
    Set-Content -LiteralPath $p4 -Value $noBoth -Encoding utf8
    Check 'mem_limit + MaxRAMPercentage retires' (Invoke-Lint $p4) 1
}
finally {
    Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "[TEST-MEMCAP] ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "[TEST-MEMCAP] OK : le lint discrimine correctement (plafond present => 0, retire => 1)." -ForegroundColor Green
exit 0
