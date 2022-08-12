// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Health.Fhir.SchemaManager.Properties;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class CommandOptions
{
    public static Option ConnectionStringOption()
    {
        var connectionStringOption = new Option<string>(
            name: OptionAliases.ConnectionString,
            description: Resources.ConnectionStringOptionDescription)
        {
            Arity = ArgumentArity.ExactlyOne,
            IsRequired = true,
        };

        connectionStringOption.AddAlias(OptionAliases.ConnectionStringShort);

        return connectionStringOption;
    }

    public static Option ManagedIdentityClientIdOption()
    {
        var managedIdentityClientIdOption = new Option<string>(
            name: OptionAliases.ManagedIdentityClientId,
            description: Resources.ManagedIdentityClientIdDescription)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        managedIdentityClientIdOption.AddAlias(OptionAliases.ManagedIdentityClientIdShort);

        return managedIdentityClientIdOption;
    }

    public static Option AuthenticationTypeOption()
    {
        var connectionStringOption = new Option<string>(
            name: OptionAliases.AuthenticationType,
            description: Resources.AuthenticationTypeDescription)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        connectionStringOption.AddAlias(OptionAliases.AuthenticationTypeShort);

        return connectionStringOption;
    }

    public static Option VersionOption()
    {
        var versionOption = new Option<int>(
            name: OptionAliases.Version,
            description: Resources.VersionOptionDescription)
        {
            Arity = ArgumentArity.ExactlyOne,
        };

        versionOption.AddAlias(OptionAliases.VersionShort);

        return versionOption;
    }

    public static Option NextOption()
    {
        var nextOption = new Option<bool>(
            name: OptionAliases.Next,
            description: Resources.NextOptionDescription)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        nextOption.AddAlias(OptionAliases.NextShort);

        return nextOption;
    }

    public static Option LatestOption()
    {
        var latestOption = new Option<bool>(
            name: OptionAliases.Latest,
            description: Resources.LatestOptionDescription)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        latestOption.AddAlias(OptionAliases.LatestShort);

        return latestOption;
    }

    public static Option ForceOption()
    {
        var forceOption = new Option<bool>(
            name: OptionAliases.Force,
            description: Resources.ForceOptionDescription)
        {
            Arity = ArgumentArity.ZeroOrOne,
        };

        forceOption.AddAlias(OptionAliases.ForceShort);

        return forceOption;
    }
}
