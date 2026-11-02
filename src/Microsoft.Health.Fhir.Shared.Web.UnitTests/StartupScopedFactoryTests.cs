// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Scoping;
using Microsoft.Health.Fhir.Tests.Common;
using Microsoft.Health.Fhir.Web;
using Microsoft.Health.JobManagement;
using Microsoft.Health.Test.Utilities;
using Xunit;

namespace Microsoft.Health.Fhir.Web.UnitTests
{
    [Trait(Traits.OwningTeam, OwningTeam.Fhir)]
    [Trait(Traits.Category, Categories.Validate)]
    public class StartupScopedFactoryTests
    {
        [Fact]
        public async Task GivenTaskHostingEnabled_WhenResolvingScopes_ThenRealJobHostingGraphIsCallerOwned()
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
            {
                { "DataStore", "SqlServer" },
                { "TaskHosting:Enabled", "true" },
                { "Telemetry:Provider", "None" },
                { "FhirServer:Security:Authentication:Authority", "https://offline.invalid" },
                { "FhirServer:Security:Authentication:Audience", "https://fhir.offline.invalid" },
                { "SqlServer:ConnectionString", "Server=localhost;Initial Catalog=offline-scoping;Integrated Security=true;TrustServerCertificate=true" },
            }).Build();
            IServiceCollection services = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true }).Services;
            services.AddSingleton(configuration);
            services.AddLogging();
            new Startup(configuration).ConfigureServices(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

            ScopedFactoryAssert.NotRetainedByRoot<JobHosting>(provider, nameof(JobHosting));

            IScopeProvider<JobHosting> factory = provider.GetRequiredService<IScopeProvider<JobHosting>>();
            using IScoped<JobHosting> first = factory.Invoke();
            using IScoped<JobHosting> second = factory.Invoke();
            JobHosting secondValue = Assert.IsType<JobHosting>(second.Value);

            Assert.IsType<JobHosting>(first.Value);
            Assert.NotSame(first.Value, secondValue);
            first.Dispose();

            Assert.Throws<ObjectDisposedException>(() => first.Value);
            Assert.Same(secondValue, second.Value);

            using IServiceScope jobScope = provider.CreateScope();
            IJobFactory jobFactory = jobScope.ServiceProvider.GetRequiredService<IJobFactory>();
            foreach (JobType jobType in Enum.GetValues<JobType>())
            {
                if (jobType == JobType.Unknown)
                {
                    continue;
                }

                using IScoped<IJob> job = jobFactory.Create(new JobInfo { Definition = $"{{\"typeId\":{(int)jobType}}}" });
                var attribute = Attribute.GetCustomAttribute(job.Value.GetType(), typeof(JobTypeIdAttribute)) as JobTypeIdAttribute;
                Assert.NotNull(attribute);
                Assert.Equal((int)jobType, attribute.JobTypeId);
            }
        }
    }
}
