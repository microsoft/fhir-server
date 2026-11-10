// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Hl7.Fhir.ElementModel;
using Hl7.Fhir.Specification;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.Core.UnitTests.Extensions;
using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.FhirPackages
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchParameterPackageWriterTests
    {
        private const string PackageId = "partner.search";
        private const string Version = "1.2.3";
        private const string Integrity = "sha512-abc";
        private const string Url1 = "http://partner.example/SearchParameter/one";
        private const string Url2 = "http://partner.example/SearchParameter/two";

        private static readonly DateTimeOffset ReferenceSetLastUpdated = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        private readonly IFhirDataStore _dataStore = Substitute.For<IFhirDataStore>();
        private readonly ITransactionHandler _transactionHandler = Substitute.For<ITransactionHandler>();
        private readonly ITransactionScope _transactionScope = Substitute.For<ITransactionScope>();
        private readonly ISearchParameterPackageStoreCheck _storeCheck = Substitute.For<ISearchParameterPackageStoreCheck>();
        private readonly ISearchParameterPackageRecordStore _recordStore = Substitute.For<ISearchParameterPackageRecordStore>();
        private readonly IResourceWrapperFactory _resourceWrapperFactory = Substitute.For<IResourceWrapperFactory>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly RequestContextAccessor<IFhirRequestContext> _requestContextAccessor = Substitute.For<RequestContextAccessor<IFhirRequestContext>>();
        private readonly List<ResourceWrapperOperation> _mergedOperations = new List<ResourceWrapperOperation>();
        private readonly SqlServerSearchParameterPackageWriter _writer;

        public SqlServerSearchParameterPackageWriterTests()
        {
            ModelInfoProvider.SetProvider(MockModelInfoProviderBuilder.Create(FhirSpecification.R4).AddKnownTypes(KnownResourceTypes.SearchParameter).Build());

            _transactionHandler.BeginTransaction().Returns(_transactionScope);
            _storeCheck.ContainsDataAsync(Arg.Any<CancellationToken>()).Returns(false);
            _recordStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SearchParameterPackageRecord)null);

            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call => CreateWrapper(call.ArgAt<ResourceElement>(0)));

            _dataStore
                .MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    IReadOnlyList<ResourceWrapperOperation> operations = call.ArgAt<IReadOnlyList<ResourceWrapperOperation>>(0);
                    _mergedOperations.AddRange(operations);
                    return Outcome(operations, failed: null);
                });

            _writer = new SqlServerSearchParameterPackageWriter(
                _dataStore,
                _transactionHandler,
                _storeCheck,
                _recordStore,
                _resourceWrapperFactory,
                Substitute.For<IModelInfoProvider>(),
                _searchParameterOperations,
                _requestContextAccessor,
                NullLogger<SqlServerSearchParameterPackageWriter>.Instance);
        }

        public static IEnumerable<object[]> RetryableConflicts()
        {
            yield return new object[] { new JobConflictException("A reindex job is in progress.") };
            yield return new object[] { new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict) };
            yield return new object[] { new ResourceConflictException("Concurrent update.") };
            yield return new object[] { SqlExceptionFactory.GetSqlException(1205, "Transaction was deadlocked.") };
        }

        [Fact]
        public async Task GivenAnEmptyStore_WhenWritten_ThenEverySearchParameterIsCreatedAsEnabledInOneEnlistedAtomicMerge()
        {
            // Act
            await WriteAsync();

            // Assert
            await _dataStore.Received(1).MergeAsync(
                Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(),
                Arg.Is<MergeOptions>(x => x.EnlistInTransaction && x.IsBundleTransaction),
                Arg.Any<CancellationToken>());

            Assert.Equal(new[] { Url1, Url2 }, _mergedOperations.Select(x => x.PendingSearchParameterStatus.Uri.OriginalString));
            Assert.All(_mergedOperations, x =>
            {
                Assert.True(x.CreateOnly);
                Assert.True(x.AllowCreate);
                Assert.Null(x.WeakETag);
                Assert.Equal(SearchParameterStatus.Enabled, x.PendingSearchParameterStatus.Status);
                Assert.Equal(ReferenceSetLastUpdated, x.PendingSearchParameterStatus.LastUpdated);
                Assert.False(x.PendingSearchParameterStatus.IsPartiallySupported);
            });
        }

        [Fact]
        public async Task GivenAPackageSearchParameterWithoutIdOrMeta_WhenWritten_ThenTheStoredResourceHasAnIdAndLastUpdated()
        {
            // Arrange
            var created = new List<ResourceElement>();
            _resourceWrapperFactory
                .Create(Arg.Do<ResourceElement>(created.Add), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call => CreateWrapper(call.ArgAt<ResourceElement>(0)));

            // Act
            await WriteAsync();

            // Assert
            Assert.Equal(2, created.Count);
            Assert.All(created, x =>
            {
                Assert.False(string.IsNullOrWhiteSpace(x.Instance.Children("id").Single().Value as string));
                Assert.NotNull(x.Instance.Children("meta").Single().Children("lastUpdated").Single().Value);
            });
        }

        [Fact]
        public async Task GivenAnEmptyStore_WhenWritten_ThenTheRecordIsAddedInTheSameTransactionBeforeItCompletes()
        {
            // Act
            await WriteAsync();

            // Assert
            Received.InOrder(() =>
            {
                _transactionHandler.BeginTransaction();
                _storeCheck.ContainsDataAsync(Arg.Any<CancellationToken>());
                _dataStore.MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>());
                _recordStore.AddAsync(
                    Arg.Is<SearchParameterPackageRecord>(r =>
                        r.PackageId == PackageId
                        && r.Version == Version
                        && r.Integrity == Integrity
                        && r.CanonicalUrls.SequenceEqual(new[] { Url1, Url2 })),
                    Arg.Any<CancellationToken>());
                _transactionScope.Complete();
                _transactionScope.Dispose();
            });
        }

        [Fact]
        public async Task GivenAnEmptyStore_WhenWritten_ThenTheCacheIsRefreshedAndNoReindexIsStarted()
        {
            // Act
            await WriteAsync();

            // Assert
            await _searchParameterOperations.Received(1).GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>(), Arg.Any<bool>());
            Assert.DoesNotContain(_searchParameterOperations.ReceivedCalls(), x => x.GetMethodInfo().Name.Contains("Reindex", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task GivenAStoreWithData_WhenWritten_ThenTheStoreNotEmptyStatusIsReturnedAndNothingIsWritten()
        {
            // Arrange
            _storeCheck.ContainsDataAsync(Arg.Any<CancellationToken>()).Returns(true);

            // Act
            FhirPackageStoreNotEmptyException ex = await Assert.ThrowsAsync<FhirPackageStoreNotEmptyException>(WriteAsync);

            // Assert
            Assert.Contains(PackageId, ex.Message, StringComparison.Ordinal);
            await AssertNothingWrittenAsync();
            await _searchParameterOperations.DidNotReceiveWithAnyArgs().GetAndApplySearchParameterUpdates(default);
        }

        [Fact]
        public async Task GivenTheSamePackageContentIsInstalled_WhenWritten_ThenItIsANoOp()
        {
            // Arrange
            _recordStore.GetAsync(PackageId, Arg.Any<CancellationToken>()).Returns(Record(Version, Integrity));

            // Act
            await WriteAsync();

            // Assert
            _transactionHandler.DidNotReceive().BeginTransaction();
            await AssertNothingWrittenAsync();
            await _searchParameterOperations.DidNotReceiveWithAnyArgs().GetAndApplySearchParameterUpdates(default);
        }

        [Theory]
        [InlineData("2.0.0", Integrity)]
        [InlineData(Version, "sha512-other")]
        public async Task GivenDifferentPackageContentIsInstalled_WhenWritten_ThenANonRetryableConflictIsReturned(string installedVersion, string installedIntegrity)
        {
            // Arrange
            _recordStore.GetAsync(PackageId, Arg.Any<CancellationToken>()).Returns(Record(installedVersion, installedIntegrity));

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(WriteAsync);

            // Assert
            Assert.False(ex.IsRetryable);
            await AssertNothingWrittenAsync();
        }

        [Theory]
        [MemberData(nameof(RetryableConflicts))]
        public async Task GivenAConcurrentChangeOrReindex_WhenWritten_ThenARetryableConflictIsReturnedAndNothingIsCommitted(Exception conflict)
        {
            // Arrange
            _dataStore
                .MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(conflict);

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(WriteAsync);

            // Assert
            Assert.True(ex.IsRetryable);
            Assert.Same(conflict, ex.InnerException);
            await _recordStore.DidNotReceiveWithAnyArgs().AddAsync(default, default);
            _transactionScope.DidNotReceive().Complete();
            _transactionScope.Received(1).Dispose();
        }

        [Fact]
        public async Task GivenABadRequestThatIsNotAConcurrencyConflict_WhenWritten_ThenItIsNotTreatedAsRetryable()
        {
            // Arrange
            var badRequest = new BadRequestException("Some other validation failure.");
            _dataStore
                .MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(badRequest);

            // Act
            BadRequestException ex = await Assert.ThrowsAsync<BadRequestException>(WriteAsync);

            // Assert
            Assert.Same(badRequest, ex);
            _transactionScope.DidNotReceive().Complete();
        }

        [Fact]
        public async Task GivenAConflictCausedByAnotherWriterInstallingTheSamePackage_WhenWritten_ThenItIsANoOp()
        {
            // Arrange
            _recordStore.GetAsync(PackageId, Arg.Any<CancellationToken>()).Returns(null, Record(Version, Integrity));
            _dataStore
                .MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>())
                .ThrowsAsync(new BadRequestException(Core.Resources.SearchParameterConcurrencyConflict));

            // Act
            await WriteAsync();

            // Assert
            _transactionScope.DidNotReceive().Complete();
            await _recordStore.DidNotReceiveWithAnyArgs().AddAsync(default, default);
            await _searchParameterOperations.DidNotReceiveWithAnyArgs().GetAndApplySearchParameterUpdates(default);
        }

        [Fact]
        public async Task GivenASearchParameterResourceAlreadyExists_WhenWritten_ThenANonRetryableConflictIsReturnedAndNothingIsCommitted()
        {
            // Arrange
            _dataStore
                .MergeAsync(Arg.Any<IReadOnlyList<ResourceWrapperOperation>>(), Arg.Any<MergeOptions>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    IReadOnlyList<ResourceWrapperOperation> operations = call.ArgAt<IReadOnlyList<ResourceWrapperOperation>>(0);
                    return Outcome(operations, failed: operations[1]);
                });

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(WriteAsync);

            // Assert
            Assert.False(ex.IsRetryable);
            Assert.IsType<ResourceConflictException>(ex.InnerException);
            await _recordStore.DidNotReceiveWithAnyArgs().AddAsync(default, default);
            _transactionScope.DidNotReceive().Complete();
        }

        [Fact]
        public async Task GivenTheRecordWriteFails_WhenWritten_ThenTheTransactionIsNotCompleted()
        {
            // Arrange
            var failure = new InvalidOperationException("record write failed");
            _recordStore.AddAsync(Arg.Any<SearchParameterPackageRecord>(), Arg.Any<CancellationToken>()).ThrowsAsync(failure);

            // Act
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(WriteAsync);

            // Assert
            Assert.Same(failure, ex);
            _transactionScope.DidNotReceive().Complete();
            _transactionScope.Received(1).Dispose();
        }

        [Fact]
        public async Task GivenNoRequestContext_WhenWritten_ThenABackgroundContextIsUsedAndCleared()
        {
            // Arrange
            _requestContextAccessor.RequestContext = null;
            IFhirRequestContext contextDuringCreate = null;
            _resourceWrapperFactory
                .Create(Arg.Any<ResourceElement>(), Arg.Any<bool>(), Arg.Any<bool>(), Arg.Any<bool>())
                .Returns(call =>
                {
                    contextDuringCreate = _requestContextAccessor.RequestContext;
                    return CreateWrapper(call.ArgAt<ResourceElement>(0));
                });

            // Act
            await WriteAsync();

            // Assert
            Assert.NotNull(contextDuringCreate);
            Assert.True(contextDuringCreate.IsBackgroundTask);
            Assert.Null(_requestContextAccessor.RequestContext);
        }

        [Fact]
        public async Task GivenARequestContext_WhenWritten_ThenItIsUsedAndKept()
        {
            // Arrange
            var existing = new FhirRequestContext("POST", "https://localhost/x", "https://localhost/", "c", new Dictionary<string, StringValues>(), new Dictionary<string, StringValues>());
            _requestContextAccessor.RequestContext = existing;

            // Act
            await WriteAsync();

            // Assert
            Assert.Same(existing, _requestContextAccessor.RequestContext);
        }

        [Theory]
        [InlineData(Version, Integrity, true)]
        [InlineData("9.9.9", Integrity, false)]
        [InlineData(Version, "sha512-other", false)]
        [InlineData(Version, "SHA512-ABC", false)]
        public void GivenTwoRecords_WhenCompared_ThenOnlyTheSameIdVersionAndIntegrityMatch(string version, string integrity, bool expected)
        {
            // Arrange
            SearchParameterPackageRecord installed = Record(Version, Integrity);
            SearchParameterPackageRecord candidate = Record(version, integrity);

            // Act
            bool actual = installed.IsSameContentAs(candidate);

            // Assert
            Assert.Equal(expected, actual);
        }

        private static SearchParameterPackageRecord Record(string version, string integrity) =>
            new SearchParameterPackageRecord(PackageId, version, integrity, new[] { Url1, Url2 });

        private static MergeOutcome Outcome(IReadOnlyList<ResourceWrapperOperation> operations, ResourceWrapperOperation failed)
        {
            var results = new Dictionary<DataStoreOperationIdentifier, DataStoreOperationOutcome>();
            foreach (ResourceWrapperOperation operation in operations)
            {
                DataStoreOperationOutcome result = operation == failed
                    ? new DataStoreOperationOutcome(new ResourceConflictException("already exists"))
                    : new DataStoreOperationOutcome(new UpsertOutcome(operation.Wrapper, SaveOutcomeType.Created));
                results.Add(operation.GetIdentifier(), result);
            }

            return new MergeOutcome(failed == null ? MergeOutcomeFinalState.Completed : MergeOutcomeFinalState.CompletedWithFailures, results);
        }

        private static ResourceWrapper CreateWrapper(ResourceElement resource)
        {
            string url = (string)resource.Instance.Children("url").Single().Value;
            return new ResourceWrapper(
                url.Split('/').Last(),
                null,
                KnownResourceTypes.SearchParameter,
                new RawResource("{}", FhirResourceFormat.Json, isMetaSet: false),
                null,
                DateTimeOffset.UtcNow,
                deleted: false,
                searchIndices: null,
                compartmentIndices: null,
                lastModifiedClaims: null);
        }

        private Task WriteAsync()
        {
            var package = new FhirPackage(new FhirPackageDescriptor(PackageId, Version, Integrity), Array.Empty<FhirPackageEntry>());
            var searchParameters = new[] { new ResourceElement(new SearchParameterNode(Url1)), new ResourceElement(new SearchParameterNode(Url2)) };
            return _writer.WriteAsync(package, searchParameters, ReferenceSetLastUpdated, CancellationToken.None);
        }

        private async Task AssertNothingWrittenAsync()
        {
            await _dataStore.DidNotReceiveWithAnyArgs().MergeAsync(default, default, default);
            await _recordStore.DidNotReceiveWithAnyArgs().AddAsync(default, default);
            _transactionScope.DidNotReceive().Complete();
        }

        /// <summary>
        /// A minimal SearchParameter element with only a url, so this version-agnostic test project needs no FHIR model.
        /// </summary>
        private sealed class SearchParameterNode : ITypedElement
        {
            private readonly ITypedElement[] _children;

            public SearchParameterNode(string url)
            {
                _children = new ITypedElement[] { new PrimitiveNode("url", url) };
            }

            public string Name => KnownResourceTypes.SearchParameter;

            public string InstanceType => KnownResourceTypes.SearchParameter;

            public object Value => null;

            public string Location => KnownResourceTypes.SearchParameter;

            public IElementDefinitionSummary Definition => null;

            public IEnumerable<ITypedElement> Children(string name = null) =>
                _children.Where(x => name == null || x.Name == name);
        }

        private sealed class PrimitiveNode : ITypedElement
        {
            public PrimitiveNode(string name, string value)
            {
                Name = name;
                Value = value;
            }

            public string Name { get; }

            public string InstanceType => "uri";

            public object Value { get; }

            public string Location => Name;

            public IElementDefinitionSummary Definition => null;

            public IEnumerable<ITypedElement> Children(string name = null) => Enumerable.Empty<ITypedElement>();
        }
    }
}
