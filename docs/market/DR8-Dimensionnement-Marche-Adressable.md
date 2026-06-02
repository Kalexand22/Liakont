# DR8 — Dimensionnement du marché adressable non-API

> Deep research exécutée le 2026-06-02 (103 agents, 21 sources, 63 claims extraits, 25 vérifiés).
> Plan parent : `Plan-DR-Marche-Commercial.md`.
> ⚠️ Méthode : la vérification adversariale a subi des défaillances techniques (votes 0-0 = abstention, PAS réfutation). Les sections sont classées par niveau de fiabilité.

---

## Verdict synthétique

**Le chiffrage bottom-up par parc logiciel (AS400 + Sage + Navision + Windev/Delphi/Access…) n'est PAS réalisable de manière sourcée.** Aucune statistique primaire fiable n'existe sur le parc logiciel legacy français ventilé par capacité native EN 16931/API. Tous les chiffres en circulation proviennent de blogs d'intégrateurs ou sont mondiaux. **C'est le résultat le plus important de cette recherche** : toute affirmation du type « il y a X milliers d'entreprises sur AS400 en France » dans nos documents doit être traitée comme non sourcée.

**En revanche, le dimensionnement TOP-DOWN est solide** (sources primaires INSEE + OpinionWay) :

| Cohorte | Taille | Échéance | Solidité |
|---|---|---|---|
| **GE + ETI** (émission + e-reporting) | **~7 343 entreprises** (312 GE + 7 031 ETI) ; ~7 775 en périmètre élargi | 1er sept. 2026 | ✅ INSEE Focus 372 (3-0) |
| **PME + TPE + micro** (émission + e-reporting) | **~4,2 M d'entreprises** dont 4 042 715 micro (~96 % du tissu) | 1er sept. 2027 | ✅ INSEE Focus 372 (3-0) |
| **Toutes entreprises** (réception) | totalité du tissu (~4,2 M+) | 1er sept. 2026 | ✅ service-public.gouv.fr (3-0) |
| **Impréparation <250 salariés** (fév. 2026) | **65 % sans PA choisie ; 42 % n'en connaissent aucune ; >50 % non inscrites à l'annuaire** | — | ✅ OpinionWay/CNOEC 7e éd. (3-0 et 2-1) |

---

## 1. Constats CONFIRMÉS

1. **Cohorte émission 2026 (GE/ETI) : ~7 343 entreprises** (312 GE + 7 031 ETI, INSEE Focus 372, périmètre marchand non agricole/non financier 2023 ; ~7 775 en périmètre « ensemble »). Vote 3-0.
   - *Implication : le segment CMP-like (GE/ETI legacy, échéance immédiate) est petit en nombre mais à panier élevé et urgence maximale.*
2. **Cohorte émission 2027 : massivement des micro-entreprises** — 4 042 715 micro (~96 % du tissu marchand), plus la strate PME. Vote 3-0.
   - ⚠️ Le décompte « 163 992 PME hors micro » a été **réfuté 0-3** — ne pas l'utiliser sans re-vérification directe sur INSEE.
3. **Réception obligatoire pour TOUTES les entreprises dès le 1er sept. 2026** (toutes tailles). Vote 3-0.
   - *Implication directe pour DR16 (offre réception) : le besoin de raccordement touche ~4,2 M d'entreprises dès 2026, bien au-delà des 7 343 émettrices.*
4. **Impréparation (fév. 2026, <250 salariés)** : seules **35 %** ont choisi leur PA, **42 %** n'en connaissent aucune (vote 3-0) ; **plus d'une sur deux** non inscrite à l'annuaire national (vote 2-1).
   - ⚠️ Ces taux portent sur les <250 salariés (94 % <10 salariés) — **ne PAS les extrapoler aux GE/ETI**, généralement plus avancées.

## 2. Estimation du marché adressable (dérivation analytique — confidence LOW)

> Cette estimation mélange « absence de PA choisie » (mesuré) et « socle non-API » (non mesuré). À présenter comme ordre de grandeur du marché du raccordement, PAS comme décompte de parcs legacy.

