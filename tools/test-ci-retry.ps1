#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Self-test de la logique de retry anti-flake Testcontainers de tools/ci-test.ps1.
.DESCRIPTION
    Exerce la bibliotheque pure tools/ci-test-lib.ps1 sur des sorties canoniques, SANS lancer dotnet.
    Garde permanente (CLAUDE.md : un test ecrit-mais-jamais-execute est un faux-vert) cablee en CI
    (ci.yml, job plateforme) et dans verify-fast.ps1. Prouve l'invariant critique : le retry ne peut
    JAMAIS rendre vert un echec deterministe - seul un flake reellement transitoire passe.
    ASCII-only (PS 5.1 lit les .ps1 sans BOM en ANSI). Compatible pwsh 7 et Windows PowerShell 5.1.
#>
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot/ci-test-lib.ps1"

$script:failures = 0
function Assert-That([bool]$Condition, [string]$Name) {
    if ($Condition) {
        Write-Host "PASS  $Name" -ForegroundColor Green
    }
    else {
        Write-Host "FAIL  $Name" -ForegroundColor Red
        $script:failures = $script:failures + 1
    }
}

# --- Sorties canoniques --------------------------------------------------------------------------
# Flake d'infra Testcontainers : les DEUX marqueurs de signature presents (texte reel du run #48).
$flakeOutput = @"
  Failed Liakont.Modules.Supervision.Tests.Integration.AlertLifecycleIntegrationTests.Insert_Then_Read_RoundTrips_The_Alert [1 ms]
  Error Message:
   System.TypeInitializationException : The type initializer for 'DotNet.Testcontainers.Configurations.TestcontainersSettings' threw an exception.
   ---- System.TypeInitializationException : The type initializer for 'DotNet.Testcontainers.Containers.ResourceReaper' threw an exception.
   -------- System.Text.RegularExpressions.RegexMatchTimeoutException : The Regex engine has timed out while trying to match a pattern.
Failed!  - Failed:     7, Passed:    84, Skipped:     0, Total:    91, Duration: 3 s
"@

# Succes franc.
$passOutput = "Passed!  - Failed:     0, Passed:    91, Skipped:     0, Total:    91, Duration: 1 s"

# Echec de test METIER (assertion) : aucun marqueur de signature -> ne doit jamais etre retente.
$realFailOutput = @"
  Failed Liakont.Modules.Validation.Tests.Unit.VatRoundingTests.Should_Round_Half_Up [12 ms]
  Error Message:
   Assert.Equal() Failure: Expected: 20.00  Actual: 19.99
Failed!  - Failed:     1, Passed:    90, Skipped:     0, Total:    91, Duration: 1 s
"@

# Zero test execute (faux-vert potentiel) : exit 0 mais aucun resume reconnu.
$zeroTestOutput = 'No test matches the given testcase filter Category=Nope in the test assembly.'

# MEME flake, mais message d'exception LOCALISE (runner non-anglais) : la phrase "type initializer
# for" est traduite ; seuls le nom de l'exception et le NOM DE TYPE entre quotes restent invariants.
# Doit quand meme etre detecte -> prouve l'independance a la locale (P2 robustesse, round 1).
$flakeOutputLocalized = @"
   System.TypeInitializationException : L'initialiseur de type pour 'DotNet.Testcontainers.Containers.ResourceReaper' a leve une exception.
   -------- System.Text.RegularExpressions.RegexMatchTimeoutException : Le moteur d'expressions regulieres a depasse son delai.
Failed!  - Failed:     7, Passed:    84, Skipped:     0, Total:    91, Duration: 3 s
"@

# --- Detecteur de signature (fonction pure) ------------------------------------------------------
Assert-That (Test-TransientTestcontainersInitFailure -Output $flakeOutput) 'signature: flake Testcontainers detecte'
Assert-That (-not (Test-TransientTestcontainersInitFailure -Output $realFailOutput)) 'signature: echec metier NON pris pour un flake'
Assert-That (-not (Test-TransientTestcontainersInitFailure -Output $passOutput)) 'signature: succes NON pris pour un flake'
Assert-That (-not (Test-TransientTestcontainersInitFailure -Output '')) 'signature: sortie vide geree'
Assert-That (Test-TransientTestcontainersInitFailure -Output $flakeOutputLocalized) 'signature: flake detecte malgre un message d exception localise (independance locale)'

