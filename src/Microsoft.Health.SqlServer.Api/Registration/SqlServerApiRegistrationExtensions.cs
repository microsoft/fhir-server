// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.SqlServer.Api.Controllers;
using Microsoft.Health.SqlServer.Features.Health;

namespace Microsoft.Health.SqlServer.Api.Registration
{
    public static class SqlServerApiRegistrationExtensions
    {
        public static IServiceCollection AddSqlServerApi<TSchemaVersionEnum>(
            this IServiceCollection services)
            where TSchemaVersionEnum : Enum
        {
            EnsureArg.IsNotNull(services);

            services.AddMvc()
                .ConfigureApplicationPartManager(p =>
                    p.FeatureProviders.Add(new SchemaControllerFeatureProvider(typeof(TSchemaVersionEnum))));

            services
                .AddHealthChecks()
                .AddCheck<SqlServerHealthCheck>(nameof(SqlServerHealthCheck));

            return services;
        }
    }
}
