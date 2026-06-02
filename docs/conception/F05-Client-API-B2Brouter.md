# F5 — Client API B2Brouter
### Document de conception — Gateway.Core/B2Brouter

> Statut : 🟨 rédigé sans deep research (l'API est déjà documentée et validée en staging — cf. `RECAP-Test-B2Brouter-eReporting-DGFiP.md` partie B et `Intégration de l'API B2Brouter eDocExchange....md`). À revoir ensemble.
> Dernière mise à jour : 2026-06-02

---

## 1. Objectif

Encapsuler **toutes** les interactions avec l'API B2Brouter eDocExchange dans un composant unique, testable, du Core. Aucun autre composant de la Passerelle ne doit connaître les URL, le format JSON ou les particularités de B2Brouter — c'est ce qui rendra possible le multi-PA plus tard (interface `IPaClient`, implémentation `B2BrouterClient`).

## 2. Faits établis (validés en staging, ne pas re-découvrir)

| Fait | Détail | Source |
|---|---|---|
| Authentification | Header `X-B2B-API-Key: <clé>` — clé statique, pas d'OAuth2 | RECAP B.1 |
| Versionnement | Header `X-B2B-API-Version` — minimum `2026-03-02` pour les champs DGFiP | Doc API |
| URL staging | `https://api-staging.b2brouter.net` (⚠️ PAS `app-staging` → `api_version_subdomain_mismatch`) | RECAP B.1 |
| URL production | `https://api.b2brouter.net` | Doc API |
| Création + envoi | `POST /accounts/{accountId}/invoices.json` avec `send_after_import: true` | RECAP B.4 |
| Création sans envoi | même endpoint, `send_after_import: false` → état `new`, non facturable | RECAP B.5.1 |
| Lecture facture | `GET /invoices/{id}.json` | RECAP B.4 |
| Pas de resend | Aucun endpoint de renvoi → **recréer une facture avec un numéro unique** | RECAP B.4 |
| Tax reports | Lecture seule : `GET /accounts/{id}/tax_reports.json`, `GET /tax_reports/{id}.json` (champ `xml_base64` après soumission du ledger, batch ~02:00) | RECAP B.5.2 |
| tax_report_setting | `GET/POST/PATCH /accounts/{id}/tax_report_settings[/dgfip].json` — `naf_code` = code INSEE 2 chiffres, champs requis `start_date`, `type_operation`, `enterprise_size` | RECAP B.3 |
| Réglage SIREN | Le PA doit être assigné au niveau **SIREN** (`cin_scheme: "0002"`), pas SIRET | RECAP B.2 |
| start_date | Si dans le futur → SIREN NON PUBLIÉ, aucun envoi possible. Prod : 2026-09-01 | RECAP B.5.2 |
| Annuaire | `GET /directory/fr/{scheme}/{identifier}` — **non fiable en sandbox** (404 permanent), ne pas en faire un prérequis bloquant | RECAP B.5.2 |
| Erreurs silencieuses | Une réponse **HTTP 200 peut contenir `errors[]` non vide** (ex. VATEX manquant sur une ligne 0 %) → toujours inspecter `errors[]` et `tax_report_ids[]` | Doc API §5 |
| Facturation | 2 transactions facturables par opération avec tax report (facture + report). Suivi via `GET /accounts/{id}.json` (`transactions_count`, `transactions_limit`) | Doc API §6 |
| Avoirs | `is_credit_note: true` + `is_amend: true` + `amended_number` + `amended_date` (ou lignes à quantité négative pour annulation partielle) | Doc API §4 |

## 3. Surface du client (proposition)

```csharp
public interface IPaClient
{
    // Envoi d'un document (facture B2C/B2B ou avoir) — retourne l'identifiant PA + erreurs éventuelles
    Task<PaSendResult> SendDocumentAsync(PivotDocument doc, bool sendAfterImport = true);

    // Relecture d'un document envoyé (état, tax_report_ids, errors)
    Task<PaDocumentStatus> GetDocumentStatusAsync(string paDocumentId);

    // Tax reports
    Task<IReadOnlyList<PaTaxReport>> ListTaxReportsAsync(DateTime? since = null);
    Task<PaTaxReport> GetTaxReportAsync(string taxReportId);   // inclut xml_base64 si dispo

    // Compte / consommation / configuration
    Task<PaAccountInfo> GetAccountInfoAsync();                  // transactions_count, limites
    Task<PaTaxReportSetting> GetTaxReportSettingAsync();
    Task EnsureTaxReportSettingAsync(PaTaxReportSettingRequest request); // idempotent (GET puis POST/PATCH si écart)

    // Diagnostic
    Task<PaDirectoryResult> LookupDirectoryAsync(string scheme, string identifier); // informatif seulement
}
```

### DTOs principaux
- `PaSendResult` : `PaDocumentId`, `State` (new/sending/issued/error), `TaxReportIds[]`, `Errors[]` (code + message), `RawResponse` (pour la piste d'audit).
- `PaDocumentStatus` : état courant + erreurs + tax reports liés.
- `PaTaxReport` : id, type, transport, état (`new → sent → acknowledged → registered`), `XmlBase64`, montants, `has_errors`.
- Tous les DTOs conservent le **JSON brut** reçu (`RawJson`) → exigence de piste d'audit (cf. F6/DR6).

## 4. Règles de comportement

### 4.1 Gestion des erreurs — 3 familles
| Famille | Exemples | Comportement |
|---|---|---|
| **Erreur réseau / HTTP 5xx / timeout** | DNS, coupure, 502 | Retry automatique : 3 tentatives, backoff exponentiel (5 s, 30 s, 2 min). Au-delà → document en état `ErreurTechnique`, re-tentable au prochain run. |
| **Erreur API HTTP 4xx** | 401 (clé), 422 (payload invalide), 404 | **Pas de retry.** Document en état `Rejeté` avec le détail. 401 → erreur de configuration (bloquer tout le run, pas juste le document). |
| **Erreur silencieuse (200 + errors[])** | VATEX manquant, transport indisponible | **Pas de retry automatique.** Document en état `RejetéParPa` avec `errors[]` détaillé. C'est le cas le plus piégeux — validé en staging. |

### 4.2 Idempotence / anti-double-envoi
- Le **numéro de document** (`invoice.number`) est la clé d'unicité côté B2Brouter.
- Règle : la Passerelle ne renvoie JAMAIS un numéro déjà envoyé avec succès (contrôle dans le Tracking AVANT l'appel API — cf. F6).
- En cas de doute (timeout sur un POST : envoyé ou pas ?) : relire via `GET` la liste des factures du compte avant de retenter. Si introuvable → renvoyer ; si trouvée → récupérer son id et raccrocher.
- Pour un renvoi après rejet : nouveau numéro suffixé (`BA-2026-00123-R1`) + lien vers l'original dans le Tracking. ⚠️ Décision à valider : le suffixe est-il acceptable fiscalement ? (cf. DR6 — la numérotation des factures doit être séquentielle et continue ; pour le e-reporting B2C c'est moins contraint que pour le e-invoicing).

### 4.3 Timeouts
- Timeout HTTP : 60 s par appel (l'API peut être lente à la création + envoi).
- Timeout global d'un run : pas de limite dure, mais journaliser tout appel > 30 s.

### 4.4 Multi-compte
- Un `B2BrouterClient` est construit pour **un compte** (clé API + account id + URL de base). 
- Le cas multi-études = N configurations = N instances. Pas de gestion multi-compte à l'intérieur du client.

## 5. Implémentation .NET 4.8

- `HttpClient` statique par configuration (pas de `using` par appel — socket exhaustion).
- `Newtonsoft.Json` pour la sérialisation (cohérent avec le pattern SynchroAxelor).
- TLS : forcer `ServicePointManager.SecurityProtocol = Tls12 | Tls13` au démarrage (par défaut .NET 4.8 est OK mais les vieux Windows Server peuvent nécessiter Tls12 explicite — point de vigilance déploiement).
- Pas de dépendance à des packages exotiques : `System.Net.Http`, `Newtonsoft.Json`, c'est tout.

## 6. Ce que le client ne fait PAS (frontières)

- Ne construit pas le payload métier → c'est le rôle du `Pipeline` (transformation Pivot → JSON B2Brouter, dans le Core mais hors du client).
- Ne décide pas de renvoyer ou non → c'est le rôle du Tracking/Pipeline.
- Ne stocke rien → il retourne des résultats, le Tracking persiste.

## 7. Tests

- **Tests unitaires** : sérialisation/désérialisation des DTOs sur les réponses réelles capturées en staging (les coller dans `TestData/`).
- **Tests d'intégration** (manuels, sur staging) : un projet `Gateway.Tests.Staging` qui rejoue les scénarios du RECAP B.6 contre le compte test 272985.
- Capturer chaque nouvelle forme de réponse rencontrée → enrichir `TestData/`.

## 8. Décisions à valider ensemble

| # | Décision | Options | Recommandation |
|---|---|---|---|
| 1 | Numérotation des renvois après rejet | suffixe -R1 / nouvelle séquence / réutiliser le numéro | suffixe, à confirmer avec DR6 |
| 2 | Polling des statuts vs webhooks | polling (simple, on-premise) / webhooks (nécessite URL entrante chez le client !) | **polling** — un webhook entrant est inadapté à du on-premise derrière NAT |
| 3 | Fréquence de synchro des statuts/tax reports | à chaque run / horaire / quotidien | quotidien (les ledgers DGFiP sont générés à ~02:00, inutile de poller plus) |
| 4 | Faut-il abstraire `IPaClient` dès la V1 (multi-PA futur) ou coder B2Brouter en dur ? | interface maintenant / refactor plus tard | **interface maintenant** — coût quasi nul, argument produit |
