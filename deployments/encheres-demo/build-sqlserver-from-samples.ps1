# ============================================================================
#  build-sqlserver-from-samples.ps1  (ASCII-only : PS 5.1 lit les .ps1 en Win1252)
#  Lit les extractions reelles (samples/*.json, format EncheresExtract) et genere un script
#  SQL SERVER reproduisant la base SOURCE EncheresV6 de demo (schema [enc] + donnees reelles).
#
#  - Schema FIDELE : vrais noms de tables/colonnes (tronques 20 car. cote moteur), types mappes.
#  - Donnees REELLES (noms deja fictifs en base demo, CLAUDE.md n.7).
#  - Accents re-encodes (source UTF-8 lue comme Win1252) + padding CHAR binaire (NUL) nettoye.
#  - 2 societes (volontaire no_ba 100xxx / judiciaire 2000xxx) dans la MEME base ; split en
#    2 tenants cote agent (filtre par plage). On charge tout, le parcours cible les cas complets.
#  - Tables volumineuses (stock_lots, requisitions) filtrees sur ce qui est reference par ligne_pv.
# ============================================================================
param(
  [string]$SamplesDir = 'C:\samples',
  [string]$OutFile    = 'C:\Source\Liakont4\deployments\encheres-demo\encheresv6-demo-sqlserver.sql'
)
$ErrorActionPreference = 'Stop'
$ci = [System.Globalization.CultureInfo]::InvariantCulture

$tables = @('entete_etude','Regime_tva','Frais_acheteur','frais_inv',
            'entete_pv','ligne_pv','entete_ba','lignes_ba','entete_bv','lignes_bv',
            'stock_lots','requisitions','vendeurs_societes',
            'entete_facture_clien','ligne_facture_client',
            'entete_notes_hono','lignes_notes_hono','dossiers_inv',
            # GED (PDF des BA/BV) : tables declarees au dictionnaire Zen puis extraites par
            # ged-extract.ps1 (workspace source HORS repo, a cote de EncheresExtract.exe --
            # meme convention que les autres samples ; noms SQL <= 20 car., limite du dictionnaire).
            # Tant que les samples GED ne sont pas revenus du serveur, ces tables sont SKIP et
            # encheresv6-demo-sqlserver.sql reste sans GED : regeneration differee, voulue.
            'GED_Type','GED_Param_Global','GED_Param_Document',
            'GED_document_joint','GED_doc_joint_Ext','GED_Relation')

function Read-Result($name) {
  $p = Join-Path $SamplesDir ($name + '.json')
  if (-not (Test-Path -LiteralPath $p)) { return $null }
  try { return ((Get-Content -LiteralPath $p -Raw) | ConvertFrom-Json).results[0] } catch { return $null }
}

# Corrige le double-encodage (UTF-8 lu comme Win1252).
function Fix-Enc([string]$s) {
  if ([string]::IsNullOrEmpty($s)) { return $s }
  try { return [Text.Encoding]::UTF8.GetString([Text.Encoding]::GetEncoding(1252).GetBytes($s)) } catch { return $s }
}

function To-SqlType([string]$odbc) {
  switch ($odbc) {
    'INTEGER'     { 'int' }
    'SMALLINT'    { 'smallint' }
    'UTINYINT'    { 'tinyint' }   # logiques Magic des tables GED (1 octet, 0/1)
    'BIT'         { 'bit' }
    'DATE'        { 'datetime2' }
    'DECIMAL'     { 'decimal(18,3)' }
    'DOUBLE'      { 'float' }
    'REAL'        { 'real' }
    'LONGVARCHAR' { 'nvarchar(max)' }
    # EncheresExtract serialise les byte[] en chaine hex "0x..." : le default texte les
    # corromprait (strip des controles + re-encodage). Aucune colonne binaire attendue
    # (schema GED = fichiers sur disque, pas de blob), mais si une arrive, elle reste intacte.
    'BINARY'        { 'varbinary(max)' }
    'VARBINARY'     { 'varbinary(max)' }
    'LONGVARBINARY' { 'varbinary(max)' }
    default       { 'nvarchar(max)' }
  }
}

# Litteral binaire SQL Server depuis la forme EncheresExtract ("0x4A..."). Une valeur
# inattendue (pas "0x" + hex) est un defaut d'extraction : on echoue, on ne corrompt pas.
function To-SqlBinary($v) {
  $s = [string]$v
  if ($s -notmatch '^0x[0-9A-Fa-f]*$') { throw "Valeur binaire inattendue (attendu '0x...') : $s" }
  if ($s.Length -eq 2) { return 'NULL' }   # 0x vide
  return $s
}

