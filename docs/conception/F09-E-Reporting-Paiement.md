# F9 — E-reporting de paiement (Flux 10.2 / 10.4)
### Document de conception — Gateway.Core (Pivot paiements, Pipeline)

> **⚠️ AMENDEMENTS (2026-06-03, décisions D2/D4 — tasks/decisions.md) :**
> 1. **V1 = Flux 10.4 (domestique) UNIQUEMENT.** Aucune règle sourcée ne permet de dériver
>    domestique/international d'un paiement ; le Flux 10.2 (international) reste une capacité
>    déclarable par les plug-ins PA mais n'est PAS alimenté par le pipeline avant la phase 2.
> 2. **La méthode d'imputation des frais (§5.2) est un PARAMÈTRE de tenant validé par
>    l'expert-comptable** — jamais une règle codée en dur ni un défaut.
> 3. **La fréquence déclarative (§2) est portée par le paramétrage du tenant**
>    (`reportingFrequency`, nullable). Null = pas de calcul d'échéance, transmissions suspendues —
>    jamais de cadence devinée.

> Statut : 🟨 issu de la deep research DR5 (2026-06-02) + DR1/DR4 + API B2Brouter. À revoir ensemble.
> ⚠️ **Particularité DR5** : comme DR2, la plupart des affirmations sont en abstention (0-0) — les vérificateurs n'ont pas pu ouvrir les PDF impots.gouv.fr cités. Les faits ci-dessous proviennent de ces **fiches DGFiP officielles** et du **décret n° 2022-1299** ; ils sont cohérents entre eux et avec le RECAP, mais à re-confirmer sur les fiches primaires avant figeage.
> Légende : 🔶 source DGFiP officielle (non re-vérifiée) · ✅ validé staging · ❓ décision / point ouvert B2Brouter

---

## 1. De quoi il s'agit, et pourquoi c'est un point dur

L'e-reporting de **paiement** transmet à la DGFiP la **date et le montant des encaissements** des **prestations de services** (et acomptes), car pour ces opérations la TVA est exigible à l'**encaissement** (pas à la facturation). C'est ce qui permet à l'administration de déterminer l'exigibilité et de pré-remplir la TVA.

**Pourquoi ça concerne le CMP** : dans un bordereau d'enchères, les **frais acheteur / honoraires sont une prestation de services** (cf. F1/F2 `OperationCategory = Mixte`). La part adjudication est une livraison de biens (TVA exigible à la livraison), mais **la part frais relève de la TVA sur encaissements** → e-reporting de paiement potentiellement dû sur cette part. C'est le point qui élargit le périmètre au-delà du simple Flux 10.3.

## 2. Ce que la recherche a établi (DR5 — 🔶 fiches DGFiP)

### Qui / quoi
- Concerne les **prestations de services soumises à la TVA sur encaissements** et les **acomptes**, **sauf** option pour la TVA sur les débits ou autoliquidation.
- Données à transmettre : **SIREN** du fournisseur, **période**, et — **agrégé par jour** — la date + le **montant encaissé ventilé par taux de TVA**. **Pas de données nominatives client.**
- Le client/acheteur **n'a aucune obligation** de transmettre des infos de paiement (c'est le fournisseur).

### Fréquences (selon régime TVA — décret n° 2022-1299, section « C : Transmission des données relatives au paiement »)
| Régime du client | Fréquence | Délai |
|---|---|---|
| Réel normal mensuel | décadaire (3×/mois) | J+10 après chaque décade |
| Réel normal trimestriel | mensuel | avant le 10 du mois suivant |
| Réel simplifié | mensuel | entre le 25 et le 30 du mois suivant |
| Franchise en base | bimestriel | entre le 25 et le 30 du mois suivant la période |

> ❓ **À confirmer : sous quel régime est le CMP ?** (Grande Entreprise → vraisemblablement réel normal mensuel → décadaire). Détermine la cadence de transmission.

### Statut « Encaissée » / CDAR 212
- Les PA doivent supporter le statut **« Encaissée »** (4e statut, porte la donnée de paiement).
- ⚠️ **Côté B2Brouter (✅ confirmé RECAP + DR5)** : le statut « Encaissée » / CDAR 212 est annoncé **« planned for a future release »** — donc **pas disponible aujourd'hui** via API ni interface. B2Brouter a un type de ledger `xml.ledger.dgfip.payments` (rôle SE, « Paid issued invoices »), mais sa disponibilité réelle est à vérifier.

## 3. Le point ouvert décisif

> **Peut-on transmettre les données de paiement via l'API B2Brouter aujourd'hui ?**
>
> La recherche confirme l'**obligation** et confirme que le statut « Encaissée » n'est **pas encore** dispo chez B2Brouter. Le ledger `payments` existe mais on n'a pas validé son usage en staging (contrairement au Flux 10.3 qu'on a fait passer de bout en bout).

