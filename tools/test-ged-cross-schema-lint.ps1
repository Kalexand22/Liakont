# Self-test du lint anti-jointure cross-schéma GED (GED11, RL-27).
#
# Prouve que tools/lint-ged-cross-schema.ps1 N'EST PAS pass-by-default : il PASSE sur le code réel du
# module (aucune référence cross-schéma) et ÉCHOUE dès qu'une référence documents./mandats./tvamapping.
# est injectée dans du SQL (migration ou requête en chaîne), tout en NE se déclenchant PAS sur (a) une
# référence située en COMMENTAIRE (un soft-link DOCUMENTÉ), (b) un accès membre C# PascalCase
# (`request.Documents.Count`), (c) un fichier de projet de TEST. Sans ce self-test, un lint qui renvoie
# toujours 0 serait un faux-vert (RL-27). Logique pure (pas de dotnet).
#
# La mécanique commune (tmpRoot, écriture des cas, fichier bénin, invocation du lint, assertion d'exit)
# vit dans tools/ged-lint-lib.ps1 (GDF13) — ce self-test ne garde que ses CAS. Un fix de la mécanique
# s'applique UNE fois (cf. GDF06 : divergence quand le fix encodage n'avait été posé qu'à un endroit).
#
# Exit 0 = le lint discrimine correctement. Exit 1 = le lint ne discrimine pas (bug du lint).

$ErrorActionPreference = 'Stop'

. "$PSScriptRoot/ged-lint-lib.ps1"

$tag      = '[TEST-GED-XSCHEMA]'
$lint     = Join-Path $PSScriptRoot 'lint-ged-cross-schema.ps1'
$realRoot = Get-GedModuleRoot
$psExe    = (Get-Process -Id $PID).Path   # pwsh (Linux/CI) ou powershell.exe (Windows)

if (-not (Test-Path -LiteralPath $lint))     { Write-Host "$tag lint introuvable : $lint" -ForegroundColor Red; exit 1 }
if (-not (Test-Path -LiteralPath $realRoot)) { Write-Host "$tag module GED introuvable : $realRoot" -ForegroundColor Red; exit 1 }

$failures = [System.Collections.Generic.List[string]]::new()
$tmpRoot = New-GedLintTmpRoot 'ged-xschema-lint'

