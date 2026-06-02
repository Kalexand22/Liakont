# F6 — Tracking, anti-doublons & Piste d'audit fiable
### Document de conception — Gateway.Core/Tracking

> Statut : 🟨 issu de la deep research DR6 (2026-06-02) + conception interne. À revoir ensemble.
> Légende : ✅ confirmé source primaire (Légifrance, BOFiP) · 🔶 probable · ❓ zone d'ombre / décision
>
> **⚠️ AMENDEMENT (2026-06-02 — décision utilisateur, postérieure à la rédaction)** : la décision
> ouverte n°3 (§9, « Scellement renforcé en V1 ? recommandation : non ») est TRANCHÉE dans l'autre
> sens : **l'archivage fiscal 10 ans est intégré au produit en V1** — coffre WORM local + chaîne
> de hashes SHA-256 + ancrage temporel (OpenTimestamps par défaut / RFC 3161 en option / NoAnchor).
> Motif : l'offre commerciale vend « archivage 10 ans inclus » et l'archive doit être indépendante
> de la PA. Voir tasks/decisions.md (2026-06-02) et les items TRK06/TRK07 qui font foi.

---

## 1. Double rôle du Tracking

Le composant Tracking remplit deux fonctions qui partagent le même stockage (SQLite local) :
1. **Opérationnel** : savoir ce qui a été extrait / envoyé / rejeté, éviter les doublons, permettre la reprise après incident.
2. **Légal** : constituer la **piste d'audit fiable** — prouver, des années après, ce qui a été transmis, à partir de quelle donnée source, via quelle règle de mapping.

Le second rôle impose des contraintes que le premier seul n'imposerait pas (rétention longue, non-altération, exportabilité pour un contrôle fiscal).

## 2. Ce que la recherche a établi (DR6)

### ✅ Confirmé (sources primaires)
1. **Art. 289 V CGI** : l'**authenticité de l'origine, l'intégrité du contenu et la lisibilité** de la facture doivent être garanties **depuis l'émission jusqu'à la fin de la conservation**. La **piste d'audit fiable** (art. 289 VII) est le mécanisme qui le satisfait, applicable à la chaîne **logiciel source → passerelle → PA**.
2. **BOFiP (contrôle des comptabilités informatisées)** : « la suppression ou la modification d'enregistrements sans laisser de trace » est un **indice d'irrégularité**. → Il faut **conserver la trace horodatée de toute altération** des données entre la source et le format transmis (journalisation, scellement/empreinte).
3. **Conservation : 10 ans** à compter de la clôture de l'exercice (art. L.123-22 Code de commerce) — durée commerciale qui prévaut en pratique sur le minimum fiscal de 6 ans (art. L.102 B LPF).

### ❓ Zones d'ombre confirmées (non tranchées par les sources — à border autrement)
- **Répartition exacte des responsabilités** éditeur SC / PA / assujetti : **non documentée**. → à border **contractuellement** (clauses), pas à déduire.
- **Charge de l'archivage à valeur probante NF Z42-013 / NF 461** : non confirmée. → notre position (déléguer à la PA) reste à valider.
- **RGPD sur les données B2C** : aucune affirmation confirmée. → traiter par principe de minimisation (cf. §6).

## 3. Schéma de données (SQLite)

```
Document
  id (PK), source_reference, document_number, document_type,
  issue_date, supplier_siren, customer_name, customer_is_company_hint,
  total_net, total_tax, total_gross,
  state,                       -- Detected/Blocked/ReadyToSend/Sending/Issued/RejectedByPa/TechnicalError/Superseded
  payload_hash,                -- hash SHA-256 du JSON pivot (anti-doublon + non-altération)
  pa_document_id,              -- id côté B2Brouter
  mapping_version,             -- version de table TVA appliquée (F3)
  first_seen_utc, last_update_utc

DocumentEvent                  -- journal immuable (append-only), cœur de la piste d'audit
  id (PK), document_id (FK), timestamp_utc, event_type,
  detail,                      -- texte
  payload_snapshot,            -- JSON pivot envoyé (pour Issued)
  pa_response_snapshot,        -- réponse brute PA (pour Issued/Rejected)
  mapping_trace                -- la/les règle(s) de mapping appliquées (F3)

TaxReport                      -- les tax reports DGFiP récupérés
  id (PK), document_id (FK), pa_tax_report_id, type, transport,
  state, xml_base64, retrieved_utc

PaymentAggregate               -- pour F9 (e-reporting paiement)
  id (PK), period, date, vat_rate, taxable_base, vat_amount, state, ...

RunLog
  id (PK), started_utc, ended_utc, command, counts_json, exit_code
```

