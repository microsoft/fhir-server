// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Api.Features.Audit;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Api.Controllers;
using Microsoft.Health.Fhir.Api.Features.Audit;
using Microsoft.Health.Fhir.Core.Features.Audit;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class AuditModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.Add<AuditLoggingFilterAttribute>()
                .Singleton()
                .AsSelf();

            services.Add<AadSmartOnFhirClaimsExtractor>()
                .Singleton()
                .AsSelf();

            services.Add<AadSmartOnFhirProxyAuditLoggingFilterAttribute>()
                .Singleton()
                .AsSelf();

            services.AddSingleton<IAuditLogger, AuditLogger>();

            services.AddSingleton<IAuditHeaderReader, AuditHeaderReader>();

            services.Add<AuditHelper>()
                .Singleton()
                .AsService<IAuditHelper>();

            services.Add<AuditEventTypeMapping>()
                .Singleton()
                .AsService<IAuditEventTypeMapping>()
                .AsService<IHostedService>();
        }
    }
}