**→ Action prioritaire : ticket support B2Brouter** (à ajouter à celui déjà ouvert sur la marge) :
1. Comment transmettre les données de paiement (Flux 10.2/10.4) via l'API aujourd'hui ?
2. Le ledger `xml.ledger.dgfip.payments` est-il alimentable, et comment (quel POST, quels champs) ?
3. Calendrier réel de disponibilité du statut « Encaissée » / CDAR 212 ?

## 4. Conséquence sur le périmètre V1 / la démo

Trois scénarios selon la réponse B2Brouter :

| Scénario | Décision produit |
|---|---|
| **(a) B2Brouter expose déjà la transmission paiement** | On l'implémente en V1 : extraction des règlements (EncheresV6 `type_ligne=3`) → agrégation jour/taux → envoi. Démo complète. |
| **(b) Disponible bientôt (avant sept. 2026)** | On **prépare tout côté passerelle** (extraction, agrégation, modèle pivot paiement) et on branche l'envoi dès que dispo. En démo : « préparé, en attente d'activation B2Brouter ». |
| **(c) Indisponible à l'échéance** | Risque de conformité pour le CMP sur la part frais. À remonter explicitement au CMP. Solution transitoire possible : déclaration manuelle (CA3) de la TVA sur encaissements des frais. **Argument démo paradoxalement fort** : « notre passerelle est prête avant la PA elle-même ». |

> Quel que soit le scénario, **on développe l'extraction + l'agrégation paiement en V1** (la donnée existe dans EncheresV6, c'est peu coûteux), seul l'envoi est conditionné à B2Brouter.

## 5. Conception

### 5.1 Extraction (déjà cadrée — cf. F1/F2 `ExtractPayments`)
EncheresV6 : `lignes_ba.type_ligne='3'` → `montant_ligne`, `date_reglement`, mode de règlement, `no_remise`. Rattachement au bordereau via `no_ba`.

### 5.2 Le défi du lettrage (🔶 + connaissance legacy)
Pour le e-reporting de paiement « propre », il faut lier l'encaissement à la part **prestation de services** (frais) et à son **taux**. Dans EncheresV6, les règlements (`type_ligne=3`) sont au niveau bordereau, pas ventilés par part adjudication/frais. → Il faut une **règle d'imputation** :
- Option simple : imputer l'encaissement au prorata HT frais / HT total du bordereau.
- Option DGFiP-conforme : la donnée attendue est **agrégée par jour et par taux** au niveau SIREN — donc on agrège tous les frais encaissés du jour par taux, sans avoir besoin d'un lettrage parfait ligne à ligne.

**[Amendement 2026-06-03]** Le choix entre ces options N'EST PAS tranché par cette spec : la méthode
d'imputation est un paramètre de tenant, validé par l'expert-comptable du client (même workflow de
validation que la table TVA). Le pipeline (PIP03) n'applique JAMAIS l'une des deux options par
défaut : méthode non renseignée/non validée = transmissions de paiement suspendues avec message opérateur.

> ✅ Bonne nouvelle : l'agrégation jour×taux est **moins exigeante** qu'un lettrage parfait. On somme, par jour de règlement, la part frais (taxable) des bordereaux encaissés, ventilée par taux. Les acomptes/paiements partiels se gèrent par la date d'encaissement réelle.

### 5.3 Modèle (rappel F1/F2)
`PivotPayment { PaymentDate, Amount, Method, RelatedDocumentNumber, SourceReference }` — le Pipeline agrège en `PaymentReportPeriod { Date, Rate, TotalCollectedTaxableBase, TotalCollectedVat }` avant envoi.

### 5.4 Cas limites
| Cas | Traitement |
|---|---|
| Paiement partiel | imputé à sa date ; l'agrégat du jour le reflète |
| Acompte | encaissement → exigibilité à la date d'acompte |
| Trop-perçu / remboursement | montant négatif dans l'agrégat de la période (via rectificatif RE — cf. F7) |
| Règlement sans rattachement bordereau | ⚠️ alerte (encaissement non rattaché) |
| Option TVA sur les débits | ❓ si le CMP a opté → **pas d'e-reporting de paiement** (exigibilité à la facturation) → à confirmer ! Pourrait annuler tout F9 |

## 6. Décisions à valider ensemble

| # | Décision | Recommandation |
|---|---|---|
| 1 | **Le CMP a-t-il opté pour la TVA sur les débits ?** | ❓ **question fiscale prioritaire** — si oui, F9 disparaît (exigibilité à la facturation). À poser au CMP/expert-comptable AVANT de développer |
| 2 | Régime TVA du CMP (cadence de transmission) | confirmer (probablement réel normal mensuel) |
| 3 | `OperationCategory` du bordereau = Mixte ? | confirmer — déclenche l'e-reporting paiement sur la part frais |
| 4 | Développer extraction+agrégation paiement en V1 même si envoi indispo ? | **Oui** — coût faible, donnée présente, prêt pour activation. Démo « en avance sur la PA » |
| 5 | Ticket B2Brouter paiements | à ouvrir (cumulé avec celui sur la marge) |
| 6 | Règle d'imputation frais/adjudication pour le lettrage | agrégation jour×taux (pas de lettrage ligne à ligne) — à valider |
