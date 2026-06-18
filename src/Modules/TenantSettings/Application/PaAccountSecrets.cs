namespace Liakont.Modules.TenantSettings.Application;

using Liakont.Modules.TenantSettings.Domain.Entities;

/// <summary>
/// Secrets CHIFFRÉS d'un compte PA actif, pour résolution par un résolveur de plug-in côté Host (qui
/// déchiffre via <see cref="ISecretProtector"/>). Les valeurs <c>Encrypted*</c> sont des textes opaques —
/// JAMAIS le clair (CLAUDE.md n°10). C'est l'unique seam de LECTURE des secrets : à la différence du DTO
/// public <c>PaAccountDto</c> (qui n'expose que des booléens <c>Has*</c>), elle reste interne au module
/// TenantSettings et au Host — elle n'est JAMAIS consommée par Pipeline (frontière B1 du plan SuperPDP).
/// </summary>
public sealed record PaAccountSecrets(
    PaEnvironment Environment,
    string AccountIdentifiers,
    string? EncryptedApiKey,
    string? EncryptedClientId,
    string? EncryptedClientSecret);
