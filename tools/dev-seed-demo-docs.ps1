# Script de DEV/DEMO — pousse des factures fictives vers l'endpoint d'ingestion
# de l'agent pour peupler la console (bring-up GATE_CONSOLE_WEB, 1 tenant).
# Sert aussi d'exemple vivant du contrat d'ingestion agent v1 (POST /api/agent/v1/documents/batch).
# Données 100% fictives (SIREN exemple 123456782). Montants en decimal, TTC = HT + TVA.
# Usage : .\tools\dev-seed-demo-docs.ps1 -AgentKey "prefix.secret" [-BaseUrl "http://localhost:55996"]
# La clé d'agent vient de la page Gestion des agents de la console (jamais versionnée).
param(
  [Parameter(Mandatory = $true)] [string] $AgentKey,
  [string] $BaseUrl = "http://localhost:55996"
)
$ErrorActionPreference = 'Stop'

# Fournisseur = l'entreprise cliente (l'émetteur, EN 16931 BG-4). SIREN fictif documenté.
$supplier = @{ name = "SVV Exemple"; siren = "123456782" }

# 15 documents variés : FAC/AVO, 3 natures d'opération, taux 20/10/5,5/0, B2B et B2C.
# Dates RELATIVES à la date du jour (champ `ago` = nb de jours en arrière) — jamais en dur :
# la page Documents filtre par défaut sur le mois courant, des dates figées rendraient l'écran
# vide juste après le seed (bug-inbox FIX07a). `cur=$true` = document garanti dans le mois
# courant (plusieurs, pour une première impression non vide). Chaque ligne porte son régime
# source brut (`reg`) aligné sur sourceTaxRegimes (20/10/5.5/0) et sa ventilation TVA (`rate`).
$docs = @(
  @{ k="FAC"; n="F-2026-001"; ago=150; cur=$false; r="SRC-0001"; cat="LivraisonBiens";    ht=1000.00; tva=200.00; reg="20";  rate=20;  cust="Garage Dupont SARL" }
  @{ k="FAC"; n="F-2026-002"; ago=135; cur=$false; r="SRC-0002"; cat="LivraisonBiens";    ht=450.50;  tva=90.10;  reg="20";  rate=20;  cust="Boucherie Martin" }
  @{ k="FAC"; n="F-2026-003"; ago=120; cur=$false; r="SRC-0003"; cat="PrestationServices"; ht=800.00;  tva=160.00; reg="20";  rate=20;  cust="Cabinet Lefevre" }
  @{ k="FAC"; n="F-2026-004"; ago=105; cur=$false; r="SRC-0004"; cat="LivraisonBiens";    ht=250.00;  tva=25.00;  reg="10";  rate=10;  cust="Hotel des Voyageurs" }
  @{ k="FAC"; n="F-2026-005"; ago=92;  cur=$false; r="SRC-0005"; cat="LivraisonBiens";    ht=200.00;  tva=11.00;  reg="5.5"; rate=5.5; cust="Librairie Centrale" }
  @{ k="FAC"; n="F-2026-006"; ago=78;  cur=$false; r="SRC-0006"; cat="PrestationServices"; ht=500.00;  tva=0.00;   reg="0";   rate=0;   cust="Clinique Saint-Joseph" }
  @{ k="FAC"; n="F-2026-007"; ago=64;  cur=$false; r="SRC-0007"; cat="LivraisonBiens";    ht=80.00;   tva=16.00;  reg="20";  rate=20;  cust=$null }
  @{ k="FAC"; n="F-2026-008"; ago=50;  cur=$false; r="SRC-0008"; cat="Mixte";             ht=1500.00; tva=300.00; reg="20";  rate=20;  cust="Commune de Plouezoch" }
  @{ k="FAC"; n="F-2026-009"; ago=40;  cur=$false; r="SRC-0009"; cat="PrestationServices"; ht=320.00;  tva=64.00;  reg="20";  rate=20;  cust="Agence Web Bleue" }
  @{ k="FAC"; n="F-2026-010"; ago=33;  cur=$false; r="SRC-0010"; cat="LivraisonBiens";    ht=2750.00; tva=550.00; reg="20";  rate=20;  cust="Distrib Ouest SAS" }
  @{ k="AVO"; n="A-2026-001"; ago=24;  cur=$false; r="SRC-0011"; cat="LivraisonBiens";    ht=100.00;  tva=20.00;  reg="20";  rate=20;  cust="Garage Dupont SARL" }
  @{ k="FAC"; n="F-2026-011"; ago=10;  cur=$true;  r="SRC-0012"; cat="PrestationServices"; ht=640.00;  tva=128.00; reg="20";  rate=20;  cust="Cabinet Lefevre" }
  @{ k="FAC"; n="F-2026-012"; ago=7;   cur=$true;  r="SRC-0013"; cat="LivraisonBiens";    ht=125.00;  tva=6.88;   reg="5.5"; rate=5.5; cust="Librairie Centrale" }
  @{ k="AVO"; n="A-2026-002"; ago=4;   cur=$true;  r="SRC-0014"; cat="PrestationServices"; ht=50.00;   tva=10.00;  reg="20";  rate=20;  cust="Agence Web Bleue" }
  @{ k="FAC"; n="F-2026-013"; ago=1;   cur=$true;  r="SRC-0015"; cat="LivraisonBiens";    ht=900.00;  tva=180.00; reg="20";  rate=20;  cust=$null }
)

