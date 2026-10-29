// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Health.Fhir.SchemaManager.Properties;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class CommandOptions
{
    public static Option<string> ConnectionStringOption()
    {
        var connectionStringOption = new Option<string>(
            name: OptionAliases.ConnectionString,
            aliases: [OptionAliases.ConnectionStringShort])
        {
            Description = Resources.ConnectionStringOptionDescription,
            Arity = ArgumentArity.ExactlyOne,
            Required = true,
        };

        return connectionStringOption;
    }

    public static Option<string> ManagedIdentityClientIdOption()
    {
        var managedIdentityClientIdOption = new Option<string>(
            name: OptionAliases.ManagedIdentityClientId,
            aliases: [OptionAliases.ManagedIdentityClientIdShort])
        {
            Description = Resources.ManagedIdentityClientIdDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return managedIdentityClientIdOption;
    }

    public static Option<string> AuthenticationTypeOption()
    {
        var authenticationTypeOption = new Option<string>(
            name: OptionAliases.AuthenticationType,
            aliases: [OptionAliases.AuthenticationTypeShort])
        {
            Description = Resources.AuthenticationTypeDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return authenticationTypeOption;
    }

    public static Option<string> EnableWorkloadIdentityOptions()
    {
        var enableWorkloadIdentityOptions = new Option<string>(
            name: OptionAliases.EnableWorkloadIdentity,
            aliases: [OptionAliases.EnableWorkloadIdentityShort])
        {
            Description = Resources.EnableWorkloadIdentityDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return enableWorkloadIdentityOptions;
    }

    public static Option<int> VersionOption()
    {
        var versionOption = new Option<int>(
            name: OptionAliases.Version,
            aliases: [OptionAliases.VersionShort])
        {
            Description = Resources.VersionOptionDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return versionOption;
    }

    public static Option<bool> NextOption()
    {
        var nextOption = new Option<bool>(
            name: OptionAliases.Next,
            aliases: [OptionAliases.NextShort])
        {
            Description = Resources.NextOptionDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return nextOption;
    }

    public static Option<bool> LatestOption()
    {
        var latestOption = new Option<bool>(
            name: OptionAliases.Latest,
            aliases: [OptionAliases.LatestShort])
        {
            Description = Resources.LatestOptionDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return latestOption;
    }

    public static Option<bool> ForceOption()
    {
        var forceOption = new Option<bool>(
            name: OptionAliases.Force,
            aliases: [OptionAliases.ForceShort])
        {
            Description = Resources.ForceOptionDescription,
            Arity = ArgumentArity.ZeroOrOne,
        };

        return forceOption;
    }
}
