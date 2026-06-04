namespace Liakont.Modules.Ingestion.Contracts;

/// <summary>
/// Référence d'un PDF déposé dans le POOL de réconciliation d'un tenant (F06/TRK07). Le pool est
/// énuméré par le module Reconciliation pour rapprocher chaque PDF non lié d'un document émis
/// (ADR-0008 : « le système de fichiers EST le registre du pool »).
/// </summary>
/// <param name="PoolPdfId">
/// Identifiant STABLE du dépôt dans le pool du tenant (le nom de fichier unique sous <c>pool/</c>,
/// de la forme <c>{guid}__{nom}</c>). Sert de clé de suivi de réconciliation (file d'attente) et
/// d'argument d'ouverture du flux.
/// </param>
/// <param name="FileName">
/// Nom de fichier lisible (la partie après le préfixe d'unicité <c>{guid}__</c>), utilisé par la
/// stratégie de rapprochement « numéro de document dans le nom de fichier ».
/// </param>
public sealed record PooledPdfReference(string PoolPdfId, string FileName);
