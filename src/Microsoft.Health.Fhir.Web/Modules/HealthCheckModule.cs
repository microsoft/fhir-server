// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Health;
using Microsoft.Health.Fhir.CosmosDb.Features.Health;

namespace Microsoft.Health.Fhir.Web.Modules
{
    /// <summary>
    /// Registration of health check components.
    /// </summary>
    public class HealthCheckModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            // We can move to framework such as https://github.com/dotnet-architecture/HealthChecks
            // once they are released to do health check on multiple dependencies.
            services.Add<CosmosHealthCheck>()
                .Scoped()
                .AsSelf()
                .AsService<IHealthCheck>();
        }
    }
}
