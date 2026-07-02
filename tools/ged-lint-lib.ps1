# Bibliothèque partagée des lints de généricité GED (GED11, RL-27, F19 §7/§8).
#
# Deux gardes outillées s'appuient dessus :
#   - tools/lint-ged-generic-literals.ps1 (règle 7 : aucun vocabulaire métier codé en dur)
#   - tools/lint-ged-cross-schema.ps1      (règle 9 : aucune jointure SQL cross-schéma ged_* → autres)
#
# Deux services communs :
#   1. Get-GedLintFiles  : énumère les fichiers de CODE du module (hors artefacts de build et hors
#      projets de TEST — le vocabulaire métier FICTIF est LÉGITIME dans les tests et les seeds).
#   2. Convert-CommentsToBlanks : remplace le contenu des COMMENTAIRES par des espaces (les sauts de
#      ligne sont préservés → les numéros de ligne restent exacts). String-aware : un marqueur de
#      commentaire (// -- /*) situé DANS une chaîne n'ouvre PAS de commentaire, et les chaînes sont
#      CONSERVÉES. Motif : « lot » veut dire « lot/paquet » en français courant et apparaît
#      LÉGITIMEMENT dans les commentaires de prose ; en revanche un littéral métier CODÉ (identifiant
#      ou chaîne) doit être vu. On lint donc le code, jamais la prose.

$ErrorActionPreference = 'Stop'

# Racine par défaut du module GED, résolue relativement à ce script (fonctionne quel que soit le
# répertoire courant — appelée aussi bien depuis verify-fast que depuis la CI).
function Get-GedModuleRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot '../src/Modules/Ged')).Path
}

# Énumère les fichiers de code du périmètre GED sous $Root pour les extensions demandées.
# Exclusions :
#   - artefacts de build : tout segment /bin/ ou /obj/ ;
#   - projets de TEST du module : Tests.Unit / Tests.Integration (le vocabulaire métier fictif y est
#     légitime — c'est ce qui prouve la généricité). Les seeds FICTIFS vivent dans deployments/<demo>/,
#     hors de src/Modules/Ged, donc déjà hors périmètre (règle 7).
function Get-GedLintFiles {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Extensions
    )
    if (-not (Test-Path -LiteralPath $Root)) { return @() }
    $exts = $Extensions | ForEach-Object { $_.ToLowerInvariant() }
    Get-ChildItem -LiteralPath $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $exts -contains $_.Extension.ToLowerInvariant() } |
        Where-Object {
            $p = ($_.FullName -replace '\\', '/')
            ($p -notmatch '/(bin|obj)/') -and ($p -notmatch '/Tests\.(Unit|Integration)/')
        }
}

