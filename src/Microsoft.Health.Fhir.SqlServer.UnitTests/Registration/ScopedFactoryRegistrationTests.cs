// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.SqlServer.UnitTests.Registration
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class ScopedFactoryRegistrationTests
    {
        [Fact]
        public void GivenRootResolvedTransactionWatchdogFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            // Arrange
            var services = new ServiceCollection();
            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });
            services.AddScoped(_ => new TransactionWatchdog());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            // Act and Assert
            ScopedFactoryAssert.NotRetainedByRoot<TransactionWatchdog>(provider, nameof(TransactionWatchdog));
        }
    }
}
