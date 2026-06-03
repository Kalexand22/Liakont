namespace Stratum.Modules.Notification.Infrastructure;

using System.Text.Json;
using Stratum.Modules.Notification.Domain.ValueObjects;

internal static class RoutingConditionSerializer
{
    public static string Serialize(IReadOnlyList<RoutingCondition> conditions)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream);
        writer.WriteStartArray();
        foreach (var condition in conditions)
        {
            WriteCondition(writer, condition);
        }

        writer.WriteEndArray();
        writer.Flush();
        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCondition(Utf8JsonWriter writer, RoutingCondition condition)
    {
        if (condition.IsCompound && condition.Children is not null)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(condition.LogicalOperator!);
            writer.WriteStartArray();
            foreach (var child in condition.Children)
            {
                WriteCondition(writer, child);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else if (condition.IsLeaf)
        {
            writer.WriteStartObject();
            writer.WriteString("field", condition.Field);
            writer.WriteString("op", condition.Operator);
            if (condition.Value.HasValue)
            {
                writer.WritePropertyName("value");
                condition.Value.Value.WriteTo(writer);
            }

            writer.WriteEndObject();
        }
    }
}
