// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.SqlServer.Configs;

namespace Microsoft.Health.Fhir.SchemaManager;

/// <summary>
/// Contains the collection extensions for adding the CLI commands.
/// </summary>
public static class CommandCollectionExtensions
{
    /// <summary>
    /// Adds the CLI commands to the DI container. These are resolved when the commands are registered with the
    /// <c>CommandLineBuilder</c>.
    /// </summary>
    /// <param name="services">The service collection to add to.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// We are using convention to register the commands; essentially everything in the same namespace as the
    /// added in other namespaces, this method will need to be modified/extended to deal with that.
    /// </remarks>
    public static IServiceCollection AddCliCommands(this IServiceCollection services)
    {
        Type grabCommandType = typeof(ApplyCommand);
        Type commandType = typeof(Command);

        IEnumerable<Type> commands = grabCommandType
            .Assembly
            .GetExportedTypes()
            .Where(x => x.Namespace == grabCommandType.Namespace && commandType.IsAssignableFrom(x));

        foreach (Type command in commands)
        {
            services.AddSingleton(commandType, command);
        }

        return services;
    }

    public static IConfigurationBuilder AddSchemaCommandLine(this IConfigurationBuilder configurationBuilder, string[] args)
    {
        var switchMappings = new Dictionary<string, string>()
        {
            { OptionAliases.ConnectionStringShort, OptionAliases.ConnectionString },
            { OptionAliases.ForceShort, OptionAliases.Force },
            { OptionAliases.LatestShort, OptionAliases.Latest },
            { OptionAliases.NextShort, OptionAliases.Next },
            { OptionAliases.ManagedIdentityClientIdShort, OptionAliases.ManagedIdentityClientId },
            { OptionAliases.AuthenticationTypeShort, OptionAliases.AuthenticationType },
            { OptionAliases.VersionShort, OptionAliases.Version },
            { OptionAliases.ConnectionString, OptionAliases.ConnectionString },
            { OptionAliases.Force, OptionAliases.Force },
            { OptionAliases.Latest, OptionAliases.Latest },
            { OptionAliases.Next, OptionAliases.Next },
            { OptionAliases.ManagedIdentityClientId, OptionAliases.ManagedIdentityClientId },
            { OptionAliases.AuthenticationType, OptionAliases.AuthenticationType },
            { OptionAliases.Version, OptionAliases.Version },
        };

        configurationBuilder.AddCommandLine(args, switchMappings);

        return configurationBuilder;
    }

    public static IServiceCollection SetCommandLineOptions(this IServiceCollection services, IConfiguration config)
    {
        services.AddOptions<CommandLineOptions>().Configure(x =>
        {
            x.ConnectionString = config[OptionAliases.ConnectionString];

            x.ManagedIdentityClientId = config[OptionAliases.ManagedIdentityClientId];

            string? authenticationTypeValue = config[OptionAliases.AuthenticationType];
            if (!string.IsNullOrWhiteSpace(authenticationTypeValue))
            {
#pragma warning disable CS0618 // Type or member is obsolete
                if (Enum.TryParse(authenticationTypeValue, true, out SqlServerAuthenticationType sqlServerAuthenticationType))
                {
                    x.AuthenticationType = sqlServerAuthenticationType;
                }
#pragma warning restore CS0618 // Type or member is obsolete
            }
        });

        return services;
    }
}
