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
using Newtonsoft.Json;
using NSubstitute;
using Xunit;
using EvaluationContext = Hl7.FhirPath.EvaluationContext;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPath
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirPathProviderTests : IDisposable
    {
        private readonly IFhirPathProvider _originalAmbientProvider = FhirPathProvider.Instance;

        [Fact]
        public void GivenFirelyProvider_WhenHelpersEvaluate_ThenFirely5114BehaviorIsPreserved()
        {
            var patient = new Patient
            {
                Id = "patient-1",
                Active = true,
                Name =
                [
                    new HumanName { Family = "One" },
                    new HumanName { Family = "Two" },
                ],
            }.ToTypedElement();

            FhirPathProvider.SetProviderFactory(static () => new FirelyFhirPathProvider());

            Assert.Equal(2, patient.Select("name").Count());
            Assert.Null(patient.Scalar("{}"));
            Assert.Throws<InvalidOperationException>(() => patient.Scalar("name"));
            Assert.True(patient.Predicate("{}"));
            Assert.False(patient.Predicate("active = false"));
            Assert.True(patient.Predicate("'content'"));
            Assert.False(patient.IsTrue("{}"));
            Assert.True(patient.IsTrue("'content'"));
            Assert.False(patient.IsBoolean("{}", false));
            Assert.True(patient.IsBoolean("active", true));

            EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext();
            context.Resource = patient;
            Assert.Equal("patient-1", patient.Scalar("%resource.id", context));
            Assert.Equal("patient-1", patient.Scalar("%rootResource.id", context));
            Assert.NotNull(context.RootResource);
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
        public async Task GivenVersionedResourceCorpus_WhenApplicableGeneratedExpressionsAreEvaluated_ThenResultsMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();
            int nonEmptyExpressionCount = 0;

            foreach (ResourceElement resource in GetResourceCorpus())
            {
                EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext();
                context.Resource = resource.Instance;
                context.RootResource = resource.Instance;
                string[] expressions = definitions.GetSearchParameters(resource.InstanceType)
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
                        firelyValues = Normalize(firely.Compile(expression).Select(resource.Instance, context));
                        ignixaValues = Normalize(ignixa.Compile(expression).Select(resource.Instance, context));
                    }
                    catch (Exception exception)
                    {
                        throw new InvalidOperationException(
                            $"Failed to evaluate generated expression '{expression}' against '{resource.InstanceType}'.",
                            exception);
                    }

                    Assert.Equal(firelyValues, ignixaValues);

                    if (firelyValues.Length > 0)
                    {
                        nonEmptyExpressionCount++;
                    }
                }
            }

            Assert.True(nonEmptyExpressionCount > 0, "The parity corpus must exercise non-empty generated-expression results.");
        }

        [Fact]
        public void GivenRepositoryOwnedLiteralExpressions_WhenEvaluatedByBothProviders_ThenResultsMatch()
        {
            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();
            ITypedElement narrative = Samples.GetJsonSample("BasicExampleNarrative").Instance;
            ITypedElement bundle = Samples.GetDefaultTransaction().Instance;
            const string capabilityJson =
                """{"resourceType":"CapabilityStatement","rest":[{"resource":[{"type":"Patient","versioning":"versioned-update","updateCreate":true,"readHistory":true,"interaction":[{"code":"read"}]}]}]}""";
            ITypedElement capability = ModelInfoProvider.Instance.ToTypedElement(
                new RawResource(capabilityJson, FhirResourceFormat.Json, isMetaSet: false));
            ITypedElement address = new Address
            {
                City = "Seattle",
                Country = "USA",
                District = "King",
                Line = ["1 Main Street"],
                PostalCode = "98101",
                State = "WA",
                Text = "1 Main Street, Seattle",
            }.ToTypedElement();
            (ITypedElement Input, string Expression)[] cases =
            [
                (narrative, KnownFhirPaths.ResourceNarrative),
                (bundle, KnownFhirPaths.BundleEntries),
                (bundle, KnownFhirPaths.BundleType),
                (bundle, KnownFhirPaths.BundleNextLink),
                (bundle, KnownFhirPaths.BundleSelfLink),
                (narrative, KnownFhirPaths.IsSoftDeletedExtension),
                (capability, "CapabilityStatement.rest.resource.where(type = 'Patient').interaction.where(code = 'read').exists()"),
                (capability, "CapabilityStatement.rest.resource.where(type = 'Patient').where(versioning = 'versioned-update').exists()"),
                (capability, "CapabilityStatement.rest.resource.where(type = 'Patient').updateCreate = true"),
                (capability, "CapabilityStatement.rest.resource.where(type = 'Patient').readHistory"),
                (address, "city"),
                (address, "country"),
                (address, "district"),
                (address, "line"),
                (address, "postalCode"),
                (address, "state"),
                (address, "text"),
            ];
            int nonEmptyResultCount = 0;

            foreach ((ITypedElement input, string expression) in cases)
            {
                string[] firelyValues = Normalize(firely.Compile(expression).Select(input));
                string[] ignixaValues = Normalize(ignixa.Compile(expression).Select(input));

                Assert.Equal(firelyValues, ignixaValues);
                if (expression == KnownFhirPaths.ResourceNarrative)
                {
                    Assert.NotEmpty(firelyValues);
                }

                nonEmptyResultCount += firelyValues.Length > 0 ? 1 : 0;
            }

            Assert.True(nonEmptyResultCount > 0, "The literal-expression corpus must exercise non-empty results.");
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
            foreach (ResourceElement resource in GetResourceCorpus())
            {
                IReadOnlyCollection<SearchIndexEntry> firelyEntries = firelyIndexer.Extract(resource);
                IReadOnlyCollection<SearchIndexEntry> ignixaEntries = ignixaIndexer.Extract(resource);
                string[] firelyValues = firelyEntries.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                string[] ignixaValues = ignixaEntries.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();

                Assert.Equal(firelyValues, ignixaValues);
            }
        }

        private static IFhirPathProvider CreateIgnixaProvider()
            => new IgnixaFhirPathProvider(new IgnixaSchemaContext(ModelInfoProvider.Instance));

        private static ResourceElement[] GetResourceCorpus()
            =>
            [
                Samples.GetDefaultPatient(),
                Samples.GetDefaultOrganization(),
                Samples.GetDefaultObservation(),
                Samples.GetDefaultCoverage(),
                Samples.GetDefaultPractitioner(),
                Samples.GetDefaultMedication(),
            ];

        private static string[] Normalize(IEnumerable<ITypedElement> elements)
            => elements
                .Select(element => $"{element.Value?.GetType().FullName}|{element.Value}")
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

        private static string Normalize(SearchIndexEntry entry)
            => $"{entry.SearchParameter.Url}|{entry.SearchParameter.Code}|{JsonConvert.SerializeObject(entry.Value)}";

        public void Dispose()
            => FhirPathProvider.SetProviderFactory(() => _originalAmbientProvider);
    }
}