### Règles de stockage
- **`DocumentEvent` est append-only** : on n'écrit jamais par-dessus, on ajoute. C'est ce qui matérialise la non-altération (DR6 point 2).
- Pour chaque document **Issued**, on archive **3 choses** : le `payload_snapshot` (ce qu'on a envoyé), la `pa_response_snapshot` (ce que la PA a répondu, avec `tax_report_ids`), la `mapping_trace` (pourquoi telle TVA). Ce triplet = la preuve complète.
- Le `payload_hash` sert deux usages : **anti-doublon** (ne pas réenvoyer) et **détection d'altération** (si la source change après envoi, le hash recalculé diffère → alerte, jamais réémission silencieuse).

## 4. Anti-doublons (règle opérationnelle)

```
Avant tout envoi d'un document :
1. Chercher dans Document par (supplier_siren, document_number)
2. Si trouvé en état Issued → NE PAS renvoyer (doublon). Log.
3. Si trouvé en état RejectedByPa → renvoi autorisé sous nouveau numéro (cf. F5), ancien passé Superseded
4. Si payload_hash identique à un Issued → doublon strict, bloqué
5. Sinon → envoi
```

> Le numéro de document (`document_number`) est la clé fonctionnelle ; le `payload_hash` est le garde-fou contre les ré-extractions involontaires.

## 5. Reprise sur incident

| Incident | Comportement |
|---|---|
| Crash pendant un run | SQLite en mode WAL + transactions par document → état cohérent ; le run suivant reprend les `Detected`/`ReadyToSend`/`TechnicalError` |
| Timeout sur un POST (envoyé ou pas ?) | avant de retenter : `GET` la liste des factures du compte (F5) → raccrocher si déjà créée, sinon renvoyer |
| Base SQLite corrompue | sauvegarde quotidienne de la base (copie horodatée) — c'est la piste d'audit, elle ne doit jamais être perdue |

## 6. Rétention & RGPD

| Donnée | Rétention | Justification |
|---|---|---|
| `Document`, `DocumentEvent`, `TaxReport` (payloads, réponses, traces) | **10 ans** (jamais purgé automatiquement) | piste d'audit fiable (✅ art. L.123-22) |
| Logs fichiers techniques (F11) | 90 jours | exploitation, pas de valeur probante |
| Données nominatives acheteur (nom, adresse, email) | **minimisation** | ❓ Le e-reporting B2C ne transmet PAS de nominatif à la DGFiP. On stocke le nom pour l'affichage console + audit, mais à questionner : durée, anonymisation possible après N années ? (RGPD — zone d'ombre DR6) |

> ⚠️ **Décision RGPD à prendre** : faut-il conserver les noms/adresses acheteurs 10 ans, ou les pseudonymiser passé le délai fiscal utile ? La facture elle-même (chez la PA) porte ces données ; notre Tracking pourrait n'en garder qu'une référence. À arbitrer avec le DPO du CMP.

## 7. Ce que la passerelle doit pouvoir restituer lors d'un contrôle fiscal

Fonction d'**export d'audit** (depuis la console F10 ou la CLI) : pour une période ou un document donné, produire un dossier lisible contenant :
- le document source (référence, montants) ;
- le payload transmis (JSON + idéalement le XML récupéré du tax report) ;
- la réponse de la PA et l'identifiant de transmission DGFiP (`tax_report_id`, `ledger_id`) ;
- la trace de mapping TVA (quelle règle, quelle version, validée par qui/quand) ;
- la chronologie horodatée complète.

C'est l'objet `DocumentEvent` + `TaxReport` mis en forme. **Format** : PDF ou dossier de fichiers (JSON + XML + PDF récapitulatif).

## 8. Responsabilité & contractuel (DR6 — à border, non déductible)

Puisque la répartition légale des responsabilités SC/PA/assujetti n'est pas clairement établie par les textes, **le contrat de prestation doit l'expliciter** :
- La passerelle a une **obligation de moyens** sur la transformation et la transmission, pas de résultat sur la justesse fiscale de la donnée source.
- La **qualité de la donnée source** et la **validation du mapping TVA** incombent au client (d'où `validatedBy` dans la table F3).
- La **responsabilité fiscale** (déclaration, exigibilité) reste celle de l'assujetti (le CMP).
- **RC Pro** adaptée recommandée (cf. analyses de marché).
- Archivage à valeur probante (NF Z42-013) **délégué à la PA** — à confirmer dans le contrat PA.

> Ces clauses rejoignent celles déjà recommandées dans les analyses d'opportunité (clauses limitatives de responsabilité, obligation de moyens).

## 9. Décisions à valider ensemble

| # | Décision | Recommandation |
|---|---|---|
| 1 | Rétention des données nominatives acheteur (RGPD) | minimiser : référence + pseudonymisation passé le délai utile, à valider DPO CMP |
| 2 | Format de l'export d'audit | dossier (JSON+XML+PDF récap) — couvre contrôle informatisé et lisibilité |
| 3 | Scellement renforcé (hash chaîné / horodatage qualifié) en V1 ? | ~~non en V1~~ **TRANCHÉ (2026-06-02) : OUI en V1** — coffre WORM + chaîne de hashes + ancrage (voir amendement en tête + items TRK06/TRK07) |
| 4 | Sauvegarde de la base SQLite | quotidienne, horodatée, hors machine si possible — c'est la pièce maîtresse de l'audit |
| 5 | Confirmer la délégation archivage probatoire à B2Brouter | à inscrire au contrat PA |
| 6 | Clauses de responsabilité (obligation de moyens, qualité donnée source au client) | à intégrer à la proposition commerciale CMP |
