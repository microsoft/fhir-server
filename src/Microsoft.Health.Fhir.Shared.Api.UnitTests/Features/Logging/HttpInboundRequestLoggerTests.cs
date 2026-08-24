// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Api.Features.Logging;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Features.Logging
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ServiceRuntimeState)]
    [Trait(Traits.Category, Categories.Throttling)]
    [Trait(Traits.Category, Categories.Web)]
    public class HttpInboundRequestLoggerTests
    {
        /// <summary>
        /// Verifies that a completed request contains only the selected HTTP dimensions.
        /// </summary>
        [Fact]
        public void GivenHttpContext_WhenLoggingRequest_ThenSelectedRequestDimensionsAreLogged()
        {
            var innerLogger = new CapturingLogger();
            var inboundRequestLogger = new HttpInboundRequestLogger(innerLogger);
            var context = new DefaultHttpContext();
            context.Request.Host = new HostString("fhir.example.com");
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = "/Patient";
            context.Response.StatusCode = StatusCodes.Status201Created;

            inboundRequestLogger.LogRequest(context);

            Assert.Equal(LogLevel.Information, innerLogger.LogLevel);
            Assert.Null(innerLogger.Exception);
            Assert.Equal(
                new Dictionary<string, object>
                {
                    ["duration"] = 0L,
                    ["httpHost"] = "fhir.example.com",
                    ["httpMethod"] = HttpMethods.Post,
                    ["httpPath"] = "/Patient",
                    ["httpStatusCode"] = StatusCodes.Status201Created,
                },
                innerLogger.State);
        }

        /// <summary>
        /// Verifies that an exception is attached and changes a success status to 500.
        /// </summary>
        [Theory]
        [InlineData(StatusCodes.Status200OK, StatusCodes.Status200OK)]
        [InlineData(StatusCodes.Status403Forbidden, StatusCodes.Status403Forbidden)]
        [InlineData(StatusCodes.Status503ServiceUnavailable, StatusCodes.Status503ServiceUnavailable)]
        public void GivenException_WhenLoggingRequest_ThenProcessingErrorStatusIsNormalized(
            int responseStatusCode,
            int expectedLogStatusCode)
        {
            var innerLogger = new CapturingLogger();
            var inboundRequestLogger = new HttpInboundRequestLogger(innerLogger);
            var context = new DefaultHttpContext();
            context.Response.StatusCode = responseStatusCode;
            var exception = new InvalidOperationException("Request failed");

            inboundRequestLogger.LogRequest(context, exception: exception);

            if (responseStatusCode >= 500 && exception != null)
            {
                Assert.Equal(LogLevel.Error, innerLogger.LogLevel);
            }
            else
            {
                Assert.Equal(LogLevel.Information, innerLogger.LogLevel);
            }

            Assert.Same(exception, innerLogger.Exception);
            Assert.Equal(expectedLogStatusCode, innerLogger.State["httpStatusCode"]);
        }

        private sealed class CapturingLogger : ILogger<HttpInboundRequestLogger>
        {
            public LogLevel LogLevel { get; private set; }

            public Exception Exception { get; private set; }

            public IReadOnlyDictionary<string, object> State { get; private set; }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
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
                LogLevel = logLevel;
                Exception = exception;

                var stateDictionary = new Dictionary<string, object>();

                if (state is IEnumerable<KeyValuePair<string, object>> convertedState)
                {
                    foreach (var item in convertedState)
                    {
                        stateDictionary.Add(item.Key, item.Value);
                    }
                }

                State = stateDictionary;
            }
        }
    }
}
