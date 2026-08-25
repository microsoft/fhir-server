// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using Hl7.Fhir.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
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
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Core.Configs;
using Microsoft.Health.Fhir.CosmosDb.Core.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Queries;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.CosmosDb.UnitTests.Features.Storage
{
    /// <summary>
    /// Covers the writes a guarded upsert reduced to a logical no-op is allowed to make. Every test runs against
    /// a simulated container that keeps the document as JSON and hands it over through the production serializer,
    /// so an assertion about the stored document is an assertion about what Cosmos DB would really be left with.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.DataSourceValidation)]
    public class CosmosFhirDataStoreGuardedNoOpTests
    {
        /// <summary>
        /// The number of times a guarded no-op may try to settle its precondition against the current document
        /// before it reports a failed precondition instead of trying again. Stated here independently of the
        /// data store so that moving the cap has to be a deliberate change to this contract.
        /// </summary>
        private const int GuardConfirmationAttemptCap = 5;

        private readonly IScoped<Container> _container;
        private readonly SimulatedCosmosContainer _simulatedContainer;
        private readonly CosmosFhirDataStore _dataStore;

        public CosmosFhirDataStoreGuardedNoOpTests()
        {
            _container = Substitute.For<Container>().CreateMockScope();
            _simulatedContainer = new SimulatedCosmosContainer(_container.Value);

            var cosmosDataStoreConfiguration = new CosmosDataStoreConfiguration();
            var fhirRequestContext = Substitute.For<IFhirRequestContext>();
            var requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
            requestContextAccessor.RequestContext.Returns(fhirRequestContext);

            var bundleOptions = Substitute.For<IOptions<BundleConfiguration>>();
            bundleOptions.Value.Returns(new BundleConfiguration { SupportsBundleOrchestrator = true });

            _dataStore = new CosmosFhirDataStore(
                _container,
                cosmosDataStoreConfiguration,
                Substitute.For<IOptionsMonitor<CosmosCollectionConfiguration>>(),
                Substitute.For<ICosmosQueryFactory>(),
                new RetryExceptionPolicyFactory(cosmosDataStoreConfiguration, requestContextAccessor, NullLogger<RetryExceptionPolicyFactory>.Instance),
                NullLogger<CosmosFhirDataStore>.Instance,
                Options.Create(new CoreFeatureConfiguration()),
                new BundleOrchestrator(bundleOptions, Substitute.For<ILogger<BundleOrchestrator>>()),
                new Lazy<ISupportedSearchParameterDefinitionManager>(Substitute.For<ISupportedSearchParameterDefinitionManager>()),
                ModelInfoProvider.Instance,
                Substitute.For<ISearchParameterStatusDataStore>(),
                requestContextAccessor);
        }

        [Fact]
        public async Task GivenAGuardedDeleteOfAnAlreadyDeletedTarget_WhenTheGuardIsConfirmed_ThenTheStoredDocumentIsLeftIntact()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("already-deleted", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            JObject documentBeforeDelete = _simulatedContainer.StoredDocument;

            // Deleting a resource that is already deleted is a no-op, but the If-Match still has to be honoured.
            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            Assert.Null(outcome);
            AssertDocumentUnchangedApartFromETag(documentBeforeDelete);
            Assert.Equal("1", _simulatedContainer.ReadStoredDocument().Version);

            // A no-op must not turn into a new version of the resource.
            _container.Value.DidNotReceiveWithAnyArgs().CreateTransactionalBatch(default);
        }

        [Fact]
        public async Task GivenAGuardedUpdateWithNoDataChange_WhenTheGuardIsConfirmed_ThenTheStoredDocumentIsLeftIntact()
        {
            ResourceWrapper resource = CreateResourceWrapper("unchanged-content", version: "1", deleted: false);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(resource));

            JObject documentBeforeUpdate = _simulatedContainer.StoredDocument;

            // Re-submitting identical content is a no-op, guarded here by the version a conditional update observed.
            var operation = new ResourceWrapperOperation(
                resource,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                comparedVersion: "1");

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            Assert.Equal(SaveOutcomeType.Updated, outcome.OutcomeType);
            Assert.Equal("1", outcome.Wrapper.Version);
            AssertDocumentUnchangedApartFromETag(documentBeforeUpdate);
            _container.Value.DidNotReceiveWithAnyArgs().CreateTransactionalBatch(default);
        }

        [Fact]
        public async Task GivenAGuardedNoOpOnAResourceVersionedByItsETag_WhenTheGuardIsConfirmed_ThenTheResourceVersionDoesNotChange()
        {
            // A resource that has never been updated is stored without a "version" property and reports its
            // _etag as its version, so confirming a guard against it must not leave the resource on a version
            // the caller never asked for.
            ResourceWrapper tombstone = CreateResourceWrapper("etag-versioned", version: null, deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone), versionedByETag: true);

            string versionBeforeDelete = _simulatedContainer.ReadStoredDocument().Version;
            string etagBeforeDelete = _simulatedContainer.StoredETag;
            Assert.Equal(etagBeforeDelete.Trim('"'), versionBeforeDelete);

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId(versionBeforeDelete),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            Assert.Null(outcome);
            Assert.Equal(versionBeforeDelete, _simulatedContainer.ReadStoredDocument().Version);
        }

        [Fact]
        public async Task GivenAGuardedNoOp_WhenTheReadWasStaleAndTheResourceHasMovedOn_ThenPreconditionFailedIsThrownAndNothingIsWritten()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("stale-read", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            // The read is served from a replica that has not caught up, so the guard passes against a version
            // that is no longer current. Only a write can settle that authoritatively.
            _simulatedContainer.ServeNextReadFromASnapshot();
            _simulatedContainer.SimulateConcurrentWrite(document => document[KnownResourceWrapperProperties.Version] = "2");

            JObject documentAfterTheConcurrentWrite = _simulatedContainer.StoredDocument;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            // The conflict is resolved by re-reading and re-validating against the current document, and the
            // rejected attempt leaves nothing behind.
            Assert.Equal(0, _simulatedContainer.WriteCount);
            Assert.Equal(documentAfterTheConcurrentWrite.ToString(Formatting.None), _simulatedContainer.StoredDocument.ToString(Formatting.None));
            await _container.Value.Received(2).ReadItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<ItemRequestOptions>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAGuardedNoOp_WhenTheTargetIsHardDeletedBeforeTheGuardIsConfirmed_ThenPreconditionFailedIsThrown()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("hard-deleted-under-us", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            // The tombstone is read, and is then purged before the guard can be confirmed against it.
            _simulatedContainer.ServeNextReadFromASnapshot();
            _simulatedContainer.SimulateConcurrentHardDelete();

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            // Re-reading finds nothing to compare the If-Match against, which is a failed precondition rather
            // than a raw storage error escaping to the caller.
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            await _container.Value.Received(2).ReadItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<ItemRequestOptions>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAnUnguardedDeleteOfAnAlreadyDeletedTarget_WhenNoGuardWasSupplied_ThenNothingIsWritten()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("unguarded-no-op", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            JObject documentBeforeDelete = _simulatedContainer.StoredDocument;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            UpsertOutcome outcome = await _dataStore.UpsertAsync(operation, CancellationToken.None);

            // There is no precondition to confirm, so there is nothing to write either.
            Assert.Null(outcome);
            Assert.Equal(0, _simulatedContainer.WriteCount);
            Assert.Equal(documentBeforeDelete.ToString(Formatting.None), _simulatedContainer.StoredDocument.ToString(Formatting.None));
        }

        [Fact]
        public async Task GivenAGuardedDeleteOfAnAlreadyDeletedTarget_WhenTheDocumentCarriesNoETagToConfirmAgainst_ThenPreconditionFailedIsThrownAndNothingIsWritten()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("delete-without-an-etag", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));
            _simulatedContainer.SimulateADocumentWithoutAnETag();

            JObject documentBeforeDelete = _simulatedContainer.StoredDocument;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            // There is nothing to make a write conditional on, so the precondition cannot be settled against
            // the current document. Reporting success would tell the caller their If-Match held when nothing
            // ever checked it against anything but a read, so the request fails closed.
            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            Assert.Equal(0, _simulatedContainer.WriteCount);
            Assert.Equal(documentBeforeDelete.ToString(Formatting.None), _simulatedContainer.StoredDocument.ToString(Formatting.None));

            // Failing closed must not mean writing unconditionally instead.
            await _container.Value.DidNotReceiveWithAnyArgs().PatchItemAsync<FhirCosmosResourceWrapper>(
                default,
                default,
                default,
                default,
                default);
        }

        [Fact]
        public async Task GivenAGuardedUpdateWithNoDataChange_WhenTheDocumentCarriesNoETagToConfirmAgainst_ThenPreconditionFailedIsThrownAndNothingIsWritten()
        {
            ResourceWrapper resource = CreateResourceWrapper("update-without-an-etag", version: "1", deleted: false);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(resource));
            _simulatedContainer.SimulateADocumentWithoutAnETag();

            JObject documentBeforeUpdate = _simulatedContainer.StoredDocument;

            var operation = new ResourceWrapperOperation(
                resource,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                comparedVersion: "1");

            await Assert.ThrowsAsync<PreconditionFailedException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            Assert.Equal(0, _simulatedContainer.WriteCount);
            Assert.Equal(documentBeforeUpdate.ToString(Formatting.None), _simulatedContainer.StoredDocument.ToString(Formatting.None));

            await _container.Value.DidNotReceiveWithAnyArgs().PatchItemAsync<FhirCosmosResourceWrapper>(
                default,
                default,
                default,
                default,
                default);
        }

        [Fact]
        public async Task GivenAGuardedDeleteOfAnAlreadyDeletedTarget_WhenEveryConfirmationLosesToAWriterThatKeepsTheVersion_ThenARetryableConflictIsThrownAfterACappedNumberOfAttempts()
        {
            ResourceWrapper tombstone = CreateResourceWrapper("contended-delete-no-op", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            // A competing writer keeps re-assigning the _etag without moving the resource version on, which is
            // exactly what another no-op settling its own precondition does. Every confirmation loses its race
            // and every re-read still finds the version the guard expects, so retrying can never converge
            // in-process. The supplied ETag was never stale - it matched on every single re-read - so the
            // cap must not be reported as a 412 PreconditionFailed/ResourceVersionConflict, which would falsely
            // tell the caller its version was wrong. It is a retryable conflict between equally-valid writers.
            _simulatedContainer.AConcurrentWriterWinsEveryRace = true;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            ResourceConflictException exception = await Assert.ThrowsAsync<ResourceConflictException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            // The classification, not just the CLR type, has to be right: this must not be mistaken for, or
            // rendered as, a stale-version precondition failure.
            Assert.IsNotType<PreconditionFailedException>(exception);
            Assert.DoesNotContain("did not match", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(OperationOutcomeConstants.IssueType.Conflict, exception.Issues.Select(issue => issue.Code));

            Assert.Equal(GuardConfirmationAttemptCap, _simulatedContainer.WriteAttemptCount);
            Assert.Equal(0, _simulatedContainer.WriteCount);
            await _container.Value.Received(GuardConfirmationAttemptCap).ReadItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<ItemRequestOptions>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAGuardedUpdateWithNoDataChange_WhenEveryConfirmationLosesToAWriterThatKeepsTheVersion_ThenARetryableConflictIsThrownAfterACappedNumberOfAttempts()
        {
            ResourceWrapper resource = CreateResourceWrapper("contended-update-no-op", version: "1", deleted: false);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(resource));

            // Same contention as the delete no-op above, driven through the ComparedVersion guard used by
            // updates whose content did not change: every attempt keeps finding the same, correct version.
            _simulatedContainer.AConcurrentWriterWinsEveryRace = true;

            var operation = new ResourceWrapperOperation(
                resource,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null,
                comparedVersion: "1");

            ResourceConflictException exception = await Assert.ThrowsAsync<ResourceConflictException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            Assert.IsNotType<PreconditionFailedException>(exception);
            Assert.DoesNotContain("did not match", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(OperationOutcomeConstants.IssueType.Conflict, exception.Issues.Select(issue => issue.Code));

            Assert.Equal(GuardConfirmationAttemptCap, _simulatedContainer.WriteAttemptCount);
            Assert.Equal(0, _simulatedContainer.WriteCount);
            await _container.Value.Received(GuardConfirmationAttemptCap).ReadItemAsync<FhirCosmosResourceWrapper>(
                Arg.Any<string>(),
                Arg.Any<PartitionKey>(),
                Arg.Any<ItemRequestOptions>(),
                Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenAGuardedNoOpThatExhaustsItsRetryBudget_WhenTheConflictIsReported_ThenTheMessageComesFromALocalizedResource()
        {
            // The contention message is user visible, so it must live in the resource file with every other
            // Cosmos DB message rather than being hardcoded at the throw site.
            ResourceWrapper tombstone = CreateResourceWrapper("localized-contention-no-op", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));
            _simulatedContainer.AConcurrentWriterWinsEveryRace = true;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId("1"),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            ResourceConflictException exception = await Assert.ThrowsAsync<ResourceConflictException>(() => _dataStore.UpsertAsync(operation, CancellationToken.None));

            string expectedDiagnostics = string.Format(
                CultureInfo.InvariantCulture,
                Microsoft.Health.Fhir.CosmosDb.Resources.NoOpGuardConfirmationConflict,
                tombstone.ResourceTypeName,
                tombstone.ResourceId,
                GuardConfirmationAttemptCap);

            Assert.Contains(expectedDiagnostics, exception.Issues.Select(issue => issue.Diagnostics));
        }

        [Fact]
        public async Task GivenAGuardedNoOpOnAResourceVersionedByItsETag_WhenTheGuardIsConfirmed_ThenTheDocumentStatesTheVersionItPreviouslyDerived()
        {
            // A resource that has never been updated is stored with no "version" property at all and derives
            // its version from its _etag. Confirming a precondition against it writes that derived value out
            // as an explicit property, because a new _etag would otherwise silently re-version the resource.
            ResourceWrapper tombstone = CreateResourceWrapper("etag-versioned-shape", version: null, deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone), versionedByETag: true);

            JObject documentBeforeDelete = _simulatedContainer.StoredDocument;
            string versionBeforeDelete = _simulatedContainer.ReadStoredDocument().Version;
            Assert.Null(documentBeforeDelete[KnownResourceWrapperProperties.Version]);

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                WeakETag.FromVersionId(versionBeforeDelete),
                requireETagOnUpdate: false,
                keepVersion: false,
                bundleResourceContext: null);

            Assert.Null(await _dataStore.UpsertAsync(operation, CancellationToken.None));

            // The document shape changes - it now states its version instead of implying it - while the
            // version the resource reports, before and after, is the same string.
            JObject documentAfterDelete = _simulatedContainer.StoredDocument;
            Assert.Equal(versionBeforeDelete, (string)documentAfterDelete[KnownResourceWrapperProperties.Version]);
            Assert.Equal(versionBeforeDelete, _simulatedContainer.ReadStoredDocument().Version);
            Assert.NotEqual((string)documentBeforeDelete[KnownDocumentProperties.ETag], (string)documentAfterDelete[KnownDocumentProperties.ETag]);
        }

        [Fact]
        public async Task GivenAnAlreadyDeletedTargetAndAVersionedUpdatePolicy_WhenNoIfMatchIsSupplied_ThenTheDeleteStaysANoOp()
        {
            // Deleting a target that is already a tombstone leaves no new version behind, so the versioned
            // update policy has nothing to guard and an absent If-Match is not rejected here.
            ResourceWrapper tombstone = CreateResourceWrapper("versioned-policy-no-op", version: "1", deleted: true);
            _simulatedContainer.StoreDocument(new FhirCosmosResourceWrapper(tombstone));

            JObject documentBeforeDelete = _simulatedContainer.StoredDocument;

            var operation = new ResourceWrapperOperation(
                tombstone,
                allowCreate: true,
                keepHistory: true,
                weakETag: null,
                requireETagOnUpdate: true,
                keepVersion: false,
                bundleResourceContext: null);

            Assert.Null(await _dataStore.UpsertAsync(operation, CancellationToken.None));

            Assert.Equal(0, _simulatedContainer.WriteCount);
            Assert.Equal(documentBeforeDelete.ToString(Formatting.None), _simulatedContainer.StoredDocument.ToString(Formatting.None));
        }

        /// <summary>
        /// Regression test for a type-design defect: <see cref="GuardConfirmationResult"/> must fail closed when a
        /// result is never explicitly assigned (e.g. a defaulted field or local). Before the enum values were
        /// reordered, <see cref="GuardConfirmationResult.Confirmed"/> was the zero/default member, so any code path
        /// that forgot to assign a result would silently behave as a confirmed guard instead of failing closed.
        /// This assertion is deterministic - it never depends on Cosmos DB behavior, timing, or retries - and is
        /// the guard against that defect ever coming back.
        /// </summary>
        [Fact]
        public void GivenAnUnassignedGuardConfirmationResult_WhenReadAsItsDefaultValue_ThenItIsUnconfirmableSoTheCallerFailsClosed()
        {
            Assert.Equal(GuardConfirmationResult.Unconfirmable, default(GuardConfirmationResult));
            Assert.Equal(0, (int)GuardConfirmationResult.Unconfirmable);
            Assert.NotEqual(GuardConfirmationResult.Confirmed, default(GuardConfirmationResult));
            Assert.NotEqual(GuardConfirmationResult.Superseded, default(GuardConfirmationResult));
        }

        private static ResourceWrapper CreateResourceWrapper(string id, string version, bool deleted)
        {
            var observation = Samples.GetDefaultObservation().ToPoco<Observation>();
            observation.Id = id;
            observation.VersionId = version;
            ResourceElement typedElement = observation.ToResourceElement();

            var wrapper = new ResourceWrapper(
                typedElement,
                new RawResourceFactory(new FhirJsonSerializer()).Create(typedElement, keepMeta: true, keepVersion: true),
                new ResourceRequest(deleted ? HttpMethod.Delete : HttpMethod.Put, "http://fhir"),
                deleted,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);

            wrapper.SearchIndices = new List<SearchIndexEntry>
            {
                new SearchIndexEntry(new SearchParameterInfo("code", "code"), new StringSearchValue("body-weight")),
                new SearchIndexEntry(new SearchParameterInfo("value-quantity", "value-quantity"), new NumberSearchValue(67)),
            };

            return wrapper;
        }

        /// <summary>
        /// Asserts that the stored document still holds every field it held before, with the exception of the
        /// <c>_etag</c>, which any write - including one made purely to confirm a precondition - reassigns.
        /// </summary>
        /// <param name="documentBeforeTheOperation">The document as it was stored before the operation ran.</param>
        private void AssertDocumentUnchangedApartFromETag(JObject documentBeforeTheOperation)
        {
            JObject documentAfterTheOperation = _simulatedContainer.StoredDocument;

            documentBeforeTheOperation.Remove(KnownDocumentProperties.ETag);
            documentAfterTheOperation.Remove(KnownDocumentProperties.ETag);

            Assert.Equal(documentBeforeTheOperation.ToString(Formatting.None), documentAfterTheOperation.ToString(Formatting.None));
        }
    }
}
