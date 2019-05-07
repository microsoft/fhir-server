// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using FhirSchemaManager.Commands;

namespace FhirSchemaManager
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            var fhirServerOption = new Option(
                OptionAliases.FhirServer,
                Resources.FhirServerOptionDescription,
                new Argument<Uri> { Arity = ArgumentArity.ExactlyOne });

            var connectionStringOption = new Option(
                OptionAliases.ConnectionString,
                Resources.ConnectionStringOptionDescription,
                new Argument<string> { Arity = ArgumentArity.ExactlyOne });

            var versionOption = new Option(
                OptionAliases.Version,
                Resources.VersionOptionDescription,
                new Argument<int> { Arity = ArgumentArity.ExactlyOne });

            var rootCommand = new RootCommand();

            var currentCommand = new Command(CommandNames.Current, Resources.CurrentCommandDescription)
            {
                fhirServerOption,
            };
            currentCommand.Handler = CommandHandler.Create<Uri>(CurrentCommand.Handler);
            currentCommand.Argument.AddValidator(symbol => Validators.RequiredOptionValidator.Validate(symbol, fhirServerOption, Resources.FhirServerRequiredValidation));

            var applyCommand = new Command(CommandNames.Apply, Resources.ApplyCommandDescription)
            {
                connectionStringOption,
                fhirServerOption,
                versionOption,
            };
            applyCommand.Handler = CommandHandler.Create<string, Uri, int>(ApplyCommand.Handler);
            applyCommand.Argument.AddValidator(symbol => Validators.RequiredOptionValidator.Validate(symbol, connectionStringOption, Resources.ConnectionStringRequiredValidation));
            applyCommand.Argument.AddValidator(symbol => Validators.RequiredOptionValidator.Validate(symbol, fhirServerOption, Resources.FhirServerRequiredValidation));
            applyCommand.Argument.AddValidator(symbol => Validators.RequiredOptionValidator.Validate(symbol, versionOption, Resources.VersionRequiredValidation));

            var availableCommand = new Command(CommandNames.Available, Resources.AvailableCommandDescription)
            {
                fhirServerOption,
            };
            availableCommand.Handler = CommandHandler.Create<InvocationContext, Uri>(AvailableCommand.Handler);
            availableCommand.Argument.AddValidator(symbol => Validators.RequiredOptionValidator.Validate(symbol, fhirServerOption, Resources.FhirServerRequiredValidation));

            rootCommand.AddCommand(applyCommand);
            rootCommand.AddCommand(availableCommand);
            rootCommand.AddCommand(currentCommand);

            rootCommand.InvokeAsync(args).Wait();
        }
    }
}
