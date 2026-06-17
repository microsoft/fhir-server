// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.SchemaManager.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Schema)]
    public class SchemaManagerServiceCollectionBuilderTests
    {
        [Fact]
        public void GivenSchemaManagerServices_WhenAdded_ThenSchemaUpgradeMediatorIsRegistered()
        {
            var services = new ServiceCollection();
            var configuration = new ConfigurationBuilder().Build();

            services.AddSchemaManager(configuration);

            Assert.Contains(services, service => service.ServiceType == typeof(IMediator));
        }
    }
}
