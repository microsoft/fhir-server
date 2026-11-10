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
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.FhirPackages;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search.Parameters;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.FhirPackages;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.FhirPackages
{
    /// <summary>
    /// Covers the paths that finish before the write transaction. Paths that run SQL (the empty-store check, the merge,
    /// conflicts and rollback) are covered by the SQL integration tests.
    /// </summary>
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlServerSearchParameterPackageWriterTests
    {
        private const string PackageId = "partner.search";
        private const string Version = "1.2.3";
        private const string Integrity = "sha512-abc";
        private const string Url1 = "http://partner.example/SearchParameter/one";
        private const string Url2 = "http://partner.example/SearchParameter/two";

        private readonly IFhirDataStore _dataStore = Substitute.For<IFhirDataStore>();
        private readonly ITransactionHandler _transactionHandler = Substitute.For<ITransactionHandler>();
        private readonly ISearchParameterPackageRecordStore _recordStore = Substitute.For<ISearchParameterPackageRecordStore>();
        private readonly ISearchParameterOperations _searchParameterOperations = Substitute.For<ISearchParameterOperations>();
        private readonly SqlServerSearchParameterPackageWriter _writer;

        public SqlServerSearchParameterPackageWriterTests()
        {
            var config = Options.Create(new SqlServerDataStoreConfiguration());
            SqlRetryLogicBaseProvider retryProvider = SqlConfigurableRetryFactory.CreateNoneRetryProvider();

            _writer = new SqlServerSearchParameterPackageWriter(
                _dataStore,
                _transactionHandler,
                new SqlConnectionWrapperFactory(new SqlTransactionHandler(), Substitute.For<ISqlConnectionBuilder>(), retryProvider, config),
                Substitute.For<ISqlServerFhirModel>(),
                _recordStore,
                Substitute.For<IResourceWrapperFactory>(),
                Substitute.For<IModelInfoProvider>(),
                _searchParameterOperations,
                Substitute.For<RequestContextAccessor<IFhirRequestContext>>(),
                NullLogger<SqlServerSearchParameterPackageWriter>.Instance);
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
            await _searchParameterOperations.Received(1).GetAndApplySearchParameterUpdates(Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task GivenTheRecordLookupHitsATransportError_WhenWritten_ThenARetryableConflictIsReturned()
        {
            // Arrange
            SqlException transportError = SqlExceptionFactory.GetSqlException("A transport-level error has occurred when receiving results from the server.", null);
            _recordStore.GetAsync(PackageId, Arg.Any<CancellationToken>()).Returns<SearchParameterPackageRecord>(_ => throw transportError);

            // Act
            FhirPackageWriteConflictException ex = await Assert.ThrowsAsync<FhirPackageWriteConflictException>(WriteAsync);

            // Assert
            Assert.True(ex.IsRetryable);
            Assert.Same(transportError, ex.InnerException);
            _transactionHandler.DidNotReceive().BeginTransaction();
            await AssertNothingWrittenAsync();
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
            _transactionHandler.DidNotReceive().BeginTransaction();
            await AssertNothingWrittenAsync();
        }

        private static SearchParameterPackageRecord Record(string version, string integrity) =>
            new SearchParameterPackageRecord(PackageId, version, integrity, new[] { Url1, Url2 });

        private Task WriteAsync()
        {
            var package = new FhirPackage(new FhirPackageDescriptor(PackageId, Version, Integrity), Array.Empty<FhirPackageEntry>());
            var searchParameters = new[] { new ResourceElement(new SearchParameterNode(Url1)), new ResourceElement(new SearchParameterNode(Url2)) };
            return _writer.WriteAsync(package, searchParameters, DateTimeOffset.UtcNow, CancellationToken.None);
        }

        private async Task AssertNothingWrittenAsync()
        {
            await _dataStore.DidNotReceiveWithAnyArgs().MergeAsync(default, default, default);
            await _recordStore.DidNotReceiveWithAnyArgs().AddAsync(default, default);
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
