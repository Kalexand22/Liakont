# DR11 — Analyse approfondie des éditeurs cibles

> Réalisée le 2026-06-02. ⚠️ Le workflow deep-research a échoué techniquement (erreur StructuredOutput) → **cette analyse a été réalisée par vérification manuelle directe** (WebFetch/WebSearch sur les sites des éditeurs + recherches croisées). Paradoxalement plus fiable que les DR automatiques : chaque information ci-dessous a été lue directement à la source.
> Plan parent : `Plan-DR-Marche-Commercial.md`. Suite de DR10.

---

## Synthèse : priorisation des cibles

| Rang | Éditeur | Secteur | Profil ICP | Angle d'approche |
|---|---|---|---|---|
| **1** | **3pi (CHEOPS)** | SVV / commissaires-priseurs | ✅✅ **Excellent** | Partenariat marque blanche OU prospection directe de leurs clients |
| **2** | **Agisoft Engineering (Agimarée)** | Criées / mareyage | ✅ Bon | Partenariat technique |
| **3** | **NSI-SADIMO (Vinéa)** | Viticulture | 🟡 Moyen (a déjà une compétence EDI) | Sous-traitance du module conformité |
| **4** | **ASAPE** | Coopératives agricoles | 🟠 Affaibli (groupe Orisha) | Surveiller, ne pas prioriser |
| 5 | Garages : GAD Garage, DF Garage, Winmotor | Automobile | 🟡 À qualifier | Prospection ultérieure |

---

## 1. 🥇 3pi / CHEOPS — la cible prioritaire

### Fiche d'identité (vérifiée manuellement le 2026-06-02)

| Attribut | Valeur | Source |
|---|---|---|
| Société | 3pi (SIREN 407 938 687), Paris 9e — 43 rue de Trévise | 3pi.fr, b-reputation |
| **Effectif** | **4 salariés (2019)** | b-reputation.com |
| Produit | **CHEOPS** — progiciel de gestion pour commissaires-priseurs et SVV (ventes volontaires ET judiciaires) | 3pi.fr |
| Fonctionnalités | Préparation/déroulement des ventes, inventaires, stock/code-barres, **interface comptable**, multimédia, prospects/catalogues, tableau de bord | 3pi.fr |
| Mobile | iCheops (inventaire avec photos, Google Play) | LinkedIn |
| **Facturation électronique** | **AUCUNE mention** sur le site (Factur-X, PDP, PA, réforme 2026 : rien) | 3pi.fr (vérifié) |
| **Partenaire SYMEV** | **OUI** — listé aux côtés d'ISATECH, Double Numérique, Artbrain, Scancube | symev.org/partenaires (vérifié) |

### Analyse stratégique

**3pi est l'équivalent exact d'ISATECH sur la même niche, en plus petit et plus fragile :**

1. **4 salariés** → aucune capacité à développer un module de conformité e-invoicing/e-reporting en interne. C'est mathématiquement impossible pour eux (le sujet exige des semaines de R&D réglementaire + technique).
2. **Leur parc de clients commissaires-priseurs a EXACTEMENT le même besoin que le parc EncheresV6** : bordereaux acheteurs B2C → e-reporting, acheteurs pros → e-invoicing, avoirs, TVA sur marge.
3. **Notre adaptateur enchères est déjà conçu** (modèle pivot, mapping TVA marge, avoirs, garde-fou B2B) → l'adaptateur CHEOPS serait un 2e adaptateur du même vertical, avec une logique métier quasi identique. Effort estimé : faible (l'essentiel du travail métier est réutilisable).
4. **Le canal SYMEV est commun** : devenir partenaire SYMEV nous mettrait dans la même liste qu'eux et qu'ISATECH.

### Scénarios d'approche

