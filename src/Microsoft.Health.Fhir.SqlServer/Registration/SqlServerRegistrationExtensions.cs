// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class SqlServerRegistrationExtensions
    {
        public static IServiceCollection AddSqlServer(this IServiceCollection services)
        {
            services.Add<SchemaUpgradeRunner>()
                .Singleton()
                .AsSelf();

            services.Add<SchemaInformation>()
                .Singleton()
                .AsSelf();

            services.Add<SchemaInitializer>()
                .Singleton()
                .AsService<IStartable>();

            return services;
        }
    }
}
