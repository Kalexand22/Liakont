namespace Liakont.Modules.FacturX.Tests.Integration;

using System.IO;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Images;
using Xunit;

/// <summary>
/// Conteneurs de validation de conformité partagés par la collection (ADR-0023 §4, INV-FX-5) :
/// <list type="bullet">
///   <item><b>veraPDF</b> (<c>verapdf/cli</c>) — conformité PDF/A-3b du Factur-X scellé ;</item>
///   <item><b>Mustangproject</b> (image construite depuis <c>docker/mustang/Dockerfile</c>, jar CLI figé
///   sur une JRE) — conformité Factur-X / Schematron EN 16931 du PDF (XML CII embarqué + conteneur).</item>
/// </list>
/// Les deux images embarquent leur runtime (aucun Java sur le runner). L'ENTRYPOINT « one-shot » de
/// chaque image est remplacé par <c>sleep infinity</c> pour garder le conteneur vivant et l'interroger
/// via <see cref="IContainer.ExecAsync(System.Collections.Generic.IList{string}, System.Threading.CancellationToken)"/>.
/// Démarrés une fois pour toute la collection (coût Docker amorti). NB : les tests exigent un démon Docker
/// — comme toutes les suites d'intégration Testcontainers du dépôt.
/// </summary>
public sealed class FacturXValidationContainers : IAsyncLifetime
{
    private const string VeraPdfImage = "verapdf/cli:v1.30.2";
    private const string VeraPdfBinary = "/opt/verapdf/verapdf";
    private const string MustangImageName = "liakont/mustang-cli:2.24.0";
    private const string MustangJar = "/opt/mustang/mustang-cli.jar";

    private readonly IContainer _veraPdf;
    private readonly IContainer _mustang;
    private readonly IFutureDockerImage _mustangImage;

    public FacturXValidationContainers()
    {
        _mustangImage = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(MustangDockerfileDirectory())
            .WithDockerfile("Dockerfile")
            .WithName(MustangImageName)
            .WithCleanUp(false)
            .Build();

        _veraPdf = KeepAlive(new ContainerBuilder().WithImage(VeraPdfImage)).Build();
        _mustang = KeepAlive(new ContainerBuilder().WithImage(_mustangImage)).Build();
    }

    /// <summary>Construit l'image Mustang puis démarre les deux conteneurs de validation.</summary>
    public async Task InitializeAsync()
    {
        await _mustangImage.CreateAsync();
        await _veraPdf.StartAsync();
        await _mustang.StartAsync();
    }

    /// <summary>Arrête et libère les conteneurs (l'image Mustang est conservée pour réutilisation).</summary>
    public async Task DisposeAsync()
    {
        await _mustang.DisposeAsync();
        await _veraPdf.DisposeAsync();
    }

    /// <summary>
    /// Valide la conformité <b>PDF/A-3b</b> du fichier avec veraPDF. veraPDF rend : 0 = conforme,
    /// 1 = NON conforme, ≥ 2 = erreur d'exécution (à distinguer d'un verdict — anti faux-vert).
    /// </summary>
    public async Task<ExecResult> ValidatePdfA3bAsync(byte[] pdf, string name)
    {
        var path = $"/tmp/{name}.pdf";
        await _veraPdf.CopyAsync(pdf, path);
        return await _veraPdf.ExecAsync(new[] { VeraPdfBinary, "--flavour", "3b", "--format", "xml", path });
    }

    /// <summary>
    /// Valide la conformité <b>Factur-X / EN 16931</b> du fichier avec Mustangproject. Exit 0 = valide ;
    /// 255 = invalide OU erreur — le test croise donc l'exit code ET <c>&lt;summary status="valid"&gt;</c>
    /// (anti faux-vert : ne jamais conclure « valide » sur une sortie tronquée).
    /// </summary>
    public async Task<ExecResult> ValidateFacturXAsync(byte[] pdf, string name)
    {
        var path = $"/work/{name}.pdf";
        await _mustang.CopyAsync(pdf, path);
        return await _mustang.ExecAsync(new[] { "java", "-jar", MustangJar, "--action", "validate", "--source", path });
    }

    private static ContainerBuilder KeepAlive(ContainerBuilder builder) =>
        builder.WithEntrypoint("/bin/sh", "-c").WithCommand("sleep infinity").WithWorkingDirectory("/work");

    // Localise docker/mustang à côté de ce projet (src/Modules/FacturX/Tests.Integration), en remontant
    // depuis le répertoire d'exécution jusqu'à src/Liakont.sln — même stratégie que FacturXBoundaryTests.
    private static string MustangDockerfileDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "src", "Liakont.sln")))
            {
                return Path.Combine(
                    dir.FullName, "src", "Modules", "FacturX", "Tests.Integration", "docker", "mustang");
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Racine dépôt (src/Liakont.sln) introuvable depuis {AppContext.BaseDirectory}.");
    }
}