# Remplace le contenu des commentaires par des espaces, en préservant les sauts de ligne.
# $Language : 'cs' (commentaires // et /* */ ; chaînes "..." @"..." '...') ou
#             'sql' (commentaires -- et /* */ ; chaînes '...').
# Retour : texte de même longueur, commentaires blanchis, chaînes et code conservés.
function Convert-CommentsToBlanks {
    param(
        [Parameter(Mandatory)][AllowEmptyString()][string]$Text,
        [Parameter(Mandatory)][ValidateSet('cs', 'sql')][string]$Language
    )

    $n = $Text.Length
    $sb = [System.Text.StringBuilder]::new($n)
    $i = 0
    # États : normal | line | block | strDq (") | strVerbatim (@") | chr (' en C#) | strSq (' en SQL)
    $state = 'normal'

    while ($i -lt $n) {
        $c = $Text[$i]
        $next = if ($i + 1 -lt $n) { $Text[$i + 1] } else { [char]0 }

        if ($state -eq 'normal') {
            # Ouverture d'un commentaire de ligne (// en C#, -- en SQL)
            if ($Language -eq 'cs' -and $c -eq '/' -and $next -eq '/') {
                [void]$sb.Append('  '); $i += 2; $state = 'line'
            }
            elseif ($Language -eq 'sql' -and $c -eq '-' -and $next -eq '-') {
                [void]$sb.Append('  '); $i += 2; $state = 'line'
            }
            # Ouverture d'un commentaire bloc (les deux langages)
            elseif ($c -eq '/' -and $next -eq '*') {
                [void]$sb.Append('  '); $i += 2; $state = 'block'
            }
            # Ouverture d'une chaîne double-quote (C#) — verbatim si précédée de @ (ou @$ / $@)
            elseif ($Language -eq 'cs' -and $c -eq '"') {
                $prev = if ($i -ge 1) { $Text[$i - 1] } else { [char]0 }
                $prev2 = if ($i -ge 2) { $Text[$i - 2] } else { [char]0 }
                $verbatim = ($prev -eq '@') -or ($prev -eq '$' -and $prev2 -eq '@')
                [void]$sb.Append($c); $i += 1
                $state = if ($verbatim) { 'strVerbatim' } else { 'strDq' }
            }
            # Ouverture d'une chaîne simple-quote — caractère en C#, chaîne en SQL
            elseif ($c -eq "'") {
                [void]$sb.Append($c); $i += 1
                $state = if ($Language -eq 'sql') { 'strSq' } else { 'chr' }
            }
            else {
                [void]$sb.Append($c); $i += 1
            }
        }
        elseif ($state -eq 'line') {
            if ($c -eq "`n") { [void]$sb.Append($c); $state = 'normal' } else { [void]$sb.Append(' ') }
            $i += 1
        }
        elseif ($state -eq 'block') {
            if ($c -eq '*' -and $next -eq '/') { [void]$sb.Append('  '); $i += 2; $state = 'normal' }
            elseif ($c -eq "`n") { [void]$sb.Append($c); $i += 1 }
            else { [void]$sb.Append(' '); $i += 1 }
        }
        elseif ($state -eq 'strDq') {
            # Chaîne classique "..." : \" est un guillemet échappé, ne ferme pas la chaîne.
            if ($c -eq '\') {
                [void]$sb.Append($c); $i += 1
                if ($i -lt $n) { [void]$sb.Append($Text[$i]); $i += 1 }
            }
            elseif ($c -eq '"') { [void]$sb.Append($c); $i += 1; $state = 'normal' }
            else { [void]$sb.Append($c); $i += 1 }
        }
        elseif ($state -eq 'strVerbatim') {
            # Chaîne verbatim @"..." : "" est un guillemet échappé (reste dans la chaîne).
            if ($c -eq '"' -and $next -eq '"') { [void]$sb.Append('""'); $i += 2 }
            elseif ($c -eq '"') { [void]$sb.Append($c); $i += 1; $state = 'normal' }
            else { [void]$sb.Append($c); $i += 1 }
        }
        elseif ($state -eq 'chr') {
            # Littéral caractère C# '...' : \' échappé.
            if ($c -eq '\') {
                [void]$sb.Append($c); $i += 1
                if ($i -lt $n) { [void]$sb.Append($Text[$i]); $i += 1 }
            }
            elseif ($c -eq "'") { [void]$sb.Append($c); $i += 1; $state = 'normal' }
            else { [void]$sb.Append($c); $i += 1 }
        }
        elseif ($state -eq 'strSq') {
            # Chaîne SQL '...' : '' est un apostrophe échappé (reste dans la chaîne).
            if ($c -eq "'" -and $next -eq "'") { [void]$sb.Append("''"); $i += 2 }
            elseif ($c -eq "'") { [void]$sb.Append($c); $i += 1; $state = 'normal' }
            else { [void]$sb.Append($c); $i += 1 }
        }
    }

    return $sb.ToString()
}

# 3. Invoke-GedLintScan : LA boucle de scan partagée des deux lints (RL-27, GDF13). Un seul endroit lit les
#    fichiers en UTF-8 EXPLICITE (norme du repo = .cs sans BOM ; Get-Content -Raw de PS 5.1 les décoderait en
#    CP1252 → « enchères » deviendrait « enchÃ¨res » et échapperait au scan, faux-vert LOCAL / divergence CI
#    pwsh), blanchit les commentaires, découpe en lignes et applique le MATCHER fourni par le lint. Chaque lint
#    ne garde plus que son vocabulaire/regex ($LineMatcher) et ses messages/rendus. Un correctif de la mécanique
#    (encodage, blanchiment, garde 0-fichier) s'applique UNE fois (cf. GDF06 : le fix encodage n'avait été posé
#    qu'à un endroit → divergence). Retour : objet { Root ; FileCount ; Offenders[] } où chaque offender porte
#    { Rel ; Line ; Value }. La DÉCISION 0-fichier/rendu reste au lint (messages propres à chacun).
# $LineMatcher : scriptblock param($line, $lang) → chaînes captées sur cette ligne ($lang = 'cs' | 'sql').
function Invoke-GedLintScan {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string[]]$Extensions,
        [Parameter(Mandatory)][scriptblock]$LineMatcher
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $Root).Path
    $files = @(Get-GedLintFiles -Root $resolvedRoot -Extensions $Extensions)
    $offenders = New-Object System.Collections.Generic.List[object]

    foreach ($f in $files) {
        $raw = [System.IO.File]::ReadAllText($f.FullName, [System.Text.Encoding]::UTF8)
        if (-not $raw) { continue }
        $lang = if ($f.Extension -ieq '.sql') { 'sql' } else { 'cs' }
        $code = Convert-CommentsToBlanks -Text $raw -Language $lang
        $lines = $code -split "`n"
        for ($ln = 0; $ln -lt $lines.Count; $ln++) {
            foreach ($val in @(& $LineMatcher $lines[$ln] $lang)) {
                $rel = $f.FullName.Substring($resolvedRoot.Length).TrimStart('\', '/')
                $offenders.Add([pscustomobject]@{ Rel = $rel; Line = ($ln + 1); Value = $val })
            }
        }
    }

    return [pscustomobject]@{ Root = $resolvedRoot; FileCount = $files.Count; Offenders = $offenders }
}

