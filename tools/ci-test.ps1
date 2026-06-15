#!/usr/bin/env pwsh
<#
.SYNOPSIS
    CI test wrapper: runs `dotnet test` then FAILS if zero tests actually ran.
.DESCRIPTION
    Reproduces in CI the anti-false-green guard of tools/run-tests.ps1. A bare
    `dotnet test --filter` returns exit 0 when no test matches ("No test matches..."), when test
    discovery breaks, or when a test project drops out of the solution - a green job that executed
    nothing. This wrapper parses the test runner summary (same approach as run-tests.ps1: the CLI
    locale is forced to English so the totals are matchable) and fails if the total is zero OR if no
    recognized summary was emitted (an unverifiable result is a failure, never a false green).

    It ALSO retries ONCE, in a fresh process, the narrow Testcontainers infrastructure flake where the
    ResourceReaper/TestcontainersSettings static initializer throws a RegexMatchTimeoutException under
    runner CPU starvation (the cached TypeInitializationException then cascades over a whole assembly).
    The retry is gated on the EXACT failure signature, so it can never turn a deterministic test
    failure green - that would re-fail in the fresh process. See tools/ci-test-lib.ps1 for the logic
    and tools/test-ci-retry.ps1 for the self-test proving it.

    Cross-platform: pwsh 7 on the Linux and Windows runners (syntax also valid on Windows
    PowerShell 5.1 for local use). ASCII-only: a BOM-less .ps1 is read as ANSI by Windows
    PowerShell 5.1, so accents would corrupt parsing.
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

# On pwsh 7.4+ (the GitHub runners) $PSNativeCommandUseErrorActionPreference defaults to $true, so a
# native command returning a non-zero exit code would THROW under ErrorActionPreference='Stop' -
# before `$LASTEXITCODE` is read, making the explicit exit-code branch (and its diagnostic) dead. We
# handle dotnet's exit code ourselves, so disable that behaviour. Harmless on Windows PowerShell 5.1
# (the variable does not exist there; assigning it is a no-op).
$PSNativeCommandUseErrorActionPreference = $false

# Force the dotnet CLI summary to English so the test-count regexes are locale-independent (a French
# runner prints "Reussi! ... total : N", which the English patterns would miss) - same fix as
# tools/run-tests.ps1.
$env:DOTNET_CLI_UI_LANGUAGE = 'en'

. "$PSScriptRoot/ci-test-lib.ps1"

$tag = [System.IO.Path]::GetFileNameWithoutExtension($Solution)
if ($Platform) { $tag = "$tag-$Platform" }

$dotnetArgs = @($Solution, '--no-build', '-c', $Configuration, '--filter', $Filter)
if ($Platform) { $dotnetArgs += "-p:Platform=$Platform" }

# The runner executes one full dotnet test pass, prints its output (so BOTH passes stay visible in the
# CI log on a retry) and returns it with the exit code. GetNewClosure snapshots $dotnetArgs (wanted -
# frozen args). We read $global:LASTEXITCODE EXPLICITLY: GetNewClosure also captures a frozen copy of
# $LASTEXITCODE if it is set when the closure is created, so a bare $LASTEXITCODE could return that
# stale value instead of dotnet's real exit code - a silent false-green. $global: always reads the
# live global the native command sets.
$runner = {
    $out = dotnet test @dotnetArgs 2>&1 | Out-String
    Write-Host $out
    return @{ Output = $out; Exit = $global:LASTEXITCODE }
}.GetNewClosure()

exit (Invoke-CiTest -Runner $runner -Tag $tag -Filter $Filter)
