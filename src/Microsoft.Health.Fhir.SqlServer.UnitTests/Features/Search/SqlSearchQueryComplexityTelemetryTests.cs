// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Features.Search.Expressions;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.ValueSets;
using Microsoft.Health.Test.Utilities;
using Xunit;
using Expression = Microsoft.Health.Fhir.Core.Features.Search.Expressions.Expression;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Features.Search
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Search)]
    public class SqlSearchQueryComplexityTelemetryTests
    {
        private static readonly SearchParameterInfo IdParameter = new SearchParameterInfo(SearchParameterNames.Id, SearchParameterNames.Id, SearchParamType.Token);
        private static readonly SearchParameterInfo ResourceTypeParameter = new SearchParameterInfo(SearchParameterNames.ResourceType, SearchParameterNames.ResourceType, SearchParamType.Token);

        [Fact]
        public void GivenClientSearchRequest_WhenRecorded_ThenLogContainsRequestCorrelationFieldsAndHeadersAreSet()
        {
            var logger = new RecordingLogger();
            var requestContext = CreateRequestContext(AuditEventSubType.SearchType);
            SearchOptions searchOptions = CreateSearchOptions();

            SqlSearchQueryComplexityTelemetry.Record(searchOptions, requestContext, logger);

            LogEntry logEntry = Assert.Single(logger.Entries);
            Assert.Equal(LogLevel.Information, logEntry.LogLevel);
            Assert.Equal(1, logEntry.GetValue<int>("QueryComplexityScore"));
            Assert.Equal(SqlSearchQueryComplexityTier.Standard, logEntry.GetValue<SqlSearchQueryComplexityTier>("QueryComplexityTier"));
            Assert.Equal("correlation-id", logEntry.GetValue<string>("CorrelationId"));
            Assert.Equal(AuditEventSubType.SearchType, logEntry.GetValue<string>("FhirOperation"));
            Assert.Equal("GET", logEntry.GetValue<string>("RequestMethod"));
            Assert.Equal("SearchResources", logEntry.GetValue<string>("RouteName"));
            Assert.Equal("Patient", logEntry.GetValue<string>("ResourceType"));
            Assert.False(logEntry.GetValue<bool>("IsIncludesOperation"));
            Assert.False(logEntry.GetValue<bool>("IsCountOnly"));
            Assert.False(logEntry.GetValue<bool>("IsBackgroundTask"));
            Assert.Equal("1", requestContext.ResponseHeaders[KnownHeaders.QueryComplexityScore]);
            Assert.Equal(nameof(SqlSearchQueryComplexityTier.Standard), requestContext.ResponseHeaders[KnownHeaders.QueryComplexityTier]);
        }

        [Fact]
        public void GivenInternalNonSearchRequest_WhenRecorded_ThenLogContainsCorrelationFieldsAndHeadersAreNotSet()
        {
            var logger = new RecordingLogger();
            var requestContext = CreateRequestContext(AuditEventSubType.Read);
            SearchOptions searchOptions = CreateSearchOptions();

            SqlSearchQueryComplexityTelemetry.Record(searchOptions, requestContext, logger);

            LogEntry logEntry = Assert.Single(logger.Entries);
            Assert.Equal("correlation-id", logEntry.GetValue<string>("CorrelationId"));
            Assert.Equal(AuditEventSubType.Read, logEntry.GetValue<string>("FhirOperation"));
            Assert.False(requestContext.ResponseHeaders.ContainsKey(KnownHeaders.QueryComplexityScore));
            Assert.False(requestContext.ResponseHeaders.ContainsKey(KnownHeaders.QueryComplexityTier));
        }

        private static SearchOptions CreateSearchOptions()
        {
            return new SearchOptions
            {
                Expression = Expression.And(
                    Expression.SearchParameter(ResourceTypeParameter, Expression.StringEquals(FieldName.TokenCode, null, "Patient", false)),
                    Expression.SearchParameter(IdParameter, Expression.StringEquals(FieldName.TokenCode, null, "abc", false))),
                MaxItemCount = 10,
                IncludeCount = 10,
                IncludeTotal = TotalType.None,
                Sort = Array.Empty<(SearchParameterInfo, SortOrder)>(),
                UnsupportedSearchParams = Array.Empty<Tuple<string, string>>(),
            };
        }

        private static FhirRequestContext CreateRequestContext(string auditEventType)
        {
            return new FhirRequestContext(
                "GET",
                "https://localhost/Patient?_id=abc",
                "https://localhost/",
                "correlation-id",
                new Dictionary<string, StringValues>(),
                new Dictionary<string, StringValues>())
            {
                AuditEventType = auditEventType,
                ResourceType = "Patient",
                RouteName = "SearchResources",
            };
        }

        private sealed class RecordingLogger : ILogger
        {
            public List<LogEntry> Entries { get; } = new List<LogEntry>();

            public IDisposable BeginScope<TState>(TState state)
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                Entries.Add(new LogEntry(
                    logLevel,
                    state as IEnumerable<KeyValuePair<string, object>> ?? Enumerable.Empty<KeyValuePair<string, object>>()));
            }
        }

        private sealed class LogEntry
        {
            private readonly Dictionary<string, object> _values;

            public LogEntry(LogLevel logLevel, IEnumerable<KeyValuePair<string, object>> values)
            {
                LogLevel = logLevel;
                _values = values.ToDictionary(x => x.Key, x => x.Value);
            }

            public LogLevel LogLevel { get; }

            public T GetValue<T>(string key)
            {
                return (T)_values[key];
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new NullScope();

            public void Dispose()
            {
            }
        }
    }
}
