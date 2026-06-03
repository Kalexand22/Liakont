# deployments — paramétrage versionné PAR client (seed)

Ce dossier porte le **paramétrage versionné** de chaque déploiement, sous la forme de **seed**
à importer dans une instance/tenant. Un sous-dossier par client final :

```
deployments/
└─ <client>/        ex. deployments/cmp/ (table TVA CMP, SIREN, fixtures démo ISATECH)
```

## Pourquoi ici et pas dans le code (blueprint.md §2, CLAUDE.md n°7)

Liakont est un **produit générique** : le code n'embarque aucune donnée client. Le paramétrage
fiscal d'un tenant — table TVA validée par son expert-comptable, SIREN, comptes Plateforme
Agréée, planification — est :

1. **édité depuis la console web** (droit « paramétrage »), journalisé, avec revalidation ; et/ou
2. **versionné en seed** dans `deployments/<client>/` lorsqu'il doit être reproductible
   (rejouable à l'identique sur une nouvelle instance, audité, réversible par tenant).

À l'inverse, les exemples **fictifs** pour les tests et les démos vivent dans
[`config/exemples/`](../config/exemples/README.md).

## Règles

- Aucune **donnée client réelle** dans `src/` ni dans `config/exemples/` — uniquement ici
  (un SIREN réel, une table TVA réelle, une chaîne ODBC ou un compte PA hors de `deployments/`
  est un **P1** en review — CLAUDE.md n°15).
- Les **secrets** (clés API PA, identifiants) ne sont **jamais** en clair dans un fichier
  versionné : chiffrés en base (par tenant) ou injectés au déploiement (CLAUDE.md n°10).
- Le seed d'un client concret (`deployments/cmp/`) est produit par le lot CMP, pas par le socle.

Le squelette (SOL02) ne crée que ce README ; les seeds concrets arrivent avec leurs lots.
