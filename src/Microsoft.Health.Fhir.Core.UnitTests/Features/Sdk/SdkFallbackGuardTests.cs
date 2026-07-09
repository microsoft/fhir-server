// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Sdk
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SdkFallbackGuardTests
    {
        [Fact]
        public void GivenIgnixaMode_WhenFirelyFallbackIsRequested_ThenInvalidOperationExceptionIsThrown()
        {
            var guard = CreateGuard(FhirSdkMode.Ignixa);

            var exception = Assert.Throws<InvalidOperationException>(() => guard.FirelyFallback("projection", "native projection missing"));

            Assert.Contains("Firely fallback is not allowed in Ignixa SDK mode", exception.Message, StringComparison.Ordinal);
            Assert.Contains("projection", exception.Message, StringComparison.Ordinal);
            Assert.Contains("native projection missing", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void GivenHybridMode_WhenFirelyFallbackIsRequested_ThenDiagnosticsAreLogged()
        {
            var logger = new TestLogger<SdkFallbackGuard>();
            var guard = CreateGuard(FhirSdkMode.Hybrid, logger);

            guard.FirelyFallback("projection", "native projection missing");

            var message = Assert.Single(logger.Messages);
            Assert.Contains("Firely SDK fallback used", message, StringComparison.Ordinal);
            Assert.Contains("projection", message, StringComparison.Ordinal);
            Assert.Contains("native projection missing", message, StringComparison.Ordinal);
            Assert.Contains("Hybrid", message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(FhirSdkMode.Firely)]
        [InlineData(FhirSdkMode.Hybrid)]
        public void GivenFirelyOrHybridMode_WhenFirelyFallbackIsRequested_ThenNoExceptionIsThrown(FhirSdkMode mode)
        {
            var guard = CreateGuard(mode);

            guard.FirelyFallback("projection", "summary projection");
        }

        [Fact]
        public void GivenFirelyMode_WhenIgnixaFallbackIsRequested_ThenInvalidOperationExceptionIsThrown()
        {
            var guard = CreateGuard(FhirSdkMode.Firely);

            var exception = Assert.Throws<InvalidOperationException>(() => guard.IgnixaFallback("parsing", "Ignixa adapter"));

            Assert.Contains("Ignixa fallback is not allowed in Firely SDK mode", exception.Message, StringComparison.Ordinal);
            Assert.Contains("parsing", exception.Message, StringComparison.Ordinal);
            Assert.Contains("Ignixa adapter", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(FhirSdkMode.Ignixa)]
        [InlineData(FhirSdkMode.Hybrid)]
        public void GivenIgnixaOrHybridMode_WhenIgnixaFallbackIsRequested_ThenNoExceptionIsThrown(FhirSdkMode mode)
        {
            var guard = CreateGuard(mode);

            guard.IgnixaFallback("parsing", "Ignixa adapter");
        }

        private static SdkFallbackGuard CreateGuard(FhirSdkMode mode, ILogger<SdkFallbackGuard> logger = null)
        {
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = mode });

            return new SdkFallbackGuard(modeProvider, logger ?? NullLogger<SdkFallbackGuard>.Instance);
        }

        private sealed class TestLogger<T> : ILogger<T>
        {
            public List<string> Messages { get; } = new List<string>();

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
                Messages.Add(formatter(state, exception));
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
}
