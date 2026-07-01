namespace Liakont.Modules.Ged.Infrastructure.Backfill;

using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Dérive une identité GED STABLE et REPRODUCTIBLE d'une entrée de coffre fiscal (GED10). Deux passages du backfill
/// sur la MÊME <c>archive_entry_id</c> produisent le MÊME <c>managed_document_id</c> → l'idempotence (RL-21) repose
/// sur l'<c>ON CONFLICT (id) DO NOTHING</c> + la garde de statut existants, SANS colonne d'unicité inventée sur
/// <c>managed_documents</c>. Empreinte SHA-256 d'un préfixe fixe + l'identité de l'entrée (pas un usage cryptographique
/// sensible : simple fonction déterministe ; version 8 « custom » RFC 9562).
/// </summary>
internal static class GedDeterministicId
{
    private const string BackfillNamespacePrefix = "ged.backfill.archive-entry:";

    public static Guid ForArchiveEntry(Guid archiveEntryId)
    {
        var input = Encoding.UTF8.GetBytes(BackfillNamespacePrefix + archiveEntryId.ToString("D"));
        var hash = SHA256.HashData(input);

        var bytes = new byte[16];
        Array.Copy(hash, 0, bytes, 0, 16);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x80); // version 8 (custom, RFC 9562)
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // variante RFC 4122
        return new Guid(bytes);
    }
}
