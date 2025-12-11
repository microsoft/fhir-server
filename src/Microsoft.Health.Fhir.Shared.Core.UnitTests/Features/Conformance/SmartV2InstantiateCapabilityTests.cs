// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Shared.Core.UnitTests.Features.Conformance
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Conformance)]
    public class SmartV2InstantiateCapabilityTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(false, false)]
        public void GivenSecurityConfiguration_WhenSmartV2IsEnabled_ThenInstantiateCapabilityUrlsAreReturned(
            bool enabled,
            bool enabledWithout)
        {
            var securityConfiguration = new SecurityConfiguration
            {
                Authorization = new AuthorizationConfiguration
                {
                    Enabled = enabled,
                    EnableSmartWithoutAuth = enabledWithout,
                },
            };

            var instantiateCapability = new SmartV2InstantiateCapability(
                Options.Create(securityConfiguration));
            if (instantiateCapability.TryGetUrls(out var urls))
            {
                Assert.True(enabled || enabledWithout);
                Assert.NotNull(urls);
                Assert.NotEmpty(urls);
            }
            else
            {
                Assert.False(enabled || enabledWithout);
                Assert.Null(urls);
            }
        }
    }
}
