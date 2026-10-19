// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Rest;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Access;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions.Parsers;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Features.Security;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.Core.UnitTests.Features.Context;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using SortOrder = Microsoft.Health.Fhir.Core.Features.Search.SortOrder;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class FhirCosmosSearchServiceTests
    {
        private readonly SearchParameterInfo _idParameter = new(SearchParameterNames.Id, SearchParameterNames.Id, SearchParamType.Token, SearchParameterNames.IdUri);
        private readonly SearchParameterInfo _tagParameter = new("_tag", "_tag", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-tag"));
        private readonly SearchParameterInfo _subjectParameter = new(
            "subject",
            "subject",
            SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"),
            targetResourceTypes: new[] { KnownResourceTypes.Patient });

        [Fact]
        public async Task GivenWildcardScopeWithMultiTypeFilter_WhenSearchingWithForwardInclude_ThenAuthorizedTargetResourceIsIncluded()
        {
            // Arrange: real parser represents _type=Patient,Observation as MultiaryExpression(Or, ...).
            var scope = new ScopeRestriction(
                KnownResourceTypes.All,
                DataActions.Read | DataActions.Search,
                "system",
                CreateSearchParams(("_type", "Patient,Observation")));

            var observationMatch = CreateWrapper(
                KnownResourceTypes.Observation,
                "obs-1",
                new ResourceTypeAndId(KnownResourceTypes.Patient, "patient-1"));
            var includedPatient = CreateWrapper(KnownResourceTypes.Patient, "patient-1");

            (FhirCosmosSearchService service, List<SearchOptions> capturedSubqueries) = CreateService(
                new[] { observationMatch },
                new[] { includedPatient },
                scope);

            // Act
            SearchResult result = await service.SearchAsync(
                KnownResourceTypes.Observation,
                new[]
                {
                    Tuple.Create("_id", "obs-1"),
                    Tuple.Create("_include", "Observation:subject"),
                },
                CancellationToken.None);

            // Assert: Patient include subquery is executed and Patient/patient-1 is returned as an Include entry.
            Assert.Single(capturedSubqueries);
            Assert.Equal(2, result.Results.Count());
            Assert.Contains(result.Results, r => r.SearchEntryMode == SearchEntryMode.Match && r.Resource.ResourceId == "obs-1");
            Assert.Contains(result.Results, r => r.SearchEntryMode == SearchEntryMode.Include && r.Resource.ResourceId == "patient-1");
        }

        [Fact]
        public async Task GivenWildcardScopeWithMultiTypeFilter_WhenSearchingWithNamedRevInclude_ThenAuthorizedSourceResourceIsIncluded()
        {
            // Arrange: real parser represents _type=Patient,Observation as MultiaryExpression(Or, ...).
            var scope = new ScopeRestriction(
                KnownResourceTypes.All,
                DataActions.Read | DataActions.Search,
                "system",
                CreateSearchParams(("_type", "Patient,Observation")));

            var patientMatch = CreateWrapper(KnownResourceTypes.Patient, "patient-1");
            var revIncludedObservation = CreateWrapper(KnownResourceTypes.Observation, "obs-1");

            (FhirCosmosSearchService service, List<SearchOptions> capturedSubqueries) = CreateService(
                new[] { patientMatch },
                new[] { revIncludedObservation },
                scope);

            // Act
            SearchResult result = await service.SearchAsync(
                KnownResourceTypes.Patient,
                new[]
                {
                    Tuple.Create("_id", "patient-1"),
                    Tuple.Create("_revinclude", "Observation:subject"),
                },
                CancellationToken.None);

            // Assert: Observation revinclude subquery is executed and Observation/obs-1 is returned as an Include entry.
            Assert.Single(capturedSubqueries);
            Assert.Equal(2, result.Results.Count());
            Assert.Contains(result.Results, r => r.SearchEntryMode == SearchEntryMode.Match && r.Resource.ResourceId == "patient-1");
            Assert.Contains(result.Results, r => r.SearchEntryMode == SearchEntryMode.Include && r.Resource.ResourceId == "obs-1");
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenWildcardScopeWithTagFilter_WhenSearchingWithIncludeOrRevInclude_ThenTagFilterIsAppliedToSubquery(bool reversed)
        {
            // Arrange: a pure wildcard scope with _tag=allowed (no _type filter) has resourceTypeExprs.Count == 0
            // in GetSmartScopeFilterForResourceType and must still apply _tag=allowed to the include/revinclude subquery.
            var scope = new ScopeRestriction(
                KnownResourceTypes.All,
                DataActions.Read | DataActions.Search,
                "patient",
                CreateSearchParams(("_tag", "allowed")));

            FhirCosmosResourceWrapper match = reversed
                ? CreateWrapper(KnownResourceTypes.Patient, "patient-1")
                : CreateWrapper(KnownResourceTypes.Observation, "obs-1", new ResourceTypeAndId(KnownResourceTypes.Patient, "patient-1"));
            FhirCosmosResourceWrapper includeCandidate = reversed
                ? CreateWrapper(KnownResourceTypes.Observation, "obs-1")
                : CreateWrapper(KnownResourceTypes.Patient, "patient-1");

            (FhirCosmosSearchService service, List<SearchOptions> capturedSubqueries) = CreateService(
                new[] { match },
                new[] { includeCandidate },
                scope);

            // Act
            SearchResult result = await service.SearchAsync(
                reversed ? KnownResourceTypes.Patient : KnownResourceTypes.Observation,
                new[]
                {
                    Tuple.Create("_id", reversed ? "patient-1" : "obs-1"),
                    Tuple.Create(reversed ? "_revinclude" : "_include", "Observation:subject"),
                },
                CancellationToken.None);

            // Assert: subquery expression preserves the _tag=allowed scope predicate.
            SearchOptions subquery = Assert.Single(capturedSubqueries);
            Assert.Contains("(Param _tag (StringEquals TokenCode 'allowed'))", subquery.Expression.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, result.Results.Count());
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GivenWildcardScopeWithTagAndMultiTypeFilter_WhenSearchingWithIncludeOrRevInclude_ThenTagFilterIsAppliedToSubquery(bool reversed)
        {
            // Arrange: combine _type=Patient,Observation and _tag=allowed on a wildcard scope so both the OR-parsed
            // _type check and the remaining _tag filter are exercised in GetSmartScopeFilterForResourceType.
            var scope = new ScopeRestriction(
                KnownResourceTypes.All,
                DataActions.Read | DataActions.Search,
                "system",
                CreateSearchParams(("_type", "Patient,Observation"), ("_tag", "allowed")));

            FhirCosmosResourceWrapper match = reversed
                ? CreateWrapper(KnownResourceTypes.Patient, "patient-1")
                : CreateWrapper(KnownResourceTypes.Observation, "obs-1", new ResourceTypeAndId(KnownResourceTypes.Patient, "patient-1"));
            FhirCosmosResourceWrapper includeCandidate = reversed
                ? CreateWrapper(KnownResourceTypes.Observation, "obs-1")
                : CreateWrapper(KnownResourceTypes.Patient, "patient-1");

            (FhirCosmosSearchService service, List<SearchOptions> capturedSubqueries) = CreateService(
                new[] { match },
                new[] { includeCandidate },
                scope);

            // Act
            SearchResult result = await service.SearchAsync(
                reversed ? KnownResourceTypes.Patient : KnownResourceTypes.Observation,
                new[]
                {
                    Tuple.Create("_id", reversed ? "patient-1" : "obs-1"),
                    Tuple.Create(reversed ? "_revinclude" : "_include", "Observation:subject"),
                },
                CancellationToken.None);

            // Assert: subquery expression preserves the _tag=allowed scope predicate.
            SearchOptions subquery = Assert.Single(capturedSubqueries);
            Assert.Contains("(Param _tag (StringEquals TokenCode 'allowed'))", subquery.Expression.ToString(), StringComparison.Ordinal);
            Assert.Equal(2, result.Results.Count());
        }

        [Fact]
        public async Task GivenWildcardScopeWithMultiTypeFilterExcludingTarget_WhenSearchingWithForwardInclude_ThenExcludedResourceIsOmitted()
        {
            // Arrange: Practitioner is not in _type=Patient,Observation, so including Practitioner must be skipped.
            var scope = new ScopeRestriction(
                KnownResourceTypes.All,
                DataActions.Read | DataActions.Search,
                "system",
                CreateSearchParams(("_type", "Patient,Observation")));

            var observationMatch = CreateWrapper(
                KnownResourceTypes.Observation,
                "obs-1",
                new ResourceTypeAndId(KnownResourceTypes.Practitioner, "pract-1"));

            (FhirCosmosSearchService service, List<SearchOptions> capturedSubqueries) = CreateService(
                new[] { observationMatch },
                Array.Empty<FhirCosmosResourceWrapper>(),
                scope);

            // Act
            SearchResult result = await service.SearchAsync(
                KnownResourceTypes.Observation,
                new[]
                {
                    Tuple.Create("_id", "obs-1"),
                    Tuple.Create("_include", "Observation:subject"),
                },
                CancellationToken.None);

            // Assert: no include subquery is issued for the unauthorized Practitioner reference.
            Assert.Empty(capturedSubqueries);
            Assert.Single(result.Results);
        }

        private (FhirCosmosSearchService Service, List<SearchOptions> CapturedSubqueries) CreateService(
            IReadOnlyList<FhirCosmosResourceWrapper> primaryMatches,
            IReadOnlyList<FhirCosmosResourceWrapper> subqueryResults,
            params ScopeRestriction[] scopes)
        {
            var searchParameterDefinitionManager = Substitute.For<ISearchParameterDefinitionManager>();
            searchParameterDefinitionManager.GetSearchParameter(Arg.Any<string>(), Arg.Any<string>())
                .Returns(callInfo =>
                {
                    string code = callInfo.ArgAt<string>(1);
                    return code switch
                    {
                        SearchParameterNames.ResourceType => SearchParameterInfo.ResourceTypeSearchParameter,
                        SearchParameterNames.Id => _idParameter,
                        "_tag" => _tagParameter,
                        "subject" => _subjectParameter,
                        _ => throw new SearchParameterNotSupportedException(callInfo.ArgAt<string>(0), code),
                    };
                });
            searchParameterDefinitionManager.TryGetSearchParameter(Arg.Any<string>(), Arg.Any<string>(), out Arg.Any<SearchParameterInfo>())
                .Returns(callInfo =>
                {
                    string code = callInfo.ArgAt<string>(1);
                    callInfo[2] = code switch
                    {
                        SearchParameterNames.ResourceType => SearchParameterInfo.ResourceTypeSearchParameter,
                        SearchParameterNames.Id => _idParameter,
                        "_tag" => _tagParameter,
                        "subject" => _subjectParameter,
                        _ => null,
                    };
                    return callInfo[2] != null;
                });

            var requestContext = new DefaultFhirRequestContext
            {
                AccessControlContext = new AccessControlContext
                {
                    ApplyFineGrainedAccessControl = true,
                    ApplyFineGrainedAccessControlWithSearchParameters = true,
                },
            };
            foreach (ScopeRestriction scope in scopes)
            {
                requestContext.AccessControlContext.AllowedResourceActions.Add(scope);
            }

            RequestContextAccessor<IFhirRequestContext> contextAccessor = requestContext.SetupAccessor();

            var referenceParser = Substitute.For<IReferenceSearchValueParser>();
            var searchParameterExpressionParser = new SearchParameterExpressionParser(referenceParser);
            var expressionParser = new ExpressionParser(() => searchParameterDefinitionManager, searchParameterExpressionParser);
            var sortingValidator = Substitute.For<ISortingValidator>();
            sortingValidator.ValidateSorting(Arg.Any<IReadOnlyList<(SearchParameterInfo, SortOrder)>>(), out Arg.Any<IReadOnlyList<string>>())
                .Returns(true);

            var searchOptionsFactory = new SearchOptionsFactory(
                expressionParser,
                () => searchParameterDefinitionManager,
                new OptionsWrapper<CoreFeatureConfiguration>(new CoreFeatureConfiguration()),
                contextAccessor,
                sortingValidator,
                new ExpressionAccessControl(contextAccessor),
                NullLogger<SearchOptionsFactory>.Instance);

            var cosmosQueryFactory = new TestCosmosQueryFactory(primaryMatches, subqueryResults);

            var cosmosConfig = new CosmosDataStoreConfiguration();
            var bundleOptions = Options.Create(new BundleConfiguration { SupportsBundleOrchestrator = true });
            var dataStore = new CosmosFhirDataStore(
                Substitute.For<Container>().CreateMockScope(),
                cosmosConfig,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                cosmosQueryFactory,
                new RetryExceptionPolicyFactory(cosmosConfig, contextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                new BundleOrchestrator(bundleOptions, NullLogger<BundleOrchestrator>.Instance),
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()),
                ModelInfoProvider.Instance,
                Substitute.For<ISearchParameterStatusDataStore>(),
                contextAccessor);

            var capturedSubqueries = new List<SearchOptions>();
            var queryBuilder = new TestQueryBuilder(capturedSubqueries);

            var compartmentRewriter = new CosmosCompartmentSearchRewriter(
                new Lazy<ICompartmentDefinitionManager>(Substitute.For<ICompartmentDefinitionManager>()),
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager));
            var smartCompartmentRewriter = new SmartCompartmentSearchRewriter(
                compartmentRewriter,
                new Lazy<ISearchParameterDefinitionManager>(() => searchParameterDefinitionManager),
                Options.Create(new CoreFeatureConfiguration()));

            var service = new FhirCosmosSearchService(
                searchOptionsFactory,
                dataStore,
                queryBuilder,
                contextAccessor,
                cosmosConfig,
                Substitute.For<ICosmosDbCollectionPhysicalPartitionInfo>(),
                compartmentRewriter,
                smartCompartmentRewriter,
                ModelInfoProvider.Instance,
                NullLogger<FhirCosmosSearchService>.Instance);

            return (service, capturedSubqueries);
        }

        private static FhirCosmosResourceWrapper CreateWrapper(
            string resourceType,
            string resourceId,
            params ResourceTypeAndId[] referencesToInclude)
        {
            return new FhirCosmosResourceWrapper(
                resourceId,
                "1",
                resourceType,
                new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                new ResourceRequest("PUT"),
                DateTimeOffset.UtcNow,
                deleted: false,
                history: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null)
            {
                ReferencesToInclude = referencesToInclude,
            };
        }

        private static SearchParams CreateSearchParams(params (string Name, string Value)[] parameters)
        {
            var searchParams = new SearchParams();
            foreach ((string name, string value) in parameters)
            {
                searchParams.Add(name, value);
            }

            return searchParams;
        }

        private sealed class TestQueryBuilder : IQueryBuilder
        {
            private readonly List<SearchOptions> _capturedSubqueries;
            private readonly QueryBuilder _inner = new();

            public TestQueryBuilder(List<SearchOptions> capturedSubqueries)
            {
                _capturedSubqueries = capturedSubqueries;
            }

            public QueryDefinition BuildSqlQuerySpec(SearchOptions searchOptions, QueryBuilderOptions queryOptions = null)
            {
                if (queryOptions == null)
                {
                    _capturedSubqueries.Add(searchOptions);
                }

                return _inner.BuildSqlQuerySpec(searchOptions, queryOptions);
            }

            public QueryDefinition GenerateReindexSql(SearchOptions searchOptions, string searchParameterHash)
            {
                return _inner.GenerateReindexSql(searchOptions, searchParameterHash);
            }
        }

        private sealed class TestCosmosQueryFactory : ICosmosQueryFactory
        {
            private readonly IReadOnlyList<FhirCosmosResourceWrapper> _primaryMatches;
            private readonly IReadOnlyList<FhirCosmosResourceWrapper> _subqueryResults;
            private int _callIndex;

            public TestCosmosQueryFactory(
                IReadOnlyList<FhirCosmosResourceWrapper> primaryMatches,
                IReadOnlyList<FhirCosmosResourceWrapper> subqueryResults)
            {
                _primaryMatches = primaryMatches;
                _subqueryResults = subqueryResults;
            }

            public ICosmosQuery<T> Create<T>(Container container, CosmosQueryContext context)
            {
                IReadOnlyList<FhirCosmosResourceWrapper> batch = _callIndex++ == 0 ? _primaryMatches : _subqueryResults;
                return new TestCosmosQuery<T>(batch.Cast<T>().ToList());
            }
        }

        private sealed class TestCosmosQuery<T> : ICosmosQuery<T>
        {
            private readonly IReadOnlyList<T> _items;

            public TestCosmosQuery(IReadOnlyList<T> items)
            {
                _items = items;
            }

            public bool HasMoreResults => false;

            public Task<FeedResponse<T>> ExecuteNextAsync(CancellationToken token = default)
            {
                return Task.FromResult<FeedResponse<T>>(new TestFeedResponse<T>(_items));
            }
        }

        private sealed class TestFeedResponse<T> : FeedResponse<T>
        {
            private readonly IReadOnlyList<T> _items;

            public TestFeedResponse(IReadOnlyList<T> items)
            {
                _items = items;
            }

            public override string ContinuationToken => null;

            public override int Count => _items.Count;

            public override string IndexMetrics => string.Empty;

            public override Headers Headers { get; } = new Headers();

            public override IEnumerable<T> Resource => _items;

            public override HttpStatusCode StatusCode => HttpStatusCode.OK;

            public override CosmosDiagnostics Diagnostics => null;

            public override IEnumerator<T> GetEnumerator() => _items.GetEnumerator();
        }
    }
}
