// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Fhir.SqlServer.Features.Schema;
using Microsoft.Health.SqlServer.Configs;
using Microsoft.Health.SqlServer.Features.Schema.Manager;
using Microsoft.Health.SqlServer.Features.Schema.Messages.Notifications;
using Microsoft.Health.SqlServer.Registration;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class SchemaManagerServiceCollectionBuilder
{
    public static IServiceCollection AddSchemaManager(this IServiceCollection services, IConfiguration config)
    {
        services.AddCliCommands();

        services.SetCommandLineOptions(config);

        services.AddOptions<SqlServerDataStoreConfiguration>().Configure<IOptions<CommandLineOptions>>((s, c) =>
        {
            s.ConnectionString = c.Value.ConnectionString;
            s.AuthenticationType = c.Value.AuthenticationType ?? SqlServerAuthenticationType.ConnectionString;

            if (!string.IsNullOrWhiteSpace(c.Value.ManagedIdentityClientId))
            {
                s.ManagedIdentityClientId = c.Value.ManagedIdentityClientId;
            }
        });

        services.AddSqlServerConnection();

        services.AddSqlServerManagement<SchemaVersion>();

        services.AddSingleton<BaseSchemaRunner>();
        services.AddSingleton<IBaseSchemaRunner, FhirBaseSchemaRunner>();

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SchemaUpgradedNotification).Assembly));

        services.AddSingleton<ISchemaClient, FhirSchemaClient>();
        services.AddSingleton<ISchemaManager, SqlSchemaManager>();
        services.AddLogging(configure => configure.AddConsole());
        return services;
    }
}
