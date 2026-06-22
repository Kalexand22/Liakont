# Exemple FICTIF de seed de tenant (lot CFG)

Exemple **fictif** de paramétrage de tenant au format attendu par l'import `ImportTenantSeed`
(module `TenantSettings`, F12-A §8). Sert aux tests et aux démos — **aucune donnée client réelle**
(voir [`../README.md`](../README.md) et CLAUDE.md n°7).

Un **vrai** déploiement vit dans [`deployments/<client>/`](../../../deployments/README.md), jamais ici.

## Fichiers

- `tenant-profile.json` — profil (SIREN **fictif** `123456782`), paramétrage fiscal, planification
  et seuils. Les paramètres fiscaux sont volontairement `null` (= décision de l'expert-comptable
  en attente = suspension ; jamais de valeur devinée — F12-A §3.1).
- `pa-accounts.json` — deux comptes **fictifs** : la PA `Fake` (clé API **placeholder** `${...}`)
  et un compte **`ChorusPro`** en environnement `Staging` (qualif). L'import **n'écrit jamais** de
  secret en clair — les secrets se saisissent ensuite depuis la console, chiffrés en base (F12-A
  §8.2, CLAUDE.md n°10). Pour `ChorusPro`, le champ `accountIdentifiers` ne porte que des valeurs
  **non sensibles** (`accountId`, `technicalLogin`, `connectionEmail`, `baseUrl`, `tokenEndpoint`
  *.piste.gouv.fr) ; les secrets OAuth2 PISTE (`client_id`/`client_secret`) et le mot de passe du
  compte technique restent vides jusqu'à leur saisie en console. Procédure de raccordement qualif :
  [`deployments/chorus-pro-raccordement-qualif.md`](../../../deployments/chorus-pro-raccordement-qualif.md).
- `mapping-tva.json` — table de mapping TVA **fictive**, **NON VALIDÉE** (marqueur `validatedBy`,
  `validatedDate: null`) et **générique** : elle couvre exactement les régimes des documents de démo
  (`tools/dev-seed-demo-docs.ps1` : 20 / 10 / 5.5 / 0) en part `Autre`, donc **0 règle morte** au
  contrôle de cohérence (FIX03) et **0 « régime absent »** au CHECK sur un environnement neuf (item
  FIX304). Chaque règle se trace vers F03 §2.1 (`20→S`, `10/5,5→AA`, `0→Z`) — aucune catégorie inventée.
  Importée par le même point d'entrée OPS03 (item FIX01b), **idempotente** (jamais d'écrasement d'une
  table déjà paramétrée). Reste « NON VALIDÉE » : le garde-fou d'envoi (PIP01) suspend les envois réels
  tant que l'expert-comptable ne l'a pas validée dans la console. Format complet (toutes catégories
  UNCL5305, flags) : [`../mapping-exemple.json`](../mapping-exemple.json).
- `encheres/` — **variante** de la table de mapping pour le **vertical enchères** (découpage
  adjudication / frais, régime de la marge). À n'utiliser **que** le vertical activé, sinon ses règles
  `Adjudication` / `Frais` sont mortes (FIX03). Détail : [`encheres/README.md`](encheres/README.md).

## Note sur `reportingFrequency`

Laissé `null` ici à dessein : son énumération exacte n'est pas figée (F12-A §3.3, point à trancher
avec l'expert-comptable). Le produit ne devine jamais une cadence.

## Points d'entrée de l'import (FIX01a)

L'import de ce format est câblé sur deux points d'entrée :

- **Développement** : `DevTenantSeeder` amorce le profil du tenant `default` au démarrage du Host
  (Development uniquement, section `DevTenantSeed` avec `CompanyId` + `SeedDirectoryPath`). Le
  `CompanyId` **doit** correspondre au claim `company_id` du realm de dev.
- **Production (OPS03)** : `POST /api/v1/admin/tenants/{tenantId}/seed` (rôle **SystemAdmin**), corps
  `{ "companyId": "<guid>", "seedDirectoryPath": "<dossier serveur>" }`. Le `companyId` est celui que
  l'IdP du tenant présentera (claim `company_id` de son realm).

Aucune clé API n'est jamais importée (placeholder ⇒ saisie ensuite via la console, chiffrée).

## Tant que le profil n'existe pas : traitement suspendu (CFG02)

Sans profil tenant, le CHECK ne peut pas mapper/valider : il **suspend** le document (il reste
`Detected`) — état rendu **explicite** dans la console (bandeau « Paramétrage incomplet — le traitement
des documents est suspendu », tableau de bord et Paramétrage). Ce blocage est **transitoire** : l'outbox
re-livre l'événement, donc créer le profil **avant** que l'agent ne pousse des documents suffit (cas
nominal — c'est ce que fait l'amorçage au démarrage / le provisioning). Pour des documents poussés
**avant** la création du profil et dont la fenêtre de re-livraison de l'outbox est épuisée (dead-letter),
le **rejeu** se fait en **re-poussant** le document depuis l'agent : l'extraction re-déclenche
l'ingestion puis le CHECK, qui aboutit cette fois (profil présent).
