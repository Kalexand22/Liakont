# Self-test du lint anti-jointure cross-schéma GED (GED11, RL-27).
#
# Prouve que tools/lint-ged-cross-schema.ps1 N'EST PAS pass-by-default : il PASSE sur le code réel du
# module (aucune référence cross-schéma) et ÉCHOUE dès qu'une référence documents./mandats./tvamapping.
# est injectée dans du SQL (migration ou requête en chaîne), tout en NE se déclenchant PAS sur (a) une
# référence située en COMMENTAIRE (un soft-link DOCUMENTÉ), (b) un accès membre C# PascalCase
# (`request.Documents.Count`), (c) un fichier de projet de TEST. Sans ce self-test, un lint qui renvoie
# toujours 0 serait un faux-vert (RL-27). Logique pure (pas de dotnet).
#
# Exit 0 = le lint discrimine correctement. Exit 1 = le lint ne discrimine pas (bug du lint).

$ErrorActionPreference = 'Stop'

$lint     = Join-Path $PSScriptRoot 'lint-ged-cross-schema.ps1'
$realRoot = (Resolve-Path (Join-Path $PSScriptRoot '../src/Modules/Ged')).Path
$psExe    = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))     { Write-Host "[TEST-GED-XSCHEMA] lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $realRoot)) { Write-Host "[TEST-GED-XSCHEMA] module GED introuvable : $realRoot" -ForegroundColor Red; exit 1 }

function Invoke-Lint([string]$root) {
    & $psExe -NoProfile -ExecutionPolicy Bypass -File $lint -Root $root *> $null
    return $LASTEXITCODE
}

$failures = @()
function Check([string]$label, [int]$got, [int]$want) {
    if ($got -ne $want) {
        $script:failures += "$label : exit $got attendu $want"
        Write-Host "[TEST-GED-XSCHEMA] FAIL  $label (exit $got, attendu $want)" -ForegroundColor Red
    } else {
        Write-Host "[TEST-GED-XSCHEMA] ok    $label (exit $got)" -ForegroundColor Green
    }
}

$tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ged-xschema-lint-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmpRoot -Force | Out-Null

function New-Case([string]$name, [string]$relFile, [string]$content) {
    $dir = Join-Path $tmpRoot $name
    $path = Join-Path $dir $relFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    Set-Content -LiteralPath $path -Value $content -Encoding utf8
    return $dir
}

try {
    # 1) Code réel du module : aucun cross-schéma → le lint DOIT passer (exit 0). Preuve « vert sur le code réel ».
    Check 'code reel (aucun cross-schéma)' (Invoke-Lint $realRoot) 0

    # 2) Jointure cross-schéma dans une migration SQL → le lint DOIT échouer (exit 1).
    $c2 = New-Case 'bad-sql-join' 'Migrations/V999__join.sql' "SELECT * FROM ged_index.managed_documents m JOIN documents.documents d ON d.id = m.fiscal_document_id;"
    Check 'jointure ged_index -> documents. en SQL' (Invoke-Lint $c2) 1

    # 3) Référence cross-schéma dans une requête SQL en CHAINE C# → le lint DOIT échouer (exit 1).
    $c3 = New-Case 'bad-cs-query' 'Infrastructure/Queries.cs' 'class Q { const string Sql = "SELECT ref FROM mandats.mandants WHERE tenant = @t"; }'
    Check 'référence mandats. en chaîne SQL C#' (Invoke-Lint $c3) 1

    # 4) Référence cross-schéma dans une autre migration (tvamapping) → le lint DOIT échouer (exit 1).
    $c4 = New-Case 'bad-tva' 'Migrations/V998__tva.sql' "SELECT * FROM ged_catalog.axis_definitions a JOIN tvamapping.rules r ON r.code = a.code;"
    Check 'jointure ged_catalog -> tvamapping.' (Invoke-Lint $c4) 1

    # 5) Référence UNIQUEMENT en commentaire (soft-link documenté) → le lint DOIT passer (exit 0).
    $c5 = New-Case 'only-comment' 'Domain/ManagedDocument.cs' "public sealed class ManagedDocument {`n    // Soft-link LOGIQUE vers documents.documents.id (sans FK cross-schéma, F19 §3.4.1).`n    public System.Guid? FiscalDocumentId { get; init; }`n}"
    Check 'soft-link seulement en commentaire' (Invoke-Lint $c5) 0

    # 6) Accès membre C# PascalCase (request.Documents.Count) → le lint NE DOIT PAS se déclencher (exit 0).
    #    C'est le motif du VRAI code (IngestManagedDocumentBatchHandler) — zéro faux positif exigé.
    $c6 = New-Case 'cs-member-access' 'Application/Handler.cs' 'class H { int N(Req request) => request.Documents.Count + mandats.Mandants.Length; }'
    Check 'accès membre C# PascalCase (.Documents.Count)' (Invoke-Lint $c6) 0

    # 7) Référence cross-schéma dans un projet de TEST → le lint DOIT passer (exit 0). Exclusion Tests.Integration.
    $c7 = New-Case 'test-project' 'Tests.Integration/JoinTests.cs' 'class T { const string Sql = "SELECT * FROM documents.documents"; }'
    Check 'référence dans Tests.Integration (exclu)' (Invoke-Lint $c7) 0
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "[TEST-GED-XSCHEMA] ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "[TEST-GED-XSCHEMA] OK : le lint discrimine (code réel/commentaire/accès membre/test => 0 ; cross-schéma codé => 1)." -ForegroundColor Green
exit 0
