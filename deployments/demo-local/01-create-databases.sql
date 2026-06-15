-- 01-create-databases.sql
-- Crée les 2 bases sources de démonstration + un login SQL DÉDIÉ EN LECTURE SEULE.
-- Données 100 % fictives (CLAUDE.md n°7). L'agent lit ces bases en lecture seule stricte (n°5).
-- Variable sqlcmd attendue : RoPassword (mot de passe du login, fourni AU RUNTIME par
-- seed-demo.ps1, JAMAIS versionné — CLAUDE.md n°10). Ne JAMAIS coder un mot de passe en dur ici.
-- Idempotent : ré-exécutable sans erreur (les bases/login existants sont réutilisés).
SET NOCOUNT ON;
GO

IF DB_ID('LiakontDemoErpA') IS NULL CREATE DATABASE LiakontDemoErpA;
GO
IF DB_ID('LiakontDemoErpB') IS NULL CREATE DATABASE LiakontDemoErpB;
GO

-- Login dédié, lecture seule. Auth SQL (mode mixte). CHECK_POLICY OFF : le mot de passe est
-- aléatoire fort (généré par le wrapper), on n'impose pas la politique Windows locale.
IF SUSER_ID('liakont_demo_ro') IS NULL
    CREATE LOGIN liakont_demo_ro WITH PASSWORD = '$(RoPassword)', CHECK_POLICY = OFF;
ELSE
    ALTER LOGIN liakont_demo_ro WITH PASSWORD = '$(RoPassword)';
GO

USE LiakontDemoErpA;
GO
IF USER_ID('liakont_demo_ro') IS NULL CREATE USER liakont_demo_ro FOR LOGIN liakont_demo_ro;
GO
ALTER ROLE db_datareader ADD MEMBER liakont_demo_ro;   -- SELECT uniquement (lecture seule stricte)
GO

USE LiakontDemoErpB;
GO
IF USER_ID('liakont_demo_ro') IS NULL CREATE USER liakont_demo_ro FOR LOGIN liakont_demo_ro;
GO
ALTER ROLE db_datareader ADD MEMBER liakont_demo_ro;
GO
