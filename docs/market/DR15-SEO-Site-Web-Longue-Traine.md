# DR15 — SEO / site web longue traîne

> Deep research exécutée le 2026-06-02 (104 agents).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️ **Vérification adversariale techniquement en échec sur la quasi-totalité des claims (abstentions 0-0)**. Informations issues de sources cohérentes mais non re-confirmées. Une exception : la présence de DevVersPA sur la niche WinDev a reçu un vote 1-0 (faible confirmation).

---

## Verdict synthétique

1. **Les requêtes de tête sont inaccessibles** — « plateforme agréée », « facturation électronique 2026 », « PDP » sont dominées par 134 PA immatriculées, des fintechs (Qonto), des éditeurs (B2Brouter avec des pages piliers mises à jour en continu) et des blogs comptables. Un site neuf n'y a aucune chance à court terme.
2. **La longue traîne technologique est la bonne stratégie** — MAIS la niche WinDev est déjà occupée par **DevVersPA (SEALOG)**, le concurrent direct repéré en DR7 et reconfirmé ici. Les niches AS400, Sage 30/100 i7, Navision ancien, Delphi, Access semblent libres.
3. **Stratégie pour site neuf** : viser exclusivement des mots-clés à difficulté < 20 (Domain Rating < 30), avec des pages programmatiques par techno/secteur/logiciel. Premier trafic significatif : **3 à 6 mois**.
4. ⚠️ **Implication calendrier** : pour capter le pic de demande PME/TPE de mi-2027, le site doit être en ligne **avant fin 2026**.

---

## 1. Concurrence SEO sur les requêtes de tête (non re-confirmé)

| Requête | Qui domine | Accessible ? |
|---|---|---|
| « liste des plateformes agréées » | Qonto (listicle), DLS Services (« guide des 129 PA »), comparateurs | ❌ Non |
| « plateforme agréée » / « PDP » | B2Brouter (page pilier « Guide Complet 2026 », mise à jour 14 avril 2026), 134 PA, blogs comptables | ❌ Non |
| « facturation électronique 2026 » | Gouvernement, médias, PA, experts-comptables | ❌ Non |
| « Factur-X » | FNFE-MPE, éditeurs, documentation technique | ❌ Non |

**Enseignement** : même les acteurs non-PA (Qonto, DLS Services — un éditeur de logiciel de caisse) réussissent sur ces requêtes uniquement avec des listicles exhaustifs mis à jour en continu — un travail de production que la micro-structure ne peut pas soutenir.

## 2. La longue traîne technologique — état des lieux

### ⚠️ Niche WinDev : DÉJÀ OCCUPÉE

**DevVersPA (SEALOG, Chasseneuil-du-Poitou)** occupe le positionnement « Windev Factur-X / facturation électronique » avec un produit API packagé qui transfère les factures des ERP WinDev vers les Plateformes Agréées. (Vote 1-0 — la seule quasi-confirmation de cette DR ; déjà repéré en DR7.)

Par ailleurs, **WinDev 2026 intègre des composants natifs Factur-X** (PC SOFT) — mais l'intégration dans les applications existantes reste un travail substantiel. Et **Clic Concept** publie aussi sur « facturation électronique 2026 WinDev ».

→ **La niche WinDev est la plus disputée des niches legacy.** Ne pas en faire la cible SEO prioritaire.

### Niches probablement libres (à confirmer avec un outil SEO)

| Requête type | Concurrence observée | Priorité |
|---|---|---|
| « AS400 facturation électronique » / « IBM i Factur-X » | Aucun acteur dédié repéré | 🔴 Haute |
| « Sage 100 i7 plateforme agréée » / « Sage 30 facturation électronique » | Blogs Sage génériques (migration V12), pas de solution tierce | 🔴 Haute |
| « Navision facturation électronique » / « Dynamics NAV 2009 conforme » | Forum Microsoft, ISATECH (article Continia) | 🟠 Moyenne |
| « Delphi facturation électronique » / « Access facturation 2026 » | Rien | 🔴 Haute (volume faible mais intention max) |
| « logiciel métier non compatible facturation électronique » | Rien de dédié | 🔴 Haute (la requête problème) |
| Requêtes sectorielles (« logiciel criée conformité », « coopérative agricole facture électronique »…) | À explorer après DR11 | 🟠 Moyenne |

### Note Sage 100

**Sage 100 V12** (déployé depuis juillet 2025) permet l'enrôlement à la PA Sage « en 2 minutes » → les clients Sage 100 qui PEUVENT migrer en V12 ne sont pas notre cible. Notre cible : ceux qui sont bloqués sur des versions anciennes (i7, V10-V11) pour des raisons de personnalisations, de coût ou de compatibilité. Les pages SEO doivent cibler ces versions précises.

## 3. Stratégie de mots-clés pour site neuf (bonnes pratiques, non re-confirmées)

- **Difficulté** : Domain Rating < 30 → viser exclusivement des mots-clés KD < 20.
- **Longue traîne** : chaque mot-clé apporte peu de trafic, mais le cumul de dizaines de pages programmatiques produit un trafic agrégé substantiel — c'est la justification de la structure par techno/secteur/logiciel.
- **Volumes** : les estimations des outils (Google Ads, Semrush, Ahrefs) divergent énormément entre elles — ne pas sur-optimiser sur les chiffres de volume, privilégier l'intention.
- **Délais** : SEO programmatique B2B = **3-6 mois avant trafic significatif** ; déploiement recommandé en 90 jours avec 50-100 pages de test.

## 4. Arborescence de site recommandée

