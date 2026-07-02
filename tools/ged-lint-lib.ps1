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
