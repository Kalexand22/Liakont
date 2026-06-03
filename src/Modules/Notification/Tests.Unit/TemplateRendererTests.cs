namespace Stratum.Modules.Notification.Tests.Unit;

using FluentAssertions;
using Stratum.Modules.Notification.Domain.Services;
using Xunit;

public class TemplateRendererTests
{
    [Fact]
    public void Render_Should_Replace_All_Placeholders()
    {
        var template = "Hello {{name}}, your order {{orderId}} is confirmed.";
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = "Alice",
            ["orderId"] = "12345",
        };

        var result = TemplateRenderer.Render(template, placeholders);

        result.Should().Be("Hello Alice, your order 12345 is confirmed.");
    }

    [Fact]
    public void Render_Should_Preserve_Missing_Placeholders()
    {
        var template = "Hello {{name}}, ref {{ref}}";
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = "Bob",
        };

        var result = TemplateRenderer.Render(template, placeholders);

        result.Should().Be("Hello Bob, ref {{ref}}");
    }

    [Fact]
    public void Render_Should_Handle_No_Placeholders()
    {
        var template = "Plain text without any placeholders.";
        var placeholders = new Dictionary<string, string>();

        var result = TemplateRenderer.Render(template, placeholders);

        result.Should().Be("Plain text without any placeholders.");
    }

    [Fact]
    public void Render_Should_Handle_Empty_Template()
    {
        var placeholders = new Dictionary<string, string> { ["key"] = "value" };

        var result = TemplateRenderer.Render(string.Empty, placeholders);

        result.Should().BeEmpty();
    }

    [Fact]
    public void Render_Should_Replace_Repeated_Placeholders()
    {
        var template = "{{name}} said hello to {{name}}.";
        var placeholders = new Dictionary<string, string>
        {
            ["name"] = "Charlie",
        };

        var result = TemplateRenderer.Render(template, placeholders);

        result.Should().Be("Charlie said hello to Charlie.");
    }

    [Fact]
    public void Render_Should_Handle_Adjacent_Placeholders()
    {
        var template = "{{first}}{{last}}";
        var placeholders = new Dictionary<string, string>
        {
            ["first"] = "John",
            ["last"] = "Doe",
        };

        var result = TemplateRenderer.Render(template, placeholders);

        result.Should().Be("JohnDoe");
    }
}
