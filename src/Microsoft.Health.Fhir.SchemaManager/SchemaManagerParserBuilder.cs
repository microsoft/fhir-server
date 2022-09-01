// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Fhir.SchemaManager;

public static class SchemaManagerParserBuilder
{
    public static Parser Build(IServiceProvider serviceProvider)
    {
        var commandLineBuilder = new CommandLineBuilder();

        foreach (Command command in serviceProvider.GetServices<Command>())
        {
            commandLineBuilder.Command.AddCommand(command);
        }

        return commandLineBuilder.UseDefaults().Build();
    }
}
