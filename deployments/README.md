# deployments/ — Paramétrage par déploiement (données réelles d'UN client)

Conformat est un **produit générique** : le code (`src/`) ne connaît aucun client. Tout ce qui
est spécifique à une installation — une étude, un site, un client — est du **paramétrage** et vit
ici, dans un sous-dossier par déploiement :

```
deployments/
└─ <client>/                 (ex : deployments/cmp/)
   ├─ gateway.config.json    chemins, planification, PA actif, SMTP
   ├─ mapping-tva.json       table de mapping TVA VALIDÉE par l'expert-comptable du client
   ├─ secrets/               clé API PA et mot de passe SMTP, chiffrés DPAPI (machine scope)
   └─ fixtures/              (optionnel) jeux de rejeu propres au client
```

## Pourquoi cette frontière

> **Produit vs paramétrage** (tasks/lessons.md, 2026-06-02) : une donnée d'UN client (table TVA,
> SIREN, chaîne ODBC, compte PA) ne va **jamais** dans `src/` ni dans `config/exemples/`. Elle est
> du paramétrage et vit dans `deployments/<client>/`. Le comportement du produit est piloté par la
> configuration et par les **capacités déclarées** des plug-ins (`PaCapabilities`), jamais par un
> `if (pa is B2Brouter)` ni par un flag produit (CLAUDE.md règles 7 et 8).

## Règles

1. **Données réelles, donc sensibles.** Un dossier `deployments/<client>/` contenant des données
   fiscales réelles ne doit pas être publié dans un dépôt public. Le versionnement de ces dossiers
   relève de la procédure de déploiement du client, pas du dépôt produit.
2. **Secrets toujours chiffrés.** Clé API PA, identifiants SMTP : DPAPI uniquement, jamais en clair
   dans un fichier versionné ni dans un log (CLAUDE.md règle 10).
3. **Table TVA = décision de l'expert-comptable.** Le produit ne l'invente pas et ne la complète pas
   tout seul ; en cas de régime non mappé, il **bloque** le document et le comptable complète la
   table via la console (CLAUDE.md règles 2 et 3, blueprint.md §11).

Au stade du socle (SOL01), seul ce README existe : aucun déploiement réel n'est versionné ici.