- **Segment prioritaire 2026 (émission GE/ETI)** : borné par 7,3-7,8 k entreprises. La fraction sur socle non-API est indéterminée, mais même 5-10 % = 350-780 entreprises à urgence absolue et budget élevé.
- **Segment 2027 (émission PME/TPE)** : borne basse ≈ 42 % × ~0,9 M de PME/TPE structurées ≈ **plusieurs centaines de milliers d'entreprises sans PA connue** ; borne haute ≈ 65 % du tissu ≈ plusieurs millions (en incluant les micro).
- **La cible réelle de la Passerelle** (socle non-API + volume de facturation significatif + budget) est un sous-ensemble inconnu de ces bornes — vraisemblablement les « dizaines de milliers » du Cadrage, mais **ce chiffre reste une hypothèse de travail, pas un fait sourcé**.

## 3. Chiffres NON CONFIRMÉS (abstentions techniques) ou RÉFUTÉS — à ne pas citer comme établis

| Chiffre | Statut | Source d'origine |
|---|---|---|
| « 100 000 entreprises AS400 dans le monde, dont 3 000-4 000 en France (IBM) » | Non confirmé (1-0) — la partie « mondial ≠ français » est cohérente avec notre alerte | silicon.fr |
| « ~12 000 serveurs IBM i actifs en France en 2026 » | Non confirmé (0-0), source blog intégrateur | ocsigroup.fr |
| « Sage 100 = 43 000 clients France » | Non confirmé (0-0), source blog | chift.eu |
| « Sage 100/Sage 50 sans API native » | Non confirmé (0-0), source blog | chift.eu |
| « 163 992 PME hors micro en France » | **RÉFUTÉ (0-3)** | insee.fr (mauvaise lecture) |
| Quadient : « 1 % des entreprises 10+ salariés prêtes début 2025 », « 87 % utilisent des formats non conformes » | Non confirmé (0-0) | quadient.com |
| ECMA : « 38 % sans plan d'action » (avril 2026) | Non confirmé (0-0) — cohérent avec le baromètre déjà cité dans `L'opportunité` | compta-online.com |

## 4. Implications pour la suite

1. **Pour DR9 (pricing/CA)** : les scénarios doivent être construits sur les cohortes top-down (7,3 k GE/ETI + 4,2 M PME/TPE) × taux de pénétration prudents, PAS sur des parcs logiciels. La contrainte de capacité solo restera de toute façon le facteur limitant.
2. **Pour DR10 (secteurs/éditeurs)** : le dimensionnement par secteur devra passer par les éditeurs (taille de leur parc client) plutôt que par les statistiques de parc technologique — ce qui renforce la stratégie « grappes via éditeurs ».
3. **Pour le discours commercial** : remplacer « des dizaines de milliers d'entreprises sur socle non-API » par « 42 % des PME/TPE n'ont identifié aucune Plateforme Agréée à 18 mois de leur échéance » (sourcé, vérifié, plus percutant).
4. **Critère d'arrêt du plan : NON déclenché.** Le marché n'est pas démontré < 5 000 entreprises — il est simplement non mesurable bottom-up. Les cohortes top-down et l'impréparation mesurée justifient la poursuite.

## 5. Questions ouvertes

1. Existe-t-il une source primaire (IBM France, IDC, Markess, Numeum, DGFiP) chiffrant le parc installé par capacité API ? → à creuser via contact direct (pas researchable en ligne).
2. Combien de GE/ETI opèrent sur un socle non-API ? (le baromètre ne couvre que les <250 salariés)
3. Quelle part des 4,2 M d'entreprises a un volume de facturation justifiant une passerelle dédiée ?
4. Les baromètres Tiime/Payt/Quadient ventilent-ils par type de logiciel ? → si oui, ce serait LA donnée manquante.

## 6. Données techniques de la recherche

- **Stats** : 5 angles, 21 sources, 63 claims extraits, 25 vérifiés → 6 confirmés, 19 tués (majoritairement par abstention technique).
- **Sources primaires exploitées** : INSEE Focus 372 (statistiques 2023), opinion-way.com (baromètre 7e édition), entreprendre.service-public.gouv.fr, economie.gouv.fr.
- **Lacune structurelle identifiée** : aucun organisme ne publie de statistique « parc logiciel par capacité d'intégration » — cette donnée n'existe probablement pas publiquement.
