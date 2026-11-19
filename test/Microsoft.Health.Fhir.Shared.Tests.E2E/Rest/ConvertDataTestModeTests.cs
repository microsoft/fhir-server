// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.ConvertData)]
    public sealed class ConvertDataTestModeTests
    {
        [Fact]
        public void IsEnabled_WhenRemoteServer_ReturnsTrue_EvenIfConfiguredEnabledFalse()
        {
            // Arrange
            bool isUsingInProcTestServer = false;
            bool configuredEnabled = false;

            // Act
            bool result = ConvertDataTestMode.IsEnabled(isUsingInProcTestServer, configuredEnabled);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsEnabled_WhenRemoteServer_ReturnsTrue_WhenConfiguredEnabledTrue()
        {
            // Arrange
            bool isUsingInProcTestServer = false;
            bool configuredEnabled = true;

            // Act
            bool result = ConvertDataTestMode.IsEnabled(isUsingInProcTestServer, configuredEnabled);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsEnabled_WhenInProcServer_ReturnsFalse_WhenConfiguredEnabledFalse()
        {
            // Arrange
            bool isUsingInProcTestServer = true;
            bool configuredEnabled = false;

            // Act
            bool result = ConvertDataTestMode.IsEnabled(isUsingInProcTestServer, configuredEnabled);

            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsEnabled_WhenInProcServer_ReturnsTrue_WhenConfiguredEnabledTrue()
        {
            // Arrange
            bool isUsingInProcTestServer = true;
            bool configuredEnabled = true;

            // Act
            bool result = ConvertDataTestMode.IsEnabled(isUsingInProcTestServer, configuredEnabled);

            // Assert
            Assert.True(result);
        }
    }
}
