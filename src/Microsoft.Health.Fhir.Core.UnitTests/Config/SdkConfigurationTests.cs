// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Features.Sdk;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Web)]
    public class SdkConfigurationTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenModeProviderIsCreated_ThenHybridModeIsReturned()
        {
            var configuration = new SdkConfiguration();

            var provider = new SdkModeProvider(configuration);

            Assert.Equal(FhirSdkMode.Hybrid, provider.Mode);
            Assert.False(provider.IsFirelyMode);
            Assert.False(provider.IsIgnixaMode);
            Assert.True(provider.IsHybridMode);
        }

        [Theory]
        [InlineData(FhirSdkMode.Firely)]
        [InlineData(FhirSdkMode.Ignixa)]
        [InlineData(FhirSdkMode.Hybrid)]
        public void GivenSupportedMode_WhenModeProviderIsCreated_ThenModeIsReturned(FhirSdkMode mode)
        {
            var configuration = new SdkConfiguration { Mode = mode };

            var provider = new SdkModeProvider(configuration);

            Assert.Equal(mode, provider.Mode);
        }
    }
}
