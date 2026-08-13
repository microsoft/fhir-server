// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
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
        public void GivenDefaultConfiguration_WhenSeamsRead_ThenEverySeamIsFirely()
        {
            var configuration = new CoreFeatureConfiguration();

            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.Default);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveSerialization);
        }

        [Fact]
        public void GivenOnlyTheDefaultChanged_WhenSeamsRead_ThenEverySeamFollowsIt()
        {
            // The one-line adoption path: set the default and every migrated seam moves together.
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration { Default = FhirSdkProvider.Ignixa },
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveFhirPath);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveSerialization);
        }

        [Fact]
        public void GivenASeamOverride_WhenSeamsRead_ThenOnlyThatSeamMoves()
        {
            // Enabling the fast $import path while leaving indexing and serialization on Firely.
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration { Import = FhirSdkProvider.Ignixa },
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveSerialization);
        }

        [Fact]
        public void GivenAnOverrideOpposingTheDefault_WhenSeamsRead_ThenTheOverrideWins()
        {
            // Rolling one seam back after a problem without giving up the others.
            var configuration = new CoreFeatureConfiguration
            {
                FhirSdkProvider = new FhirSdkProviderConfiguration
                {
                    Default = FhirSdkProvider.Ignixa,
                    FhirPath = FhirSdkProvider.Firely,
                },
            };

            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Firely, configuration.FhirSdkProvider.EffectiveFhirPath);
            Assert.Equal(FhirSdkProvider.Ignixa, configuration.FhirSdkProvider.EffectiveSerialization);
        }

        [Fact]
        public void GivenConfigurationBoundFromSettings_WhenSeamsRead_ThenOverridesApply()
        {
            // Mirrors how the setting actually arrives: bound from configuration keys rather than constructed.
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["FhirSdkProvider:Default"] = "Firely",
                    ["FhirSdkProvider:Import"] = "Ignixa",
                    ["FhirSdkProvider:FhirPath"] = "Ignixa",
                })
                .Build();

            var core = new CoreFeatureConfiguration();
            configuration.Bind(core);

            Assert.Equal(FhirSdkProvider.Ignixa, core.FhirSdkProvider.EffectiveImport);
            Assert.Equal(FhirSdkProvider.Ignixa, core.FhirSdkProvider.EffectiveFhirPath);
            Assert.Equal(FhirSdkProvider.Firely, core.FhirSdkProvider.EffectiveSerialization);
        }
    }
}