try {
    # 1) Code réel du module : aucun cross-schéma → le lint DOIT passer (exit 0). Preuve « vert sur le code réel ».
    Assert-GedLintExit $failures $tag 'code reel (aucun cross-schéma)' (Invoke-GedLintExe $lint $realRoot $psExe) 0

    # 2) Jointure cross-schéma dans une migration SQL → le lint DOIT échouer (exit 1).
    $c2 = New-GedLintCase $tmpRoot 'bad-sql-join' 'Migrations/V999__join.sql' "SELECT * FROM ged_index.managed_documents m JOIN documents.documents d ON d.id = m.fiscal_document_id;"
    Assert-GedLintExit $failures $tag 'jointure ged_index -> documents. en SQL' (Invoke-GedLintExe $lint $c2 $psExe) 1

    # 3) Référence cross-schéma dans une requête SQL en CHAINE C# → le lint DOIT échouer (exit 1).
    $c3 = New-GedLintCase $tmpRoot 'bad-cs-query' 'Infrastructure/Queries.cs' 'class Q { const string Sql = "SELECT ref FROM mandats.mandants WHERE tenant = @t"; }'
    Assert-GedLintExit $failures $tag 'référence mandats. en chaîne SQL C#' (Invoke-GedLintExe $lint $c3 $psExe) 1

    # 4) Référence cross-schéma dans une autre migration (tvamapping) → le lint DOIT échouer (exit 1).
    $c4 = New-GedLintCase $tmpRoot 'bad-tva' 'Migrations/V998__tva.sql' "SELECT * FROM ged_catalog.axis_definitions a JOIN tvamapping.rules r ON r.code = a.code;"
    Assert-GedLintExit $failures $tag 'jointure ged_catalog -> tvamapping.' (Invoke-GedLintExe $lint $c4 $psExe) 1

    # 4bis) Jointure cross-schéma en CASSE MIXTE dans un .sql (`documents.Documents`) → le lint DOIT
    #    échouer (exit 1). PostgreSQL replie la casse des identifiants non cités : la jointure est
    #    FONCTIONNELLE quelle que soit la casse. Sans le post-point insensible à la casse RÉSERVÉ aux
    #    .sql, la classe `[a-z_]` laisserait filer le `D` majuscule (faux-vert de la règle 9). Le cas .cs
    #    PascalCase (cas 6) reste vert → la distinction est bien PAR LANGAGE, pas globale.
    $c4b = New-GedLintCase $tmpRoot 'bad-sql-mixed-case' 'Migrations/V997__mixedcase.sql' "SELECT * FROM ged_index.managed_documents m JOIN documents.Documents d ON d.id = m.fiscal_document_id;"
    Assert-GedLintExit $failures $tag 'jointure cross-schéma en casse mixte en SQL (documents.Documents)' (Invoke-GedLintExe $lint $c4b $psExe) 1

    # 5) Référence UNIQUEMENT en commentaire (soft-link documenté) → le lint DOIT passer (exit 0).
    $c5 = New-GedLintCase $tmpRoot 'only-comment' 'Domain/ManagedDocument.cs' "public sealed class ManagedDocument {`n    // Soft-link LOGIQUE vers documents.documents.id (sans FK cross-schéma, F19 §3.4.1).`n    public System.Guid? FiscalDocumentId { get; init; }`n}"
    Assert-GedLintExit $failures $tag 'soft-link seulement en commentaire' (Invoke-GedLintExe $lint $c5 $psExe) 0

    # 6) Accès membre C# PascalCase (request.Documents.Count) → le lint NE DOIT PAS se déclencher (exit 0).
    #    C'est le motif du VRAI code (IngestManagedDocumentBatchHandler) — zéro faux positif exigé.
    $c6 = New-GedLintCase $tmpRoot 'cs-member-access' 'Application/Handler.cs' 'class H { int N(Req request) => request.Documents.Count + mandats.Mandants.Length; }'
    Assert-GedLintExit $failures $tag 'accès membre C# PascalCase (.Documents.Count)' (Invoke-GedLintExe $lint $c6 $psExe) 0

    # 7) Référence cross-schéma dans un projet de TEST → le lint DOIT passer (exit 0). Exclusion Tests.Integration.
    #    Un fichier générique bénin est ajouté pour que le scan ne soit pas vide (sinon la garde 0-fichier
    #    échouerait et masquerait ce que le cas veut prouver).
    $c7 = New-GedLintCase $tmpRoot 'test-project' 'Tests.Integration/JoinTests.cs' 'class T { const string Sql = "SELECT * FROM documents.documents"; }'
    Add-GedLintGenericFile $c7
    Assert-GedLintExit $failures $tag 'référence dans Tests.Integration (exclu)' (Invoke-GedLintExe $lint $c7 $psExe) 0

    # 8) Scan à ZÉRO fichier (racine sans aucun .cs/.sql) → le lint DOIT échouer (exit 1). Prouve la garde
    #    anti-faux-vert « pass-by-default » : un module renommé/déplacé ne doit pas rendre un OK vide.
    $c8 = Join-Path $tmpRoot 'empty-root'
    New-Item -ItemType Directory -Path $c8 -Force | Out-Null
    Assert-GedLintExit $failures $tag 'scan vide (module introuvable/renommé) → échec' (Invoke-GedLintExe $lint $c8 $psExe) 1
}
finally {
    Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    Write-Host "$tag ECHEC : $($failures.Count) cas non conforme(s)." -ForegroundColor Red
    exit 1
}
Write-Host "$tag OK : le lint discrimine (code réel/commentaire/accès membre/test => 0 ; cross-schéma codé => 1)." -ForegroundColor Green
exit 0
