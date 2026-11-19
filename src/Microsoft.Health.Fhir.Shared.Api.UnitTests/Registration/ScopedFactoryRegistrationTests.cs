// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Api.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ScopedFactoryRegistrationTests
    {
        [Fact]
        public void GivenFhirServerRegistration_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            // Arrange
            var services = new ServiceCollection();
            services.AddFhirServer(new ConfigurationBuilder().Build());
            services.AddScoped(_ => new object());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            // Act and Assert
            ScopedFactoryAssert.NotRetainedByRoot<object>(provider, "object");
        }
    }
}
