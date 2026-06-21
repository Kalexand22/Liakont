// Liakont addition (FIX211/FIX210 §4.20/§4.21 catalogue et executions de jobs) - not part of the original Stratum vendoring.
namespace Stratum.Modules.Job.Infrastructure.Services;

using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Stratum.Modules.Job.Contracts.Services;

// Liakont addition (FIX211) : catalogue en lecture seule des types de jobs enregistrés. Construit depuis les
// JobHandlerRegistration (mêmes singletons que JobHandlerResolver) : la liste reflète EXACTEMENT les
// IJobHandler<T> réellement câblés. Le libellé français vient de l'enregistrement (AddJobHandler) ; à défaut,
// repli humanisé sur le nom court du type — JAMAIS le FullName.
internal sealed class JobTypeCatalog : IJobTypeCatalog
{
    private readonly IReadOnlyList<JobTypeDescriptor> _descriptors;
    private readonly Dictionary<string, JobTypeDescriptor> _byKey;

    public JobTypeCatalog(IEnumerable<JobHandlerRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);

        var list = new List<JobTypeDescriptor>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var reg in registrations)
        {
            var key = reg.PayloadType.FullName ?? reg.PayloadType.Name;
            if (!seen.Add(key))
            {
                continue;
            }

            var label = string.IsNullOrWhiteSpace(reg.DisplayName)
                ? Humanize(reg.PayloadType.Name)
                : reg.DisplayName!;

            list.Add(new JobTypeDescriptor(key, label, DescribeParameters(reg.PayloadType)));
        }

        _descriptors = list
            .OrderBy(d => d.DisplayName, StringComparer.CurrentCulture)
            .ToList();
        _byKey = _descriptors.ToDictionary(d => d.TechnicalKey, StringComparer.Ordinal);
    }

    public IReadOnlyList<JobTypeDescriptor> GetAll() => _descriptors;

    public JobTypeDescriptor? Find(string technicalKey) =>
        technicalKey is not null && _byKey.TryGetValue(technicalKey, out var descriptor)
            ? descriptor
            : null;

    private static List<JobParameterDescriptor> DescribeParameters(Type payloadType)
    {
        // Le constructeur primaire d'un record positionnel porte les valeurs par défaut et les paramètres
        // « sans défaut » (= requis). On l'apparie aux propriétés publiques pour couvrir records positionnels
        // ET records à propriétés init.
        var ctorParams = payloadType.GetConstructors()
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault()
            ?.GetParameters() ?? [];

        var nullabilityContext = new NullabilityInfoContext();
        var result = new List<JobParameterDescriptor>();

        foreach (var prop in payloadType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || prop.GetIndexParameters().Length > 0)
            {
                continue;
            }

            // Les records exposent EqualityContract (protected — déjà hors binding Public) ; garde défensive.
            if (string.Equals(prop.Name, "EqualityContract", StringComparison.Ordinal))
            {
                continue;
            }

            var kind = ClassifyKind(prop.PropertyType, out var enumOptions);
            if (kind is null)
            {
                // Type complexe non pris en charge par un champ typé : ignoré (pas d'exception, pas de JSON brut
                // réintroduit). Les payloads des jobs réels n'utilisent que des primitives.
                continue;
            }

            var ctorParam = ctorParams.FirstOrDefault(p =>
                string.Equals(p.Name, prop.Name, StringComparison.OrdinalIgnoreCase));

            var required = IsRequired(prop, ctorParam, nullabilityContext);

            string? defaultValue = null;
            if (ctorParam is { HasDefaultValue: true } && ctorParam.DefaultValue is not null)
            {
                defaultValue = kind == JobParameterKind.Boolean
                    ? (ctorParam.DefaultValue is true ? "true" : "false")
                    : Convert.ToString(ctorParam.DefaultValue, CultureInfo.InvariantCulture);
            }

            result.Add(new JobParameterDescriptor(
                prop.Name,
                Humanize(prop.Name),
                kind.Value,
                required,
                defaultValue,
                enumOptions));
        }

        return result;
    }

    private static bool IsRequired(PropertyInfo prop, ParameterInfo? ctorParam, NullabilityInfoContext nullabilityContext)
    {
        if (prop.GetCustomAttribute<RequiredMemberAttribute>() is not null)
        {
            return true;
        }

        // Pas de paramètre de constructeur apparié, ou paramètre avec valeur par défaut → optionnel.
        if (ctorParam is null || ctorParam.HasDefaultValue)
        {
            return false;
        }

        // Paramètre de constructeur sans défaut : requis sauf si le type est annulable.
        if (Nullable.GetUnderlyingType(prop.PropertyType) is not null)
        {
            return false;
        }

        if (prop.PropertyType.IsValueType)
        {
            return true;
        }

        return nullabilityContext.Create(prop).WriteState != NullabilityState.Nullable;
    }

    private static JobParameterKind? ClassifyKind(Type type, out IReadOnlyList<string> enumOptions)
    {
        enumOptions = [];

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return JobParameterKind.Boolean;
        }

        if (underlying.IsEnum)
        {
            enumOptions = Enum.GetNames(underlying);
            return JobParameterKind.Enum;
        }

        if (underlying == typeof(string) || underlying == typeof(Guid))
        {
            return JobParameterKind.Text;
        }

        if (underlying == typeof(byte) || underlying == typeof(sbyte)
            || underlying == typeof(short) || underlying == typeof(ushort)
            || underlying == typeof(int) || underlying == typeof(uint)
            || underlying == typeof(long) || underlying == typeof(ulong)
            || underlying == typeof(decimal) || underlying == typeof(double) || underlying == typeof(float))
        {
            return JobParameterKind.Number;
        }

        return null;
    }

    // « DryRun » → « Dry run », « RecipientEmail » → « Recipient email », « TenantId » → « Tenant id ».
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var sb = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
            {
                sb.Append(' ');
                sb.Append(char.ToLower(c, CultureInfo.CurrentCulture));
            }
            else if (i == 0)
            {
                sb.Append(char.ToUpper(c, CultureInfo.CurrentCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
