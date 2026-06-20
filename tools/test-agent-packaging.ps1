#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test de la logique de packaging/installation de l'agent (OPS05).
.DESCRIPTION
    Vérifie la LOGIQUE PURE du module deploy/agent-installer/AgentInstall.psm1 (dérivation
    d'instance, transport chiffré de la clé de pré-configuration, mot de passe à usage unique) ET
    le contrôle de bitness (PE machine du natif, architecture managée de l'EXE) sur les binaires de
    l'agent déjà construits.

    En PowShell pur (AUCUNE dépendance Pester, dont la syntaxe diffère entre versions) : ce script
    est câblé dans tools/run-tests.ps1 et .github/workflows/ci.yml — une régression du module fait
    donc échouer une gate permanente (pas seulement un nœud d'orchestration ponctuel).

    Exit code 0 = tout vert, 1 = au moins un cas en échec. Les contrôles sur les binaires ÉCHOUENT
    (jamais ne « passent » silencieusement) si l'agent n'a pas été construit — anti-faux-vert.
#>
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
Import-Module (Join-Path $repoRoot 'deploy\agent-installer\AgentInstall.psm1') -Force

$script:passed = 0
$script:failed = 0

function Test-Case {
    param([string]$Name, [scriptblock]$Body)
    try {
        & $Body
        $script:passed++
        Write-Host "  [OK]   $Name" -ForegroundColor Green
    }
    catch {
        $script:failed++
        Write-Host "  [FAIL] $Name : $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-True { param([bool]$Cond, [string]$Msg) if (-not $Cond) { throw $Msg } }
function Assert-Equal { param($Expected, $Actual, [string]$Msg) if ($Expected -ne $Actual) { throw "$Msg (attendu « $Expected », obtenu « $Actual »)" } }
function Assert-Throws { param([scriptblock]$Body, [string]$Msg) $threw = $false; try { & $Body } catch { $threw = $true }; if (-not $threw) { throw "$Msg (aucune exception levée)" } }

Write-Host "=== Self-test packaging agent (OPS05) ===" -ForegroundColor Cyan

# ── Resolve-AgentInstance (miroir strict de AgentInstance.cs) ──
Test-Case 'Resolve Default (chaîne vide)' {
    $r = Resolve-AgentInstance -Name ''
    Assert-True $r.IsDefault 'IsDefault'
    Assert-Equal 'LiakontAgent' $r.ServiceName 'ServiceName'
    Assert-Equal 'Global\LiakontAgentRun' $r.RunMutexName 'RunMutexName'
    Assert-True ($r.DataDirectory -match 'Liakont$') 'DataDirectory'
}
Test-Case 'Resolve default insensible à la casse' { Assert-True (Resolve-AgentInstance -Name 'DEFAULT').IsDefault 'IsDefault' }
Test-Case 'Resolve instance nommée' {
    $r = Resolve-AgentInstance -Name 'ClientA'
    Assert-Equal 'LiakontAgent$ClientA' $r.ServiceName 'ServiceName'
    Assert-Equal 'Global\LiakontAgentRun-CLIENTA' $r.RunMutexName 'mutex en majuscules'
    Assert-True ($r.DataDirectory -match 'Liakont\\ClientA$') 'DataDirectory'
}
Test-Case 'Resolve rejette un nom invalide' { Assert-Throws { Resolve-AgentInstance -Name 'mauvais nom' } 'nom invalide' }
Test-Case 'Resolve rejette un tiret en tête' { Assert-Throws { Resolve-AgentInstance -Name '-client' } 'tiret en tête' }
Test-Case 'Resolve rejette plus de 32 caractères' { Assert-Throws { Resolve-AgentInstance -Name ('a' * 33) } 'trop long' }
Test-Case 'Resolve rejette un nom réservé (logs)' { Assert-Throws { Resolve-AgentInstance -Name 'logs' } 'réservé logs' }
Test-Case 'Resolve rejette un périphérique réservé (CON)' { Assert-Throws { Resolve-AgentInstance -Name 'CON' } 'réservé CON' }

# ── Transport chiffré de la clé de pré-configuration ──
Test-Case 'Pré-config : round-trip avec le bon mot de passe' {
    $s = Protect-AgentPreConfigSecret -PlainText 'CLE-API-XYZ' -Password 'otp' -Iterations 10000
    Assert-Equal 'CLE-API-XYZ' (Unprotect-AgentPreConfigSecret -Secret $s -Password 'otp') 'round-trip'
}
Test-Case 'Pré-config : la clé en clair est absente du chiffré' {
    $s = Protect-AgentPreConfigSecret -PlainText 'CLE-API-XYZ' -Password 'otp' -Iterations 10000
    Assert-True (-not (($s | ConvertTo-Json) -match 'CLE-API-XYZ')) 'clé en clair absente'
}
Test-Case 'Pré-config : rejette un mauvais mot de passe (MAC)' {
    $s = Protect-AgentPreConfigSecret -PlainText 'CLE' -Password 'bon' -Iterations 10000
    Assert-Throws { Unprotect-AgentPreConfigSecret -Secret $s -Password 'mauvais' } 'mauvais mot de passe'
}
Test-Case 'Pré-config : rejette un chiffré altéré' {
    $s = Protect-AgentPreConfigSecret -PlainText 'CLE' -Password 'otp' -Iterations 10000
    $t = $s.PSObject.Copy()
    $b = [System.Convert]::FromBase64String($s.ciphertext); $b[0] = $b[0] -bxor 0xFF
    $t.ciphertext = [System.Convert]::ToBase64String($b)
    Assert-Throws { Unprotect-AgentPreConfigSecret -Secret $t -Password 'otp' } 'chiffré altéré'
}

# ── Plancher de taille de clé de signature d'auto-update (RDF14, RL-UPD-1) ──
function New-RsaPublicKeyXmlFile {
    param([int]$Bits)
    $rsa = New-Object System.Security.Cryptography.RSACryptoServiceProvider($Bits)
    try {
        $xml = $rsa.ToXmlString($false)
    }
    finally {
        $rsa.Dispose()
    }
    $path = [System.IO.Path]::GetTempFileName()
    Set-Content -LiteralPath $path -Value $xml -Encoding UTF8
    return $path
}

Test-Case 'Clé signature : 2048 bits acceptée (plancher inclusif)' {
    $p = New-RsaPublicKeyXmlFile -Bits 2048
    try { Assert-Equal 2048 (Test-UpdateSigningPublicKey -Path $p) 'taille rapportée' }
    finally { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
}
Test-Case 'Clé signature : 1024 bits rejetée (trop courte)' {
    $p = New-RsaPublicKeyXmlFile -Bits 1024
    try { Assert-Throws { Test-UpdateSigningPublicKey -Path $p } 'clé trop courte' }
    finally { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
}
Test-Case 'Clé signature : fichier absent rejeté' {
    Assert-Throws { Test-UpdateSigningPublicKey -Path (Join-Path $env:TEMP 'pubkey-inexistante-RDF14.xml') } 'fichier absent'
}
Test-Case 'Clé signature : XML invalide rejeté' {
    $p = [System.IO.Path]::GetTempFileName()
    Set-Content -LiteralPath $p -Value 'pas une clé RSA' -Encoding UTF8
    try { Assert-Throws { Test-UpdateSigningPublicKey -Path $p } 'XML invalide' }
    finally { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
}

# ── Mot de passe à usage unique ──
Test-Case 'OTP : format attendu' { Assert-True ((New-AgentOneTimePassword) -match '^[2-9A-HJ-NP-Z]{5}(-[2-9A-HJ-NP-Z]{5}){4}$') 'format OTP' }
Test-Case 'OTP : valeurs distinctes' { Assert-True ((New-AgentOneTimePassword) -ne (New-AgentOneTimePassword)) 'OTP distincts' }

# ── Comparaison en temps constant ──
Test-Case 'Compare : tableaux égaux' { Assert-True (Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2, 3))) 'égaux' }
Test-Case 'Compare : tableaux différents' { Assert-True (-not (Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2, 4)))) 'différents' }
Test-Case 'Compare : longueurs différentes' { Assert-True (-not (Compare-ConstantTime ([byte[]](1, 2, 3)) ([byte[]](1, 2)))) 'longueurs différentes' }

# ── Contrôle de bitness sur les binaires CONSTRUITS (échec si absents : anti-faux-vert) ──
$agentDir = Join-Path $repoRoot 'agent'
$x86Native = Get-ChildItem -Path $agentDir -Recurse -Filter 'SQLite.Interop.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x86$' } | Select-Object -First 1
$x64Native = Get-ChildItem -Path $agentDir -Recurse -Filter 'SQLite.Interop.dll' -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -match '\\x64$' } | Select-Object -First 1
$x86Exe = Get-ChildItem -Path $agentDir -Recurse -Filter 'Liakont.Agent.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\bin\\x86\\' } | Select-Object -First 1
$x64Exe = Get-ChildItem -Path $agentDir -Recurse -Filter 'Liakont.Agent.exe' -ErrorAction SilentlyContinue |
    Where-Object { $_.FullName -match '\\bin\\x64\\' } | Select-Object -First 1

Test-Case 'PE : SQLite.Interop natif x86 = x86' {
    if (-not $x86Native) { throw 'SQLite.Interop.dll x86 introuvable — construire la solution agent d''abord.' }
    Assert-Equal 'x86' (Get-PeMachineType -Path $x86Native.FullName) 'machine x86'
}
Test-Case 'PE : SQLite.Interop natif x64 = x64' {
    if (-not $x64Native) { throw 'SQLite.Interop.dll x64 introuvable — construire la solution agent d''abord.' }
    Assert-Equal 'x64' (Get-PeMachineType -Path $x64Native.FullName) 'machine x64'
}
Test-Case 'Architecture : EXE x86 = x86 (et non AnyCPU)' {
    if (-not $x86Exe) { throw 'Liakont.Agent.exe x86 introuvable — construire la solution agent d''abord.' }
    Assert-Equal 'x86' (Get-AgentBinaryArchitecture -Path $x86Exe.FullName) 'architecture x86'
}
Test-Case 'Architecture : EXE x64 = x64' {
    if (-not $x64Exe) { throw 'Liakont.Agent.exe x64 introuvable — construire la solution agent d''abord.' }
    Assert-Equal 'x64' (Get-AgentBinaryArchitecture -Path $x64Exe.FullName) 'architecture x64'
}
Test-Case 'Architecture : binaire natif rapporté « native »' {
    if (-not $x86Native) { throw 'SQLite.Interop.dll x86 introuvable — construire la solution agent d''abord.' }
    Assert-Equal 'native' (Get-AgentBinaryArchitecture -Path $x86Native.FullName) 'natif = native'
}

Write-Host ""
Write-Host "=== Self-test : $($script:passed) OK, $($script:failed) FAIL ===" -ForegroundColor Cyan
if ($script:failed -gt 0) {
    Write-Host "ÉCHEC du self-test packaging." -ForegroundColor Red
    exit 1
}
Write-Host "PASS" -ForegroundColor Green
exit 0
