// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Telemetry;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using OpenTelemetry.Logs;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Web.UnitTests
{
    public class AzureMonitorOpenTelemetryLogEnricherTests
    {
        private const string DefaultClusterValue = "wus2";
        private const string DefaultPublishAddressValue = "1.0.0.0";
        private const string DefaultEnvironmentGroupName = "envGroupName";
        private const string DefaultEnvironmentName = "envName";
        private const string DefaultSystemSubscriptionName = "systemSub";
        private const string DefaultProductVersion = "1.0.0.0";
        private const string DefaultRouteValueAction = "Search";
        private const string DefaultRouteValueControler = "Fhir";

        private readonly AzureMonitorOpenTelemetryLogEnricher _enricher;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly HttpContext _httpContext;

        public AzureMonitorOpenTelemetryLogEnricherTests()
        {
            _httpContextAccessor = Substitute.For<IHttpContextAccessor>();
            _httpContext = Substitute.For<HttpContext>();
            _httpContextAccessor.HttpContext.Returns(_httpContext);
            _httpContext.Request.Returns(Substitute.For<HttpRequest>());
            _enricher = new AzureMonitorOpenTelemetryLogEnricher(_httpContextAccessor);
        }

        [Fact]
        public void GivenRequest_WhenOperationNameIsAbsent_ThenOperationNameShouldBeAddedWithMethodAndPath()
        {
            _httpContext.Request.RouteValues.ReturnsNull();
            _httpContext.Request.Method = "GET";
            _httpContext.Request.Path = $"/{Guid.NewGuid()}/health/check";

            var operationName = $"{_httpContext.Request.Method} {_httpContext.Request.Path}";
            LogRecord log = CreateLogRecord();

            var attributes = new Dictionary<string, object>(log.Attributes);
            _enricher.OnEnd(log);

            Assert.Equal(attributes.Count + 1, log.Attributes.Count);
            Assert.Equal(
                operationName,
                log.Attributes.SingleOrDefault(kv => kv.Key == KnownApplicationInsightsDimensions.OperationName).Value);
        }

        [Fact]
        public void GivenRequest_WhenOperationNameIsAbsent_ThenOperationNameShouldBeAddedWithRouteValues()
        {
            var routeValues = RouteValueDictionary.FromArray(new[]
            {
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueController, DefaultRouteValueControler),
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueAction, DefaultRouteValueAction),
            });

            _httpContext.Request.RouteValues.Returns(routeValues);
            _httpContext.Request.Method = "GET";
            _httpContext.Request.Path = string.Empty;

            var operationName = $"{_httpContext.Request.Method} {routeValues[KnownHttpRequestProperties.RouteValueController]}/{routeValues[KnownHttpRequestProperties.RouteValueAction]}";
            LogRecord log = CreateLogRecord();

            var attributes = new Dictionary<string, object>(log.Attributes);
            _enricher.OnEnd(log);

            Assert.Equal(attributes.Count + 1, log.Attributes.Count);
            Assert.Equal(
                operationName,
                log.Attributes.SingleOrDefault(kv => kv.Key == KnownApplicationInsightsDimensions.OperationName).Value);
        }

        [Fact]
        public void GivenRequest_WhenOperationNameIsAbsent_ThenOperationNameShouldBeAddedWithRouteValueAndParameters()
        {
            var routeValues = RouteValueDictionary.FromArray(new[]
            {
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueController, DefaultRouteValueControler),
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueAction, "SearchCompartmentByResourceType"),
                new KeyValuePair<string, object>("typeParameter", Guid.NewGuid().ToString()),
                new KeyValuePair<string, object>("idParameter", Guid.NewGuid().ToString()),
                new KeyValuePair<string, object>("compartmentTypeParameter", Guid.NewGuid().ToString()),
            });

            _httpContext.Request.RouteValues.Returns(routeValues);
            _httpContext.Request.Method = "GET";
            _httpContext.Request.Path = "/health/check";

            var parameters = string.Join("/", routeValues.Keys.Where(k => k.EndsWith(KnownHttpRequestProperties.RouteValueParameterSuffix, StringComparison.OrdinalIgnoreCase)).OrderBy(k => k));
            var operationName = $"{_httpContext.Request.Method} {routeValues[KnownHttpRequestProperties.RouteValueController]}/{routeValues[KnownHttpRequestProperties.RouteValueAction]} [{parameters}]";
            LogRecord log = CreateLogRecord();

            var attributes = new Dictionary<string, object>(log.Attributes);
            _enricher.OnEnd(log);

            Assert.Equal(attributes.Count + 1, log.Attributes.Count);
            Assert.Equal(
                operationName,
                log.Attributes.SingleOrDefault(kv => kv.Key == KnownApplicationInsightsDimensions.OperationName).Value);
        }

        [Fact]
        public void GivenRequest_WhenOperationNameIsPresentAndEmpty_ThenOperationNameShouldBeAdded()
        {
            var routeValues = RouteValueDictionary.FromArray(new[]
            {
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueController, DefaultRouteValueControler),
                new KeyValuePair<string, object>(KnownHttpRequestProperties.RouteValueAction, DefaultRouteValueAction),
            });

            _httpContext.Request.RouteValues.Returns(routeValues);
            _httpContext.Request.Method = "GET";
            _httpContext.Request.Path = string.Empty;

            var operationName = $"{_httpContext.Request.Method} {routeValues[KnownHttpRequestProperties.RouteValueController]}/{routeValues[KnownHttpRequestProperties.RouteValueAction]}";
            var originalAttributes = new Dictionary<string, object>();
            originalAttributes.Add(KnownApplicationInsightsDimensions.OperationName, null);
            LogRecord log = CreateLogRecord(
                DateTime.UtcNow,
                Guid.NewGuid().ToString(),
                LogLevel.Information,
                new EventId(1),
                "Creating a log record",
                null,
                originalAttributes.ToList());

            _enricher.OnEnd(log);
            Assert.Equal(
                operationName,
                log.Attributes.Where(kv => kv.Key == KnownApplicationInsightsDimensions.OperationName).Select(kv => kv.Value).FirstOrDefault());
        }

        [Fact]
        public void GivenRequest_WhenOperationNameIsAlreadySet_ThenOperationNameShouldNoteBeChanged()
        {
            var operationName = "MyOperationName";
            var originalAttributes = new Dictionary<string, object>
            {
                { KnownApplicationInsightsDimensions.OperationName, operationName },
            };

            LogRecord log = CreateLogRecord(
                DateTime.UtcNow,
                Guid.NewGuid().ToString(),
                LogLevel.Information,
                new EventId(1),
                "Creating a log record",
                null,
                originalAttributes.ToList());

            _enricher.OnEnd(log);
            Assert.Equal(
                operationName,
                log.Attributes.Where(kv => kv.Key == KnownApplicationInsightsDimensions.OperationName).Select(kv => kv.Value).FirstOrDefault());
        }

        [Fact]
        public void GivenRequest_WhenOperatoinNameIsAbsentAndHttpContextIsNull_ThenOperationNameShouldNotBeAdded()
        {
            _httpContextAccessor.HttpContext.ReturnsNull();
            LogRecord log = CreateLogRecord();

            _enricher.OnEnd(log);
            Assert.DoesNotContain(log.Attributes, kv => kv.Key == KnownApplicationInsightsDimensions.OperationName);
        }

        public static LogRecord CreateLogRecord()
        {
            var attributes = new List<KeyValuePair<string, object>>
            {
                new KeyValuePair<string, object>("key1", "value1"),
                new KeyValuePair<string, object>("key2", "value2"),
                new KeyValuePair<string, object>("key3", "value3"),
                new KeyValuePair<string, object>("key4", "value4"),
                new KeyValuePair<string, object>("key5", "value5"),
            };

            return CreateLogRecord(
                DateTime.UtcNow,
                Guid.NewGuid().ToString(),
                LogLevel.Information,
                new EventId(1),
                "Creating a log record",
                null,
                attributes);
        }

        public static LogRecord CreateLogRecord(
            DateTime timestamp,
            string categoryName,
            LogLevel logLevel,
            EventId eventId,
            string formattedMessage,
            Exception exception,
            IReadOnlyList<KeyValuePair<string, object>> attributes)
        {
            var logRecord = (LogRecord)Activator.CreateInstance(typeof(LogRecord), true);
            logRecord.Timestamp = timestamp;
            logRecord.CategoryName = categoryName;
            logRecord.LogLevel = logLevel;
            logRecord.EventId = eventId;
            logRecord.FormattedMessage = formattedMessage;
            logRecord.Exception = exception;
            logRecord.Attributes = attributes;
            return logRecord;
        }
    }
}
