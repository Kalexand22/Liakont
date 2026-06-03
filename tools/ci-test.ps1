#!/usr/bin/env pwsh
<#
.SYNOPSIS
    CI test wrapper: runs `dotnet test` then FAILS if zero tests actually ran.
.DESCRIPTION
    Reproduces in CI the anti-false-green guard of tools/run-tests.ps1. A bare
    `dotnet test --filter` returns exit 0 when no test matches ("No test matches..."), when test
    discovery breaks, or when a test project drops out of the solution — a green job that executed
    nothing. This wrapper parses the test runner summary (same approach as run-tests.ps1: the CLI
    locale is forced to English so the totals are matchable) and fails if the total is zero OR if no
    recognized summary was emitted (an unverifiable result is a failure, never a false green).
    Cross-platform: pwsh 7 on the Linux and Windows runners (syntax also valid on Windows
    PowerShell 5.1 for local use).
.PARAMETER Solution
    Path to the solution to test.
.PARAMETER Filter
    dotnet test --filter expression.
.PARAMETER Platform
    Optional MSBuild platform (x86 / x64) for the net48 agent.
.PARAMETER Configuration
    Build configuration (default Release). The build is done by a previous CI step; this wrapper
    runs with --no-build.
#>
param(
    [Parameter(Mandatory = $true)][string]$Solution,
    [Parameter(Mandatory = $true)][string]$Filter,
    [string]$Platform = '',
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

# Force the dotnet CLI summary to English so the test-count regexes below are locale-independent
# (a French runner prints "Réussi! ... total : N", which the English patterns would miss) — same
# fix as tools/run-tests.ps1.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

$tag = [System.IO.Path]::GetFileNameWithoutExtension($Solution)
if ($Platform) { $tag = "$tag-$Platform" }

$dotnetArgs = @($Solution, '--no-build', '-c', $Configuration, '--filter', $Filter)
if ($Platform) { $dotnetArgs += "-p:Platform=$Platform" }

$output = dotnet test @dotnetArgs 2>&1 | Out-String
$testExit = $LASTEXITCODE
Write-Host $output

# Count the tests really executed, summing across every test project in the solution. Recognized
# summary formats (same as run-tests.ps1): VSTest "Total tests: N", VSTest one-line "...Total: N",
# Microsoft.Testing.Platform "total: N".
$matches1 = [regex]::Matches($output, '(?im)^\s*Total tests:\s*(\d+)')
if ($matches1.Count -eq 0) { $matches1 = [regex]::Matches($output, '(?i)(?:Passed!|Failed!)[^\r\n]*?Total:\s*(\d+)') }
if ($matches1.Count -eq 0) { $matches1 = [regex]::Matches($output, '(?im)^\s*total:\s*(\d+)') }
[int]$total = 0
foreach ($m in $matches1) { $total += [int]$m.Groups[1].Value }
$summaryRecognized = ($matches1.Count -gt 0)

if ($testExit -ne 0) {
    Write-Host "FAIL: dotnet test a echoue (exit $testExit) pour $tag." -ForegroundColor Red
    exit 1
}
if (-not $summaryRecognized) {
    Write-Host "FAIL: format de resume non reconnu pour $tag - impossible de verifier le nombre de tests (faux-vert evite)." -ForegroundColor Red
    exit 1
}
if ($total -le 0) {
    Write-Host "FAIL: zero test execute pour $tag ($Filter) - faux-vert (garde anti zero-suite CI)." -ForegroundColor Red
    exit 1
}
Write-Host "OK: $total test(s) execute(s) pour $tag." -ForegroundColor Green
exit 0
