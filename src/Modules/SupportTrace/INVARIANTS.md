# Invariants — module `SupportTrace` (FX06)

- **INV-SUPPORTTRACE-001** — La trace de support est **distincte** de la piste d'audit append-only
  (`documents.document_events`) et de l'archive probante (coffre WORM). Le module ne référence NI le module
  Documents NI le module Archive — vérifié par `SupportTraceBoundaryTests` (NetArchTest). La purge ne peut
  donc pas, par construction, altérer l'audit ni l'archive (CLAUDE.md n°4).
- **INV-SUPPORTTRACE-002** — Toute entrée est **tenant-scopée** par construction (un répertoire par tenant) :
  une opération d'un tenant ne désigne, ne lit ni ne purge jamais l'entrée d'un autre (CLAUDE.md n°9).
- **INV-SUPPORTTRACE-003** — Les octets du Factur-X sont **chiffrés au repos** via ASP.NET Core Data
  Protection, avec un protecteur dérivé par tenant (isolation cryptographique inter-tenants — CLAUDE.md n°10).
- **INV-SUPPORTTRACE-004** — La **rétention** est un paramétrage (proposition 90 jours, F16 §10), jamais un
  seuil fiscal en dur (CLAUDE.md n°2). Une rétention non positive est refusée (ne purge jamais tout par erreur).
- **INV-SUPPORTTRACE-005** — La purge ne supprime **que ce que le store a écrit** : seuls les répertoires-jour
  au nom conforme (`yyyy-MM-dd`) et strictement antérieurs à la borne sont supprimés.
