# Exemple FICTIF de seed de tenant (lot CFG)

Exemple **fictif** de paramétrage de tenant au format attendu par l'import `ImportTenantSeed`
(module `TenantSettings`, F12-A §8). Sert aux tests et aux démos — **aucune donnée client réelle**
(voir [`../README.md`](../README.md) et CLAUDE.md n°7).

Un **vrai** déploiement vit dans [`deployments/<client>/`](../../../deployments/README.md), jamais ici.

## Fichiers

- `tenant-profile.json` — profil (SIREN **fictif** `123456782`), paramétrage fiscal, planification
  et seuils. Les paramètres fiscaux sont volontairement `null` (= décision de l'expert-comptable
  en attente = suspension ; jamais de valeur devinée — F12-A §3.1).
- `pa-accounts.json` — un compte de la PA fictive `Fake`. La clé API est un **placeholder**
  (`${...}`) : l'import **n'écrit jamais** de secret en clair ; la clé réelle se saisit ensuite
  depuis la console (chiffrée en base — F12-A §8.2, CLAUDE.md n°10).

## Note sur `reportingFrequency`

Laissé `null` ici à dessein : son énumération exacte n'est pas figée (F12-A §3.3, point à trancher
avec l'expert-comptable). Le produit ne devine jamais une cadence.
