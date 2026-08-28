// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Resources.Patch;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Logging.Metrics;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Search;
using Microsoft.Health.Fhir.Ignixa;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using EvaluationContext = Hl7.FhirPath.EvaluationContext;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.FhirPath
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    [Collection(FhirPathProviderTestCollection.Name)]
    public class FhirPathProviderTests : IDisposable
    {
        private readonly IFhirPathProvider _originalAmbientProvider = FhirPathProvider.Instance;

        [Fact]
        public void GivenProviderFactory_WhenProviderIsReadMultipleTimes_ThenItIsCreatedLazilyOnce()
        {
            var expectedProvider = Substitute.For<IFhirPathProvider>();
            int factoryInvocationCount = 0;

            FhirPathProvider.SetProviderFactory(() =>
            {
                Interlocked.Increment(ref factoryInvocationCount);
                return expectedProvider;
            });

            Assert.Equal(0, Volatile.Read(ref factoryInvocationCount));
            Assert.Same(expectedProvider, FhirPathProvider.Instance);
            Assert.Same(expectedProvider, FhirPathProvider.Instance);
            Assert.Equal(1, Volatile.Read(ref factoryInvocationCount));
        }

        [Fact]
        public void GivenMaterializedProvider_WhenFactoryIsReplaced_ThenNewProviderIsCreatedLazily()
        {
            var originalProvider = Substitute.For<IFhirPathProvider>();
            var replacementProvider = Substitute.For<IFhirPathProvider>();
            int originalFactoryInvocationCount = 0;
            int replacementFactoryInvocationCount = 0;

            FhirPathProvider.SetProviderFactory(() =>
            {
                Interlocked.Increment(ref originalFactoryInvocationCount);
                return originalProvider;
            });
            Assert.Same(originalProvider, FhirPathProvider.Instance);

            FhirPathProvider.SetProviderFactory(() =>
            {
                Interlocked.Increment(ref replacementFactoryInvocationCount);
                return replacementProvider;
            });

            Assert.Equal(1, Volatile.Read(ref originalFactoryInvocationCount));
            Assert.Equal(0, Volatile.Read(ref replacementFactoryInvocationCount));
            Assert.Same(replacementProvider, FhirPathProvider.Instance);
            Assert.Equal(1, Volatile.Read(ref replacementFactoryInvocationCount));
        }

        [Fact]
        public void GivenProviderFactoryReturningNull_WhenProviderIsRead_ThenAnExceptionIsThrown()
        {
            FhirPathProvider.SetProviderFactory(static () => null);

            Assert.Throws<InvalidOperationException>(() => FhirPathProvider.Instance);
        }

        [Fact]
        public async Task GivenConcurrentProviderReads_WhenProviderHasNotBeenCreated_ThenItIsCreatedOnce()
        {
            var expectedProvider = Substitute.For<IFhirPathProvider>();
            int factoryInvocationCount = 0;
            FhirPathProvider.SetProviderFactory(() =>
            {
                Interlocked.Increment(ref factoryInvocationCount);
                return expectedProvider;
            });

            System.Threading.Tasks.Task<IFhirPathProvider>[] reads = Enumerable.Range(0, 32)
                .Select(_ => System.Threading.Tasks.Task.Run(() => FhirPathProvider.Instance))
                .ToArray();

            IFhirPathProvider[] providers = await System.Threading.Tasks.Task.WhenAll(reads);

            Assert.All(providers, provider => Assert.Same(expectedProvider, provider));
            Assert.Equal(1, Volatile.Read(ref factoryInvocationCount));
        }

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
        public void GivenEitherProvider_WhenCallerContextHasNoResource_ThenContextIsPopulated()
        {
            var patient = new Patient { Id = "patient-1" }.ToTypedElement();
            IFhirPathProvider[] providers =
            [
                new FirelyFhirPathProvider(),
                CreateIgnixaProvider(),
            ];

            foreach (IFhirPathProvider provider in providers)
            {
                EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext();

                string[] values = Normalize(provider.Compile("%resource.id | %rootResource.id").Select(patient, context));

                Assert.NotNull(context.Resource);
                Assert.NotNull(context.RootResource);
                Assert.Equal("Patient", context.Resource.InstanceType);
                Assert.Equal("Patient", context.RootResource.InstanceType);
                Assert.Equal(["System.String|patient-1"], values);
            }
        }

        [Fact]
        public async Task GivenVersionedResourceCorpus_WhenGeneratedAndResolverExpressionsAreEvaluated_ThenResultsMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();
            int nonEmptyExpressionCount = 0;
            int evaluatedExpressionCount = 0;
            int resolveExpressionCount = 0;
            int nonEmptyResolveExpressionCount = 0;
            var resolver = new CorpusReferenceToElementResolver();

            foreach (ResourceElement resource in GetResourceCorpus())
            {
                EvaluationContext context = ModelInfoProvider.Instance.GetEvaluationContext(resolver.Resolve);
                context.Resource = resource.Instance;
                context.RootResource = resource.Instance;
                string[] expressions = definitions.GetSearchParameters(resource.InstanceType)
                    .Where(parameter => parameter.IsSupported)
                    .Where(parameter => parameter.Code != SearchParameterNames.ResourceType)
                    .Select(parameter => parameter.Expression)
                    .Where(expression => !string.IsNullOrWhiteSpace(expression))
                    .Concat(
                        resource.InstanceType == KnownResourceTypes.Patient
                            ? new[] { "managingOrganization.resolve().id" }
                            : Array.Empty<string>())
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();

                foreach (string expression in expressions)
                {
                    evaluatedExpressionCount++;
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

                    if (expression.Contains("resolve()", StringComparison.Ordinal))
                    {
                        resolveExpressionCount++;
                        nonEmptyResolveExpressionCount += firelyValues.Length > 0 ? 1 : 0;
                    }
                }
            }

            Assert.True(evaluatedExpressionCount >= 100, $"Expected at least 100 generated expressions, but evaluated {evaluatedExpressionCount}.");
            Assert.True(nonEmptyExpressionCount >= 10, $"Expected at least 10 non-empty generated-expression results, but observed {nonEmptyExpressionCount}.");
            Assert.True(resolveExpressionCount >= 2, $"Expected at least two resolve() corpus evaluations, but observed {resolveExpressionCount}.");
            Assert.True(nonEmptyResolveExpressionCount >= 1, "The parity corpus must produce a non-empty resolve() result.");
        }

        [Fact]
        public async Task GivenEveryGeneratedSearchParameterExpression_WhenEvaluatedByBothProviders_ThenResultsMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            IFhirPathProvider firely = new FirelyFhirPathProvider();
            IFhirPathProvider ignixa = CreateIgnixaProvider();
            ITypedElement input = new Patient { Id = "synthetic-patient" }.ToTypedElement();
            string[] parentExpressions = definitions.AllSearchParameters
                .Where(parameter => parameter.Code != SearchParameterNames.ResourceType)
                .Select(parameter => parameter.Expression)
                .Where(expression => !string.IsNullOrWhiteSpace(expression))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(expression => expression, StringComparer.Ordinal)
                .ToArray();
            string[] componentExpressions = definitions.AllSearchParameters
                .Where(parameter => parameter.Code != SearchParameterNames.ResourceType)
                .SelectMany(parameter => parameter.Component ?? Array.Empty<SearchParameterComponentInfo>())
                .Select(component => component.Expression)
                .Where(expression => !string.IsNullOrWhiteSpace(expression))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(expression => expression, StringComparer.Ordinal)
                .ToArray();

            Assert.True(parentExpressions.Length >= 500, $"Expected at least 500 generated parent expressions, but evaluated {parentExpressions.Length}.");
            Assert.NotEmpty(componentExpressions);

            string[] expressions = parentExpressions
                .Concat(componentExpressions)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(expression => expression, StringComparer.Ordinal)
                .ToArray();

            foreach (string expression in expressions)
            {
                EvaluationContext firelyContext = ModelInfoProvider.Instance.GetEvaluationContext();
                EvaluationContext ignixaContext = ModelInfoProvider.Instance.GetEvaluationContext();
                string[] firelyValues;
                string[] ignixaValues;

                try
                {
                    firelyValues = Normalize(firely.Compile(expression).Select(input, firelyContext));
                    ignixaValues = Normalize(ignixa.Compile(expression).Select(input, ignixaContext));
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException($"Failed to evaluate generated expression '{expression}'.", exception);
                }

                Assert.Equal(firelyValues, ignixaValues);
            }
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
            cases = cases.Concat(PatchPayload.ImmutableProperties.Select(expression => (narrative, expression))).ToArray();
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

            Assert.True(nonEmptyResultCount >= 10, $"Expected at least 10 non-empty literal-expression results, but observed {nonEmptyResultCount}.");
        }

        [Fact]
        public async Task GivenResourceCorpus_WhenIndexedByBothProviders_ThenSearchIndexEntriesMatch()
        {
            var fixture = new SearchParameterFixtureData();
            SearchParameterDefinitionManager definitions = await fixture.GetSearchDefinitionManagerAsync();
            var supportedDefinitions = new SupportedSearchParameterDefinitionManager(definitions);
            var converters = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            var resolver = new CorpusReferenceToElementResolver();
            var firelyFailures = Substitute.For<IFailureMetricHandler>();
            var ignixaFailures = Substitute.For<IFailureMetricHandler>();
            IFhirPathProvider firelyProvider = new FirelyFhirPathProvider();
            IFhirPathProvider ignixaProvider = CreateIgnixaProvider();
            var firelyIndexer = new TypedElementSearchIndexer(
                supportedDefinitions,
                converters,
                resolver,
                ModelInfoProvider.Instance,
                firelyProvider,
                NullLogger<TypedElementSearchIndexer>.Instance,
                firelyFailures);
            var ignixaIndexer = new TypedElementSearchIndexer(
                supportedDefinitions,
                converters,
                resolver,
                ModelInfoProvider.Instance,
                ignixaProvider,
                NullLogger<TypedElementSearchIndexer>.Instance,
                ignixaFailures);
            var firelyEntriesByResourceType = new Dictionary<string, IReadOnlyCollection<SearchIndexEntry>>(StringComparer.Ordinal);
            int totalFirelyEntryCount = 0;
            try
            {
                foreach (ResourceElement resource in GetResourceCorpus())
                {
                    FhirPathProvider.SetProviderFactory(() => firelyProvider);
                    IReadOnlyCollection<SearchIndexEntry> firelyEntries = firelyIndexer.Extract(resource);

                    FhirPathProvider.SetProviderFactory(() => ignixaProvider);
                    IReadOnlyCollection<SearchIndexEntry> ignixaEntries = ignixaIndexer.Extract(resource);
                    firelyEntriesByResourceType.TryAdd(resource.InstanceType, firelyEntries);
                    totalFirelyEntryCount += firelyEntries.Count;

                    string[] firelyValues = firelyEntries.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    string[] ignixaValues = ignixaEntries.Select(Normalize).OrderBy(x => x, StringComparer.Ordinal).ToArray();

                    Assert.Equal(firelyValues, ignixaValues);
                }
            }
            finally
            {
                FhirPathProvider.SetProviderFactory(() => _originalAmbientProvider);
            }

            firelyFailures.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
            ignixaFailures.DidNotReceive().EmitException(Arg.Any<IExceptionMetricNotification>());
            Assert.True(
                totalFirelyEntryCount >= 10,
                "The index parity corpus must produce at least 10 search index entries.");
            Assert.Contains(
                firelyEntriesByResourceType[KnownResourceTypes.Patient],
                entry => entry.SearchParameter.Code == "name" &&
                    entry.Value is StringSearchValue value &&
                    value.String == "Chalmers");
            Assert.Contains(
                firelyEntriesByResourceType[KnownResourceTypes.Observation],
                entry => entry.SearchParameter.Code == "code" &&
                    entry.Value is TokenSearchValue value &&
                    value.System == "http://loinc.org" &&
                    value.Code == "29463-7");
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
                new Patient
                {
                    Id = "patient-with-reference",
                    ManagingOrganization = new ResourceReference("Organization/org-1"),
                }.ToResourceElement(),
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

        private sealed class CorpusReferenceToElementResolver : IReferenceToElementResolver
        {
            public ITypedElement Resolve(string reference)
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    return null;
                }

                string[] parts = reference.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !ModelInfoProvider.Instance.IsKnownResource(parts[^2]))
                {
                    return null;
                }

                ISourceNode node = FhirJsonNode.Create(
                    JObject.FromObject(
                        new
                        {
                            resourceType = parts[^2],
                            id = parts[^1],
                        }));

                return node.ToTypedElement(ModelInfoProvider.Instance.StructureDefinitionSummaryProvider);
            }
        }
    }
}
