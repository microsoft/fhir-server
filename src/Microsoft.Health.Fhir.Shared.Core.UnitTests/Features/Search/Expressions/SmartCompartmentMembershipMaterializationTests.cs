// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#if R4 || R4B

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.FhirPath;
using Hl7.Fhir.Model;
using Hl7.FhirPath;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    /// <summary>
    /// Guards the security-sensitive invariant that SMART compartment membership can actually match indexed rows.
    /// </summary>
    /// <remarks>
    /// SMART compartment membership is enforced by seeking <c>ReferenceSearchParam</c> rows whose
    /// <c>SearchParamId</c> is one of the compartment definition's membership parameters. If a membership
    /// parameter is never written by the search indexer, that seek matches nothing and every resource of that
    /// type is silently dropped from compartment-scoped results — an undercount that fails closed and is
    /// therefore invisible in leak-focused tests. These tests index a real resource that references the
    /// compartment root and assert the resolved membership parameters intersect what the indexer actually
    /// materialized.
    /// </remarks>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SmartCompartmentMembershipMaterializationTests
    {
        private const string PatientReference = "Patient/pat1";
        private const string PractitionerReference = "Practitioner/prac1";

        static SmartCompartmentMembershipMaterializationTests()
        {
            FhirPathCompiler.DefaultSymbolTable.AddFhirExtensions();
        }

        [Theory]
        [InlineData("AllergyIntolerance")]
        [InlineData("AuditEvent")]
        [InlineData("Basic")]
        [InlineData("CarePlan")]
        [InlineData("Condition")]
        [InlineData("DiagnosticReport")]
        [InlineData("Encounter")]
        [InlineData("ImagingStudy")]
        [InlineData("Immunization")]
        [InlineData("Invoice")]
        [InlineData("MeasureReport")]
        [InlineData("MedicationRequest")]
        [InlineData("Observation")]
        [InlineData("Person")]
        [InlineData("Procedure")]
        [InlineData("Provenance")]
        public async System.Threading.Tasks.Task GivenAPatientCompartmentResource_WhenIndexed_ThenItsMembershipParametersAreMaterialized(string resourceType)
        {
            await AssertMembershipIsMaterializedAsync(
                compartmentType: KnownResourceTypes.Patient,
                resourceType: resourceType,
                targetResourceType: KnownResourceTypes.Patient,
                resource: BuildPatientCompartmentResource(resourceType));
        }

        [Theory]
        [InlineData("Encounter")]
        [InlineData("EpisodeOfCare")]
        [InlineData("Observation")]
        [InlineData("Person")]
        public async System.Threading.Tasks.Task GivenAPractitionerCompartmentResource_WhenIndexed_ThenItsMembershipParametersAreMaterialized(string resourceType)
        {
            await AssertMembershipIsMaterializedAsync(
                compartmentType: KnownResourceTypes.Practitioner,
                resourceType: resourceType,
                targetResourceType: KnownResourceTypes.Practitioner,
                resource: BuildPractitionerCompartmentResource(resourceType));
        }

        private static async System.Threading.Tasks.Task AssertMembershipIsMaterializedAsync(
            string compartmentType,
            string resourceType,
            string targetResourceType,
            Resource resource)
        {
            SearchParameterDefinitionManager definitionManager = await new SearchParameterFixtureData().GetSearchDefinitionManagerAsync();

            var compartmentDefinitionManager = new CompartmentDefinitionManager(ModelInfoProvider.Instance);
            await compartmentDefinitionManager.StartAsync(CancellationToken.None);

            var rewriter = new SqlCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(() => compartmentDefinitionManager),
                new Lazy<ISearchParameterDefinitionManager>(() => definitionManager));

            IReadOnlyDictionary<string, IReadOnlyCollection<SearchParameterInfo>> membership =
                rewriter.GetMaterializedCompartmentSearchParameters(compartmentType, filteredResourceTypes: null);

            Assert.True(
                membership.ContainsKey(resourceType),
                $"{resourceType} resolved no {compartmentType} compartment membership parameters at all.");

            HashSet<string> membershipUrls = membership[resourceType]
                .Select(parameter => parameter.Url.AbsoluteUri)
                .ToHashSet(StringComparer.Ordinal);

            HashSet<string> materializedUrls = ExtractMaterializedReferenceParameters(definitionManager, resource, targetResourceType);

            string[] usable = membershipUrls.Intersect(materializedUrls, StringComparer.Ordinal).ToArray();

            string failure =
                $"No {compartmentType} compartment membership parameter for {resourceType} is materialized by the indexer. " +
                $"Compartment-scoped search would silently drop every {resourceType} in the compartment.{Environment.NewLine}" +
                $"  membership parameters: {string.Join(", ", membershipUrls.OrderBy(u => u, StringComparer.Ordinal))}{Environment.NewLine}" +
                $"  indexed parameters:    {string.Join(", ", materializedUrls.OrderBy(u => u, StringComparer.Ordinal))}";

            Assert.True(usable.Length > 0, failure);
        }

        private static HashSet<string> ExtractMaterializedReferenceParameters(
            SearchParameterDefinitionManager definitionManager,
            Resource resource,
            string targetResourceType)
        {
            var supported = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            supported.GetSearchParameters(Arg.Any<string>())
                .Returns(definitionManager.GetSearchParameters(resource.TypeName).ToList());

            var resolver = new LightweightReferenceToElementResolver(
                Mock.TypeWithArguments<ReferenceSearchValueParser>(new FhirRequestContextAccessor()),
                ModelInfoProvider.Instance);

            var indexer = new TypedElementSearchIndexer(
                supported,
                SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync().GetAwaiter().GetResult(),
                resolver,
                ModelInfoProvider.Instance,
                NullLogger<TypedElementSearchIndexer>.Instance);

            return indexer.Extract(resource.ToResourceElement())
                .Where(entry => entry.Value is ReferenceSearchValue reference
                    && string.Equals(reference.ResourceType, targetResourceType, StringComparison.Ordinal))
                .Select(entry => entry.SearchParameter.Url.AbsoluteUri)
                .ToHashSet(StringComparer.Ordinal);
        }

        private static Resource BuildPatientCompartmentResource(string resourceType)
        {
            var patient = new ResourceReference(PatientReference);

            return resourceType switch
            {
                "AllergyIntolerance" => new AllergyIntolerance { Id = "ai1", Patient = patient },
                "AuditEvent" => new AuditEvent
                {
                    Id = "ae1",
                    Agent = new List<AuditEvent.AgentComponent> { new AuditEvent.AgentComponent { Who = patient } },
                    Entity = new List<AuditEvent.EntityComponent> { new AuditEvent.EntityComponent { What = patient } },
                },
                "Basic" => new Basic { Id = "b1", Subject = patient },
                "CarePlan" => new CarePlan { Id = "cp1", Subject = patient },
                "Condition" => new Condition { Id = "c1", Subject = patient },
                "DiagnosticReport" => new DiagnosticReport { Id = "dr1", Subject = patient },
                "Encounter" => new Encounter { Id = "e1", Subject = patient },
                "ImagingStudy" => new ImagingStudy { Id = "is1", Subject = patient },
                "Immunization" => new Immunization { Id = "im1", Patient = patient },
                "Invoice" => new Invoice { Id = "i1", Subject = patient },
                "MeasureReport" => new MeasureReport { Id = "mr1", Subject = patient },
                "MedicationRequest" => new MedicationRequest { Id = "mrq1", Subject = patient },
                "Observation" => new Observation { Id = "o1", Subject = patient },
                "Person" => new Person
                {
                    Id = "p1",
                    Link = new List<Person.LinkComponent> { new Person.LinkComponent { Target = patient } },
                },
                "Procedure" => new Procedure { Id = "pr1", Subject = patient },
                "Provenance" => new Provenance { Id = "pv1", Target = new List<ResourceReference> { patient } },
                _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unhandled resource type."),
            };
        }

        private static Resource BuildPractitionerCompartmentResource(string resourceType)
        {
            var practitioner = new ResourceReference(PractitionerReference);

            return resourceType switch
            {
                "Encounter" => new Encounter
                {
                    Id = "e2",
                    Participant = new List<Encounter.ParticipantComponent>
                    {
                        new Encounter.ParticipantComponent { Individual = practitioner },
                    },
                },
                "EpisodeOfCare" => new EpisodeOfCare { Id = "eoc1", CareManager = practitioner },
                "Observation" => new Observation
                {
                    Id = "o2",
                    Performer = new List<ResourceReference> { practitioner },
                },
                "Person" => new Person
                {
                    Id = "p2",
                    Link = new List<Person.LinkComponent> { new Person.LinkComponent { Target = practitioner } },
                },
                _ => throw new ArgumentOutOfRangeException(nameof(resourceType), resourceType, "Unhandled resource type."),
            };
        }
    }
}

#endif
