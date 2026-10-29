// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Core.Registration;
using Microsoft.Health.Fhir.SqlServer.Features.Watchdogs;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.SqlServer;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Client;
using Microsoft.Health.SqlServer.Features.Storage;
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
        public void GivenRootResolvedConnectionWrapperFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            IServiceCollection services = CreateServices();
            services.AddScoped<SqlTransactionHandler>();
            services.AddScoped(provider => new SqlConnectionWrapperFactory(
                provider.GetRequiredService<SqlTransactionHandler>(),
                Substitute.For<ISqlConnectionBuilder>(),
                SqlConfigurableRetryFactory.CreateFixedRetryProvider(new SqlClientRetryOptions().Settings),
                Options.Create(new SqlServerDataStoreConfiguration())));
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<SqlConnectionWrapperFactory>(provider, nameof(SqlConnectionWrapperFactory));
        }

        [Fact]
        public void GivenRootResolvedTransactionWatchdogFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            IServiceCollection services = CreateServices();
            services.AddScoped(_ => new TransactionWatchdog());
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<TransactionWatchdog>(provider, nameof(TransactionWatchdog));
        }

        private static IServiceCollection CreateServices()
        {
            var services = new ServiceCollection();

            var builder = Substitute.For<IFhirServerBuilder>();
            builder.Services.Returns(services);
            builder.AddSqlServer(_ => { });

            return services;
        }
    }
}
