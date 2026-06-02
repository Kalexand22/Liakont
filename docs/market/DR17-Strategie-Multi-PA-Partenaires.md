# DR17 — Stratégie multi-PA / conditions partenaires

> Deep research exécutée le 2026-06-02 (106 agents).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️ **Vérification adversariale techniquement en échec sur 100 % des claims (abstentions 0-0)**. Les informations proviennent des pages officielles des PA concernées (sources primaires auto-déclaratives) — cohérentes mais non re-confirmées. Comme prévu au plan, les conditions commerciales précises (taux, tarifs de gros) ne sont de toute façon PAS publiques → le contact commercial direct (AC1/AC2) reste indispensable.

---

## Verdict synthétique

1. **B2Brouter reste le bon choix de PA principale** — leur offre « eDocSync PDP » cible explicitement les intégrateurs/éditeurs en mode Solution Compatible, avec marque blanche ET marque grise, OpenAPI, SDK, webhooks. C'est exactement notre cas d'usage, et on a déjà le staging validé.
2. **Iopole est la meilleure alternative identifiée** — API-first, conçue pour les éditeurs, et **la seule à documenter publiquement la couverture des 4 flux d'e-reporting, y compris les flux de paiement 10.2/10.4** qui sont LE point ouvert majeur avec B2Brouter (cf. F9 et RECAP).
3. **Le point faible de B2Brouter est confirmé par leur propre doc** : le e-reporting de paiement pour les factures reçues (statut « Encaissée » / CDAR 212) est « planned for a future release ». Iopole semble couvrir ce cas.
4. **Seqino et Super PDP** sont des alternatives crédibles (Seqino : SecNumCloud + ISO 27001 + NF203/NF525 ; Super PDP : prix cassés 0,0025 €/facture) mais moins documentées sur les flux de paiement.

---

## 1. Comparatif des PA partenaires (informations non re-confirmées, issues des sites officiels)

### B2Brouter (PA principale actuelle)

| Critère | Information | Source |
|---|---|---|
| Offre partenaire | **« eDocSync PDP »** — marque blanche 100 % (l'intégrateur garde l'UX, la relation client et son offre) OU marque grise (visibilité de l'infrastructure) | b2brouter.net/fr/edocsync-pdp |
| Cible | **Explicitement les intégrateurs/éditeurs en mode Solution Compatible** connectés via API | b2brouter.net |
| API | OpenAPI téléchargeable, SDK officiel, webhooks, doc publique | b2brouter.net, developer.b2brouter.net |
| E-reporting B2C (Flux 10.3) | ✅ Supporté : `IssuedSimplifiedInvoice` agrégées en Ledgers quotidiens vers le PPF | developer.b2brouter.net/docs/dgfip |
| **Archivage** (vérifié 2026-06-02) | ❓ **Rien sur la page eDocExchange** (inclusion, durée, valeur probante non précisées). Le site FR les dit « habilités » à archiver avec valeur probante, sans détail commercial → question à poser (DR17-A5) | b2brouter.net |
| **E-reporting paiement (10.2/10.4)** | ⚠️ Ledger paiements dédié documenté, **MAIS le marquage « Encaissée » (CDAR 212) des factures reçues est « planned for a future release »** | developer.b2brouter.net/docs/dgfip |
| Atout pour nous | Staging déjà validé de bout en bout (RECAP), payloads marge acceptés | interne |

### Iopole (meilleure alternative)

| Critère | Information | Source |
|---|---|---|
| Statut | **PA immatriculée DGFiP le 11/12/2025** (confirmé en DR10, vote 2-0) | impots.gouv.fr |
| Offre partenaire | Marque grise (Iopole reste la PA immatriculée, zéro démarche admin pour le partenaire) OU marque blanche (le partenaire devient lui-même PA — nécessite ISO 27001, Iopole accompagne) | iopole.com/plateforme-agreee-marque-blanche |
| API | **API-first pour les éditeurs** : SDK, sandbox self-service gratuite, doc Swagger publique, statuts par webhooks, observabilité | iopole.com |
| **E-reporting — point fort** | **Couvre les 4 flux DGFiP documentés publiquement : Flux 10.1 (B2B hors France), 10.2 (encaissements B2B internationaux), 10.3 (ventes B2C), 10.4 (encaissements POS)** | iopole.com/e-reporting |
| Certifications | ISO 27001, SecNumCloud, point d'accès PEPPOL certifié | iopole.com |
| Modèle éditeur | « Embarquer l'e-reporting dans votre logiciel sous votre marque » | iopole.com |

### Seqino

| Critère | Information |
|---|---|
| Offre partenaire | Marque blanche (devenir PA immatriculée soi-même) ET marque grise (utiliser le statut PA de Seqino, zéro démarche) |
| API | RESTful, SDK plusieurs langages, doc publique, sandbox gratuite |
| Certifications | **SecNumCloud, ISO 27001, NF203, NF525** (la plus certifiée du panel) |
| Limite | Ne documente PAS publiquement les flux 10.2/10.4 ni XP Z12-013, ni les taux de commission |

### Super PDP ⬆️ rôle revalorisé le 2026-06-02 — ✅ intégrabilité API VÉRIFIÉE le 2026-06-02

| Critère | Information vérifiée |
|---|---|
| Statut | PA immatriculée définitivement, ISO 27001 (LNE), Peppol AP + SMP, infra souveraine France (réplication x4, 2 datacenters, stack Go/PostgreSQL propriétaire) |
| **Pricing API (vérifié sur superpdp.tech/tarifs)** | ⚠️ La gratuité « < 1 000 factures/mois » ne concerne que le **compte en ligne manuel**. L'**API est toujours payante** : **0,01 €/facture** (0-10 k/mois), 0,005 € (10-100 k), 0,0025 € (> 100 k). Pas de minimum mensuel mentionné. |
| ⭐ **E-reporting (point en or pour les enchères)** | Compté comme **« une facture par journée d'encaissements », quel que soit le nombre de transactions** → une vente avec 300 bordereaux B2C = 0,01 €. Coût PA réel d'une étude type : **~3-5 €/mois (~40-60 €/an)** |
| **API** (vérifié) | REST/JSON + **OpenAPI**, **webhooks/callbacks** de statuts, **sandbox** + quick start OAuth 2.0 client credentials, onboarding développeur self-service, cycle de vie complet (statuts, logs, notifications), annuaire mono/multi-PA, KYC, archivage/horodatage |
| Interopérabilité | **AFNOR XP Z12-013**, connexion annuaire PPF, Peppol |
| **Offre intégrateurs** (vérifié) | **Marque grise avec tarification grossiste** : l'intégrateur règle Super PDP directement et facture librement ses clients (la marque Super PDP apparaît à l'inscription du client). Marque blanche sur demande. |
| **Archivage** (vérifié 2026-06-02) | ✅ **« Conservation des factures pendant 10 ans avec garantie d'intégrité »** — listé dans les fonctionnalités (a priori inclus). Non précisé : certification NF Z42-013, horodatage, sort de l'archive en cas de résiliation → à clarifier (DR17-A4) |
| Limites | Pas de modules « lourds » (OCR, rapprochement, workflows) — sans importance pour nous (la Passerelle fait ce travail). Support technique exigeant une maturité SI côté client (= nous). |
| **Positionnement révisé (décision utilisateur)** | ⭐ **PA de l'« Offre Éco »** — coût si faible qu'il peut être **absorbé dans l'abonnement Passerelle** → le client a UN contrat, UNE facture, zéro démarche PA (« conformité complète, PA incluse »). Voir DR9 §3 (TCO). |
| Reste à vérifier | **Couverture des flux de paiement 10.2/10.4** (non documentée publiquement — même question que pour B2Brouter) ; explorer la doc API réelle (superpdp.tech/documentation, page JS non fetchable) et ouvrir une sandbox |

