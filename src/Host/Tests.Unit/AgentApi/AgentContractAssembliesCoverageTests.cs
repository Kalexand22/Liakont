namespace Liakont.Host.Tests.Unit.AgentApi;

using System;
using System.Collections.Generic;
using System.Reflection;
using FluentAssertions;
using Liakont.Agent.Contracts;
using Liakont.Agent.Contracts.Ged;
using Liakont.Agent.Contracts.Transport;
using Liakont.Host.AgentApi;
using Xunit;

/// <summary>
/// Garde de couverture du HashSet <see cref="AgentApiJson.BoundContractAssemblies"/> (RDL04 / GDF13).
/// La liaison stricte des membres inconnus (<c>JsonUnmappedMemberHandling.Disallow</c>) n'est posée que sur
/// les assemblys listés dans ce set. Le set est maintenu À LA MAIN : un futur canal agent (nouvel assembly
/// <c>Liakont.Agent.Contracts.*</c>) dont un DTO serait bindé aux endpoints puis re-sérialisé-hashé, mais
/// oublié dans le set, dropperait silencieusement ses membres inconnus → empreinte plateforme ≠ empreinte
/// agent → anti-doublon (PIV04 / INV-GED-06) cassé. Le piège a déjà été payé à GED05a.
/// <para>Ce test PARCOURT le graphe de types des DTO réellement bindés par <c>AgentApiEndpoints</c> et exige
/// que TOUT assembly de contrat atteint figure dans le set (sinon la garde ne le couvre pas). Il échoue donc
/// aussi bien si on retire un assembly du set que si on binde un DTO d'un assembly de contrat non listé.</para>
/// </summary>
public sealed class AgentContractAssembliesCoverageTests
{
    // Préfixe des assemblys de contrat agent (le contrat FISCAL Liakont.Agent.Contracts et ses namespaces
    // Pivot/Transport, + le contrat d'ingestion GÉNÉRIQUE Liakont.Agent.Contracts.Ged).
    private const string ContractAssemblyPrefix = "Liakont.Agent.Contracts";

    // DTO de requête réellement désérialisés par les endpoints agent (AgentApiEndpoints) via la liaison
    // stricte : POST heartbeat, POST documents/batch (canal fiscal), POST managed-documents/batch (canal GED).
    // Les deux canaux batch re-sérialisent le DTO STJ-désérialisé puis le hashent (empreinte anti-doublon).
    private static readonly Type[] BoundRequestDtos =
    [
        typeof(HeartbeatRequestDto),
        typeof(PushBatchRequestDto),
        typeof(ManagedDocumentBatchRequestDto),
    ];

    [Fact]
    public void Every_contract_assembly_reachable_from_a_bound_dto_carries_the_strict_member_guard()
    {
        var referenced = CollectContractAssemblies(BoundRequestDtos);

        // Anti-faux-vert : si le parcours n'atteignait aucun type de contrat (walk cassé), l'assertion de
        // sous-ensemble passerait à vide. On exige d'abord que les DEUX canaux connus soient réellement atteints.
        referenced.Should().NotBeEmpty("le parcours des DTO bindés doit atteindre au moins un assembly de contrat");
        referenced.Should().Contain(typeof(AgentContractVersion).Assembly, "le canal fiscal (documents/batch) référence Liakont.Agent.Contracts");
        referenced.Should().Contain(typeof(GedContractVersion).Assembly, "le canal GED (managed-documents/batch) référence Liakont.Agent.Contracts.Ged");

        const string coverageReason =
            "tout assembly de contrat atteint par un DTO bindé doit porter la liaison stricte des membres inconnus "
            + "(RDL04) — l'ajouter au HashSet ContractAssemblies d'AgentApiJson, sinon ses membres inconnus sont "
            + "droppés en silence et l'empreinte anti-doublon diverge de celle de l'agent";
        referenced.Should().OnlyContain(assembly => AgentApiJson.BoundContractAssemblies.Contains(assembly), coverageReason);
    }

    /// <summary>
    /// Collecte les assemblys <c>Liakont.Agent.Contracts*</c> atteignables depuis les types racines, en
    /// parcourant récursivement propriétés et paramètres de constructeur (DTO immuables), en traversant les
    /// arguments génériques et les types d'éléments de tableau (pour atteindre les types dans les collections
    /// BCL), et en ne plongeant dans les membres que des types de contrat (borne le parcours hors BCL).
    /// </summary>
    private static HashSet<Assembly> CollectContractAssemblies(IEnumerable<Type> roots)
    {
        var contractAssemblies = new HashSet<Assembly>();
        var visited = new HashSet<Type>();
        var pending = new Queue<Type>(roots);

        while (pending.Count > 0)
        {
            var raw = pending.Dequeue();

            // Nullable<T> (ex. OperationCategory? en paramètre) : parcourir le type sous-jacent.
            var type = Nullable.GetUnderlyingType(raw) ?? raw;

            if (!visited.Add(type))
            {
                continue;
            }

            if (type.IsArray)
            {
                var element = type.GetElementType();
                if (element is not null)
                {
                    pending.Enqueue(element);
                }

                continue;
            }

            if (type.IsGenericType)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    pending.Enqueue(argument);
                }
            }

            var assemblyName = type.Assembly.GetName().Name ?? string.Empty;
            if (!assemblyName.StartsWith(ContractAssemblyPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            contractAssemblies.Add(type.Assembly);

            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                pending.Enqueue(property.PropertyType);
            }

            foreach (var constructor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in constructor.GetParameters())
                {
                    pending.Enqueue(parameter.ParameterType);
                }
            }
        }

        return contractAssemblies;
    }
}
