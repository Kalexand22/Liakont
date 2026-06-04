-- Schéma du module Documents (F06, item TRK01). Vit dans la base DU TENANT (database-per-tenant,
-- blueprint §7) : l'isolation est ASSURÉE PAR LA CONNEXION (la connexion = le tenant), il n'y a donc
-- aucune colonne de tenant dans ce schéma. Le schéma porte le document métier, sa piste d'audit
-- append-only, les tax reports DGFiP et les entrées du coffre d'archive.
CREATE SCHEMA IF NOT EXISTS documents;
