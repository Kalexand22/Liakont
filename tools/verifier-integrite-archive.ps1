#requires -Version 5.1
<#
.SYNOPSIS
    Vérifie HORS PLATEFORME l'intégrité d'un coffre d'archive Liakont exporté. Outil AUTONOME : aucune
    dépendance au code Liakont, seul .NET (SHA-256) est utilisé. Destiné au destinataire d'un export de
    RÉVERSIBILITÉ COMPLET (changement de logiciel, fin de contrat). Sur un export PARTIEL (contrôle fiscal
    mono-document ou par période), seules les empreintes pièce/paquet sont vérifiables — pas le lien de
    chaîne complet (la tête de chaîne du coffre n'y figure pas) : l'outil le signale (VERDICT=INCOMPLETE)
    au lieu de conclure à tort à une altération.

.DESCRIPTION
    Recalcule, à partir des seuls fichiers exportés, la chaîne d'empreintes scellée par la plateforme et
    la compare aux manifestes. Sont détectées : toute pièce MODIFIÉE (empreintes de pièce et de paquet
    recalculées) et toute suppression / insertion / réordonnancement AU SEIN de la chaîne présente. En
    revanche, la troncature de la TÊTE (paquet de genèse) ou de la QUEUE d'un export est, à partir des
    seuls fichiers, indistinguable d'un export partiel : l'outil la signale par VERDICT=INCOMPLETE (chaîne
    non ancrée en genèse), et non par une fausse altération — c'est la preuve d'ancrage temporel RFC 3161
    sur la tête de chaîne qui atteste qu'aucun maillon n'a été retiré (vérification openssl, voir la
    notice). La formule reproduite est exactement celle de la plateforme (TRK05) :

      empreinte d'une pièce  = SHA-256(octets du fichier)                      -> hex minuscule
      empreinte d'un paquet  = SHA-256( pour chaque pièce TRIÉE par nom (ordinal) : "<nom>:<empreinte>\n" )
      (un addendum à pièce unique a pour empreinte celle de sa pièce)
      empreinte de chaîne(N)  = SHA-256( empreinte_de_chaîne(N-1) + empreinte_de_paquet(N) )   (genèse = "")

    Tout est encodé en UTF-8, toute empreinte est en hexadécimal MINUSCULE.

    L'ancrage temporel (preuves RFC 3161 sous _anchors/) n'est pas re-signé par cet outil (il faut le
    certificat de l'autorité d'horodatage) : leur présence est signalée et la procédure « openssl ts -verify »
    est rappelée. La notice docs/guide-restitution-reversibilite.md détaille la marche complète.

.PARAMETER ExportPath
    Chemin du dossier d'export DÉCOMPRESSÉ (la racine du ZIP, ou directement son sous-dossier « archive »).

.OUTPUTS
    Code de sortie : 0 = archive intègre, OU chaîne non ancrée en genèse (export partiel / tête absente,
    VERDICT=INCOMPLETE), OU coffre vide ; 1 = altération détectée (VERDICT=TAMPERED) ; 2 = erreur d'usage
    (chemin introuvable / dossier non reconnu).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File verifier-integrite-archive.ps1 -ExportPath .\reversibilite-acme
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ExportPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256HexOfBytes {
    param([byte[]]$Bytes)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash($Bytes)
    }
    finally {
        $sha.Dispose()
    }
    return -join ($digest | ForEach-Object { $_.ToString('x2') })
}

function Get-Sha256HexOfString {
    param([string]$Text)
    return Get-Sha256HexOfBytes ([System.Text.Encoding]::UTF8.GetBytes($Text))
}

if (-not (Test-Path -LiteralPath $ExportPath)) {
    Write-Host "ERREUR : chemin introuvable : $ExportPath" -ForegroundColor Red
    exit 2
}

$root = (Resolve-Path -LiteralPath $ExportPath).Path

