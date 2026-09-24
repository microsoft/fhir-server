// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search.Expressions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SmartCompartmentSearchRewriterTests
    {
        private readonly ISearchParameterDefinitionManager _searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
        private readonly ICompartmentDefinitionManager _compartmentDefinitionManager = Substitute.For<ICompartmentDefinitionManager>();
        private readonly SearchParameterInfo _resourceTypeParam;
        private readonly SearchParameterInfo _idParam;
        private readonly SearchParameterInfo _devicePatientParam;

        public SmartCompartmentSearchRewriterTests()
        {
            // CompartmentSearchExpression validates the compartment type against the process-wide
            // ModelInfoProvider. Set it explicitly so these tests do not depend on some other test
            // in the assembly having already initialized it with a compatible provider.
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).Build());

            _resourceTypeParam = new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-type"));
            _idParam = new SearchParameterInfo(SearchParameterNames.Id, SearchParameterNames.Id, ValueSets.SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
            _devicePatientParam = new SearchParameterInfo("patient", "patient", ValueSets.SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Device-patient"));

            _searchParameterDefinitionManager.GetSearchParameter(KnownResourceTypes.Resource, SearchParameterNames.ResourceType).Returns(_resourceTypeParam);
            _searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), SearchParameterNames.Id).Returns(_idParam);
            _searchParameterDefinitionManager
                .TryGetSearchParameter(KnownResourceTypes.Device, "patient", out Arg.Any<SearchParameterInfo>())
                .Returns(x =>
                {
                    x[2] = _devicePatientParam;
                    return true;
                });

            // Empty compartment membership: keeps the compartment leg trivial for these tests.
            _compartmentDefinitionManager
                .TryGetResourceTypes(Arg.Any<CompartmentType>(), out Arg.Any<HashSet<string>>())
                .Returns(x =>
                {
                    x[1] = new HashSet<string>();
                    return true;
                });
        }

        private SmartCompartmentSearchRewriter CreateRewriter(bool flagEnabled, bool useSqlCompartmentRewriter = true)
        {
            var config = new CoreFeatureConfiguration { EnableSmartCompartmentDeviceRestriction = flagEnabled };
            CompartmentSearchRewriter compartmentRewriter = useSqlCompartmentRewriter
                ? new SqlCompartmentSearchRewriter(
                    new Lazy<ICompartmentDefinitionManager>(() => _compartmentDefinitionManager),
                    new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager))
                : new CosmosCompartmentSearchRewriter(
                    new Lazy<ICompartmentDefinitionManager>(() => _compartmentDefinitionManager),
                    new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager));

            return new SmartCompartmentSearchRewriter(
                compartmentRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => _searchParameterDefinitionManager),
                Options.Create(config));
        }

        private UnionExpression Rewrite(SmartCompartmentSearchRewriter rewriter, string compartmentType = KnownResourceTypes.Patient)
        {
            var expression = Expression.SmartCompartmentSearch(compartmentType, "patient-A", new[] { KnownResourceTypes.DomainResource });
            return Assert.IsType<UnionExpression>(expression.AcceptVisitor(rewriter));
        }

        private static InExpression<string> GetUniversalTypesExpression(UnionExpression union)
        {
            // The universal-types leg is a SearchParameterExpression over _type with an In expression.
            return union.Expressions
                .OfType<SearchParameterExpression>()
                .Select(spe => spe.Expression)
                .OfType<InExpression<string>>()
                .Single();
        }

        [Fact]
        public void GivenFlagEnabled_WhenPatientCompartment_ThenDeviceIsRestrictedNotUniversal()
        {
            UnionExpression union = Rewrite(CreateRewriter(flagEnabled: true));

            Assert.DoesNotContain(KnownResourceTypes.Device, GetUniversalTypesExpression(union).Values);

            // Leg B: devices without a patient reference.
            NotReferencingExpression notReferencing = union.Expressions.OfType<NotReferencingExpression>().Single();
            Assert.Equal(KnownResourceTypes.Device, notReferencing.SourceResourceType);
            Assert.Same(_devicePatientParam, notReferencing.ReferenceSearchParameter);

            // Leg A: devices whose patient reference is the compartment patient.
            MultiaryExpression deviceLeg = union.Expressions
                .OfType<MultiaryExpression>()
                .Single(m => m.Expressions.OfType<SearchParameterExpression>().Any(spe => ReferenceEquals(spe.Parameter, _devicePatientParam)));
            Assert.Contains(
                deviceLeg.Expressions,
                e => e is StringExpression se && se.FieldName == FieldName.ReferenceResourceId && se.Value == "patient-A");
        }

        [Fact]
        public void GivenFlagEnabled_WhenNonPatientCompartment_ThenOnlyUnassignedDevicesLegAdded()
        {
            UnionExpression union = Rewrite(CreateRewriter(flagEnabled: true), compartmentType: KnownResourceTypes.Practitioner);

            Assert.DoesNotContain(KnownResourceTypes.Device, GetUniversalTypesExpression(union).Values);
            Assert.Single(union.Expressions.OfType<NotReferencingExpression>());

            // No leg A for non-Patient compartments.
            Assert.DoesNotContain(
                union.Expressions.OfType<MultiaryExpression>(),
                m => m.Expressions.OfType<SearchParameterExpression>().Any(spe => ReferenceEquals(spe.Parameter, _devicePatientParam)));
        }

        [Fact]
        public void GivenFlagDisabled_WhenRewritten_ThenDeviceRemainsUniversal()
        {
            UnionExpression union = Rewrite(CreateRewriter(flagEnabled: false));

            Assert.Contains(KnownResourceTypes.Device, GetUniversalTypesExpression(union).Values);
            Assert.Empty(union.Expressions.OfType<NotReferencingExpression>());
        }

        [Fact]
        public void GivenDevicePatientParamMissing_WhenRewritten_ThenDeviceRemainsUniversal()
        {
            // Simulates R5+, where Device has no patient search parameter.
            _searchParameterDefinitionManager
                .TryGetSearchParameter(KnownResourceTypes.Device, "patient", out Arg.Any<SearchParameterInfo>())
                .Returns(false);

            UnionExpression union = Rewrite(CreateRewriter(flagEnabled: true));

            Assert.Contains(KnownResourceTypes.Device, GetUniversalTypesExpression(union).Values);
            Assert.Empty(union.Expressions.OfType<NotReferencingExpression>());
        }

        [Fact]
        public void GivenCosmosCompartmentRewriter_WhenRewritten_ThenDeviceRemainsUniversal()
        {
            UnionExpression union = Rewrite(CreateRewriter(flagEnabled: true, useSqlCompartmentRewriter: false));

            Assert.Contains(KnownResourceTypes.Device, GetUniversalTypesExpression(union).Values);
            Assert.Empty(union.Expressions.OfType<NotReferencingExpression>());
        }

        [Fact]
        public void GivenFilteredResourceTypesWithoutDevice_WhenRewritten_ThenNoDeviceLegsAdded()
        {
            var rewriter = CreateRewriter(flagEnabled: true);
            var expression = Expression.SmartCompartmentSearch(KnownResourceTypes.Patient, "patient-A", new[] { KnownResourceTypes.Observation });
            var union = Assert.IsType<UnionExpression>(expression.AcceptVisitor(rewriter));

            Assert.Empty(union.Expressions.OfType<NotReferencingExpression>());
            Assert.DoesNotContain(
                union.Expressions.OfType<MultiaryExpression>(),
                m => m.Expressions.OfType<SearchParameterExpression>().Any(spe => ReferenceEquals(spe.Parameter, _devicePatientParam)));
        }
    }
}
