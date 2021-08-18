// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace ImportTool.Shared
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

            Command splitBlobsCommand = new Command(
                name: "split",
                description: "Split blobs with a prefix into specified size.");

            Option accountOption = new Option(
                aliases: new string[] { "--account", "-a" },
                description: "The account of source azure storage.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(accountOption);
            splitBlobsCommand.AddOption(accountOption);

            Option keyOption = new Option(
                aliases: new string[] { "--key", "-k" },
                description: "The key of source azure storage account.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(keyOption);
            splitBlobsCommand.AddOption(keyOption);

            Option prefixOption = new Option(
                aliases: new string[] { "--prefix", "-p" },
                description: "The prefix of target azure blobs.",
                argumentType: typeof(string));
            generateRequestCommand.AddOption(prefixOption);
            splitBlobsCommand.AddOption(prefixOption);

            Option sizeInBytesOption = new Option(
                aliases: new string[] { "--size", "-s" },
                description: "The size of splited blobs.",
                argumentType: typeof(long),
                getDefaultValue: () => 200 * 1024 * 1024L);
            splitBlobsCommand.AddOption(sizeInBytesOption);

            Option blockSizeInBytesOption = new Option(
                aliases: new string[] { "--block-size", "-b" },
                description: "The size of a block, which is a unit for uploading, should less than 100MB.",
                argumentType: typeof(long),
                getDefaultValue: () => 80 * 1024 * 1024L);
            splitBlobsCommand.AddOption(blockSizeInBytesOption);

            Option maxConcurrentSplitFileOption = new Option(
                aliases: new string[] { "--max-concurrent-file" },
                description: "The maximum number of concurrent spliting blobs.",
                argumentType: typeof(int),
                getDefaultValue: () => 8);
            splitBlobsCommand.AddOption(maxConcurrentSplitFileOption);

            Option maxSpliterCountPerBlobOption = new Option(
                aliases: new string[] { "--max-spliter-per-blob" },
                description: "The maximum number of split tasks used for one original blob.",
                argumentType: typeof(int),
                getDefaultValue: () => 4);
            splitBlobsCommand.AddOption(maxSpliterCountPerBlobOption);

            Option maxUploaderCountPerSplitedBlobOption = new Option(
                aliases: new string[] { "--max-uploader-per-blob"},
                description: "The maximum number of upload tasks used for one splited blob.",
                argumentType: typeof(int),
                getDefaultValue: () => 3);
            splitBlobsCommand.AddOption(maxUploaderCountPerSplitedBlobOption);

            generateRequestCommand.Handler =
                CommandHandler.Create<string, string, string>(GeneratRequestCommand.GenerateImportRequest);

            splitBlobsCommand.Handler =
                CommandHandler.Create<string, string, string, long, long, int, int, int>(SplitBlobsCommand.Split);

            rootCommand.AddCommand(generateRequestCommand);
            rootCommand.AddCommand(splitBlobsCommand);

            return await rootCommand.InvokeAsync(args);
        }
    }
}
