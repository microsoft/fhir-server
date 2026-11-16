// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Definition.BundleWrappers;
using Microsoft.Health.Fhir.Core.Features.FhirPath;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Converters;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.IndexAndReindex)]
    [Trait(Traits.Category, Categories.Search)]
    public class TypedElementSearchIndexerTests
    {
        private readonly ISearchIndexer _searchIndexer;
        private readonly ISupportedSearchParameterDefinitionManager _definitionManager;

        private static readonly string ResourceStaus = "http://hl7.org/fhir/SearchParameter/Resource-status";
        private static readonly string ResourceUse = "http://hl7.org/fhir/SearchParameter/Resource-use";
        private static readonly string ResourceName = "http://hl7.org/fhir/SearchParameter/name";

        private const string CoverageStausExpression = "Coverage.status";
        private const string ObservationStausExpression = "Observation.status";
        private const string ClaimUseExpression = "Claim.use";
        private const string PatientNameExpression = "Patient.name";

        private static SearchParameterInfo statusSearchParameterInfo;

        public TypedElementSearchIndexerTests()
        {
            var supportedSearchParameterDefinitionManager = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            _definitionManager = supportedSearchParameterDefinitionManager;
            var typedElementToSearchValueConverterManager = GetTypeConverterAsync().Result;
            var referenceToElementResolver = Substitute.For<IReferenceToElementResolver>();
            var modelInfoProvider = ModelInfoProvider.Instance;
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();

            _searchIndexer = new TypedElementSearchIndexer(
                supportedSearchParameterDefinitionManager,
                typedElementToSearchValueConverterManager,
                referenceToElementResolver,
                modelInfoProvider,
                new FirelyFhirPathProvider(),
                logger);

            List<string> baseResourceTypes = new List<string>() { "Resource" };
            List<string> targetResourceTypes = new List<string>() { "Coverage", "Observation", "Claim", "Patient" };
            statusSearchParameterInfo = new SearchParameterInfo("_status", "_status", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceStaus), expression: CoverageStausExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes);
            var searchParameterInfos = new[]
            {
                statusSearchParameterInfo,
                new SearchParameterInfo("_status", "_status", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceStaus), expression: ObservationStausExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("_use", "_use", (ValueSets.SearchParamType)SearchParamType.Token, new Uri(ResourceUse), expression: ClaimUseExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
                new SearchParameterInfo("name", "name", (ValueSets.SearchParamType)SearchParamType.String, new Uri(ResourceName), expression: PatientNameExpression, targetResourceTypes: targetResourceTypes, baseResourceTypes: baseResourceTypes),
            };
            supportedSearchParameterDefinitionManager.GetSearchParameters(Arg.Any<string>()).Returns(searchParameterInfos);
        }

        protected async Task<ITypedElementToSearchValueConverterManager> GetTypeConverterAsync()
        {
            FhirTypedElementToSearchValueConverterManager fhirTypedElementToSearchValueConverterManager = await SearchParameterFixtureData.GetFhirTypedElementToSearchValueConverterManagerAsync();
            return fhirTypedElementToSearchValueConverterManager;
        }

        /// <summary>
        /// A deferred expression evaluation failure must identify the search parameter without exposing resource values.
        /// </summary>
        [Theory]
        [InlineData("Observation.value.where(matches('['))", ValueSets.SearchParamType.String)]
        [InlineData("Observation.value.where(matches('['))", ValueSets.SearchParamType.Composite)]
        [InlineData("Observation.value.where(matches($this & '['))", ValueSets.SearchParamType.String)]
        public void GivenExpressionEvaluationFailure_WhenExtract_ThenExceptionIdentifiesSearchParameter(string expression, ValueSets.SearchParamType searchParameterType)
        {
            // Arrange
            var url = new Uri("http://example.org/SearchParameter/evaluation-test");
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[] { new SearchParameterInfo("evaluation-test", "evaluation-test", searchParameterType, url, expression: expression) });
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Assert.Throws<SearchParameterEvaluationException>(() => _searchIndexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Equal(url, exception.SearchParameterUrl);
            Assert.Equal(string.Format(Core.Resources.SearchParameterEvaluationFailed, url), exception.Message);
            Assert.NotNull(exception.InnerException);
            Assert.DoesNotContain("synthetic-value", exception.Message);
        }

        /// <summary>
        /// A failing composite component expression must be reported against the owning composite search parameter.
        /// </summary>
        [Fact]
        public void GivenCompositeComponentExpressionFailure_WhenExtract_ThenExceptionIdentifiesOwningComposite()
        {
            // Arrange
            var compositeUrl = new Uri("http://example.org/SearchParameter/composite");
            var componentUrl = new Uri("http://hl7.org/fhir/SearchParameter/Observation-value");
            var componentDefinition = new SearchParameterInfo(
                "value",
                "value",
                ValueSets.SearchParamType.String,
                componentUrl,
                expression: "Observation.value");
            var component = new SearchParameterComponentInfo(
                componentUrl,
                "value.where(matches($this & '['))")
            {
                ResolvedSearchParameter = componentDefinition,
            };
            var composite = new SearchParameterInfo(
                "composite",
                "composite",
                ValueSets.SearchParamType.Composite,
                compositeUrl,
                components: new[] { component },
                expression: "Observation");
            _definitionManager.GetSearchParameters("Observation").Returns(new[] { composite });
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Assert.Throws<SearchParameterEvaluationException>(() => _searchIndexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Equal(compositeUrl, exception.SearchParameterUrl);
            Assert.Equal(string.Format(Core.Resources.SearchParameterEvaluationFailed, compositeUrl), exception.Message);
            Assert.DoesNotContain(componentUrl.ToString(), exception.Message);
            Assert.DoesNotContain("synthetic-value", exception.Message);
        }

        /// <summary>
        /// A composite search parameter without a URL cannot be identified in errors and must be rejected.
        /// </summary>
        [Fact]
        public void GivenCompositeSearchParameterWithoutUrl_WhenExtract_ThenThrowsArgumentNullException()
        {
            // Arrange
            var composite = new SearchParameterInfo(
                "composite",
                "composite",
                ValueSets.SearchParamType.Composite,
                expression: "Observation");
            _definitionManager.GetSearchParameters("Observation").Returns(new[] { composite });

            // Act
            var exception = Assert.Throws<ArgumentNullException>(() => _searchIndexer.Extract(new Observation().ToResourceElement()));

            // Assert
            Assert.Equal("Url", exception.ParamName);
        }

        /// <summary>
        /// An expression that fails immediately is skipped and must not prevent other search parameters from being indexed.
        /// </summary>
        [Theory]
        [InlineData("Observation.value.where(")]
        [InlineData("Observation.value.nonExistingFunction()")]
        public void GivenImmediateExpressionFailure_WhenExtract_ThenOtherSearchParametersAreStillIndexed(string expression)
        {
            // Arrange
            var invalidParameter = new SearchParameterInfo(
                "invalid",
                "invalid",
                ValueSets.SearchParamType.String,
                new Uri("http://example.org/SearchParameter/invalid"),
                expression: expression);
            var validParameter = new SearchParameterInfo(
                "status",
                "status",
                ValueSets.SearchParamType.Token,
                new Uri(ResourceStaus),
                expression: ObservationStausExpression);
            _definitionManager.GetSearchParameters("Observation").Returns(new[] { invalidParameter, validParameter });
            var observation = new Observation { Status = ObservationStatus.Final };

            // Act
            var entries = _searchIndexer.Extract(observation.ToResourceElement());

            // Assert
            var entry = Assert.Single(entries);
            Assert.Equal("final", Assert.IsType<TokenSearchValue>(entry.Value).Code);
        }

        /// <summary>
        /// Composite compilation failures are not evaluation failures: they are contained with a warning carrying the
        /// original exception and are never wrapped as <see cref="SearchParameterEvaluationException"/>.
        /// </summary>
        [Fact]
        public void GivenCompositeCompilationFailure_WhenExtract_ThenFailureIsLoggedAsWarningWithOriginalException()
        {
            // Arrange
            const string invalidExpression = "Observation.value.where(";
            var fhirPathProvider = new FirelyFhirPathProvider();
            Exception compileFailure = Record.Exception(() => fhirPathProvider.Compile(invalidExpression));
            Assert.NotNull(compileFailure);
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[]
                {
                    new SearchParameterInfo(
                        "invalid",
                        "invalid",
                        ValueSets.SearchParamType.Composite,
                        new Uri("http://example.org/SearchParameter/invalid"),
                        expression: invalidExpression),
                });
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();
            var indexer = new TypedElementSearchIndexer(
                _definitionManager,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                fhirPathProvider,
                logger);
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            IReadOnlyCollection<SearchIndexEntry> entries = null;
            var exception = Record.Exception(() => entries = indexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Null(exception);
            Assert.Empty(entries);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Is<Exception>(logged =>
                    logged != null &&
                    logged.GetType() == compileFailure.GetType() &&
                    logged.Message == compileFailure.Message),
                Arg.Any<Func<object, Exception, string>>());
            Assert.IsNotType<SearchParameterEvaluationException>(compileFailure);
        }

        /// <summary>
        /// Converter failures are not evaluation failures and must not be wrapped.
        /// </summary>
        [Fact]
        public void GivenConverterFailure_WhenExtract_ThenOriginalExceptionIsNotWrapped()
        {
            // Arrange
            var converterManager = Substitute.For<ITypedElementToSearchValueConverterManager>();
            var converter = Substitute.For<ITypedElementToSearchValueConverter>();
            converterManager.TryGetConverter("string", typeof(StringSearchValue), out Arg.Any<ITypedElementToSearchValueConverter>())
                .Returns(call =>
                {
                    call[2] = converter;
                    return true;
                });
            var expectedException = new InvalidOperationException("Conversion failed.");
            converter.ConvertTo(Arg.Any<ITypedElement>()).Returns(_ => throw expectedException);
            _definitionManager.GetSearchParameters("Observation").Returns(
                new[]
                {
                    new SearchParameterInfo(
                        "value",
                        "value",
                        ValueSets.SearchParamType.String,
                        new Uri("http://example.org/SearchParameter/value"),
                        expression: "Observation.value"),
                });
            var indexer = new TypedElementSearchIndexer(
                _definitionManager,
                converterManager,
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                new FirelyFhirPathProvider(),
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            var observation = new Observation { Value = new FhirString("synthetic-value") };

            // Act
            var exception = Assert.Throws<InvalidOperationException>(() => indexer.Extract(observation.ToResourceElement()));

            // Assert
            Assert.Same(expectedException, exception);
        }

        [Theory]
        [InlineData(typeof(OperationCanceledException))]
        [InlineData(typeof(OutOfMemoryException))]
        public void GivenCriticalExceptionFromGetEnumerator_WhenEnumerated_ThenOriginalExceptionEscapes(Type exceptionType)
        {
            // Arrange
            var expectedException = (Exception)Activator.CreateInstance(exceptionType);
            var values = Substitute.For<IEnumerable<ITypedElement>>();
            values.GetEnumerator().Returns(_ => throw expectedException);

            // Act
            var exception = Record.Exception(() => TypedElementSearchIndexer.EnumerateSearchParameterValues(values, new Uri("http://example.org/SearchParameter/test")).ToList());

            // Assert
            Assert.Same(expectedException, exception);
        }

        [Theory]
        [InlineData(typeof(OperationCanceledException), false)]
        [InlineData(typeof(OperationCanceledException), true)]
        [InlineData(typeof(OutOfMemoryException), false)]
        [InlineData(typeof(OutOfMemoryException), true)]
        public void GivenCriticalExceptionFromMoveNextOrCurrent_WhenEnumerated_ThenOriginalExceptionEscapes(Type exceptionType, bool throwFromCurrent)
        {
            // Arrange
            var expectedException = (Exception)Activator.CreateInstance(exceptionType);
            var enumerator = Substitute.For<IEnumerator<ITypedElement>>();
            if (throwFromCurrent)
            {
                enumerator.MoveNext().Returns(true);
                enumerator.Current.Returns(_ => throw expectedException);
            }
            else
            {
                enumerator.MoveNext().Returns(_ => throw expectedException);
            }

            var values = Substitute.For<IEnumerable<ITypedElement>>();
            values.GetEnumerator().Returns(enumerator);

            // Act
            var exception = Record.Exception(() => TypedElementSearchIndexer.EnumerateSearchParameterValues(values, new Uri("http://example.org/SearchParameter/test")).ToList());

            // Assert
            Assert.Same(expectedException, exception);
            enumerator.Received(1).Dispose();
        }

        [Fact]
        public void GivenAValidResource_WhenExtract_ThenValidSearchIndexEntriesAreCreated()
        {
            var coverageResource = Samples.GetDefaultCoverage().ToPoco<Coverage>();

            var searchIndexEntry = _searchIndexer.Extract(coverageResource.ToResourceElement());
            Assert.NotEmpty(searchIndexEntry);

            var tokenSearchValue = searchIndexEntry.First().Value as TokenSearchValue;
            Assert.NotNull(tokenSearchValue);

            Assert.True(coverageResource.Status.Value.ToString().Equals(tokenSearchValue.Code, StringComparison.CurrentCultureIgnoreCase));
        }

        [Fact]
        public async System.Threading.Tasks.Task GivenAnExpressionThatFailsDuringEvaluation_WhenExtracted_ThenTheFailurePropagates()
        {
            // Mirrors production custom SearchParameters whose matches() pattern is not a valid regex: the expression
            // compiles, but evaluating it against a matching resource throws. That must fail the write or reindex job
            // so the broken definition is visible, rather than silently leaving the resource unindexed.
            // Ignixa currently evaluates an invalid pattern to empty instead (brendankowitz/ignixa-fhir#519), so this
            // contract is asserted against the default Firely provider.
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "bad-pattern",
                    "bad-pattern",
                    ValueSets.SearchParamType.String,
                    new Uri("http://example.org/SearchParameter/bad-pattern"),
                    expression: "Patient.name.where(family.matches('abc['))"),
            ]);
            var indexer = new TypedElementSearchIndexer(
                definitions,
                await GetTypeConverterAsync(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                new FirelyFhirPathProvider(),
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            ResourceElement patient = new Patient { Id = "patient-1", Name = [new HumanName { Family = "abc" }] }.ToResourceElement();

            Assert.ThrowsAny<Exception>(() => indexer.Extract(patient));
        }

        [Fact]
        public void GivenFhirPathCompilationFailure_WhenExtract_ThenFailureIsLoggedAsWarningWithException()
        {
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "name",
                    "name",
                    (ValueSets.SearchParamType)SearchParamType.String,
                    new Uri(ResourceName),
                    expression: "Patient.name"),
            ]);
            var compileFailure = new NotSupportedException("Unsupported expression.");
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.name").Returns(_ => throw compileFailure);
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                logger);
            ResourceElement patient = new Patient { Id = "patient-1" }.ToResourceElement();

            IReadOnlyCollection<SearchIndexEntry> entries = indexer.Extract(patient);

            Assert.Empty(entries);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Is<Exception>(exception => ReferenceEquals(exception, compileFailure)),
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void GivenReferenceTargetFilterCompilationFailure_WhenExtract_ThenFailureIsLoggedAsWarningWithException()
        {
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "organization",
                    "organization",
                    (ValueSets.SearchParamType)SearchParamType.Reference,
                    new Uri("http://hl7.org/fhir/SearchParameter/Patient-organization"),
                    expression: "Patient.managingOrganization",
                    targetResourceTypes: ["Organization"]),
            ]);
            var resourceReference = Substitute.For<ITypedElement>();
            resourceReference.InstanceType.Returns("ResourceReference");
            var mainExpression = Substitute.For<ICompiledFhirPath>();
            mainExpression
                .Select(Arg.Any<ITypedElement>(), Arg.Any<Hl7.FhirPath.EvaluationContext>())
                .Returns([resourceReference]);
            var compileFailure = new NotSupportedException("Unsupported expression.");
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.managingOrganization").Returns(mainExpression);
            provider.Compile("reference").Returns(_ => throw compileFailure);
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                logger);
            ResourceElement patient = new Patient
            {
                Id = "patient-1",
                ManagingOrganization = new ResourceReference("Organization/organization-1"),
            }.ToResourceElement();

            IReadOnlyCollection<SearchIndexEntry> entries = indexer.Extract(patient);

            Assert.Empty(entries);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Is<Exception>(exception => ReferenceEquals(exception, compileFailure)),
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void GivenCompositeRootFhirPathCompilationFailure_WhenExtract_ThenFailureIsLoggedAsWarningWithException()
        {
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "composite",
                    "composite",
                    ValueSets.SearchParamType.Composite,
                    new Uri("http://hl7.org/fhir/SearchParameter/composite"),
                    components: Array.Empty<SearchParameterComponentInfo>(),
                    expression: "Patient.name"),
            ]);
            var compileFailure = new NotSupportedException("Unsupported expression.");
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.name").Returns(_ => throw compileFailure);
            var logger = Substitute.For<ILogger<TypedElementSearchIndexer>>();
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                logger);
            ResourceElement patient = new Patient { Id = "patient-1" }.ToResourceElement();

            IReadOnlyCollection<SearchIndexEntry> entries = indexer.Extract(patient);

            Assert.Empty(entries);
            logger.Received(1).Log(
                LogLevel.Warning,
                Arg.Any<EventId>(),
                Arg.Any<object>(),
                Arg.Is<Exception>(exception => ReferenceEquals(exception, compileFailure)),
                Arg.Any<Func<object, Exception, string>>());
        }

        [Fact]
        public void GivenFhirPathEvaluationCancellation_WhenExtract_ThenCancellationIsRethrown()
        {
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "name",
                    "name",
                    (ValueSets.SearchParamType)SearchParamType.String,
                    new Uri(ResourceName),
                    expression: "Patient.name"),
            ]);
            var compiledExpression = Substitute.For<ICompiledFhirPath>();
            compiledExpression
                .Select(Arg.Any<ITypedElement>(), Arg.Any<Hl7.FhirPath.EvaluationContext>())
                .Returns(_ => throw new OperationCanceledException());
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.name").Returns(compiledExpression);
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            ResourceElement patient = new Patient { Id = "patient-1" }.ToResourceElement();

            Assert.Throws<OperationCanceledException>(() => indexer.Extract(patient));
        }

        [Theory]
        [InlineData(typeof(OperationCanceledException))]
        [InlineData(typeof(OutOfMemoryException))]
        public void GivenCompositeRootCompilationFaultThatIsNotAnExpressionDefect_WhenExtract_ThenFaultIsRethrown(Type exceptionType)
        {
            // Cancellation and memory exhaustion are not defects of the expression. Swallowing them would skip the
            // caller's recovery (for example the reindex memory-pressure batch-size reduction) and persist an
            // incomplete set of index entries under the current search parameter hash.
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "composite",
                    "composite",
                    ValueSets.SearchParamType.Composite,
                    new Uri("http://hl7.org/fhir/SearchParameter/composite"),
                    components: Array.Empty<SearchParameterComponentInfo>(),
                    expression: "Patient.name"),
            ]);
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.name").Returns(_ => throw (Exception)Activator.CreateInstance(exceptionType));
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            ResourceElement patient = new Patient { Id = "patient-1" }.ToResourceElement();

            Assert.Throws(exceptionType, () => indexer.Extract(patient));
        }

        [Fact]
        public void GivenRepeatedExtraction_WhenExpressionsAreCompiled_ThenCachingIsDelegatedToProvider()
        {
            var definitions = Substitute.For<ISupportedSearchParameterDefinitionManager>();
            definitions.GetSearchParameters("Patient").Returns(
            [
                new SearchParameterInfo(
                    "name",
                    "name",
                    (ValueSets.SearchParamType)SearchParamType.String,
                    new Uri(ResourceName),
                    expression: "Patient.name"),
            ]);
            var compiledExpression = Substitute.For<ICompiledFhirPath>();
            compiledExpression
                .Select(Arg.Any<ITypedElement>(), Arg.Any<Hl7.FhirPath.EvaluationContext>())
                .Returns([]);
            var provider = Substitute.For<IFhirPathProvider>();
            provider.Compile("Patient.name").Returns(compiledExpression);
            var indexer = new TypedElementSearchIndexer(
                definitions,
                Substitute.For<ITypedElementToSearchValueConverterManager>(),
                Substitute.For<IReferenceToElementResolver>(),
                ModelInfoProvider.Instance,
                provider,
                Substitute.For<ILogger<TypedElementSearchIndexer>>());
            ResourceElement patient = new Patient { Id = "patient-1" }.ToResourceElement();

            indexer.Extract(patient);
            indexer.Extract(patient);

            provider.Received(2).Compile("Patient.name");
        }

        [Fact]
        public void GivenAValidResourceWithDuplicateSearchIndices_WhenExtract_ThenDistincSearchIndexEntriesAreCreated()
        {
            var patientResource = Samples.GetDefaultPatient().ToPoco<Patient>();
            var familyName = "Chalmers";
            var nameList = new List<HumanName>()
            {
                new HumanName() { Use = HumanName.NameUse.Official, Family = familyName },
                new HumanName() { Use = HumanName.NameUse.Official, Family = familyName },
            };
            patientResource.Name = nameList;

            var serachIndexEntry = _searchIndexer.Extract(patientResource.ToResourceElement());
            Assert.Single(serachIndexEntry);

            var nameSearchValue = serachIndexEntry.First().Value as StringSearchValue;
            Assert.Equal(familyName, nameSearchValue.String);
         }

#if !Stu3
        // For Stu3 - Coverage.status, Observation.status, and Claim.use are not required fields
        [Fact]
        public void GivenAnValidResource_WhenExtract_ThenExceptionIsNotThrown()
        {
            var requestBundle = Samples.GetJsonSample("Bundle-TransactionWithInvalidBundleEntry");

            foreach (var entry in new BundleWrapper(requestBundle.Instance).Entries)
            {
                ResourceElement resourceElement = null;
                string errorMessage = null;
                switch (entry.Resource.InstanceType)
                {
                    case "Coverage":
                        resourceElement = entry.Resource.ToPoco<Coverage>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, CoverageStausExpression);
                        break;
                    case "Observation":
                        resourceElement = entry.Resource.ToPoco<Observation>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, ObservationStausExpression);
                        break;
                    case "Claim":
                        resourceElement = entry.Resource.ToPoco<Claim>().ToResourceElement();
                        errorMessage = string.Format(Core.Resources.ValueCannotBeNull, ClaimUseExpression);
                        break;
                    default: break;
                }

                var exception = Record.Exception(() => _searchIndexer.Extract(resourceElement));
                Assert.Null(exception);
            }
        }
#endif
    }
}
