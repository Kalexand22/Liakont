# Tâche : API02c — Endpoints d'action documents : résolution terminale

Session : orch-20260608-132856-Liakont-s2 (slot-2, clone Liakont)
Sous-branche : feat/console-web-API02c
Segment : feat/console-web

## Objet
Sur le scaffold d'actions Documents.Web posé par API02a, ajouter 2 résolutions terminales
(permission liakont.actions) :
1. POST /api/v1/documents/{id}/resolve-manually → motif OBLIGATOIRE (400 sans), transition
   ManuallyHandled (TRK02), journalisé (Audit + DocumentEvent, identité opérateur).
2. POST /api/v1/documents/{id}/supersede → lier un RejectedByPa à son remplaçant (déjà reçu).
   Ancien → Superseded avec lien. Refusé si remplaçant absent dans le tenant. Journalisé.

## Plan
- [x] Explorer le scaffold Documents.Web (API02a) + transitions domaine (ManuallyHandled/Superseded)
- [x] Domaine déjà présent (MarkManuallyHandled/Supersede) ; port d'écriture = IDocumentLifecycle (Contracts)
- [x] Implémenter les 2 endpoints sur le harness API01a + scaffold API02a (outcome enum, mapping 404/409/400)
- [x] Tests d'intégration (14) : droits (401/403), motif obligatoire (400), supersede sans/auto/inexistant remplaçant, 404 isolation, 409 mauvais état, transitions réelles + audit
- [x] verify-fast vert (plateforme .NET10 + agent net48)
- [x] run-tests vert (4029 tests, 14 nouveaux exécutés contre Postgres réel)
- [ ] codex-review propre (ou P2 acceptés justifiés)
- [ ] merge-back --no-ff + push (attention collision additive avec API02b en parallèle slot-1)

## Conception retenue
- Port d'écriture `IDocumentLifecycle` (Contracts-only, déjà consommé par le pipeline) étendu de 2 méthodes
  OPÉRATEUR retournant `DocumentResolutionOutcome` (Succeeded/DocumentNotFound/InvalidState/ReplacementNotFound)
  → mapping HTTP propre (4xx, pas 500 sur refus attendu), décision autoritaire sous verrou FOR UPDATE (pas de TOCTOU).
- Web : 2 endpoints (`resolve-manually`, `supersede`) sur le groupe `/documents`, permission `liakont.actions`,
  audit (IActivityLogger, UserId) + DocumentEvent append-only (via le port, identité opérateur). 204 NoContent au succès.
- Aucune logique métier dans le Web (validation de requête + mapping outcome uniquement) ; transition dans le domaine.

## Review (codex en cours)
