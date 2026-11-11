// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.SqlServer.Features.Search.SemanticSearch;
using Microsoft.Health.Fhir.SqlServer.Features.Storage;
using Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Storage;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search.SemanticSearch
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public sealed class SqlApproximateVectorSearchProviderTests
    {
        private readonly ISqlRetryService _sqlRetryService = Substitute.For<ISqlRetryService>();
        private readonly RecordingLogger _logger = new RecordingLogger();
        private readonly FakeTimeProvider _timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 10, 12, 12, 0, 0, TimeSpan.Zero));
        private readonly VectorSearchConfiguration _configuration = new VectorSearchConfiguration
        {
            Enabled = true,
            Query = { ApproximateSearch = VectorSearchApproximateMode.Auto, ApproximateCandidateMultiplier = 7 },
        };

        [Theory]
        [InlineData(false, VectorSearchApproximateMode.Auto)]
        [InlineData(true, VectorSearchApproximateMode.Disabled)]
        public async Task GivenVectorSearchOrApproximateSearchIsOff_WhenCandidateMultiplierRequested_ThenDatabaseIsNotQueried(
            bool vectorSearchEnabled,
            VectorSearchApproximateMode mode)
        {
            // Arrange
            _configuration.Enabled = vectorSearchEnabled;
            _configuration.Query.ApproximateSearch = mode;
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act
            int? multiplier = await provider.GetCandidateMultiplierAsync(CancellationToken.None);

            // Assert
            Assert.Null(multiplier);
            Assert.Empty(_sqlRetryService.ReceivedCalls());
        }

        [Fact]
        public async Task GivenCompatibleIndexExists_WhenCandidateMultiplierRequestedRepeatedly_ThenMultiplierIsReturnedAndIndexIsDetectedOnce()
        {
            // Arrange
            List<(string Name, object Value)> probeParameters = ArrangeProbe(isIndexAvailable: true);
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act
            int? first = await provider.GetCandidateMultiplierAsync(CancellationToken.None);
            _timeProvider.Advance(TimeSpan.FromDays(1));
            int? second = await provider.GetCandidateMultiplierAsync(CancellationToken.None);

            // Assert
            Assert.Equal(7, first);
            Assert.Equal(7, second);
            Assert.Equal(1, ProbeCount());
            Assert.Contains(("@TableName", "dbo.VectorSearchParam"), probeParameters);
            Assert.Contains(("@ColumnName", "Embedding"), probeParameters);
            Assert.Contains(("@DistanceMetric", "cosine"), probeParameters);
            Assert.Contains(("@MinimumVersion", 3), probeParameters);
            Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Information);
        }

        [Fact]
        public async Task GivenNoCompatibleIndex_WhenCandidateMultiplierRequested_ThenQueriesRankExactlyUntilTheRecheckInterval()
        {
            // Arrange
            bool isIndexAvailable = false;
            ArrangeProbe(() => isIndexAvailable);
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act
            int? beforeIndex = await provider.GetCandidateMultiplierAsync(CancellationToken.None);
            isIndexAvailable = true;
            _timeProvider.Advance(SqlApproximateVectorSearchProvider.UnavailableRecheckInterval - TimeSpan.FromSeconds(1));
            int? beforeRecheck = await provider.GetCandidateMultiplierAsync(CancellationToken.None);
            _timeProvider.Advance(TimeSpan.FromSeconds(1));
            int? afterRecheck = await provider.GetCandidateMultiplierAsync(CancellationToken.None);

            // Assert
            Assert.Null(beforeIndex);
            Assert.Null(beforeRecheck);
            Assert.Equal(7, afterRecheck);
            Assert.Equal(2, ProbeCount());
            Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task GivenIndexStaysUnavailable_WhenRechecked_ThenWarningIsNotRepeated()
        {
            // Arrange
            ArrangeProbe(isIndexAvailable: false);
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act
            await provider.GetCandidateMultiplierAsync(CancellationToken.None);
            _timeProvider.Advance(SqlApproximateVectorSearchProvider.UnavailableRecheckInterval);
            await provider.GetCandidateMultiplierAsync(CancellationToken.None);

            // Assert
            Assert.Equal(2, ProbeCount());
            Assert.Single(_logger.Entries, entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task GivenDetectionFails_WhenCandidateMultiplierRequested_ThenQueriesRankExactlyAndDetectionRetriesLater()
        {
            // Arrange
            bool fail = true;
            _sqlRetryService.ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, bool>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(_ => fail
                    ? Task.FromException<IReadOnlyList<bool>>(SqlExceptionFactory.GetSqlException(208, "Invalid object name 'sys.vector_indexes'."))
                    : Task.FromResult<IReadOnlyList<bool>>(new[] { true }));
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act
            int? failed = await provider.GetCandidateMultiplierAsync(CancellationToken.None);
            fail = false;
            _timeProvider.Advance(SqlApproximateVectorSearchProvider.UnavailableRecheckInterval);
            int? recovered = await provider.GetCandidateMultiplierAsync(CancellationToken.None);

            // Assert
            Assert.Null(failed);
            Assert.Equal(7, recovered);
            Assert.Contains(_logger.Entries, entry => entry.Level == LogLevel.Warning && entry.Exception is SqlException);
        }

        [Fact]
        public async Task GivenDetectionIsCancelled_WhenCandidateMultiplierRequested_ThenCancellationPropagates()
        {
            // Arrange
            _sqlRetryService.ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, bool>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(_ => Task.FromException<IReadOnlyList<bool>>(new OperationCanceledException()));
            SqlApproximateVectorSearchProvider provider = CreateProvider();

            // Act and assert
            await Assert.ThrowsAsync<OperationCanceledException>(() => provider.GetCandidateMultiplierAsync(CancellationToken.None));
            Assert.Empty(_logger.Entries);
        }

        private SqlApproximateVectorSearchProvider CreateProvider()
        {
            return new SqlApproximateVectorSearchProvider(_sqlRetryService, Options.Create(_configuration), _logger, _timeProvider);
        }

        private List<(string Name, object Value)> ArrangeProbe(bool isIndexAvailable)
        {
            return ArrangeProbe(() => isIndexAvailable);
        }

        private List<(string Name, object Value)> ArrangeProbe(Func<bool> isIndexAvailable)
        {
            var parameters = new List<(string Name, object Value)>();
            _sqlRetryService.ExecuteReaderAsync(
                    Arg.Any<SqlCommand>(),
                    Arg.Any<Func<SqlDataReader, bool>>(),
                    Arg.Any<ILogger>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>(),
                    Arg.Any<bool>())
                .Returns(call =>
                {
                    // The provider disposes its command after the call, so read it now.
                    SqlCommand command = call.Arg<SqlCommand>();
                    Assert.Contains("sys.vector_indexes", command.CommandText, StringComparison.Ordinal);
                    Assert.True(call.ArgAt<bool>(5), "Detection must be a read-only call.");
                    parameters.Clear();
                    parameters.AddRange(command.Parameters.Cast<SqlParameter>().Select(parameter => (parameter.ParameterName, parameter.Value)));
                    return Task.FromResult<IReadOnlyList<bool>>(new[] { isIndexAvailable() });
                });
            return parameters;
        }

        private int ProbeCount()
        {
            return _sqlRetryService.ReceivedCalls().Count();
        }

        private sealed class RecordingLogger : ILogger<SqlApproximateVectorSearchProvider>
        {
            public List<(LogLevel Level, Exception Exception)> Entries { get; } = new List<(LogLevel, Exception)>();

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                Entries.Add((logLevel, exception));
            }
        }
    }
}
