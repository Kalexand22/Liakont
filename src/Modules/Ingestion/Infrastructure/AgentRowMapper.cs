namespace Liakont.Modules.Ingestion.Infrastructure;

using Liakont.Modules.Ingestion.Domain.Entities;

/// <summary>Mapping partagé d'une ligne <c>ingestion.agents</c> vers l'entité <see cref="Agent"/>.</summary>
internal static class AgentRowMapper
{
    /// <summary>Liste de colonnes à sélectionner (ordre stable, jamais une étoile).</summary>
    public const string Columns =
        "id, tenant_id, name, key_prefix, key_hash, is_revoked, created_at, revoked_at, last_seen_at, last_agent_version";

    public static Agent Map(dynamic row)
    {
        return Agent.Reconstitute(
            (Guid)row.id,
            (string)row.tenant_id,
            (string)row.name,
            (string)row.key_prefix,
            (string)row.key_hash,
            (bool)row.is_revoked,
            IngestionRowReader.ToDateTimeOffset((object)row.created_at),
            IngestionRowReader.ToNullableDateTimeOffset((object?)row.revoked_at),
            IngestionRowReader.ToNullableDateTimeOffset((object?)row.last_seen_at),
            (string?)row.last_agent_version);
    }
}
