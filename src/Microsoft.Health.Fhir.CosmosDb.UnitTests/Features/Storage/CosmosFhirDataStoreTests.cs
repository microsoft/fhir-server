// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Scripts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Exceptions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Definition;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Persistence.Orchestration;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosFhirDataStoreTests
    {
        private readonly ICosmosQueryFactory _cosmosQueryFactory;
        private readonly CosmosFhirDataStore _dataStore;
        private readonly CosmosDataStoreConfiguration _cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();
        private readonly IScoped<Container> _container;
        private readonly IBundleOrchestrator _bundleOrchestrator;

        public CosmosFhirDataStoreTests()
        {
            _container = Substitute.For<Container>().CreateMockScope();
            _cosmosQueryFactory = Substitute.For<ICosmosQueryFactory>();
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            fhirRequestContext.ExecutingBatchOrTransaction.Returns(true);
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            var bundleConfiguration = new BundleConfiguration() { SupportsBundleOrchestrator = true };
            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(bundleConfiguration);

            var logger = Substitute.For<ILogger<BundleOrchestrator>>();

            _bundleOrchestrator = new BundleOrchestrator(bundleOptions, logger);

            _dataStore = new CosmosFhirDataStore(
                _container,
                _cosmosDataStoreConfiguration,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                _cosmosQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, requestContextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                _bundleOrchestrator,
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()),
                ModelInfoProvider.Instance,
                Substitute.For<ISearchParameterStatusDataStore>(),
                requestContextAccessor);
        }

        [Fact]
        public async Task GivenAQuery_WhenASinglePageReturnsRequestedCount_ASingleQueryIsPerformced()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 10, null);

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(response);

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 });

            Assert.Equal(Enumerable.Range(0, 10), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WhenFetchingSubsequentPagesYieldsA429_ReturnsExistingResults()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 2, "token");

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => response, ci => throw CreateCosmosException(new RequestRateExceededException(null)));
            cosmosQuery.HasMoreResults.Returns(true);

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 });

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Equal("token", continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WhenFetchingSubsequentPagesTimesOut_ReturnsExistingResults()
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(default, default).ReturnsForAnyArgs(cosmosQuery);

            FeedResponse<int> response = CreateFeedResponse(0, 2, "token");

            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => response, ci => throw new OperationCanceledException());
            cosmosQuery.HasMoreResults.Returns(true);

            var time = DateTimeOffset.UtcNow;
            _cosmosDataStoreConfiguration.SearchEnumerationTimeoutInSeconds = 0;

            // lock the time
            using (Mock.Property(() => ClockResolver.TimeProvider, new Microsoft.Extensions.Time.Testing.FakeTimeProvider(time)))
            {
                (IReadOnlyList<int> results, string continuationToken) =
                    await _dataStore.ExecuteDocumentQueryAsync<int>(
                        new QueryDefinition("abc"),
                        new QueryRequestOptions { MaxItemCount = 10 });

                Assert.Equal(Enumerable.Range(0, 2), results);
                Assert.Equal("token", continuationToken);
            }
        }

        [Fact]
        public async Task GivenAQueryWhereItemCountCanBeExceeded_WhenExecuted_FetchesSubsequentPages()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 0, "1"),
                CreateFeedResponse(0, 1, "2"),
                CreateFeedResponse(0, 0, "3"),
                CreateFeedResponse(1, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: false);

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQueryWhereItemCountMustNotBeExceeded_WhenExecuted_FetchesSubsequentPagesByIssuingNewQueries()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 0, "1"),
                CreateFeedResponse(0, 1, "2"),
                CreateFeedResponse(10, 10, "3")); // if this shows up in the results, it means we did not issue a new query after the previous page yielded a result

            CreateResponses(
                9,
                "2",
                CreateFeedResponse(0, 0, "3"),
                CreateFeedResponse(1, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: true);

            Assert.Equal(Enumerable.Range(0, 2), results);
            Assert.Null(continuationToken);
        }

        [Fact]
        public async Task GivenAQuery_WithPagesWithFewResults_GivesUpAfterHalfTheResultsHaveBeenCollected()
        {
            CreateResponses(
                10,
                null,
                CreateFeedResponse(0, 1, "1"),
                CreateFeedResponse(1, 1, "2"),
                CreateFeedResponse(2, 1, "3"),
                CreateFeedResponse(3, 1, "4"),
                CreateFeedResponse(4, 1, "5"),
                CreateFeedResponse(5, 1, "6"),
                CreateFeedResponse(6, 1, "7"),
                CreateFeedResponse(7, 1, "8"),
                CreateFeedResponse(8, 1, "9"),
                CreateFeedResponse(9, 1, null));

            (IReadOnlyList<int> results, string continuationToken) = await _dataStore.ExecuteDocumentQueryAsync<int>(
                new QueryDefinition("abc"),
                new QueryRequestOptions { MaxItemCount = 10 },
                mustNotExceedMaxItemCount: false);

            Assert.Equal(Enumerable.Range(0, 5), results);
            Assert.Equal("5", continuationToken);
        }

        [Fact]
        public async Task GivenAnUpsertDuringABatch_When503ExceptionOccurs_RetryWillHappen()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam1", "newSearchParam1"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2", "newSearchParam2"), new StringSearchValue("paramValue"));

            wrapper.SearchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };
            var innerException = new Exception("RequestTimeout");

            _container.Value.When(x => x.CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())).
                Do(x => throw CreateCosmosException(innerException, HttpStatusCode.ServiceUnavailable));

            // using try catch here instead of Assert.ThrowsAsync in order to verify exception property
            try
            {
                await _dataStore.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
            }
            catch (CosmosException e)
            {
                Assert.Equal(HttpStatusCode.RequestTimeout, e.StatusCode);
            }

            await _container.Value.ReceivedWithAnyArgs(7).CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUpsertDuringABatch_When408ExceptionOccurs_RetryWillHappen()
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "id1";
            observation.VersionId = "version1";
            observation.Meta.Profile = new List<string> { "test" };
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Post, "http://fhir"), false, null, null, null);
            var searchIndex = new SearchIndexEntry(new SearchParameterInfo("newSearchParam1", "newSearchParam1"), new NumberSearchValue(1));
            var searchIndex2 = new SearchIndexEntry(new SearchParameterInfo("newSearchParam2", "newSearchParam2"), new StringSearchValue("paramValue"));

            wrapper.SearchIndices = new List<SearchIndexEntry>() { searchIndex, searchIndex2 };
            var innerException = new Exception("RequestTimeout");

            _container.Value.When(x => x.CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())).
                Do(x => throw CreateCosmosException(innerException, HttpStatusCode.RequestTimeout));

            try
            {
                await _dataStore.UpsertAsync(new ResourceWrapperOperation(wrapper, true, true, null, false, false, bundleResourceContext: null), CancellationToken.None);
            }
            catch (CosmosException e)
            {
                Assert.Equal(HttpStatusCode.RequestTimeout, e.StatusCode);
            }

            await _container.Value.ReceivedWithAnyArgs(7).CreateItemAsync(Arg.Any<FhirCosmosResourceWrapper>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAConditionalUpdateWithComparedVersion_WhenTheNoOpResultRacesAConcurrentWrite_ThenPreconditionFailedIsThrownInsteadOfSilentSuccess()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "race-no-op";
            observation.VersionId = "1";
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Put, "http://fhir"), false, null, null, null);
            wrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("raceParam", "raceParam"), new NumberSearchValue(1)),
            };

            // The compared version ("1") matches what a conditional search observed, and the submitted body
            // is byte-for-byte identical to what is already stored, so this update is a logical no-op.
            var firstRead = new FhirCosmosResourceWrapper(wrapper);
            SetETag(firstRead, "\"etag-v1\"");

            // Simulate a concurrent writer that changes the resource (to version "2") between our read of
            // version "1" and the moment the no-op decision would otherwise be trusted without verification.
            var concurrentObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            concurrentObservation.Id = "race-no-op";
            concurrentObservation.VersionId = "2";
            ResourceElement concurrentElement = concurrentObservation.ToResourceElement();
            var concurrentWrapper = new ResourceWrapper(concurrentElement, rawResourceFactory.Create(concurrentElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Put, "http://fhir"), false, null, null, null);
            var secondRead = new FhirCosmosResourceWrapper(concurrentWrapper);
            SetETag(secondRead, "\"etag-v2\"");

            ItemResponse<FhirCosmosResourceWrapper> firstResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            firstResponse.Resource.Returns(firstRead);

            ItemResponse<FhirCosmosResourceWrapper> secondResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            secondResponse.Resource.Returns(secondRead);

            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(firstResponse), Task.FromResult(secondResponse));

            // A no-op reported as a success has to be settled against the current document, which takes a
            // conditional write: here the service rejects it because another writer already advanced the
            // document past the ETag that was read.
            _container.Value.PatchItemAsync<FhirCosmosResourceWrapper>(
                    Arg.Any<string>(),
                    Arg.Any<PartitionKey>(),
                    Arg.Any<IReadOnlyList<PatchOperation>>(),
                    Arg.Any<PatchItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns<ItemResponse<FhirCosmosResourceWrapper>>(x => throw CreateCosmosException(new Exception("PreconditionFailed"), HttpStatusCode.PreconditionFailed));

            var operation = new ResourceWrapperOperation(wrapper, true, false, null, false, false, null, comparedVersion: "1");

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            // The race must be detected by re-reading and re-validating ComparedVersion against the current
            // state, not by trusting the first snapshot.
            await _container.Value.Received(2).ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());

            // The confirmation is only meaningful if it was conditional on the ETag that was read.
            await _container.Value.Received(1).PatchItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<IReadOnlyList<PatchOperation>>(),
                Arg.Is<PatchItemRequestOptions>(options => options.IfMatchEtag == "\"etag-v1\""),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAGuardedConditionalDelete_WhenTheSearchedTargetHasAlreadyDisappeared_ThenPreconditionFailedIsThrownInsteadOfSilentSuccess()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "vanished-target";
            ResourceElement typedElement = observation.ToResourceElement();

            var deleteWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            deleteWrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("vanishedParam", "vanishedParam"), new NumberSearchValue(1)),
            };

            // Between the conditional search (which observed version "1" while the resource was still live)
            // and this guarded delete reaching persistence, a concurrent actor already soft-deleted the
            // resource, advancing it to a tombstone at version "2".
            var tombstoneObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            tombstoneObservation.Id = "vanished-target";
            tombstoneObservation.VersionId = "2";
            ResourceElement tombstoneElement = tombstoneObservation.ToResourceElement();
            var tombstoneWrapper = new ResourceWrapper(tombstoneElement, rawResourceFactory.Create(tombstoneElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            var existingTombstone = new FhirCosmosResourceWrapper(tombstoneWrapper);
            SetETag(existingTombstone, "\"etag-tombstone\"");

            ItemResponse<FhirCosmosResourceWrapper> readResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            readResponse.Resource.Returns(existingTombstone);

            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(readResponse));

            var operation = new ResourceWrapperOperation(deleteWrapper, true, false, null, false, false, null, comparedVersion: "1");

            // The disappeared target must fail the precondition authoritatively, the same outcome SQL Server
            // gives when a compared version no longer matches, rather than being silently treated as an
            // already-deleted no-op success.
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAGuardedDelete_WhenTheTargetHasEntirelyDisappeared_ThenPreconditionFailedIsThrownInsteadOfSilentSuccess()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "hard-deleted-target";
            observation.VersionId = "1";
            ResourceElement typedElement = observation.ToResourceElement();

            var deleteWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            deleteWrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("hardDeletedParam", "hardDeletedParam"), new NumberSearchValue(1)),
            };

            // The client read version "1" and sent it back as an If-Match on the delete, but the target has
            // since disappeared entirely (for example a concurrent hard delete or purge), so Cosmos DB's
            // point read now raises NotFound instead of returning a tombstone to compare against.
            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns<ItemResponse<FhirCosmosResourceWrapper>>(ci => throw CreateCosmosException(new Exception("NotFound"), HttpStatusCode.NotFound));

            var operation = new ResourceWrapperOperation(deleteWrapper, true, false, WeakETag.FromVersionId("1"), false, false, null);

            // A caller-supplied client ETag/If-Match on a delete whose target has entirely disappeared must fail
            // the precondition (matching SQL Server's guarded-disappearance behavior), not be silently treated
            // as an already-deleted no-op success.
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));
        }

        [Fact]
        public async Task GivenAnUnguardedDelete_WhenTheTargetHasEntirelyDisappeared_ThenTheDeleteRemainsIdempotent()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "already-gone-target";
            ResourceElement typedElement = observation.ToResourceElement();

            var deleteWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            deleteWrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("alreadyGoneParam", "alreadyGoneParam"), new NumberSearchValue(1)),
            };

            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns<ItemResponse<FhirCosmosResourceWrapper>>(ci => throw CreateCosmosException(new Exception("NotFound"), HttpStatusCode.NotFound));

            // No If-Match header at all - deleting a target that is already missing must remain the idempotent
            // no-op FHIR delete semantics require, regardless of the guarded case above.
            var operation = new ResourceWrapperOperation(deleteWrapper, true, false, null, false, false, null);

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            Assert.Null(outcome);
        }

        [Fact]
        public async Task GivenAGuardedDeleteOfAnAlreadyDeletedTarget_WhenTheNoOpResultRacesAConcurrentWrite_ThenPreconditionFailedIsThrownInsteadOfSilentSuccess()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "already-deleted-race-target";
            ResourceElement typedElement = observation.ToResourceElement();

            var deleteWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            deleteWrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("alreadyDeletedRaceParam", "alreadyDeletedRaceParam"), new NumberSearchValue(1)),
            };

            // The client read the tombstone at version "1" (via a conditional search) and sent it back as an
            // If-Match on this repeat delete. The target is already deleted, so this is a delete-of-a-delete
            // no-op - but that no-op decision must not be trusted from the stale pre-read alone.
            var firstTombstoneObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            firstTombstoneObservation.Id = "already-deleted-race-target";
            firstTombstoneObservation.VersionId = "1";
            ResourceElement firstTombstoneElement = firstTombstoneObservation.ToResourceElement();
            var firstTombstoneWrapper = new ResourceWrapper(firstTombstoneElement, rawResourceFactory.Create(firstTombstoneElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            var firstRead = new FhirCosmosResourceWrapper(firstTombstoneWrapper);
            SetETag(firstRead, "\"etag-tombstone-v1\"");

            // Simulate a concurrent writer that advances the tombstone to version "2" (for example, a purge
            // or an update racing the delete) between our read of version "1" and the moment the no-op
            // shortcut would otherwise silently return null without any native _etag write to prove it.
            var secondTombstoneObservation = Samples.GetDefaultObservation().ToPoco<Observation>();
            secondTombstoneObservation.Id = "already-deleted-race-target";
            secondTombstoneObservation.VersionId = "2";
            ResourceElement secondTombstoneElement = secondTombstoneObservation.ToResourceElement();
            var secondTombstoneWrapper = new ResourceWrapper(secondTombstoneElement, rawResourceFactory.Create(secondTombstoneElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            var secondRead = new FhirCosmosResourceWrapper(secondTombstoneWrapper);
            SetETag(secondRead, "\"etag-tombstone-v2\"");

            ItemResponse<FhirCosmosResourceWrapper> firstResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            firstResponse.Resource.Returns(firstRead);

            ItemResponse<FhirCosmosResourceWrapper> secondResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            secondResponse.Resource.Returns(secondRead);

            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(firstResponse), Task.FromResult(secondResponse));

            // A no-op reported as a success has to be settled against the current document, which takes a
            // conditional write: here the service rejects it because another writer already advanced the
            // tombstone past the ETag that was read.
            _container.Value.PatchItemAsync<FhirCosmosResourceWrapper>(
                    Arg.Any<string>(),
                    Arg.Any<PartitionKey>(),
                    Arg.Any<IReadOnlyList<PatchOperation>>(),
                    Arg.Any<PatchItemRequestOptions>(),
                    Arg.Any<CancellationToken>())
                .Returns<ItemResponse<FhirCosmosResourceWrapper>>(x => throw CreateCosmosException(new Exception("PreconditionFailed"), HttpStatusCode.PreconditionFailed));

            var operation = new ResourceWrapperOperation(deleteWrapper, true, false, WeakETag.FromVersionId("1"), false, false, null);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            // The race must be detected by re-reading and re-validating the If-Match guard against the
            // current state, not by trusting the first snapshot.
            await _container.Value.Received(2).ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>());

            // The confirmation is only meaningful if it was conditional on the ETag that was read.
            await _container.Value.Received(1).PatchItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<IReadOnlyList<PatchOperation>>(),
                Arg.Is<PatchItemRequestOptions>(options => options.IfMatchEtag == "\"etag-tombstone-v1\""),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUnguardedDeleteOfAnAlreadyDeletedTarget_WhenNoRaceGuardApplies_ThenTheNoOpSucceedsWithoutAnExtraWrite()
        {
            var rawResourceFactory = new RawResourceFactory(new FhirJsonSerializer());

            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = "already-deleted-unguarded-target";
            observation.VersionId = "1";
            ResourceElement typedElement = observation.ToResourceElement();

            var deleteWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            deleteWrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("alreadyDeletedUnguardedParam", "alreadyDeletedUnguardedParam"), new NumberSearchValue(1)),
            };

            var existingTombstoneWrapper = new ResourceWrapper(typedElement, rawResourceFactory.Create(typedElement, keepMeta: true, keepVersion: true), new ResourceRequest(HttpMethod.Delete, "http://fhir"), true, null, null, null);
            var existingTombstone = new FhirCosmosResourceWrapper(existingTombstoneWrapper);
            SetETag(existingTombstone, "\"etag-tombstone-v1\"");

            ItemResponse<FhirCosmosResourceWrapper> readResponse = Substitute.For<ItemResponse<FhirCosmosResourceWrapper>>();
            readResponse.Resource.Returns(existingTombstone);

            _container.Value.ReadItemAsync<FhirCosmosResourceWrapper>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<ItemRequestOptions>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(readResponse));

            // No If-Match header and no ComparedVersion guard - a delete of an already deleted target remains
            // a plain no-op and must not write anything at all to confirm a precondition that was never
            // supplied.
            var operation = new ResourceWrapperOperation(deleteWrapper, true, false, null, false, false, null);

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            Assert.Null(outcome);

            await _container.Value.DidNotReceiveWithAnyArgs().ReplaceItemAsync(
                Arg.Any<FhirCosmosResourceWrapper>(),
                Arg.Any<string>(),
                Arg.Any<PartitionKey?>(),
                Arg.Any<ItemRequestOptions>(),
                Arg.Any<CancellationToken>());

            await _container.Value.DidNotReceiveWithAnyArgs().PatchItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<IReadOnlyList<PatchOperation>>(),
                Arg.Any<PatchItemRequestOptions>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenPendingSearchParameterStatusWithPreviousUri_WhenPersisted_ThenCurrentAndPreviousDeletedStatusesAreUpserted()
        {
            var pendingStatus = new ResourceSearchParameterStatus
            {
                Uri = new Uri("http://hl7.org/fhir/SearchParameter/new-url"),
                PreviousUri = new Uri("http://hl7.org/fhir/SearchParameter/old-url"),
                Status = SearchParameterStatus.Supported,
                IsPartiallySupported = true,
                SortStatus = SortParameterStatus.Enabled,
                LastUpdated = DateTimeOffset.UtcNow,
            };

            var requestContext = Substitute.For<IFhirRequestContext>();
            var properties = new Dictionary<string, object>
            {
                [SearchParameterRequestContextPropertyNames.PendingStatus] = pendingStatus,
            };
            requestContext.Properties.Returns(properties);

            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(requestContext);

            var searchParameterStatusDataStore = Substitute.For<ISearchParameterStatusDataStore>();
            var dataStore = new CosmosFhirDataStore(
                _container,
                _cosmosDataStoreConfiguration,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                _cosmosQueryFactory,
                new RetryExceptionPolicyFactory(_cosmosDataStoreConfiguration, requestContextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                _bundleOrchestrator,
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()),
                ModelInfoProvider.Instance,
                searchParameterStatusDataStore,
                requestContextAccessor);

            var persistMethod = typeof(CosmosFhirDataStore).GetMethod("PersistPendingSearchParameterStatusUpdateAsync", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(persistMethod);

            await ((Task)persistMethod.Invoke(dataStore, [CancellationToken.None]));

            await searchParameterStatusDataStore.Received(1).UpsertStatuses(
                Arg.Is<IReadOnlyList<ResourceSearchParameterStatus>>(statuses =>
                    statuses.Count == 2 &&
                    statuses.Any(s => s.Uri == pendingStatus.Uri && s.Status == SearchParameterStatus.Supported) &&
                    statuses.Any(s =>
                        s.Uri == pendingStatus.PreviousUri &&
                        s.Status == SearchParameterStatus.Deleted &&
                        s.IsPartiallySupported == pendingStatus.IsPartiallySupported &&
                        s.SortStatus == pendingStatus.SortStatus &&
                        s.LastUpdated == pendingStatus.LastUpdated)),
                CancellationToken.None);

            Assert.False(properties.ContainsKey(SearchParameterRequestContextPropertyNames.PendingStatus));
        }

        [Fact]
        public async Task GivenAHardDeleteRequest_WhenPartiallySuccessful_ThenAnExceptionIsThrown()
        {
            var resourceKey = new ResourceKey(KnownResourceTypes.Patient, "test");

            var scripts = Substitute.For<Scripts>();
            scripts.ExecuteStoredProcedureAsync<int>(Arg.Any<string>(), Arg.Any<PartitionKey>(), Arg.Any<object[]>(), cancellationToken: Arg.Any<CancellationToken>()).Returns((x) =>
            {
                var response = Substitute.For<StoredProcedureExecuteResponse<int>>();
                response.Resource.Returns(1);
                return Task.FromResult(response);
            });
            _container.Value.Scripts.Returns(scripts);

            await Assert.ThrowsAsync<IncompleteDeleteException>(() => _dataStore.HardDeleteAsync(resourceKey, false, true, CancellationToken.None));
        }

        private void CreateResponses(int pageSize, string continuationToken, params FeedResponse<int>[] responses)
        {
            ICosmosQuery<int> cosmosQuery = Substitute.For<ICosmosQuery<int>>();
            _cosmosQueryFactory.Create<int>(
                    Arg.Any<Container>(),
                    Arg.Is<CosmosQueryContext>(ctx =>
                        ctx.FeedOptions.MaxItemCount == pageSize && ctx.ContinuationToken == continuationToken))
                .Returns(cosmosQuery);

            int yieldedIndex = -1;
            cosmosQuery.ExecuteNextAsync().ReturnsForAnyArgs(ci => responses[++yieldedIndex]);
            cosmosQuery.HasMoreResults.Returns(ci => responses[yieldedIndex].ContinuationToken != null);
        }

        private static FeedResponse<int> CreateFeedResponse(int start, int count, string continuationToken)
        {
            FeedResponse<int> feedResponse = Substitute.For<FeedResponse<int>>();
            feedResponse.Count.Returns(count);
            feedResponse.GetEnumerator().Returns(Enumerable.Range(start, count).GetEnumerator());
            feedResponse.ContinuationToken.Returns(continuationToken);
            return feedResponse;
        }

        private CosmosException CreateCosmosException(Exception innerException, HttpStatusCode? statusCode = null)
        {
            CosmosException cosmosException = null;
            if (statusCode.HasValue)
            {
                cosmosException = new CosmosException("message", statusCode.Value, 0, "id", 0.0);
            }
            else
            {
                cosmosException = (CosmosException)RuntimeHelpers.GetUninitializedObject(typeof(CosmosException));
            }

            var sampleException = new Exception(null, innerException);
            foreach (FieldInfo fieldInfo in typeof(Exception).GetFields(BindingFlags.Instance | BindingFlags.NonPublic))
            {
                if (typeof(Exception).IsAssignableFrom(fieldInfo.FieldType) &&
                    fieldInfo.GetValue(sampleException) == innerException)
                {
                    fieldInfo.SetValue(cosmosException, innerException);
                }
            }

            return cosmosException;
        }

        /// <summary>
        /// Sets the Cosmos <c>_etag</c> on a test <see cref="FhirCosmosResourceWrapper"/> via its protected
        /// setter so tests can control the ETag a mocked ReadItemAsync/ReplaceItemAsync exchange observes.
        /// </summary>
        private static void SetETag(FhirCosmosResourceWrapper wrapper, string etag)
        {
            typeof(FhirCosmosResourceWrapper)
                .GetProperty(nameof(FhirCosmosResourceWrapper.ETag), BindingFlags.Public | BindingFlags.Instance)
                .SetValue(wrapper, etag);
        }
    }
}