### Tenor / eDemat (issu de DR7)

| Critère | Information |
|---|---|
| Offre partenaire | Marque grise par défaut (« Tenor assure le rôle PA », portail aux couleurs du client), marque blanche disponible |
| Cible | Éditeurs d'ERP/logiciels métier — confirmé en DR7 (vote 2-1 et 3-0) |
| Limite | Héritage EDI, orienté ETI/grands comptes |

## 2. Recommandations

### PA principale : B2Brouter (statu quo confirmé)

Le travail de validation déjà fait (staging, payloads marge, tax reports) vaut plusieurs semaines d'effort. Aucune information découverte ne justifie de changer. **MAIS** le point ouvert F9 (paiements) doit être tranché vite :

### PA de secours : Iopole

- **Critère de bascule n°1** : si B2Brouter ne donne pas de calendrier ferme pour les flux de paiement 10.2/10.4 (ticket A2 du README Conception), **Iopole devient le partenaire pour le périmètre e-reporting de paiement** — voire pour tout, puisqu'ils documentent les 4 flux.
- Action concrète : ouvrir un compte sandbox Iopole (gratuit, self-service) et tester les mêmes payloads que le staging B2Brouter. Coût : quelques jours. Bénéfice : levier de négociation avec B2Brouter + plan B opérationnel.

### Critères de bascule à surveiller

| Signal | Action |
|---|---|
| B2Brouter ne livre pas les flux 10.2/10.4 avant la prod CMP | Basculer le e-reporting de paiement sur Iopole (architecture multi-PA partielle) |
| B2Brouter durcit ses conditions reseller | Comparer avec les conditions Iopole/Seqino (AC1/AC2) |
| Le client exige SecNumCloud | Seqino ou Iopole (B2Brouter : à vérifier) |
| Volume > 10 000 factures/mois sur un client | Étudier Super PDP pour ce client (0,0025 €/facture) |
| **Client final à budget serré (TCO B2Brouter inacceptable)** | **Offre Éco avec Super PDP** (gratuit < 1 000 fact./mois) — voir DR9 §3 TCO. Ce n'est plus un cas de bascule exceptionnel mais une **offre standard à construire** |

### Coût de la portabilité multi-PA

