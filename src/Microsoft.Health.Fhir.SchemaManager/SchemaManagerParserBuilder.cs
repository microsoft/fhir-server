// -------------------------------------------------------------------------------------------------
// <copyright file="SchemaManagerParserBuilder.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SchemaManager;

using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Parsing;
using Microsoft.Extensions.DependencyInjection;

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
