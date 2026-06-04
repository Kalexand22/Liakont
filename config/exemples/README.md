# config/exemples — paramétrage d'EXEMPLE (fictif)

Ce dossier contient des exemples **fictifs** de paramétrage, utilisés par les tests et les
démos hors-ligne du produit. Il ne contient **jamais** de donnée d'un client réel.

## Règle (blueprint.md §2, CLAUDE.md n°7)

Le code Liakont est **générique**. Toute donnée propre à un client final — table TVA validée
par son expert-comptable, SIREN, raison sociale, chaîne ODBC, compte(s) Plateforme Agréée —
est du **paramétrage de tenant** :

- soit en base (édité depuis la console web, journalisé, revalidation requise) ;
- soit en **seed versionné** d'un déploiement, dans [`deployments/<client>/`](../../deployments/README.md).

Ici, on ne place que des **codes fictifs** (régimes, taux, SIREN d'exemple) servant à exercer
le moteur sans exposer de donnée réelle. Un SIREN réel, une table TVA réelle ou un compte PA
dans ce dossier (ou dans `src/`) est un **P1** en review (CLAUDE.md n°15).

## Contenu attendu (au fil des lots)

- Table TVA d'exemple (codes régime → catégorie/taux/VATEX fictifs) — lot TVA :
  [`mapping-exemple.json`](mapping-exemple.json) (livré par TVA04, marqué « NON VALIDÉE »).
- Profil tenant d'exemple (SIREN fictif, régime) — lot CFG.
- Paramétrage PA d'exemple (compte fictif de la PA Fake) — lots PA.

Le squelette (SOL02) ne crée que ce README ; les exemples concrets arrivent avec leurs lots.
