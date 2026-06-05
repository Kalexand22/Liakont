namespace Liakont.Agent.Updater.Tests;

using System.IO;

/// <summary>Remplacement de binaires simulé : trace les gestes, peut échouer à l'application.</summary>
internal sealed class FakeBinarySwapper : IBinarySwapper
{
    public bool Backed { get; private set; }

    public bool Applied { get; private set; }

    public bool Restored { get; private set; }

    public bool ThrowOnApply { get; set; }

    public void Backup(string installDirectory, string backupDirectory) => Backed = true;

    public void Apply(string stagingDirectory, string installDirectory)
    {
        if (ThrowOnApply)
        {
            throw new IOException("échec simulé du remplacement des binaires");
        }

        Applied = true;
    }

    public void Restore(string backupDirectory, string installDirectory) => Restored = true;
}
