#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Logique reutilisable et testable du wrapper de tests CI (tools/ci-test.ps1).
.DESCRIPTION
    Dot-source par tools/ci-test.ps1 (production) ET par tools/test-ci-retry.ps1 (self-test).
    Ce fichier ne produit AUCUN effet de bord au chargement : il ne contient que des definitions
    de fonctions, pour rester dot-sourcable sans lancer de tests.
    ASCII-only (les .ps1 ecrits sans BOM sont lus en ANSI par Windows PowerShell 5.1 : tout accent
    casse le parsing). Compatible pwsh 7 (runners GitHub) et Windows PowerShell 5.1 (usage local).
#>

# Signature EXACTE du flake d'infrastructure Testcontainers (observe en CI le 2026-06-15, run #48).
# L'initialiseur statique de ResourceReaper/TestcontainersSettings construit la DockerImage de Ryuk,
# dont le parseur de reference d'image (ReferenceRegex - timeout wall-clock de 1 s en Testcontainers
# 4.3.0) expire sous famine CPU du runner. Le TypeInitializationException qui en resulte est mis en
# CACHE pour toute la vie du process : CHAQUE test Testcontainers de l'assembly tombe ensuite en
# cascade sur la meme exception. Un process NEUF (relance de dotnet test) repart d'un type-init sain.
#
# On exige les DEUX marqueurs simultanement pour ne JAMAIS confondre ce flake d'infra avec un echec
# de test metier : un echec d'assertion n'emet pas de RegexMatchTimeoutException sur l'initialiseur
# d'un type DotNet.Testcontainers. C'est ce qui garantit qu'on ne masque jamais un vrai echec
# deterministe. On s'appuie sur des marqueurs NON localises : le nom de l'exception
# (RegexMatchTimeoutException) et le nom PLEINEMENT QUALIFIE du type dont l'initialiseur statique
# echoue. On EVITE volontairement la phrase "type initializer for" : c'est un message du runtime .NET
# localise selon la culture CLR (non force par DOTNET_CLI_UI_LANGUAGE, qui ne touche que le resume de
# la CLI dotnet) - sur un runner non-anglais elle ne matcherait pas. Le nom de type entre quotes, lui,
# reste invariant quelle que soit la locale.
function Test-TransientTestcontainersInitFailure {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Output)
    $hasTimeout = $Output.Contains('RegexMatchTimeoutException')
    $hasReaper = $Output.Contains('DotNet.Testcontainers.Containers.ResourceReaper')
    $hasSettings = $Output.Contains('DotNet.Testcontainers.Configurations.TestcontainersSettings')
    return ($hasTimeout -and ($hasReaper -or $hasSettings))
}

# Compte les tests reellement executes (somme sur tous les projets de la solution). Memes formats de
# resume reconnus que la garde anti faux-vert d'origine : VSTest "Total tests: N", VSTest une-ligne
# "...Total: N", Microsoft.Testing.Platform "total: N". Retourne @{ Recognized = [bool]; Total = [int] }.
function Measure-ExecutedTests {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][string]$Output)
    $found = [regex]::Matches($Output, '(?im)^\s*Total tests:\s*(\d+)')
    if ($found.Count -eq 0) { $found = [regex]::Matches($Output, '(?i)(?:Passed!|Failed!)[^\r\n]*?Total:\s*(\d+)') }
    if ($found.Count -eq 0) { $found = [regex]::Matches($Output, '(?im)^\s*total:\s*(\d+)') }
    [int]$total = 0
    foreach ($match in $found) { $total = $total + [int]$match.Groups[1].Value }
    return @{ Recognized = ($found.Count -gt 0); Total = $total }
}

# Orchestre une execution de tests : lance le runner, applique UN SEUL retry si - et seulement si -
# l'echec porte la signature transitoire Testcontainers ci-dessus, puis evalue le resultat FINAL avec
# les gardes anti faux-vert (exit non nul, resume non reconnu, zero test execute).
#
# $Runner est un scriptblock retournant @{ Output = [string]; Exit = [int] }. Il est INJECTE pour que
# le self-test (tools/test-ci-retry.ps1) puisse exercer la decision de retry sans lancer dotnet.
#
# Retourne 0 (succes) ou 1 (echec) ; n'appelle jamais exit lui-meme (testabilite). Le retry ne peut
# PAS rendre vert un echec deterministe : un vrai test casse re-tombe au second passage et l'exit reste
# non nul -> on retourne 1. Seul un flake reellement transitoire (vert au second passage) devient vert.
function Invoke-CiTest {
    param(
        [Parameter(Mandatory = $true)][scriptblock]$Runner,
        [Parameter(Mandatory = $true)][string]$Tag,
        [string]$Filter = ''
    )

    $attempt = & $Runner
    $output = [string]$attempt.Output
    $exit = [int]$attempt.Exit

    if ($exit -ne 0 -and (Test-TransientTestcontainersInitFailure -Output $output)) {
        Write-Host "FLAKE INFRA Testcontainers detecte pour $Tag (RegexMatchTimeoutException au type-init de ResourceReaper sous famine CPU) - relance UNIQUE dans un process neuf." -ForegroundColor Yellow
        $attempt = & $Runner
        $output = [string]$attempt.Output
        $exit = [int]$attempt.Exit
        if ($exit -ne 0 -and (Test-TransientTestcontainersInitFailure -Output $output)) {
            Write-Host "FAIL: le flake Testcontainers PERSISTE apres relance pour $Tag - ce n'est plus transitoire, on echoue." -ForegroundColor Red
        }
    }

    $measure = Measure-ExecutedTests -Output $output

    if ($exit -ne 0) {
        Write-Host "FAIL: dotnet test a echoue (exit $exit) pour $Tag." -ForegroundColor Red
        return 1
    }
    if (-not $measure.Recognized) {
        Write-Host "FAIL: format de resume non reconnu pour $Tag - impossible de verifier le nombre de tests (faux-vert evite)." -ForegroundColor Red
        return 1
    }
    if ($measure.Total -le 0) {
        Write-Host "FAIL: zero test execute pour $Tag ($Filter) - faux-vert (garde anti zero-suite CI)." -ForegroundColor Red
        return 1
    }
    Write-Host "OK: $($measure.Total) test(s) execute(s) pour $Tag." -ForegroundColor Green
    return 0
}
