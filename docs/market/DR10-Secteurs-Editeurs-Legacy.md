# DR10 — Secteurs propices + éditeurs nominatifs par secteur

> Deep research exécutée le 2026-06-02 (106 agents, 23 sources, 94 claims extraits, 25 vérifiés).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️ **Résultat partiel** : la synthèse finale du workflow a planté (erreur technique) — les données brutes vérifiées sont exploitables mais la liste de 30-50 éditeurs n'a pas été produite. Le document ci-dessous consolide ce qui a été confirmé + les pistes de sources identifiées. **Une DR11 approfondie ou une recherche manuelle complémentaire est nécessaire.**
> Solidité : 17/25 claims confirmés (bonne qualité sur ce qui a été vérifié).

---

## Résumé exécutif

La DR a surtout confirmé **deux points structurants** :
1. Le cadre réglementaire est solide (toutes entreprises TVA doivent passer par une PA ou SC — rappel pour calibrer le discours).
2. **Deux secteurs ressortent avec des éditeurs nominatifs confirmés** : SVV/commissaires-priseurs (3pi/CHEOPS) et secteurs niches B2B (criées, coopératives agricoles, viticulture) avec plusieurs éditeurs identifiés mais non encore vérifiés en détail.

La DR n'a pas atteint l'objectif d'un tableau de scoring complet ni d'une liste nominative de 30-50 éditeurs — c'est une limite connue à traiter en DR11.

---

## 1. Éditeurs CONFIRMÉS par la vérification adversariale

### 3pi / CHEOPS — SVV/commissaires-priseurs ✅ BON PROFIL ICP

| Attribut | Valeur | Vote |
|---|---|---|
| Éditeur | **3pi** (Paris, 43 rue de…) | 3-0 |
| Produit | **CHEOPS** — logiciel de gestion pour commissaires-priseurs et SVV | 3-0 |
| Cible | Ventes aux enchères / SVV — mêmes clients qu'EncheresV6/ISATECH | 3-0 |
| Communication réforme | **AUCUNE** sur la page produit commissaires-priseurs (Factur-X, PDP, e-reporting absents) | 3-0 |
| Profil ICP | **✅ Bon** — éditeur métier spécialisé, page silencieuse sur la réforme |

⚠️ **Caveat** : Cheops Cloud / iCheops existent (versions modernes) — le silence est confirmé sur la page produit on-premise uniquement. À vérifier : les versions cloud ont-elles intégré la conformité ? Site à visiter manuellement.

> **Intérêt stratégique direct** : 3pi est un concurrent/partenaire potentiel sur la niche SVV — leurs clients ont le même besoin que le parc EncheresV6. Le connaître permet soit de les approcher en partenariat (leur fournir la conformité pour leur parc) soit d'approcher directement leurs clients.

---

### Carbone 14 — Funéraire ❌ DISQUALIFIÉ

| Attribut | Valeur | Vote |
|---|---|---|
| Éditeur | **Carbone 14** (PARTNER Informatique, depuis 1993) | 3-0 |
| Communication réforme | **Communique publiquement** sur la facturation électronique 2026 | 3-0 |
| Profil ICP | ❌ **Disqualifié** — besoin déjà couvert |

---

### Iopole ❌ À EXCLURE

**Iopole est une Plateforme Agréée immatriculée DGFiP le 11/12/2025** (vote 2-0). À exclure de la liste de prospects. En revanche, à considérer comme PA partenaire alternative dans DR17.

---

## 2. Éditeurs identifiés mais NON VÉRIFIÉ EN DÉTAIL (sources découvertes par le workflow)

> ⚠️ Ces informations viennent des sources consultées mais les claims correspondants n'ont pas été vérifiés (budget épuisé). **À vérifier manuellement ou en DR11 avant tout contact commercial.**

### Secteur criées / mareyage / ports

| Éditeur | Produit | Source identifiée | Observation |
|---|---|---|---|
| **Agisoft-e** | **Agimaree** | agisoft-e.fr/page/agimaree.php | Logiciel spécialisé criées/mareyage — secteur niche B2B à très forte densité de transactions |
| **Analys Informatique** | Solutions métier | analys-informatique.com | Solutions pour entreprises du secteur pêche/mareyage (à confirmer) |

### Secteur coopératives agricoles

| Éditeur | Produit | Source identifiée | Observation |
|---|---|---|---|
| **ASAPE** | Logiciel coopératives agricoles | asape.fr | Logiciel spécialisé coopératives — fort volume B2B, probablement legacy |

### Secteur vins/spiritueux / viticulture

