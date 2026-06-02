# Identification des éditeurs candidats (grappes 3-4-5)

> Rédigé le 2026-06-02. Méthodologie et critères pour constituer la liste des éditeurs à contacter lors de la campagne de signatures T4 2026 → T1 2027.
> Documents liés : `Offre-Editeur-Passerelle.md` (l'offre à leur faire), `DR10-Secteurs-Editeurs-Legacy.md`, `DR11-Analyse-Editeurs-Cibles.md` (premières cibles identifiées).

---

## 1. Objectif et entonnoir

**Livrable : 10-15 éditeurs qualifiés et priorisés, avec contact dirigeant, prêts à être appelés au T4 2026.**

| Étape de l'entonnoir | Volume attendu |
|---|---|
| Vivier brut (NAF 5829C filtré) | ~2 000-4 000 sociétés |
| Pré-filtrés (taille, ancienneté, activité) | ~200-400 |
| Qualifiés (verticaux + signaux legacy) | ~30-50 |
| **Shortlist contactable (le livrable)** | **10-15** |
| Signés (objectif campagne) | 3-4 |

**Charge estimée : 1-2 semaines de travail**, à réaliser au plus tard T3 2026.

**Pourquoi cet entonnoir large** : le taux de déchet est élevé à chaque étape (éditeur mort, racheté par un groupe, déjà équipé, stack moderne, parc trop petit). Il faut partir large pour finir avec 10-15 noms réellement appelables.

---

## 2. Les trois sources, par ordre d'efficacité

### Source 1 — Les annuaires de fédérations métier ⭐ (la plus rentable)

**Le principe** : chaque profession organisée a ses 3-5 éditeurs historiques, et la fédération les liste (annuaire des partenaires, exposants du congrès annuel, guides d'équipement). C'est exactement comme ça que SYMEV nous a donné ISATECH et 3pi.

| Secteur | Fédération / point d'entrée | Pourquoi c'est un bon terrain | Statut |
|---|---|---|---|
| **Enchères / SVV** | SYMEV | Notre niche d'origine | ✅ Fait (ISATECH, 3pi) |
| **Criées / mareyage** | UAPF, FFP (pêche) ; marchés aux poissons | Ventes au cadran = même logique métier que les enchères ; fort B2B + B2C | 🔶 Repéré (Agisoft/Agimarée) — à compléter |
| **Viticulture / caves coopératives** | CCVF (caves coopératives), Vignerons indépendants | Beaucoup de vente directe B2C (e-reporting) + export (10.1) ; éditeurs régionaux anciens | 🔶 Repéré (NSI-SADIMO/Vinéa) — à compléter |
| **Marchés de gros / MIN** | FFMIN, Semmaris (Rungis) | Carreau = vente au cadran/négoce ; logiciels de carreau très anciens | ⬜ À explorer |
| **Antiquaires / galeries / dépôts-ventes** | CNES, SNA, SNCAO | Adjacence parfaite avec les enchères (objets, marge, B2C) — adaptateur quasi réutilisable | ⬜ À explorer |
| **Funéraire** | CPFM, CSNAF, FFPF | Profession réglementée, éditeurs spécialisés anciens, clients PME partout en France | ⬜ À explorer |
| **Négoce agricole / coopératives** | La Coopération Agricole, FNA (négoce) | Logiciels de négoce/appro très legacy ; gros volumes de factures | ⬜ À explorer |
| **Horticulture / pépinières** | FNPHP, Val'hor | Vente B2B + B2C, éditeurs de niche | ⬜ À explorer |
| **Commerce de bestiaux / marchés aux bestiaux** | FMBV (marchés aux bestiaux vifs) | Vente au cadran, logiciels très anciens, niche totalement ignorée | ⬜ À explorer |
| **Récupération / matériaux / ferrailleurs** | Federec | Pesée + achat/revente, fort besoin de conformité, logiciels métier anciens | ⬜ À explorer |
| **Ambulances / taxis sanitaires** | CNSA, FNAP | Facturation CPAM + B2C, éditeurs de niche régionaux | ⬜ À explorer |
| **Auto-écoles** | CNPA/Mobilians, UNIC | B2C massif, petits éditeurs historiques | ⬜ À explorer (attention : beaucoup de SaaS modernes arrivés) |
| ❌ Garages / réparation auto | Mobilians | **Dépriorisé** : Fiducial (Vega) a sa propre PA ; secteur déjà servi | ❌ Écarté (DR11) |
| ❌ Pharmacies | — | Dominé par de gros éditeurs (LGPI, Winpharma) qui feront leur conformité eux-mêmes | ❌ Écarté |

**Méthode pratique pour chaque secteur** (~½ journée par secteur) :
1. Trouver l'annuaire des partenaires/fournisseurs de la fédération (ou la liste des exposants du dernier congrès/salon)
2. Repérer les éditeurs de logiciels dans la liste
3. Vérifier chaque éditeur avec la grille de qualification (§3)

### Source 2 — Pappers / societe.com par code NAF (le ratissage systématique)

**Requête de base** : NAF **5829C** (Édition de logiciels applicatifs), filtres :
- Effectif : **1 à 20 salariés**
- Date de création : **avant 2010** (> 15 ans → produit legacy probable)
- Statut : active (pas de procédure collective)
- Exclure : Île-de-France en premier passage (optionnel — les verticaux de niche sont souvent en région, près de leur métier d'origine)

**Codes NAF voisins à ratisser aussi** :
- **5829A** (Édition de logiciels système et de réseau) — certains verticaux y sont mal classés
- **6201Z** (Programmation informatique) — les très petits éditeurs se classent souvent ici
- **6202A** (Conseil en systèmes et logiciels) — les SSII régionales qui ont UN produit métier historique (le cas ISATECH !)

**Limite de cette source** : elle donne des noms, pas la verticalité ni la stack. Chaque nom doit passer par la qualification web (§3). C'est pour ça que la Source 1 est plus rentable : elle pré-qualifie la verticalité.

### Source 3 — Les signaux indirects (pour compléter / recouper)

- **Offres d'emploi** (Indeed, HelloWork, APEC) avec mots-clés : `Delphi`, `WinDev`, `Magic XPA`, `VB6`, `Access`, `Clipper`, `AS400`, `RPG`, `Powerbuilder` → l'employeur est un éditeur legacy par définition
- **Forums de développeurs** legacy (developpez.com sections Delphi/WinDev) : les signatures et profils mentionnent l'employeur
- **PC SOFT (éditeur de WinDev)** : références clients, témoignages, awards des « Trophées WinDev » → liste d'éditeurs WinDev par métier
- **Salons professionnels sectoriels** : listes d'exposants en ligne (souvent gratuites), catégorie « informatique/logiciels »

---

## 3. La grille de qualification (à appliquer à chaque candidat)

### Critères d'inclusion (score /10)

| # | Critère | Points | Comment vérifier |
|---|---|---|---|
| Q1 | **Taille 2-20 salariés** (patron = décideur, pas de R&D capable de faire seul) | /2 | Pappers, societe.com, LinkedIn |
| Q2 | **Logiciel vertical métier** (pas un généraliste compta/gestion) | /2 | Site web — le produit a un nom et cible UNE profession |
| Q3 | **Stack legacy confirmée ou probable** | /2 | Copyright site ≤ 2018, captures d'écran d'interfaces Windows grises, mentions « client lourd » / « installation », offres d'emploi Delphi/WinDev/Access/AS400, absence des mots API/cloud/SaaS |
| Q4 | **Parc estimé ≥ 30 clients** | /1 | Page « références », « ils nous font confiance », ancienneté du produit |
| Q5 | **Zéro communication sur la réforme 2026/2027** | /2 | Site, blog, actualités, LinkedIn de l'entreprise — rien sur facture électronique/PA/Factur-X |
| Q6 | **Métier avec du B2C ou de la marge** (e-reporting, régime de la marge = nos points forts différenciants) | /1 | Nature du métier servi |

**Seuil : ≥ 7/10 = candidat shortlist.**

### Critères d'exclusion immédiate (un seul suffit)

| ✗ | Disqualifiant | Pourquoi |
|---|---|---|
| X1 | Appartient à un groupe (Orisha, Fiducial, Septeo, Cegid…) | Décision non locale, solutions groupe imposées (cas ASAPE → Orisha) |
| X2 | Communique déjà sur la réforme avec une solution nommée | Il a déjà choisi son partenaire ou développe |
| X3 | Stack moderne (SaaS web, API documentée) | Il fera sa conformité lui-même |
| X4 | Secteur déjà verrouillé par un acteur avec PA | Cas garages/Fiducial |
| X5 | Procédure collective en cours | Sauf cas particulier type ISATECH où on a un intérêt spécifique |
| X6 | Parc < 15-20 clients | Le point mort de l'adaptateur (8-12 sites à 90 % de pénétration) n'est pas atteignable confortablement |

---

## 4. Le format du livrable : la fiche candidat

Une fiche par éditeur shortlisté (tableau ou une page), prête à servir de brief d'appel :

```
## [Nom de l'éditeur] — [Nom du produit]

| Champ | Valeur |
|---|---|
| Secteur / métier servi | … |
| Fédération / annuaire source | … |
| Localisation, SIREN | … |
| Effectif, date de création, santé financière (Pappers) | … |
| Stack technique (signaux relevés) | … |
| Parc estimé | … clients |
| Communication réforme | Aucune au [date de vérification] |
| Score qualification | …/10 |
| Vocabulaire métier (comment ils appellent leurs clients) | études / criées / caves / … |
| Dirigeant + contact (LinkedIn, tél standard, email) | … |
| Angle d'approche suggéré | … (référence CMP/3pi si métier proche, sinon angle sectoriel) |
| Estimation revenu grappe (parc × 90 % × ~210 €/an net) | ~… k€/an |
```

---

## 5. Priorisation de la shortlist finale

Ordre de priorité pour la campagne d'appels :

1. **Adjacence métier** (adaptateur réutilisable) : enchères > dépôts-ventes/antiquaires > criées > marchés au cadran > tout le reste. Moins l'adaptateur coûte cher, plus vite la grappe est rentable.
2. **Taille du parc** : un parc de 80 vaut deux parcs de 40 (un seul adaptateur, une seule formation, une seule relation).
3. **Intensité B2C du métier** : plus il y a de B2C, plus notre avantage e-reporting (et le coût Super PDP « 1 facture/jour ») écrase toute alternative.
4. **Urgence du secteur** : les métiers où les clients finaux sont des PME (échéance sept. 2027) — c'est-à-dire quasiment tous nos verticaux.

---

## 6. Planning et intégration au rétroplanning de campagne

| Quand | Quoi | Charge |
|---|---|---|
| **T3 2026 — semaine 1** | Source 1 : ratisser 8-10 annuaires de fédérations (½ j par secteur) | ~4-5 j |
| **T3 2026 — semaine 2** | Source 2 : extraction Pappers NAF 5829C/6201Z/6202A + croisement ; Source 3 : recoupements | ~2-3 j |
| **T3 2026 — fin** | Qualification web des ~30-50 candidats + scoring + fiches des 10-15 retenus | ~3-4 j |
| **T4 2026** | Enrichissement contacts (dirigeants) + déclenchement de la campagne (cf. `Offre-Editeur-Passerelle.md` §4) | ~1-2 j |

> **Note** : une partie de ce travail (ratissage des annuaires, qualification web) peut être accélérée par deep research / agents — en gardant la vérification manuelle pour les critères décisifs (Q5 communication réforme, X1 appartenance à un groupe), comme pour les DR précédentes.

---

## 7. Ce que cette liste n'est PAS

- **Pas un fichier de prospection de clients finaux** : on ne démarche jamais les clients d'un éditeur en direct (ça tuerait la relation de partenariat avant qu'elle commence).
- **Pas une étude de marché** : pas besoin de chiffrer chaque secteur précisément — il faut juste assez de candidats qualifiés pour en signer 3-4.
- **Pas un document figé** : chaque vérification « zéro communication réforme » est périssable (un éditeur peut annoncer sa solution n'importe quand). Re-vérifier le critère Q5 **la veille de chaque appel**.