```
accueil (promesse : « votre logiciel reste, il devient conforme »)
│
├── /probleme/                          ← pages PROBLÈME (intention max)
│   ├── logiciel-non-compatible-facturation-electronique
│   ├── rendre-logiciel-ancien-conforme-2027
│   ├── erp-sans-api-plateforme-agreee
│   └── que-faire-avant-septembre-2027
│
├── /technologies/                      ← pages TECHNO (cœur programmatique)
│   ├── as400-ibm-i
│   ├── sage-100-i7          (cibler les versions anciennes, pas V12)
│   ├── sage-30
│   ├── navision-dynamics-nav
│   ├── delphi
│   ├── access
│   ├── vieux-sql-server
│   └── (windev — page de comparaison avec DevVersPA, pas de frontal)
│
├── /secteurs/                          ← pages SECTEUR (issues de DR10/DR11)
│   ├── ventes-aux-encheres-svv
│   ├── criees-maree
│   ├── cooperatives-agricoles
│   ├── negoce-materiaux
│   └── (à compléter selon DR11)
│
├── /solution/                          ← pages PRODUIT
│   ├── comment-ca-marche                (extraction lecture seule → PA)
│   ├── demonstration                    (formulaire de contact / démo)
│   ├── tarifs                           (fourchettes de DR9)
│   └── cas-client-grande-institution    (CMP anonymisé)
│
└── /guides/                            ← contenu d'autorité
    ├── e-reporting-b2c-guide
    ├── sanctions-2026-2027
    └── calendrier-obligations
```

## 5. Les 20 premières pages à créer (priorisées)

| # | Page | Requête cible | Intention |
|---|---|---|---|
| 1 | Accueil | (marque + « passerelle facturation électronique legacy ») | — |
| 2 | /probleme/logiciel-non-compatible | « logiciel non compatible facturation électronique » | 🔴 Max |
| 3 | /solution/comment-ca-marche | « connecter logiciel ancien plateforme agréée » | 🔴 Max |
| 4 | /technologies/as400-ibm-i | « AS400 facturation électronique » | 🔴 Max |
| 5 | /technologies/sage-100-i7 | « Sage 100 i7 facturation électronique 2027 » | 🔴 Max |
| 6 | /solution/cas-client | « cas client conformité logiciel legacy » | 🟠 Preuve |
| 7 | /probleme/rendre-logiciel-ancien-conforme | « rendre logiciel conforme facturation électronique » | 🔴 Max |
| 8 | /technologies/navision | « Navision facturation électronique » | 🟠 Forte |
| 9 | /secteurs/ventes-aux-encheres | « logiciel vente aux enchères facturation électronique » | 🔴 Max (niche maîtrisée) |
| 10 | /guides/sanctions | « sanctions facturation électronique 2027 » | 🟡 Trafic |
| 11 | /technologies/access | « Access facturation électronique 2026 » | 🟠 Forte |
| 12 | /technologies/delphi | « Delphi Factur-X » | 🟠 Forte |
| 13 | /solution/tarifs | « prix mise en conformité logiciel legacy » | 🔴 Max |
| 14 | /guides/e-reporting-b2c | « e-reporting B2C obligation » | 🟡 Trafic (différenciant) |
| 15 | /technologies/sage-30 | « Sage 30 conformité 2027 » | 🟠 Forte |
| 16 | /secteurs/criees-maree | « logiciel criée facturation électronique » | 🟠 Niche |
| 17 | /technologies/vieux-sql-server | « ERP SQL Server ancien facturation électronique » | 🟠 Forte |
| 18 | /probleme/erp-sans-api | « ERP sans API plateforme agréée » | 🔴 Max |
| 19 | /guides/calendrier | « calendrier facturation électronique PME 2027 » | 🟡 Trafic |
| 20 | /secteurs/cooperatives-agricoles | « coopérative agricole facturation électronique » | 🟠 Niche |

## 6. Calendrier recommandé

| Échéance | Action |
|---|---|
| **Été 2026** | Choisir le nom de produit (point ouvert #1 du doc de conception) + réserver domaine ; créer les 5 premières pages |
| **Sept.-oct. 2026** | Pages 6-20 en ligne |
| **Fin 2026** | Site complet indexé → début de la montée SEO |
| **Mi-2027** | Pic de demande PME/TPE = le site doit être à maturité SEO (6+ mois d'ancienneté) |

⚠️ **Le SEO est le canal qui impose le calendrier le plus contraignant** : commencer après fin 2026 = rater le pic de 2027.

## 7. Actions découlant de cette DR

| # | Action | Priorité |
|---|---|---|
| DR15-A1 | Vérifier les volumes/difficulté réels des 20 requêtes cibles avec un outil SEO (Ahrefs/Semrush, essai gratuit ou ~100 €) | 🟠 Moyenne |
| DR15-A2 | Analyser le site DevVersPA (positionnement, prix, contenu) pour calibrer la page WinDev en comparatif plutôt qu'en frontal | 🟠 Moyenne |
| DR15-A3 | Lier le choix du nom de produit (point ouvert #1) à la disponibilité du domaine | 🔴 Haute (bloquant) |

## 8. Données techniques

- **Stats** : 104 agents, 25 claims vérifiés → 0 confirmé (1 vote 1-0 sur DevVersPA), 25 abstentions techniques.
- **Sources** : impots.gouv.fr, qonto.com, dlsservices.fr, b2brouter.net, devverspa.fr, clicconcept.com, pcsoft.fr, blog.tout-pour-la-gestion.com, ahrefs.com, click-internet.fr, storybee.fr, averi.ai.
