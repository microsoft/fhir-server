// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class StartupScopedFactoryTests
    {
        [Fact]
        public void GivenRootResolvedFactory_WhenScopeIsDisposed_ThenWrapperIsNotRetained()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                { "DataStore", "SqlServer" },
                { "TaskHosting:Enabled", "true" },
                { "Telemetry:Provider", "None" },
            }).Build();
            var services = new ServiceCollection();
            new Startup(configuration).ConfigureServices(services);
            services.AddScoped(_ => new JobHosting(
                Substitute.For<IQueueClient>(),
                Substitute.For<IJobFactory>(),
                Substitute.For<IJobMetricFactory>(),
                NullLogger<JobHosting>.Instance));
            using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<JobHosting>(provider, nameof(JobHosting));
        }
    }
}