| Éditeur | Produit | Source identifiée | Observation |
|---|---|---|---|
| **NSI-SADIMO** | Logiciel viticulture | nsi-sadimo.com | Logiciel vigne/cave — niche B2B, probablement on-premise |
| **Cap Vignes** | Logiciel vigne/vin | cap-vignes.vin | Spécialisé vignobles |
| **iD Systemes** | iDErp (base Sage X3) | idsystemes.com | ⚠️ Statut ICP INCERTAIN : le claim « silencieux sur la réforme » a été **réfuté (0-3)** — ils mentionnent la réforme sur leur page. À vérifier en détail. |
| PC SOFT/WinDev | [interview Val France] | fr.pcsoft-windev-webdev.com | Un développeur WinDev dans le secteur vins — signal de présence WinDev dans ce secteur |

### Secteur négoce matériaux

| Éditeur | Produit | Source identifiée | Observation |
|---|---|---|---|
| **JLogiciels** | Logiciel négociant matériaux | jlogiciels.fr | Toulouse, 2005. ⚠️ **ICP AFFAIBLI** : positionné cloud/SaaS-first (confirmé 3-0) ; pas de module e-invoicing (réfuté le claim contraire) → probablement en train de développer la conformité nativement. |

---

## 3. Secteurs à explorer manuellement en DR11

Les secteurs suivants ont été identifiés comme candidats mais **aucun éditeur nominatif n'a été confirmé** lors de cette DR (manque de budget de recherche) :

| Secteur | Signal | Priorité DR11 |
|---|---|---|
| **BTP / artisans du bâtiment** | Koreliz.com identifié (source non fiable, non confirmé) | 🟠 Moyenne |
| **Garages / distribution automobile** | Secteur cité mais aucun éditeur trouvé | 🟠 Moyenne |
| **Imprimeries** | Secteur cité mais aucun éditeur trouvé | 🟡 Basse |
| **Transport / logistique** | Hors périmètre de cette DR | 🟡 Basse |
| **Gestion locative / syndics** | Hors périmètre | 🟡 Basse |
| **Déchets/recyclage** | Hors périmètre | 🟡 Basse |

---

## 4. Scoring préliminaire des secteurs (partiel — à compléter)

> Basé sur les informations vérifiées et les données d'entrée des documents existants.

| Secteur | Volume entreprises | B2B | Legacy probable | Éditeurs trouvés | Score partiel |
|---|---|---|---|---|---|
| **SVV/enchères** (hors ISATECH) | ~600 SVV en France | B2C mixte | ✅ Fort (Magic XPA, CHEOPS) | ✅ 3pi/CHEOPS | ⭐⭐⭐⭐ |
| **Criées/mareyage** | Quelques dizaines | B2B pur | ✅ Très probable (niche extrême) | ✅ Agimaree/Agisoft-e | ⭐⭐⭐⭐ |
| **Coopératives agricoles** | ~2 500 coopératives | B2B mixte | ✅ Probable | ✅ ASAPE | ⭐⭐⭐ |
| **Viticulture/vins** | ~80 000 exploitations | B2B mixte | ✅ Probable | ✅ NSI-SADIMO, Cap Vignes | ⭐⭐⭐ |
| **Négoce matériaux** | ~12 000 entreprises | B2B fort | 🟡 Mixte (JLogiciels = SaaS) | JLogiciels (ICP affaibli) | ⭐⭐ |
| **BTP** | ~600 000 entreprises | B2B fort | 🟡 Mixte (marché dense) | ❌ Non trouvé | ⭐⭐ |
| **Funéraire** | ~4 000 pompes funèbres | B2C mixte | ✅ Probable | Carbone 14 (disqualifié) | ⭐ |

---

## 5. Actions découlant de DR10

| # | Action | Priorité |
|---|---|---|
| DR10-A1 | **Vérifier 3pi/CHEOPS manuellement** — leur site, leur version cloud, leur communication sur la réforme, leur parc client estimé | 🔴 Haute |
| DR10-A2 | **DR11 — Analyse approfondie** : 3pi, Agimaree/Agisoft-e, ASAPE, NSI-SADIMO — fiche par éditeur (techno probable, communication réforme, approche commerciale) | 🔴 Haute |
| DR10-A3 | Vérifier iD Systemes (idsystemes.com) — leur page mentionne-t-elle la réforme, et si oui, ont-ils une PA ou une solution propre ? | 🟠 Moyenne |
| DR10-A4 | Rechercher les éditeurs BTP et garages — deux secteurs non couverts à fort volume | 🟠 Moyenne |

## 6. Données techniques

- **Stats** : 6 angles, 23 sources, 94 claims extraits, 25 vérifiés → 17 confirmés, 8 tués. Synthèse finale : échec technique (summary = "s").
- **Limite principale** : 12 claims abandonnés (budget droppés avant vérification) — les secteurs niches B2B (criées, coopératives, viticulture) ont été explorés à travers leurs éditeurs mais les claims correspondants n'ont pas pu être vérifiés.