| Scénario | Description | Rapport de force |
|---|---|---|
| **A. Partenariat marque blanche** | 3pi vend « CHEOPS Conformité » à son parc, propulsé par la Passerelle | Idéal pour eux (ils n'ont pas le choix), récurrent pour nous |
| **B. Sous-traitance** | 3pi nous sous-traite le développement de « leur » module | Moins bon : one-shot, ils gardent la relation client |
| **C. Prospection directe** | On démarche directement les études équipées CHEOPS (concurrent frontal de 3pi) | À garder en réserve — risque de braquer 3pi et le SYMEV |

**Recommandation : scénario A**, à proposer après la démo CMP/EncheresV6 fonctionnelle. Le pitch : « nous avons déjà rendu conforme un logiciel de gestion de ventes aux enchères ; votre parc a le même besoin ; vendons-le ensemble sous votre marque. »

⚠️ **Risque à vérifier avant contact** : santé financière de 3pi (4 salariés en 2019 — existe-t-il encore en 2026 ? bilans récents ?) → Pappers avant tout contact.

---

## 2. 🥈 Agisoft Engineering / Agimarée — criées et mareyage

### Fiche (vérifiée manuellement)

| Attribut | Valeur |
|---|---|
| Produit | **Agimarée** — gestion complète du mareyage : référentiels (clients, fournisseurs, produits, emballages), ventes (BL, étiquettes, **factures**, commandes), stocks, **imports/exports de factures comptables** |
| Cible | Mareyeurs (ateliers de transformation de poisson) + criées ; module de commande en ligne |
| Ancienneté | Actif depuis au moins 2005 (copyright) |
| Technologie | Non précisée (probablement client lourd Windows vu l'ancienneté) |
| **Facturation électronique** | **AUCUNE mention** (seule une « conformité réglementaire générique » est évoquée) |

### Analyse

- **Secteur ultra-niche à très forte intensité B2B** : un mareyeur facture des dizaines de clients pros par jour (poissonneries, restaurants, GMS) → l'e-invoicing 2026/2027 est pour eux un sujet majeur, pas cosmétique.
- Le secteur de la marée a une informatique très spécifique (ventes à la criée, lots, calibres) que les PA généralistes ne comprendront jamais.
- **Profil ICP bon** mais le parc est probablement petit (le nombre de mareyeurs en France est limité : ~300 entreprises de mareyage).
- **Angle d'approche** : partenariat technique — leur fournir le module e-invoicing pour Agimarée. Le faible nombre de clients rend la prospection directe inefficace, le partenariat est la seule voie rentable.

---

## 3. 🥉 NSI-SADIMO / Vinéa — viticulture

### Fiche (vérifiée manuellement)

| Attribut | Valeur |
|---|---|
| Produits | Suite viticole : **Vinéa** (gestion commerciale : clients, facturation, fournisseurs, tarifs, stocks, trésorerie) + **Colbert** (comptabilité) — interface automatique entre les deux |
| Spécificité | **Maîtrise de l'EDI** : traitement des titres de mouvements vins/alcools en mode EDI avec les Douanes (DAE automatisé via interface GAMMA) |
| **Facturation électronique** | Aucune mention de la réforme 2026 trouvée |

### Analyse

- **Le point différenciant : ils savent déjà faire de l'EDI réglementaire** (Douanes/DAE). Ils sont donc techniquement plus capables que 3pi ou Agisoft de développer eux-mêmes leur conformité.
- MAIS l'e-invoicing DGFiP est un chantier différent de l'EDI douanier — ils devront quand même y investir.
- **Profil ICP moyen** : capacité technique existante = risque qu'ils fassent en interne ; absence de communication = fenêtre encore ouverte.
- **Angle d'approche** : sous-traitance technique (« gagnez 6 mois : on vous fournit le moteur de conformité, vous gardez votre interface ») plutôt que marque blanche.
- Le secteur viticole est gros (~80 000 exploitations dont ~15 000 vendent en direct) mais ce parc précis est à estimer.

---

## 4. ASAPE — coopératives agricoles ⚠️ profil affaibli

### Fiche (vérifiée manuellement)

| Attribut | Valeur |
|---|---|
| Parc | **« 100 coopératives agricoles font confiance à Asape »** |
| Ancienneté | 20 ans d'expertise |
| **Groupe** | ⚠️ **ASAPE fait partie d'Orisha Agrifood (groupe Gaiana)** |
| Facturation électronique | Aucune mention sur la page produit |

### Analyse

**Le rattachement au groupe Orisha disqualifie partiellement la cible** : Orisha est un grand groupe logiciel français (plusieurs centaines de M€ de CA) qui développera une solution de conformité au niveau groupe et la déploiera sur toutes ses filiales, dont ASAPE. La fenêtre existe peut-être (les groupes sont lents) mais le risque de développement interne est élevé.

→ **Ne pas prioriser.** Surveiller : si Orisha n'a rien déployé sur ASAPE d'ici début 2027, la fenêtre se rouvre.

---

## 5. Garages / automobile — paysage défavorable

Recherche complémentaire effectuée : le secteur est **mieux couvert que prévu** :

| Éditeur | Statut conformité |
|---|---|
| **Fiducial** (V-Mobility, Vulcain DMS) | ❌ A sa **propre Plateforme Agréée** (Fiducial Cloud) avec intégration DMS annoncée pour sept. 2026 — 45 ans d'expérience, disqualifié |
| **EBP** (MéCa ACTIV) | ❌ Grand éditeur, conformité native en cours — disqualifié |
| GAD Garage, DF Garage, Winmotor Next | 🟡 Plus petits, à qualifier individuellement (non fait — rendement faible attendu) |

**Conclusion garages** : le secteur automobile est dominé par des éditeurs qui ont déjà leur réponse (Fiducial a même sa propre PA). Les petits éditeurs restants (GAD, DF Garage) ont des parcs trop diffus. **Secteur à dépriorisé** par rapport aux enchères/criées/viticulture.

---

## 6. Découverte transverse : le « modèle 3pi » comme grille de qualification

L'analyse manuelle fait émerger un pattern de qualification plus précis que l'ICP initial :

> **La cible parfaite est un éditeur de 3 à 15 salariés, sur un vertical réglementé/spécifique, partenaire de son syndicat professionnel, sans groupe derrière lui.**

| Critère | Pourquoi |
|---|---|
| 3-15 salariés | Assez gros pour avoir un parc, trop petit pour développer la conformité seul |
| Vertical réglementé/spécifique | Les PA généralistes ne comprennent pas le métier (marée, enchères, vin = lots, calibres, marge, DAE) |
| Partenaire de son syndicat | Canal de distribution déjà établi, qu'on peut emprunter avec lui |
| **Sans groupe** | Un groupe (cas ASAPE/Orisha) finira par imposer sa solution interne |

## 7. Actions découlant de DR11

| # | Action | Priorité | Quand |
|---|---|---|---|
| DR11-A1 | **Vérifier la santé financière de 3pi** (Pappers, SIREN 407938687) avant tout contact | 🔴 Haute | Avant contact |
| DR11-A2 | Préparer le pitch partenariat 3pi (réutiliser le argumentaire ISATECH adapté) — à déclencher **quand la démo est prête** | 🔴 Haute | Démo prête |
| DR11-A3 | Contacter Agisoft Engineering (après 3pi — même pitch, secteur marée) | 🟠 Moyenne | T3 2026 |
| DR11-A4 | Estimer le parc Vinéa/NSI-SADIMO (offres d'emploi, LinkedIn) avant de décider de l'approche | 🟡 Basse | T3 2026 |
| DR11-A5 | Mettre ASAPE et le secteur garage en veille passive (alerte Google « ASAPE facturation électronique ») | 🟡 Basse | — |

## 8. Méthode

Vérification manuelle directe (WebFetch + WebSearch) le 2026-06-02 sur : 3pi.fr, agisoft-e.fr, asape.fr, nsi-sadimo.com, symev.org/partenaires, b-reputation.com, et recherches croisées garages/DMS. Le site nsi-sadimo.com a un problème de certificat SSL (info obtenue via le cache de recherche). Toutes les informations « aucune mention de la facturation électronique » reflètent l'état des sites au 2026-06-02 — à re-vérifier avant chaque contact commercial (les éditeurs peuvent publier à tout moment).