function To-SqlVal($v, [string]$odbc) {
  if ($null -eq $v) { return 'NULL' }
  switch ($odbc) {
    'BIT'      { if ([bool]$v) { return '1' } else { return '0' } }
    'INTEGER'  { return ([long]$v).ToString($ci) }
    'SMALLINT' { return ([long]$v).ToString($ci) }
    'UTINYINT' { return ([long]$v).ToString($ci) }
    'DECIMAL'  { return ([decimal]$v).ToString($ci) }
    'DOUBLE'   { return ([double]$v).ToString('R', $ci) }
    'REAL'     { return ([double]$v).ToString('R', $ci) }
    'BINARY'        { return To-SqlBinary $v }
    'VARBINARY'     { return To-SqlBinary $v }
    'LONGVARBINARY' { return To-SqlBinary $v }
    'DATE'     { return "'" + (([string]$v) -replace "'", "''") + "'" }
    default    {
      $s = ([string]$v) -replace '\p{Cc}', ''   # supprime NUL + controles (padding CHAR Pervasive)
      $s = (Fix-Enc $s).Trim()
      return "N'" + ($s -replace "'", "''") + "'"
    }
  }
}

# Pre-charge ligne_pv pour filtrer les tables volumineuses (lots / requisitions references).
$lpv = Read-Result 'ligne_pv'
$reqSet = New-Object System.Collections.Generic.HashSet[string]
$lotSet = New-Object System.Collections.Generic.HashSet[string]
if ($lpv) {
  foreach ($r in $lpv.rows) {
    if ($null -ne $r.no_requi) { [void]$reqSet.Add([string]$r.no_requi) }
    if ($null -ne $r.no_requi -and $null -ne $r.no_lot) { [void]$lotSet.Add("$($r.no_requi)|$($r.no_lot)") }
  }
}

$sb = [System.Text.StringBuilder]::new()
[void]$sb.AppendLine("-- Base SOURCE EncheresV6 (demo) reconstruite depuis la vraie data (build-sqlserver-from-samples.ps1).")
[void]$sb.AppendLine("-- Donnees fictives (base demo). NE PAS executer sur une base de production.")
[void]$sb.AppendLine("IF SCHEMA_ID('enc') IS NULL EXEC('CREATE SCHEMA enc');")
[void]$sb.AppendLine("GO")

$summary = @()
foreach ($t in $tables) {
  $res = Read-Result $t
  if ($null -eq $res) { $summary += "SKIP (illisible) : $t"; continue }
  if ($res.error)     { $summary += "SKIP (erreur extraction) : $t"; continue }
  $cols = $res.columns
  $colList = ($cols | ForEach-Object { "[$($_.name)]" }) -join ', '

  $rows = $res.rows
  if ($t -eq 'stock_lots' -and $lotSet.Count -gt 0) {
    $rows = $rows | Where-Object { $lotSet.Contains("$($_.no_requi)|$($_.no_lot)") }
  } elseif ($t -eq 'requisitions' -and $reqSet.Count -gt 0) {
    $rows = $rows | Where-Object { $reqSet.Contains([string]$_.no_requi) }
  }

  [void]$sb.AppendLine("")
  [void]$sb.AppendLine("IF OBJECT_ID('enc.$t','U') IS NOT NULL DROP TABLE [enc].[$t];")
  $defs = ($cols | ForEach-Object { "  [$($_.name)] $(To-SqlType $_.type)" }) -join ",`r`n"
  [void]$sb.AppendLine("CREATE TABLE [enc].[$t] (`r`n$defs`r`n);")
  [void]$sb.AppendLine("GO")

  $n = 0
  foreach ($row in $rows) {
    $vals = foreach ($c in $cols) { To-SqlVal ($row.$($c.name)) $c.type }
    [void]$sb.AppendLine("INSERT INTO [enc].[$t] ($colList) VALUES (" + ($vals -join ', ') + ");")
    $n++
    if ($n % 500 -eq 0) { [void]$sb.AppendLine("GO") }
  }
  [void]$sb.AppendLine("GO")
  $summary += ("{0,-22} {1,6} lignes" -f $t, $n)
}

[System.IO.File]::WriteAllText($OutFile, $sb.ToString(), (New-Object System.Text.UTF8Encoding($true)))
Write-Host "Genere : $OutFile"
Write-Host ("Taille : {0:N0} Ko" -f ((Get-Item -LiteralPath $OutFile).Length/1KB))
$summary | ForEach-Object { Write-Host "  $_" }
