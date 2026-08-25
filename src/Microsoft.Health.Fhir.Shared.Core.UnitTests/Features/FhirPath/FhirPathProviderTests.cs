// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using EvaluationContext = Hl7.FhirPath.EvaluationContext;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPath
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirPathProviderTests
    {
        [Fact]
        public void GivenFirelyProvider_WhenHelpersEvaluate_ThenFirely5114BehaviorIsPreserved()
        {
            var patient = new Patient
            {
                Active = true,
                Name =
                [
                    new HumanName { Family = "One" },
                    new HumanName { Family = "Two" },
                ],
            }.ToTypedElement();

            FhirPathProvider.SetProviderFactory(static () => new FirelyFhirPathProvider());

            Assert.Equal(2, patient.Select("name").Count());
            Assert.Throws<InvalidOperationException>(() => patient.Scalar("name"));
            Assert.True(patient.Predicate("{}"));
            Assert.False(patient.Predicate("active = false"));
            Assert.True(patient.Predicate("'content'"));
            Assert.True(patient.IsTrue("'content'"));
            Assert.True(patient.IsBoolean("active", true));
        }

        [Fact]
        public void GivenEitherProvider_WhenContextVariablesAndResolverEvaluate_ThenResultsMatch()
        {
            var patient = new Patient
            {
                Id = "patient-1",
                BirthDate = "1970-01-01",
                ManagingOrganization = new ResourceReference("Organization/org-1"),
            }.ToTypedElement();
            var organization = new Organization { Id = "org-1" }.ToTypedElement();
            EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext(
                reference => reference == "Organization/org-1" ? organization : null);
            context.Resource = patient;
            context.RootResource = patient;

            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();
            string[] expressions =
            [
                "%context.id",
                "%resource.id",
                "%rootResource.id",
                "managingOrganization.resolve().id",
                "birthDate < @2000-01-01",
            ];

            foreach (string expression in expressions)
            {
                object[] firelyValues = firely.Compile(expression).Select(patient, context).Select(x => x.Value).ToArray();
                object[] ignixaValues = ignixa.Compile(expression).Select(patient, context).Select(x => x.Value).ToArray();

                Assert.Equal(firelyValues, ignixaValues);
            }
        }

        [Fact]
        public async Task GivenGeneratedSearchParameterExpressions_WhenEvaluatedByBothProviders_ThenResultsMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            var patient = new Patient
            {
                Id = "patient-1",
                Active = true,
                BirthDate = "1970-01-01",
                Name = [new HumanName { Family = "Smith", Given = ["Alex"] }],
                ManagingOrganization = new ResourceReference("Organization/org-1"),
            }.ToTypedElement();
            EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext();
            context.Resource = patient;
            context.RootResource = patient;
            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();

            string[] expressions = definitions.AllSearchParameters
                .Where(parameter => parameter.IsSupported)
                .Where(parameter => parameter.Code != SearchParameterNames.ResourceType)
                .Select(parameter => parameter.Expression)
                .Where(expression => !string.IsNullOrWhiteSpace(expression))
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            foreach (string expression in expressions)
            {
                string[] firelyValues;
                string[] ignixaValues;
                try
                {
                    firelyValues = Normalize(firely.Compile(expression).Select(patient, context));
                    ignixaValues = Normalize(ignixa.Compile(expression).Select(patient, context));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Failed to evaluate generated expression '{expression}'.", exception);
                }

                Assert.Equal(firelyValues, ignixaValues);
            }
        }

        [Fact]
        public async Task GivenResourceCorpus_WhenIndexedByBothProviders_ThenSearchIndexEntriesMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            var supportedDefinitions = new SupportedSearchParameterDefinitionManager(definitions);
            var converters = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            var resolver = Substitute.For<IReferenceToElementResolver>();
            var firelyFailures = Substitute.For<IFailureMetricHandler>();
            var ignixaFailures = Substitute.For<IFailureMetricHandler>();
            var firelyIndexer = new TypedElementSearchIndexer(
                supportedDefinitions,
                converters,
                resolver,
                ModelInfoProvider.Instance,
                new FirelyFhirPathProvider(),
                NullLogger<TypedElementSearchIndexer>.Instance,
                firelyFailures);
            var ignixaIndexer = new TypedElementSearchIndexer(
                supportedDefinitions,
                converters,
                resolver,
                ModelInfoProvider.Instance,
                CreateIgnixaProvider(),
                NullLogger<TypedElementSearchIndexer>.Instance,
                ignixaFailures);
            ResourceElement[] resources =
            [
                new Patient
                {
                    Id = "patient-1",
                    Active = true,
                    BirthDate = "1970-01-01",
                    Name = [new HumanName { Family = "Smith", Given = ["Alex"] }],
                }.ToResourceElement(),
                new Organization
                {
                    Id = "org-1",
                    Active = true,
                    Name = "Contoso",
                }.ToResourceElement(),
            ];

            foreach (ResourceElement resource in resources)
            {
                IReadOnlyCollection<SearchIndexEntry> firelyEntries = firelyIndexer.Extract(resource);
                IReadOnlyCollection<SearchIndexEntry> ignixaEntries = ignixaIndexer.Extract(resource);

                Assert.True(
                    new HashSet<SearchIndexEntry>(firelyEntries).SetEquals(ignixaEntries),
                    $"Search-index parity failed for {resource.InstanceType}.");
            }

            firelyFailures.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
            ignixaFailures.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
        }

        private static IFhirPathProvider CreateIgnixaProvider()
            => new IgnixaFhirPathProvider(new IgnixaSchemaContext(ModelInfoProvider.Instance));

        private static string[] Normalize(IEnumerable<ITypedElement> elements)
            => elements
                .Select(element => $"{element.Value?.GetType().FullName}|{element.Value}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
    }
}
