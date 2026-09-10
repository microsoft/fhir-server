// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Extensions
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ServiceRuntimeState)]
    public sealed class RuntimeStateConfigurationExtensionsTests
    {
        [Theory]
        [InlineData(null, FhirRuntimeState.Active)]
        [InlineData("", FhirRuntimeState.Active)]
        [InlineData("Active", FhirRuntimeState.Active)]
        [InlineData(" active ", FhirRuntimeState.Active)]
        [InlineData("DEPRECATED", FhirRuntimeState.Deprecated)]
        [InlineData(" deprecated ", FhirRuntimeState.Deprecated)]
        public void GivenAnConfiguration_WhenGettingRuntimeState_ThenProperRuntimeValueIsReturned(string configurationValue, FhirRuntimeState expected)
        {
            IConfiguration configuration = Substitute.For<IConfiguration>();
            configuration["FhirServer:CoreFeatures:RuntimeState"].Returns(configurationValue);

            FhirRuntimeState actual = RuntimeStateConfigurationExtensions.GetRuntimeStateConfiguration(configuration);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("\t\r\n")]
        public void GivenAnEmptyRuntimeStateConfiguration_WhenGettingRuntimeState_ThenActiveIsReturned(string configuredRuntimeState)
        {
            FhirRuntimeState actual = RuntimeStateConfigurationExtensions.ParseRuntimeStateConfiguration(configuredRuntimeState);

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
            FhirRuntimeState actual = RuntimeStateConfigurationExtensions.ParseRuntimeStateConfiguration(configuredRuntimeState);

            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("Unknown")]
        [InlineData("1")]
        [InlineData("2")]
        public void GivenAnInvalidRuntimeStateConfiguration_WhenGettingRuntimeState_ThenActiveIsReturned(string configuredRuntimeState)
        {
            FhirRuntimeState actual = RuntimeStateConfigurationExtensions.ParseRuntimeStateConfiguration(configuredRuntimeState);

            Assert.Equal(FhirRuntimeState.Active, actual);
        }
    }
}
