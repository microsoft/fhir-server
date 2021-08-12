// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace ImportTool
{
    public static class Program
    {
        public static async Task<int> Main(string[] args)
        {
            RootCommand rootCommand = new RootCommand(
                description: "A tool that helps fast bootstrap bulk import.");

            Command generateRequestCommand = new Command(
                name: "generate",
                description: "Generate bulk import request from source storage with a prefix.");

            Option accountOption = new Option(
                aliases: new string[] { "--account", "-a" },
                description: "The account of source azure storage.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(accountOption);

            Option keyOption = new Option(
                aliases: new string[] { "--key", "-k" },
                description: "The key of source azure storage account.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(keyOption);

            Option prefixOption = new Option(
                aliases: new string[] { "--prefix", "-p" },
                description: "The prefix of target azure blobs.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(prefixOption);

            generateRequestCommand.Handler =
                CommandHandler.Create<string, string, string>(RequestGenerator.GenerateImportRequest);

            rootCommand.AddCommand(generateRequestCommand);

            return await rootCommand.InvokeAsync(args);
        }
    }
}
