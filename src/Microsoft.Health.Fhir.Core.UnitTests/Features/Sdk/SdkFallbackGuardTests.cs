// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
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

            var exception = Assert.Throws<InvalidOperationException>(() => guard.FirelyFallback("projection", "summary projection"));

            Assert.Contains("Firely fallback is not allowed in Ignixa SDK mode", exception.Message, StringComparison.Ordinal);
            Assert.Contains("projection", exception.Message, StringComparison.Ordinal);
            Assert.Contains("summary projection", exception.Message, StringComparison.Ordinal);
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

        private static SdkFallbackGuard CreateGuard(FhirSdkMode mode)
        {
            var modeProvider = new SdkModeProvider(new SdkConfiguration { Mode = mode });

            return new SdkFallbackGuard(modeProvider, NullLogger<SdkFallbackGuard>.Instance);
        }
    }
}
