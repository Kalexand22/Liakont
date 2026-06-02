# Lessons Learned

_Ce fichier est mis à jour après chaque correction utilisateur ou erreur d'orchestration.
Il sert de mémoire pour éviter de répéter les mêmes erreurs. Les agents le lisent au début
de chaque session (protocol.md, Step 1)._

---

## 2026-06-02 — Initialisation du projet

**Contexte :** Le système d'orchestration de Conformat reprend le principe éprouvé sur Stratum
(voir C:\Source\Stratum et C:\Source\stratum-orchestration). Leçons héritées de Stratum
applicables ici :

- **Ne jamais enchaîner un 2ème item après le premier** : un item par session, EXIT propre.
- **Les tests doivent être EXÉCUTÉS, pas seulement écrits** : un test jamais lancé est un faux vert.
- **La review doit porter sur l'arbre de travail courant** : toute modification après review
  invalide la review.
- **Les P2 ne sont jamais ignorés silencieusement** : fixés ou acceptés avec justification écrite.

**Spécifique Conformat :**

- **Ne jamais inventer une règle fiscale.** Si la spec (docs/conception/) ne tranche pas une
  question TVA/VATEX/arrondi, l'item passe en `blocked` avec le nom de la décision manquante
  et son propriétaire (expert-comptable CMP ou support B2Brouter). Deviner = risque fiscal client.
- **Les montants sont en decimal.** Tout float/double sur un montant est un P1, sans exception.
- **La piste d'audit est sacrée.** Aucun code ne doit pouvoir modifier ou supprimer un
  DocumentEvent, même pour « corriger » des données de test.