# --- Comptage des tests executes (les TROIS formats de resume reconnus) ---------------------------
$mPass = Measure-ExecutedTests -Output $passOutput
Assert-That ($mPass.Recognized -and $mPass.Total -eq 91) 'comptage: format une-ligne "Passed! ... Total: N"'
$mVstest = Measure-ExecutedTests -Output "Total tests: 42`n     Passed: 42"
Assert-That ($mVstest.Recognized -and $mVstest.Total -eq 42) 'comptage: format VSTest "Total tests: N"'
$mMtp = Measure-ExecutedTests -Output "  total: 7`n  failed: 0"
Assert-That ($mMtp.Recognized -and $mMtp.Total -eq 7) 'comptage: format Microsoft.Testing.Platform "total: N"'
$mZero = Measure-ExecutedTests -Output $zeroTestOutput
Assert-That (-not $mZero.Recognized) 'comptage: aucun resume reconnu (zero test)'

# --- Orchestration : runner injectable avec compteur d'appels ------------------------------------
# Retourne @{ Runner = <scriptblock>; State = <objet .Calls> }. Le scriptblock debite $Script (un
# @{Output;Exit} par tentative) et incremente le compteur partage, ce qui permet d'asserter le
# NOMBRE exact de passages (donc qu'on relance - ou non - au bon moment).
function New-FakeRunner([object[]]$Script) {
    $state = [pscustomobject]@{ Calls = 0 }
    $runner = {
        $index = $state.Calls
        $state.Calls = $state.Calls + 1
        return $Script[$index]
    }.GetNewClosure()
    return @{ Runner = $runner; State = $state }
}

# Scenario 1 : flake puis succes -> relance UNE fois -> vert (2 appels).
$s1 = New-FakeRunner @( @{ Output = $flakeOutput; Exit = 1 }, @{ Output = $passOutput; Exit = 0 } )
$rc1 = Invoke-CiTest -Runner $s1.Runner -Tag 'sc1' -Filter 'x'
Assert-That ($rc1 -eq 0 -and $s1.State.Calls -eq 2) 'sc1: flake->succes relance une fois et passe'

# Scenario 2 : echec metier (hors signature) -> PAS de relance -> rouge (1 appel). Anti-masquage.
$s2 = New-FakeRunner @( @{ Output = $realFailOutput; Exit = 1 } )
$rc2 = Invoke-CiTest -Runner $s2.Runner -Tag 'sc2' -Filter 'x'
Assert-That ($rc2 -eq 1 -and $s2.State.Calls -eq 1) 'sc2: echec metier non retente (jamais masque)'

# Scenario 3 : flake persistant -> relance -> toujours rouge (2 appels).
$s3 = New-FakeRunner @( @{ Output = $flakeOutput; Exit = 1 }, @{ Output = $flakeOutput; Exit = 1 } )
$rc3 = Invoke-CiTest -Runner $s3.Runner -Tag 'sc3' -Filter 'x'
Assert-That ($rc3 -eq 1 -and $s3.State.Calls -eq 2) 'sc3: flake persistant echoue apres une seule relance'

# Scenario 4 : flake au 1er passage, vrai echec metier au 2e -> rouge (jamais masque).
$s4 = New-FakeRunner @( @{ Output = $flakeOutput; Exit = 1 }, @{ Output = $realFailOutput; Exit = 1 } )
$rc4 = Invoke-CiTest -Runner $s4.Runner -Tag 'sc4' -Filter 'x'
Assert-That ($rc4 -eq 1 -and $s4.State.Calls -eq 2) 'sc4: vrai echec revele a la relance reste rouge'

# Scenario 5 : succes direct -> aucun retry (1 appel), vert.
$s5 = New-FakeRunner @( @{ Output = $passOutput; Exit = 0 } )
$rc5 = Invoke-CiTest -Runner $s5.Runner -Tag 'sc5' -Filter 'x'
Assert-That ($rc5 -eq 0 -and $s5.State.Calls -eq 1) 'sc5: succes direct, pas de relance'

# Scenario 6 : exit 0 mais zero test execute -> rouge (garde anti faux-vert preservee, pas de relance).
$s6 = New-FakeRunner @( @{ Output = $zeroTestOutput; Exit = 0 } )
$rc6 = Invoke-CiTest -Runner $s6.Runner -Tag 'sc6' -Filter 'x'
Assert-That ($rc6 -eq 1 -and $s6.State.Calls -eq 1) 'sc6: garde zero-test preservee'

if ($script:failures -gt 0) {
    Write-Host "ECHEC: $($script:failures) assertion(s) du self-test ci-test retry." -ForegroundColor Red
    exit 1
}
Write-Host "OK: self-test du retry anti-flake Testcontainers vert (6 scenarios)." -ForegroundColor Green
exit 0
