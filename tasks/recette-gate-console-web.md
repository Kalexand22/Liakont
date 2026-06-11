# Recette manuelle — GATE_CONSOLE_WEB

Légende : ✅ passé · ❌ KO (finding) · ☐ non déroulé · 🔁 à re-tester

---

## Historique

- **Run 1 (2026-06-10/11)** : gate ❌ refusée — 19 findings (6 P1 « provisioning jamais câblé »), arrêt à
  « Prêt à envoyer ». → **Lot FIX** (13 items, manifest v18, PR #35) : 13/13 done.
- **Run 2 (2026-06-11, env vierge)** : gros progrès (zéro SQL : seed officiel 15/15, CHECK direct, table
  TVA créée dans l'UI, recheck audité, plug-in Fake résolu) mais l'envoi restait verrouillé par la
  publication SIREN/PA (P1) — 22 findings (1 P1 + 21 P2). → **Lot FIX2** (13 items, manifest v19,
  PR #36) : 13/13 done.
- **Run 3 (2026-06-11, env vierge)** : ✅ **VALIDÉ** — voir verdict ci-dessous.

## RUN 3 — Résultats

### Pré-requis / bring-up (FIX203a/b) — ✅ TOUT

1. ✅ Environnement vierge (liakont-pg recréé :5544, realm réimporté).
2. ✅ `dotnet run` TOUT COURT suffit (launchSettings versionné) — :55996, Development, zéro variable à poser.
3. ✅ Au boot : profil + fiscal + comptes PA + table TVA importés, compte PA Fake **publié** (FIX201),
   planifications supervision + ancrage amorcées, AdminSeed sans crash.
4. ✅ Connexion `sysadmin`/Test@1234 (compte + rôle stratum-admin livrés par le realm committé).

### Parcours nominal (le juge de paix) — ✅ COMPLET, premier envoi de bout en bout des 3 runs

1. ✅ Agent enregistré → seed officiel 15/15 Accepted.
2. ✅ Classement : 13 bloqués pour motifs légitimes (garde-fou B2C/B2B), table seedée non validée.
3. ✅ Table TVA : règles 20/10/5.5/0 créées via le rapport de couverture, contrôle de cohérence (FIX03)
   détecte 3 règles mortes du seed enchères → table validée.
4. ✅ « Revérifier tout » fonctionnel en masse (mais voir FIX207 ❌ partiel ci-dessous).
5. ✅ Verdict B2C/B2B [Confirmer particulier] → renvoi → Émis (S3.4).
6. ✅ **[Envoyer] → 2 documents Issued + 2 paquets d'archive WORM** ; run-log « SEND : 2 émis, 0 en échec » (FIX202).
7. ✅ Export contrôle fiscal d'un document émis : zip téléchargé OK.

### Validation des FIX2

- ✅ FIX201 publication PA (auto au boot + envoi passé) · ✅ FIX202 bandeau résultat réel ·
  ✅ FIX203a/b bring-up · ✅ FIX204 table TVA + cohérence · ✅ FIX209 nav assainie ·
  ✅ FIX210 alertes éditables (contact enregistré) · ✅ FIX212 matrice de routage (entrée critique enregistrée).
- ❌ FIX207 **partiel** : le recheck de masse fonctionne, mais « Revérifier la sélection » ABSENT de la
  barre, « Revérifier tout » mal placé, compteur « 0 au total » — P2 consignés.
- ☐ FIX205 (onglet Contenu), FIX206 (refresh/exports en détail), FIX208 (exports archive),
  FIX211 (écran Planifications en sysadmin) : non déroulés explicitement au run 3.

### Reliquat non déroulé (assumé, ne bloque pas la gate)

- ☐ S6 grille/filtres/exports en détail · ☐ S7 passe langue dédiée (aucun anglais RENCONTRÉ au fil du
  parcours) · ☐ S8 robustesse (guid bidon, double-clic, session expirée) · ☐ S4.6/7 rotation/révocation
  clé agent · ☐ S4.8 intégrité archive · ☐ S2 re-passage lecture seule.

### Findings run 3 — 0 P1, 10 P2 (tous au bug-inbox, commits 842cc04..79e18a5)

Paramètres fiscaux non éditables · barre d'action flottante · section AUDIT · seeds incohérents ·
« Revérifier la sélection » absent + placement + compteur · page Policies vide à masquer ·
GUID opérateur au lieu du nom · **layout global : scroll au-delà du bas de page + menu cassé** ·
**chapeau backlog : grande passe d'ergonomie globale post-gate** (décision Karl).

## Verdict gate — run 3

- ✅ Envoi de bout en bout exercé : ReadyToSend → Sending → **Issued** + paquet d'archive + export.
- ✅ Aucun P1 nouveau ; P2 consignés dans `bug-inbox.jsonl`.
- ✅ Décision opérateur (Karl) : « c'est bon globalement » — l'ergonomie fera l'objet d'une passe
  dédiée post-gate (lot UX/polish à structurer depuis les P2 ouverts du bug-inbox).
- **→ GATE_CONSOLE_WEB ✅ VALIDÉE — action humaine : merger la PR #38** (puis state.yaml :
  GATE_CONSOLE_WEB → done, ce qui déverrouille le segment toolkit OPS/BRD/DOC).
