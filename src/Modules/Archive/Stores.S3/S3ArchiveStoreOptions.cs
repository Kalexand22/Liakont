namespace Liakont.Modules.Archive.Stores.S3;

/// <summary>
/// Configuration du coffre sur backend S3-COMPATIBLE (ADR-0009). UN SEUL code couvre Amazon S3, MinIO,
/// OVH, Scaleway, Wasabi… : le endpoint, les identifiants et l'activation de l'Object Lock sont des
/// paramètres d'INSTANCE (jamais une donnée client en dur, jamais en clair dans le code — CLAUDE.md n°7,
/// n°10). Section : <c>Archive:Storage:S3</c>.
/// </summary>
public sealed class S3ArchiveStoreOptions
{
    /// <summary>Nom du bucket du coffre.</summary>
    public string BucketName { get; set; } = string.Empty;

    /// <summary>Endpoint S3-compatible (ex. <c>https://s3.gra.io.cloud.ovh.net</c>) ; vide = Amazon S3 par région.</summary>
    public string? ServiceUrl { get; set; }

    /// <summary>Région (AuthenticationRegion pour les backends S3-compatibles).</summary>
    public string? Region { get; set; }

    /// <summary>Style de chemin (requis par MinIO et la plupart des S3-compatibles).</summary>
    public bool ForcePathStyle { get; set; } = true;

    /// <summary>Clé d'accès (paramètre d'instance, fourni via un secret, jamais versionné).</summary>
    public string? AccessKeyId { get; set; }

    /// <summary>Clé secrète (paramètre d'instance, fourni via un secret, jamais versionné).</summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>Le backend offre l'Object Lock natif (mode conformité) : utilisé EN PLUS de la chaîne de hashes.</summary>
    public bool SupportsObjectLock { get; set; }

    /// <summary>Le backend offre la rétention légale (legal hold).</summary>
    public bool SupportsLegalHold { get; set; }

    /// <summary>Durée de rétention de l'Object Lock, en années (conservation fiscale 10 ans — art. L.123-22).</summary>
    public int ObjectLockRetentionYears { get; set; } = 10;
}
