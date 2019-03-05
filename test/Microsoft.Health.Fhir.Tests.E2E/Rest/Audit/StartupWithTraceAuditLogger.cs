// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Web;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Audit
{
    public class StartupWithTraceAuditLogger : Startup
    {
        public StartupWithTraceAuditLogger(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);

            services.Replace(new ServiceDescriptor(typeof(IAuditLogger), typeof(TraceAuditLogger), ServiceLifetime.Singleton));

            // Configure the test server to log a claim that is used in both local and integration environments.
            ServiceProvider serviceProvider = services.BuildServiceProvider();
            var securityConfigurationOptions = serviceProvider.GetService<IOptions<SecurityConfiguration>>();
            securityConfigurationOptions.Value.LastModifiedClaims.Clear();
            securityConfigurationOptions.Value.LastModifiedClaims.Add("appid");
        }
    }
}
