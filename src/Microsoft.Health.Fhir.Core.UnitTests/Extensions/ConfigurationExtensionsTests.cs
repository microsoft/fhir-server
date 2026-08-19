// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Operations)]
    public sealed class ConfigurationExtensionsTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        public void GivenAnEmptyRuntimeStateConfiguration_WhenGettingRuntimeState_ThenActiveIsReturned(string configuredRuntimeState)
        {
            FhirRuntimeState actual = ConfigurationExtensions.GetRuntimeStateConfiguration(configuredRuntimeState);

            Assert.Equal(FhirRuntimeState.Active, actual);
        }

        [Theory]
        [InlineData("Active", FhirRuntimeState.Active)]
        [InlineData(" active ", FhirRuntimeState.Active)]
        [InlineData("DEPRECATED", FhirRuntimeState.Deprecated)]
        [InlineData(" deprecated ", FhirRuntimeState.Deprecated)]
        public void GivenAValidRuntimeStateConfiguration_WhenGettingRuntimeState_ThenConfiguredStateIsReturned(
            string configuredRuntimeState,
            FhirRuntimeState expected)
        {
            FhirRuntimeState actual = ConfigurationExtensions.GetRuntimeStateConfiguration(configuredRuntimeState);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("Unknown")]
        [InlineData("1")]
        [InlineData("2")]
        public void GivenAnInvalidRuntimeStateConfiguration_WhenGettingRuntimeState_ThenActiveIsReturned(string configuredRuntimeState)
        {
            FhirRuntimeState actual = ConfigurationExtensions.GetRuntimeStateConfiguration(configuredRuntimeState);

            Assert.Equal(FhirRuntimeState.Active, actual);
        }
    }
}
