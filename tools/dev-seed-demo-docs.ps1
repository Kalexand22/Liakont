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
$docs = @(
  @{ k="FAC"; n="F-2026-001"; d="2026-01-08T00:00:00"; r="SRC-0001"; cat="LivraisonBiens";    ht=1000.00; tva=200.00; cust="Garage Dupont SARL" }
  @{ k="FAC"; n="F-2026-002"; d="2026-01-15T00:00:00"; r="SRC-0002"; cat="LivraisonBiens";    ht=450.50;  tva=90.10;  cust="Boucherie Martin" }
  @{ k="FAC"; n="F-2026-003"; d="2026-01-23T00:00:00"; r="SRC-0003"; cat="PrestationServices"; ht=800.00;  tva=160.00; cust="Cabinet Lefevre" }
  @{ k="FAC"; n="F-2026-004"; d="2026-02-03T00:00:00"; r="SRC-0004"; cat="LivraisonBiens";    ht=250.00;  tva=25.00;  cust="Hotel des Voyageurs" }   # 10%
  @{ k="FAC"; n="F-2026-005"; d="2026-02-11T00:00:00"; r="SRC-0005"; cat="LivraisonBiens";    ht=200.00;  tva=11.00;  cust="Librairie Centrale" }     # 5,5%
  @{ k="FAC"; n="F-2026-006"; d="2026-02-19T00:00:00"; r="SRC-0006"; cat="PrestationServices"; ht=500.00;  tva=0.00;   cust="Clinique Saint-Joseph" }  # exonéré
  @{ k="FAC"; n="F-2026-007"; d="2026-02-27T00:00:00"; r="SRC-0007"; cat="LivraisonBiens";    ht=80.00;   tva=16.00;  cust=$null }                    # B2C
  @{ k="FAC"; n="F-2026-008"; d="2026-03-05T00:00:00"; r="SRC-0008"; cat="Mixte";             ht=1500.00; tva=300.00; cust="Commune de Plouezoch" }
  @{ k="FAC"; n="F-2026-009"; d="2026-03-12T00:00:00"; r="SRC-0009"; cat="PrestationServices"; ht=320.00;  tva=64.00;  cust="Agence Web Bleue" }
  @{ k="FAC"; n="F-2026-010"; d="2026-03-20T00:00:00"; r="SRC-0010"; cat="LivraisonBiens";    ht=2750.00; tva=550.00; cust="Distrib Ouest SAS" }
  @{ k="AVO"; n="A-2026-001"; d="2026-03-25T00:00:00"; r="SRC-0011"; cat="LivraisonBiens";    ht=100.00;  tva=20.00;  cust="Garage Dupont SARL" }      # avoir
  @{ k="FAC"; n="F-2026-011"; d="2026-04-02T00:00:00"; r="SRC-0012"; cat="PrestationServices"; ht=640.00;  tva=128.00; cust="Cabinet Lefevre" }
  @{ k="FAC"; n="F-2026-012"; d="2026-04-15T00:00:00"; r="SRC-0013"; cat="LivraisonBiens";    ht=125.00;  tva=6.88;   cust="Librairie Centrale" }      # 5,5% arrondi
  @{ k="AVO"; n="A-2026-002"; d="2026-04-22T00:00:00"; r="SRC-0014"; cat="PrestationServices"; ht=50.00;   tva=10.00;  cust="Agence Web Bleue" }        # avoir
  @{ k="FAC"; n="F-2026-013"; d="2026-05-06T00:00:00"; r="SRC-0015"; cat="LivraisonBiens";    ht=900.00;  tva=180.00; cust=$null }                    # B2C
)

$documents = foreach ($x in $docs) {
  $doc = [ordered]@{
    sourceDocumentKind = $x.k
    number             = $x.n
    issueDate          = $x.d
    sourceReference    = $x.r
    supplier           = $supplier
    totals             = [ordered]@{ totalNet = [decimal]$x.ht; totalTax = [decimal]$x.tva; totalGross = ([decimal]$x.ht + [decimal]$x.tva) }
    operationCategory  = $x.cat
    currencyCode       = "EUR"
  }
  if ($x.cust) { $doc.customer = @{ name = $x.cust; isCompanyHint = $true } }
  $doc
}

$body = [ordered]@{
  contractVersion  = "1"
  documents        = @($documents)
  sourceTaxRegimes = @(
    @{ code = "20";  label = "Taux normal";        occurrences = 7 }
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
