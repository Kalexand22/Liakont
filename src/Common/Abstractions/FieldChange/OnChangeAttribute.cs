namespace Stratum.Common.Abstractions.FieldChange;

/// <summary>
/// Decorates a method on an <see cref="IFieldChangeHandler{T}"/> to declare that
/// it reacts to changes on the specified field. The method must accept a
/// <see cref="FieldChangeContext{T}"/> and return <c>Task&lt;FieldChangeResult&gt;</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class OnChangeAttribute : Attribute
{
    public OnChangeAttribute(string fieldName)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name must not be null or empty.", nameof(fieldName));
        }

        FieldName = fieldName;
    }

    /// <summary>
    /// The name of the field whose changes trigger this handler method.
    /// Use <c>nameof()</c> for compile-time safety.
    /// </summary>
    public string FieldName { get; }
}