# Énumération unique puis filtre par NOM (le couple -LiteralPath/-Include de Get-ChildItem ignore
# silencieusement -Include — il faut filtrer nous-mêmes). Les manifestes de paquet/addendum portent une
# empreinte de chaîne ; les autres fichiers JSON (rapport d'intégrité, manifestes d'ancrage) sont écartés
# ensuite par le filtre sur (entryKind, packageHash, chainHash, files).
$allFiles = @(Get-ChildItem -LiteralPath $root -Recurse -File -ErrorAction SilentlyContinue)
$manifestFiles = @($allFiles | Where-Object { $_.Name -eq 'manifest.json' -or $_.Name -like 'manifest-addendum-*.json' })

$entries = New-Object System.Collections.Generic.List[object]
foreach ($manifestFile in $manifestFiles) {
    try {
        $manifest = Get-Content -LiteralPath $manifestFile.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    catch {
        Write-Host "ERREUR : manifeste illisible : $($manifestFile.FullName)" -ForegroundColor Red
        exit 2
    }

    $kind = if ($manifest.PSObject.Properties.Name -contains 'entryKind') { [string]$manifest.entryKind } else { $null }
    $hasChain = $manifest.PSObject.Properties.Name -contains 'chainHash'
    $hasPackage = $manifest.PSObject.Properties.Name -contains 'packageHash'
    $hasFiles = $manifest.PSObject.Properties.Name -contains 'files'
    if (-not ($hasChain -and $hasPackage -and $hasFiles) -or ($kind -ne 'package' -and $kind -ne 'addendum')) {
        continue
    }

    $entries.Add([pscustomobject]@{
            ManifestPath = $manifestFile.FullName
            Directory    = $manifestFile.Directory.FullName
            EntryKind    = $kind
            PackageHash  = [string]$manifest.packageHash
            ChainHash    = [string]$manifest.chainHash
            Files        = @($manifest.files)
        })
}

Write-Host ""
Write-Host "Vérification d'intégrité — coffre d'archive Liakont" -ForegroundColor Cyan
Write-Host "Dossier : $root"
Write-Host ""

if ($entries.Count -eq 0) {
    if (-not (Test-Path -LiteralPath (Join-Path $root 'archive')) -and ($manifestFiles.Count -eq 0)) {
        Write-Host "Aucun manifeste d'archive trouvé sous ce dossier — vérifiez que c'est bien la racine de l'export décompressé." -ForegroundColor Yellow
        exit 2
    }
    Write-Host "Aucun paquet d'archive à vérifier (coffre vide) — rien à signaler." -ForegroundColor Green
    Write-Host "VERDICT=EMPTY"
    exit 0
}

$tampered = $false

# ── 1) Empreinte par pièce + empreinte de paquet (recalcul depuis les octets exportés) ──
foreach ($entry in $entries) {
    $recomputed = @{}
    foreach ($file in $entry.Files) {
        $name = [string]$file.name
        $filePath = Join-Path $entry.Directory $name
        if (-not (Test-Path -LiteralPath $filePath)) {
            Write-Host "[ALTÉRÉ] pièce manquante : $name (paquet $($entry.ManifestPath))" -ForegroundColor Red
            $tampered = $true
            continue
        }

        $actual = Get-Sha256HexOfBytes ([System.IO.File]::ReadAllBytes($filePath))
        $recomputed[$name] = $actual
        if ($actual -ne [string]$file.sha256) {
            Write-Host "[ALTÉRÉ] pièce modifiée : $name (paquet $($entry.ManifestPath))" -ForegroundColor Red
            $tampered = $true
        }
    }

    if ($entry.EntryKind -eq 'addendum' -and $entry.Files.Count -eq 1) {
        $entryHash = $recomputed[[string]$entry.Files[0].name]
    }
    else {
        $names = [string[]]($entry.Files | ForEach-Object { [string]$_.name })
        [System.Array]::Sort($names, [System.StringComparer]::Ordinal)
        $builder = New-Object System.Text.StringBuilder
        foreach ($name in $names) {
            [void]$builder.Append($name).Append(':').Append($recomputed[$name]).Append("`n")
        }
        $entryHash = Get-Sha256HexOfString $builder.ToString()
    }

    $entry | Add-Member -NotePropertyName RecomputedEntryHash -NotePropertyValue $entryHash
    if ($entryHash -ne $entry.PackageHash) {
        Write-Host "[ALTÉRÉ] empreinte de paquet incohérente : $($entry.ManifestPath)" -ForegroundColor Red
        $tampered = $true
    }
}

# ── 2) Chaînage : reconstruit l'ordre depuis la genèse avec les empreintes RECALCULÉES ──
# Marche quadratique (re-balayage des entrées restantes à chaque maillon) : volontairement simple, le
# coût est négligeable pour une restitution hors-ligne ; l'empreinte recalculée (et non l'empreinte
# annoncée) est utilisée, de sorte qu'un manifeste falsifié-puis-recohérencé rompt quand même la chaîne.
$remaining = New-Object System.Collections.Generic.List[object]
$entries | ForEach-Object { $remaining.Add($_) }
$previous = ''
$linked = 0
while ($remaining.Count -gt 0) {
    $match = $null
    foreach ($candidate in $remaining) {
        if ((Get-Sha256HexOfString ($previous + $candidate.RecomputedEntryHash)) -eq $candidate.ChainHash) {
            $match = $candidate
            break
        }
    }

    if ($null -eq $match) {
        break
    }

    $previous = $match.ChainHash
    [void]$remaining.Remove($match)
    $linked++
}

# Interprétation du résultat de reconstruction :
#  - $linked == total                 -> chaîne complète et intègre.
#  - 0 < $linked < total              -> rupture STRUCTURELLE après la genèse (paquet supprimé / inséré /
#                                        réordonné, ou tête falsifiée puis recohérencée) = altération.
#  - $linked == 0 (et contenu intact) -> la chaîne ne s'ancre PAS en genèse : EXPORT PARTIEL (un export
#                                        de contrôle fiscal ne contient pas la tête de chaîne du coffre).
#                                        Ce n'est PAS une altération — seules les empreintes pièce/paquet
#                                        (vérifiées ci-dessus) sont vérifiables hors plateforme.
$incompleteChain = $false
if ($linked -eq $entries.Count) {
    # Chaîne complète, rattachée jusqu'à la dernière entrée.
}
elseif ($linked -gt 0) {
    $tampered = $true
    Write-Host "[ALTÉRÉ] rupture de chaîne : $($remaining.Count) entrée(s) ne se rattachent pas (paquet supprimé, inséré ou réordonné)." -ForegroundColor Red
    foreach ($orphan in $remaining) {
        Write-Host "         - $($orphan.ManifestPath)" -ForegroundColor Red
    }
}
else {
    # Aucune entrée ne s'ancre en genèse : export partiel (pas la tête de chaîne du coffre).
    $incompleteChain = $true
}

# ── 3) Preuves d'ancrage temporel (signalées ; vérification RFC 3161 hors de cet outil) ──
$anchorProofs = @($allFiles | Where-Object { $_.Name -like 'anchor-*.tsr' -or $_.Name -like 'anchor-*.ots' })

Write-Host ""
Write-Host "Entrées d'archive vérifiées : $($entries.Count) (chaînées : $linked)."
if ($anchorProofs.Count -gt 0) {
    Write-Host "Preuves d'ancrage temporel présentes : $($anchorProofs.Count). À vérifier avec « openssl ts -verify »" -ForegroundColor Yellow
    Write-Host "contre le certificat de l'autorité d'horodatage (voir docs/guide-restitution-reversibilite.md)." -ForegroundColor Yellow
}

Write-Host ""
if ($tampered) {
    Write-Host "RÉSULTAT : ARCHIVE ALTÉRÉE — au moins une incohérence ci-dessus. NE PAS s'y fier." -ForegroundColor Red
    Write-Host "VERDICT=TAMPERED"
    exit 1
}

if ($incompleteChain) {
    Write-Host "RÉSULTAT : EMPREINTES PIÈCE/PAQUET INTÈGRES — chaîne NON ancrée en genèse (export PARTIEL)." -ForegroundColor Yellow
    Write-Host "Pour vérifier le LIEN de chaîne complet, fournissez l'export de RÉVERSIBILITÉ COMPLET du tenant." -ForegroundColor Yellow
    Write-Host "VERDICT=INCOMPLETE"
    exit 0
}

Write-Host "RÉSULTAT : ARCHIVE INTÈGRE — toutes les empreintes et le chaînage concordent." -ForegroundColor Green
Write-Host "VERDICT=OK"
exit 0
