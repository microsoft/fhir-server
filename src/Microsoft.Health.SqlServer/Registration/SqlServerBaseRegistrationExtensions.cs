// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.SqlServer.Registration
{
    public static class SqlServerBaseRegistrationExtensions
    {
        public static IServiceCollection AddSqlServerBase(
            this IServiceCollection services,
            Action<SqlServerDataStoreConfiguration> configureAction = null)
        {
            services.Add(provider =>
                {
                    var config = new SqlServerDataStoreConfiguration();
                    provider.GetService<IConfiguration>().GetSection("SqlServer").Bind(config);
                    configureAction?.Invoke(config);

                    return config;
                })
                .Singleton()
                .AsSelf();

            services.Add<SchemaUpgradeRunner>()
                .Singleton()
                .AsSelf();

            services.Add<SchemaInitializer>()
                .Singleton()
                .AsService<IStartable>();

            services.Add<ScriptProvider>()
                .Singleton()
                .AsSelf()
                .AsImplementedInterfaces();

            return services;
        }
    }
}