# ── Harnais de self-test partagé (GDF13) ────────────────────────────────────────────────────────────────
# Les deux self-tests (test-ged-generic-literals-lint.ps1 / test-ged-cross-schema-lint.ps1) partageaient ~50
# lignes de mécanique (tmpRoot, écriture des cas, fichier bénin de remplissage, invocation du lint, assertion
# d'exit). Mutualisées ici pour qu'un correctif s'applique UNE fois ; chaque self-test ne garde que ses CAS.

# Répertoire temporaire unique d'une passe de self-test.
function New-GedLintTmpRoot {
    param([Parameter(Mandatory)][string]$Prefix)
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("$Prefix-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    return $path
}

# Écrit un fichier de cas sous $TmpRoot/$Name/$RelFile et rend le répertoire du cas (à passer en -Root au lint).
# -NoBom : écrit en UTF-8 SANS BOM (reproduit la norme réelle du repo .cs sans BOM — le seul écrivain qui
# prouve que la lecture UTF-8 explicite du lint capte un littéral accentué décodé correctement, RL-27).
# Sans -NoBom : Set-Content -Encoding utf8 (BOM en PS 5.1) — suffisant pour les cas non accentués.
function New-GedLintCase {
    param(
        [Parameter(Mandatory)][string]$TmpRoot,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$RelFile,
        [Parameter(Mandatory)][AllowEmptyString()][string]$Content,
        [switch]$NoBom
    )
    $dir = Join-Path $TmpRoot $Name
    $path = Join-Path $dir $RelFile
    New-Item -ItemType Directory -Path (Split-Path -Parent $path) -Force | Out-Null
    if ($NoBom) {
        [System.IO.File]::WriteAllText($path, $Content, (New-Object System.Text.UTF8Encoding($false)))
    }
    else {
        Set-Content -LiteralPath $path -Value $Content -Encoding utf8
    }
    return $dir
}

# Ajoute un fichier de code GÉNÉRIQUE bénin dans un répertoire de cas : garantit que le scan n'est PAS vide,
# de sorte qu'un cas d'exclusion prouve « fichier exclu ignoré » et non « scan vide » (qui échoue via la garde
# anti-faux-vert 0-fichier).
function Add-GedLintGenericFile {
    param([Parameter(Mandatory)][string]$Dir)
    $p = Join-Path $Dir 'Domain/_GenericOk.cs'
    New-Item -ItemType Directory -Path (Split-Path -Parent $p) -Force | Out-Null
    Set-Content -LiteralPath $p -Value 'public sealed class GenericOk { public int Value => 0; }' -Encoding utf8
}

# Lance le lint sur une racine et rend son code de sortie. $PsExe = l'interpréteur courant (pwsh en CI,
# powershell.exe en local via verify-fast) → le self-test exerce EXACTEMENT l'interpréteur du pipeline.
function Invoke-GedLintExe {
    param(
        [Parameter(Mandatory)][string]$LintPath,
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$PsExe
    )
    & $PsExe -NoProfile -ExecutionPolicy Bypass -File $LintPath -Root $Root *> $null
    return $LASTEXITCODE
}

# Assertion d'exit d'un cas : accumule l'échec dans $Failures (List[string], passée par référence) et trace le
# résultat sous le $Tag du self-test.
function Assert-GedLintExit {
    param(
        [Parameter(Mandatory)][AllowEmptyCollection()][System.Collections.Generic.List[string]]$Failures,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$Label,
        [Parameter(Mandatory)][int]$Got,
        [Parameter(Mandatory)][int]$Want
    )
    if ($Got -ne $Want) {
        $Failures.Add("$Label : exit $Got attendu $Want")
        Write-Host "$Tag FAIL  $Label (exit $Got, attendu $Want)" -ForegroundColor Red
    }
    else {
        Write-Host "$Tag ok    $Label (exit $Got)" -ForegroundColor Green
    }
}
