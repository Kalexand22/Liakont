# Aiguillage B2B/B2C document-driven + fin du « V2 B2B » (session autonome 2026-06-22)

> Principe Karl : **« Jamais une capacité d'une PA n'impacte le FLUX. »** Le flux (classer B2B/B2C,
> valider, produire l'artefact) est piloté par le DOCUMENT, PA-agnostique. La capacité PA ne joue
> qu'au **bord de transmission** (garde fail-closed / résultat typé), jamais une re-classification.
> Gabarit existant à répliquer = la garde 389 self-billed dans `DocumentCheckEvaluator`.
>
> Contrainte dure (n°2) : NE PAS inventer de schéma PA. Le schéma de fil B2C de SuperPDP n'est pas
> dispo (« à mapper », sandbox) → on ne peut PAS faire transmettre du B2C par SuperPDP maintenant.

## Fait dans cette session
- [x] Phase A — effacer le « phase 2 / V2 B2B » des commentaires code (B2Brouter caps/builder/contrat/
      DTO/test ; SuperPDP builder/testdata/sendtests « différé B2B (phase 2) ») + `Fake.SupportsB2bInvoicing = true`.
- [x] Phase B — garde de transmission **document-driven** (additive, gabarit 389), CHECK + 3 sites SEND :
      un document à **destinataire identifié** (acheteur avec SIREN — B2B ou B2G) n'est émissible que si la PA
      active offre un **canal** : routage PDP B2B (`SupportsB2bInvoicing`) **OU** transport du Factur-X
      (`SupportsFacturXTransmission` — Generique, Chorus Pro). Sinon bloqué/maintenu (corrige le bug latent :
      un doc à destinataire identifié ne part plus dégradé en e-reporting B2C anonyme via une PA B2C-only, ex.
      B2Brouter). Capacité au bord, jamais dans le flux.
- [x] Vérifier — verify-fast vert ; run-tests vert ; Release (StyleCop) vert ; revue adversariale (3 angles).

## Laissé à Karl (décision fiscale — PAS touché en autonomie, trop incertain pour « sans erreur »)
- Le côté B2C de l'aiguillage : un doc B2C = e-reporting (10.3) OU une facture Factur-X au particulier ?
  La capacité `SupportsB2cReporting` de SuperPDP est déclarée `true` mais le plug-in REJETTE le B2C
  (pas de payload — « schéma de fil à mapper »). Rendre ce flag honnête (false) OU implémenter le B2C
  SuperPDP (besoin du schéma sandbox) = décision Karl. Ne pas flipper un flag fiscal à l'aveugle.
- Implémenter la transmission B2C SuperPDP → nécessite de sourcer son schéma de fil 10.3 en sandbox
  (comme le B2B / facture 72272).

## Review (fin de session)

Revue adversariale (3 angles) — **1 P1 RÉEL trouvé et corrigé avant commit** :
- **P1 — sur-blocage des PA de transport Factur-X.** Le discriminant initial (« acheteur a un SIREN ⇒ exige
  `SupportsB2bInvoicing` ») bloquait le B2G via Chorus Pro (le but de la branche `feat/pa-chorus-pro`) ET le
  B2B via Generique (Factur-X), car ces PA déclarent `SupportsFacturXTransmission=true` /
  `SupportsB2bInvoicing=false`. **Correctif** : la garde exige désormais `SupportsB2bInvoicing` **OU**
  `SupportsFacturXTransmission` (CHECK + 3 sites SEND + message + log). Test de régression ajouté
  (PA Factur-X-only → non maintenu) qui aurait échoué sans le correctif. run-tests vert après correctif.
- P2 — message opérateur recadré (les deux canaux) ; 3e commentaire « phase 2 B2B » oublié (SuperPdpClientSendTests) corrigé.
- nit — parenthèse DTO : faux positif (équilibrée), non touché.
- Trou de test connu (accepté, parité avec le 389) : la garde CHECK n'est pas testée en intégration (le harnais
  Check n'enregistre pas d'IPaClientRegistry → garde no-op) ; le filet SEND l'est (3 tests).
