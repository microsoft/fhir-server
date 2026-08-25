// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Config
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public class FhirSdkProviderConfigurationTests
    {
        [Fact]
        public void GivenDefaultConfiguration_WhenProviderRead_ThenFirelyIsSelected()
        {
            var configuration = new CoreFeatureConfiguration();
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenIgnixaConfigured_WhenProviderRead_ThenIgnixaIsSelected()
        {
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration
                {
                    Default = FhirSdkProvider.Ignixa,
                },
            };
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveFhirPath);
        }

        [Fact]
        public void GivenSeamOverrides_WhenProvidersRead_ThenSelectionsAreIndependent()
        {
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration
                {
                    Default = FhirSdkProvider.Firely,
                    Import = FhirSdkProvider.Ignixa,
                },
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
        }
    }
}
