// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    [Trait(Traits.Category, Categories.Web)]
    public class FhirServerServiceCollectionExtensionsTests
    {
        [Theory]
        [InlineData(63)]
        [InlineData(257)]
        public void GivenAMaxResourceIdLengthOutsideTheSupportedRange_WhenAddingTheFhirServer_ThenAnInvalidOperationExceptionIsThrown(int maxResourceIdLength)
        {
            // Arrange
            var services = new ServiceCollection();

            // Act
            var exception = Assert.Throws<InvalidOperationException>(
                () => services.AddFhirServer(
                    configurationRoot: null,
                    configureAction: configuration => configuration.CoreFeatures.MaxResourceIdLength = maxResourceIdLength));

            // Assert
            Assert.Contains("FhirServer:CoreFeatures:MaxResourceIdLength", exception.Message, StringComparison.Ordinal);
            Assert.Contains("between 64 and 256", exception.Message, StringComparison.Ordinal);
            Assert.Contains(maxResourceIdLength.ToString(CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        }
    }
}