L'architecture du produit (`Gateway.Core\B2Brouter\` avec interface `IPaClient`, cf. F05) isole déjà le client PA derrière une interface. Le coût de la portabilité est :
- **Implémentation d'un 2e client** (`IopoleClient : IPaClient`) : ~5-10 jours (l'API Iopole est REST/Swagger, structurellement proche).
- **Le vrai coût est dans les différences sémantiques** : formats de statuts, gestion des erreurs, modèle de ledgers — à encapsuler dans l'interface.
- **Recommandation** : ne PAS développer le 2e client maintenant (anti-scope-creep), mais valider que l'interface `IPaClient` ne fuit aucun concept spécifique B2Brouter (revue de F05 avec cette grille).

## 3. Ce que cette DR ne pouvait pas obtenir (→ actions directes)

Comme anticipé au plan, les **taux de commission, tarifs de gros et conditions contractuelles ne sont pas publics**. Les actions AC1/AC2 du plan restent nécessaires :

| # | Action | Cible |
|---|---|---|
| AC1 | Demander les conditions reseller/marque blanche réelles | B2Brouter commercial |
| AC2 | Demander les conditions partenaire + confirmer la couverture 10.2/10.4 | Iopole commercial |
| DR17-A1 (nouveau) | Ouvrir une sandbox Iopole et reproduire les tests du staging B2Brouter | Nous (~2-3 jours) |
| DR17-A2 (nouveau) | Revue de l'interface `IPaClient` (F05) avec la grille « aucun concept spécifique B2Brouter ne fuit » | Nous (session conception) |
| ~~DR17-A3~~ | ~~Évaluer l'intégrabilité de Super PDP~~ → **✅ FAIT le 2026-06-02** : API REST/OpenAPI + sandbox + webhooks confirmés ; pricing API 0,01 €/facture ; e-reporting = 1 facture/jour d'encaissements ; marque grise grossiste disponible. Restent : flux 10.2/10.4 + ouverture sandbox effective | ✅ |
| **DR17-A4 (nouveau, 2026-06-02)** | **Ouvrir une sandbox Super PDP** et tester : émission Factur-X, e-reporting B2C (10.3), webhooks de statuts. Questions à poser : flux paiement 10.2/10.4 ? Archivage 10 ans : NF Z42-013 ? horodatage ? inclus dans le prix API ? sort de l'archive en cas de résiliation ? | Nous (~1-2 jours) |
| **DR17-A5 (nouveau, 2026-06-02)** | **Questions à poser à B2Brouter** (ticket A2/AC1) : ① Comptage des ledgers Flux 10 — leur guide général dit « un ledger de 10 enregistrements = 10 transactions » ; si ça s'applique au e-reporting DGFiP, une étude SVV explose les paliers (M4+, 169+ €/mois rien qu'en e-reporting). **La réponse conditionne la viabilité de B2Brouter pour la niche enchères.** ② Archivage : inclus dans eDocExchange ? durée ? valeur probante NF Z42-013 ? surcoût ? | B2Brouter commercial |

### Archivage réglementaire — position de la Passerelle (ajouté le 2026-06-02)

**Rappel réglementaire** : la PA n'a **aucune obligation légale d'archiver** (service annexe hors agrément). La responsabilité de conservation reste chez **l'assujetti** : 6 ans fiscal (LPF art. L102 B), **10 ans** commercial (C. com. art. L123-22), en format électronique d'origine avec intégrité garantie (référence : NF Z42-013 pour la valeur probante).

| Enjeu | Implication produit |
|---|---|
| **Réversibilité** | Si l'archive est uniquement chez la PA et que le client change de PA (ou que la PA disparaît — 112 PA immatriculées, toutes ne survivront pas), il perd son archive. La Passerelle traite déjà tous les documents → **garder notre propre copie archivée** (intégrité + horodatage) est naturel et protège le client. |
| **Argument commercial** | **« Archivage 10 ans inclus »** dans l'abonnement Passerelle renforce l'Offre Éco (« conformité + PA + archivage, un seul contrat ») et justifie le récurrent. |
| **Rétention client** | Un client qui a ses archives chez nous ne résilie pas à la légère — facteur de fidélisation structurel. |
| **Coût pour nous** | Stockage de XML/PDF signés et horodatés : quelques Go par client sur 10 ans — négligeable. Le vrai sujet est la rigueur du processus (intégrité, horodatage, journal de preuve), pas le volume. |

## 4. Données techniques

- **Stats** : 106 agents, 25 claims vérifiés → 0 confirmé, 25 abstentions techniques. Aucune réfutation sur le fond.
- **Sources** : b2brouter.net/fr/edocsync-pdp, developer.b2brouter.net/docs/dgfip, iopole.com (e-reporting + marque blanche), seqino.com, superpdp.tech, norminfo.afnor.org (XP Z12-013).
- **Note** : la cohérence entre la doc B2Brouter trouvée en ligne et notre RECAP interne (limitation « Encaissée » planned) renforce la crédibilité de l'ensemble des informations malgré l'absence de vérification formelle.
