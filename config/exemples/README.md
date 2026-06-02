# config/exemples/ — Exemples de configuration (FICTIFS)

Ce dossier contient des **exemples de configuration fictifs**, versionnés avec le produit, qui
servent de modèle et de support aux tests/démos. Il illustre la *forme* attendue des fichiers de
paramétrage, jamais des données réelles d'un client.

## Règle absolue

> **Aucune donnée client dans le code ni dans `config/exemples/`** (CLAUDE.md règle 7).
> Tout code SIREN réel, toute table TVA validée, toute chaîne ODBC, tout compte PA est du
> **paramétrage** et vit dans [`deployments/<client>/`](../../deployments/README.md), jamais ici.

Les exemples utilisent donc des valeurs **fictives** :

- codes régime et codes TVA inventés (jamais la table réelle d'un client) ;
- SIREN/SIRET d'exemple non attribués ;
- chaînes de connexion factices (`Driver=...;Server=exemple;...`) ;
- aucun secret en clair — les clés API PA et identifiants SMTP sont chiffrés par DPAPI et
  ne figurent jamais, même fictifs, sous forme exploitable (CLAUDE.md règle 10).

## Contenu (à venir avec les lots concernés)

| Fichier (exemple) | Produit par | Rôle |
|---|---|---|
| `gateway.config.exemple.json` | CFG | Forme de `GatewayConfig` (chemins, planification, PA actif) |
| `mapping-tva.exemple.json`    | TVA | Forme de la table de mapping TVA (codes **fictifs**) |
| `fixtures/…`                  | ADP | Jeux de rejeu EncheresV6 anonymisés pour démo/tests |

Au stade du socle (SOL01), seul ce README existe : il fixe la convention pour les lots suivants.
