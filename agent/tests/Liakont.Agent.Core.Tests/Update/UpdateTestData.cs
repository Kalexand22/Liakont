namespace Liakont.Agent.Core.Tests.Update;

using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

/// <summary>Fabriques de données de test pour l'auto-update : paquet ZIP, empreinte, manifeste JSON.</summary>
internal static class UpdateTestData
{
    public static byte[] MakeZipPackage()
    {
        using (var memory = new MemoryStream())
        {
            using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
            {
                ZipArchiveEntry entry = archive.CreateEntry("Liakont.Agent.dll");
                using (Stream entryStream = entry.Open())
                using (var writer = new StreamWriter(entryStream))
                {
                    writer.Write("binaire fictif de test — nouvelle version");
                }
            }

            return memory.ToArray();
        }
    }

    public static string Sha256Hex(byte[] content)
    {
        using (var sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(content);
            var builder = new StringBuilder(hash.Length * 2);
            foreach (byte b in hash)
            {
                builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }
    }

    public static byte[] ManifestBytes(string version, string packageUrl, string sha256Hex)
    {
        string json = "{\"version\":\"" + version + "\",\"packageUrl\":\"" + packageUrl + "\",\"packageSha256\":\"" + sha256Hex + "\"}";
        return Encoding.UTF8.GetBytes(json);
    }
}
