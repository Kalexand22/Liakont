namespace Liakont.Host.Tests.Unit.Components;

using FluentAssertions;
using Liakont.Host.Components;
using Xunit;

/// <summary>
/// Restitution de l'acteur d'un événement de la piste d'audit (item FIX305) : le nom persisté prime, le GUID
/// retombe sur une mention « compte … » lisible (événement antérieur), le tout en infobulle. Fonction pure —
/// aucune base, aucune résolution différée (le nom vient de l'événement).
/// </summary>
public sealed class DocumentActorDisplayTests
{
    private const string Guid1 = "30da7398-1111-2222-3333-444455556666";

    [Fact]
    public void Label_Prefers_The_Persisted_Name_When_Present()
    {
        DocumentActorDisplay.Label("Marie Comptable", Guid1).Should().Be("Marie Comptable");
    }

    [Fact]
    public void Label_Trims_The_Persisted_Name()
    {
        DocumentActorDisplay.Label("  Marie Comptable  ", Guid1).Should().Be("Marie Comptable");
    }

    [Fact]
    public void Label_Falls_Back_To_A_Neutral_Account_Mention_For_A_Guid_Without_A_Name()
    {
        // Événement antérieur à FIX305 : aucun nom persisté → mention neutre abrégée, jamais le GUID brut entier.
        DocumentActorDisplay.Label(operatorName: null, operatorIdentity: Guid1).Should().Be("compte 30da7398…");
    }

    [Fact]
    public void Label_Returns_A_Non_Guid_Identity_As_Is()
    {
        // Une identité déjà lisible (nom legacy, « system ») n'est pas masquée derrière « compte … ».
        DocumentActorDisplay.Label(operatorName: null, operatorIdentity: "marie.compta").Should().Be("marie.compta");
        DocumentActorDisplay.Label(operatorName: null, operatorIdentity: "system").Should().Be("system");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Label_Is_Null_When_There_Is_No_Actor_At_All(string? blank)
    {
        DocumentActorDisplay.Label(operatorName: blank, operatorIdentity: blank).Should().BeNull();
    }

    [Fact]
    public void Tooltip_Exposes_The_Raw_Identity_As_Technical_Detail()
    {
        DocumentActorDisplay.Tooltip(Guid1).Should().Be(Guid1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Tooltip_Is_Null_When_There_Is_No_Identity(string? blank)
    {
        DocumentActorDisplay.Tooltip(blank).Should().BeNull();
    }
}