# Dates relatives, calculées à l'exécution. Pour un document `cur`, on clampe la date au 1er du
# mois courant si le décalage la ferait basculer sur le mois précédent (garantit « plusieurs dans
# le mois courant » quel que soit le jour d'exécution).
$today        = (Get-Date).Date
$firstOfMonth = (Get-Date -Day 1).Date
function Get-IssueDate([int] $ago, [bool] $currentMonth) {
  $d = $today.AddDays(-$ago)
  if ($currentMonth -and $d -lt $firstOfMonth) { $d = $firstOfMonth }
  return $d.ToString("yyyy-MM-ddT00:00:00")
}

$documents = foreach ($x in $docs) {
  # Une ligne par document (EN 16931 BG-25), porteuse du régime source brut + de la ventilation
  # de TVA — sans ligne, la validation bloque (« aucune ligne »), le parcours n'atteint jamais
  # « Prêt à envoyer » (bug-inbox FIX07a). Catégorie/VATEX sont nuls : c'est le mapping PLATEFORME
  # qui les remplit (le pivot ne calcule rien). Totaux = somme des lignes (ici une seule ligne).
  $line = [ordered]@{
    description       = "Ligne principale ($($x.cat))"
    netAmount         = [decimal]$x.ht
    sourceRegimeCodes = @($x.reg)
    taxes             = @( [ordered]@{ taxAmount = [decimal]$x.tva; rate = [decimal]$x.rate } )
  }
  $doc = [ordered]@{
    sourceDocumentKind = $x.k
    number             = $x.n
    issueDate          = (Get-IssueDate $x.ago $x.cur)
    sourceReference    = $x.r
    supplier           = $supplier
    totals             = [ordered]@{ totalNet = [decimal]$x.ht; totalTax = [decimal]$x.tva; totalGross = ([decimal]$x.ht + [decimal]$x.tva) }
    operationCategory  = $x.cat
    currencyCode       = "EUR"
    lines              = @($line)
  }
  if ($x.cust) { $doc.customer = @{ name = $x.cust; isCompanyHint = $true } }
  $doc
}

$body = [ordered]@{
  contractVersion  = "1"
  documents        = @($documents)
  sourceTaxRegimes = @(
    @{ code = "20";  label = "Taux normal";        occurrences = 11 }
    @{ code = "10";  label = "Taux intermediaire"; occurrences = 1 }
    @{ code = "5.5"; label = "Taux reduit";        occurrences = 2 }
    @{ code = "0";   label = "Exonere";            occurrences = 1 }
  )
}

$json    = $body | ConvertTo-Json -Depth 12
$bytes   = [System.Text.Encoding]::UTF8.GetBytes($json)
$headers = @{ "X-Agent-Key" = $AgentKey; "X-Contract-Version" = "1" }

Write-Output ("POST " + $BaseUrl + "/api/agent/v1/documents/batch  (" + $documents.Count + " documents)")
try {
  $resp = Invoke-RestMethod -Method Post -Uri ($BaseUrl + "/api/agent/v1/documents/batch") `
    -Headers $headers -ContentType 'application/json; charset=utf-8' -Body $bytes
  Write-Output "=== Resultats par document ==="
  $resp.results | ForEach-Object { Write-Output (" - " + $_.sourceReference + " => " + $_.status + $(if($_.reason){" (" + $_.reason + ")"})) }
  $accepted = ($resp.results | Where-Object { $_.status -eq 'Accepted' }).Count
  Write-Output ("Acceptes: " + $accepted + "/" + $documents.Count)
} catch {
  $r = $_.Exception.Response
  if ($r) {
    $code = [int]$r.StatusCode
    $sr = New-Object System.IO.StreamReader($r.GetResponseStream())
    $bodyTxt = $sr.ReadToEnd()
    Write-Output ("ECHEC HTTP " + $code + " : " + $bodyTxt)
  } else { Write-Output ("ECHEC : " + $_.Exception.Message) }
}
