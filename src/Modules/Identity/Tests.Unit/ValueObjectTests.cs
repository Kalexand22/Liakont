namespace Stratum.Modules.Identity.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Identity.Domain.ValueObjects;
using Xunit;

/// <summary>
/// Unit tests for Identity value objects.
/// INV-IDENTITY-007: Username 3-50 chars, alphanumeric + underscores only.
/// EmailAddress: basic format validation.
/// </summary>
public sealed class ValueObjectTests
{
    // ── Username ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("abc")]
    [InlineData("Alice_123")]
    [InlineData("user_name_50_chars____________________long_name_ok")]
    public void UsernameFromShouldReturnInstanceForValidValue(string value)
    {
        var result = Username.From(value);

        result.Value.Should().Be(value);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("has space")]
    [InlineData("has-hyphen")]
    [InlineData("has@symbol")]
    [InlineData("this_username_is_way_too_long_more_than_fifty_chars_here")]
    public void UsernameFromShouldThrowInv007ForInvalidValue(string value)
    {
        var act = () => Username.From(value);

        act.Should().Throw<ArgumentException>().WithMessage("*INV-IDENTITY-007*");
    }

    [Fact]
    public void UsernameFromShouldThrowForNull()
    {
        var act = () => Username.From(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UsernameFromShouldThrowForEmpty()
    {
        var act = () => Username.From(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UsernameShouldSupportValueEquality()
    {
        Username.From("alice").Should().Be(Username.From("alice"));
        Username.From("alice").Should().NotBe(Username.From("bob"));
    }

    [Fact]
    public void UsernameToStringShouldReturnValue()
    {
        var username = Username.From("alice");

        username.ToString().Should().Be("alice");
    }

    // ── EmailAddress ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("first.last+tag@sub.domain.co.uk")]
    [InlineData("simple@test.org")]
    [InlineData("UPPER@EXAMPLE.COM")]
    public void EmailAddressFromShouldReturnInstanceForValidEmail(string email)
    {
        var result = EmailAddress.From(email);

        result.Value.Should().Be(email.ToLowerInvariant());
    }

    [Theory]
    [InlineData("notanemail")]
    [InlineData("@missing.local")]
    [InlineData("missing@")]
    [InlineData("no at sign")]
    public void EmailAddressFromShouldThrowForInvalidEmail(string email)
    {
        var act = () => EmailAddress.From(email);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EmailAddressFromShouldThrowForNull()
    {
        var act = () => EmailAddress.From(null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EmailAddressFromShouldThrowForEmpty()
    {
        var act = () => EmailAddress.From(string.Empty);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void EmailAddressShouldNormaliseToLowercase()
    {
        var result = EmailAddress.From("User@Example.COM");

        result.Value.Should().Be("user@example.com");
    }

    [Fact]
    public void EmailAddressShouldSupportValueEquality()
    {
        EmailAddress.From("user@example.com").Should().Be(EmailAddress.From("user@example.com"));
        EmailAddress.From("a@b.com").Should().NotBe(EmailAddress.From("c@d.com"));
    }

    [Fact]
    public void EmailAddressToStringShouldReturnValue()
    {
        var email = EmailAddress.From("user@example.com");

        email.ToString().Should().Be("user@example.com");
    }
}
