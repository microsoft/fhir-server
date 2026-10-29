// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class SchemaManagerParserBuilder
{
    public static RootCommand Build(IServiceProvider serviceProvider)
    {
        var rootCommand = new RootCommand();

        foreach (Command command in serviceProvider.GetServices<Command>())
        {
            rootCommand.Subcommands.Add(command);
        }

        return rootCommand;
    }
}
