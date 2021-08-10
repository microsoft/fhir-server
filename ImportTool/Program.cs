// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Storage;
using Mono.Options;

namespace ImportTool
{
    public static class Program
    {
        private const string _localConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;";
        private const long _defaultSplitSizeInBytes = 200 * 1024 * 1024L;
        private const int _defaultMaxFileNumber = 2000;

        private static Config _config = new Config
        {
            StorageConnectionString = _localConnectionString,
            SplitSizeInBytes = _defaultSplitSizeInBytes,
            MaxFileNumber = _defaultMaxFileNumber,
        };

        public static async Task Main(string[] args)
        {
            string storageConnectionString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
            _config.StorageConnectionString = storageConnectionString ?? _config.StorageConnectionString;

            bool show_help = false;
            bool generate_request = false;
            bool split_file = false;
            string prefix = string.Empty;

            var option = new OptionSet()
            {
                {
                    "c|connectionString=", "the {CONNECTIONSTRING} of source blob.",
#pragma warning disable SA1501 // Statement should not be on a single line
                    value => { if (value != null) { _config.StorageConnectionString = value; } }
#pragma warning restore SA1501 // Statement should not be on a single line
                },
                {
                    "s|splitSizeInMb=", "the {SIZE} of splited file.",
#pragma warning disable SA1501 // Statement should not be on a single line
                    (long value) => { if (value > 0) { _config.SplitSizeInBytes = value; } }
#pragma warning restore SA1501 // Statement should not be on a single line
                },
                {
                    "n|maxFileNumber=", "the {MaxNumber} of files to be used.",
#pragma warning disable SA1501 // Statement should not be on a single line
                    (int value) => { if (value > 0) { _config.MaxFileNumber = value; } }
#pragma warning restore SA1501 // Statement should not be on a single line
                },
                {
                    "generate",  "generate the request from the given path",
                    value => generate_request = value != null
                },
                {
                    "split",  "split the file from given path",
                    value => split_file = value != null
                },
                {
                    "p|prefix=",  "the {PREFIX} of input or splited files",
#pragma warning disable SA1501 // Statement should not be on a single line
                    value => { if (value != null) { prefix = value; } }
#pragma warning restore SA1501 // Statement should not be on a single line
                },
                {
                    "h|help",  "show this message and exit",
                    value => show_help = value != null
                },
            };

            try
            {
                option.Parse(args);

                if (generate_request)
                {
                    await RequestGenerator.GenerateImportRequest(_config.StorageConnectionString, prefix, _config.MaxFileNumber);
                }
            }
            catch (OptionException oe)
            {
                Console.WriteLine($"Error reslove arguments due to {oe.Message}");
                return;
            }
            catch (StorageException se)
            {
                Console.WriteLine($"Failed to generate request or split file due to {se.Message}");
                return;
            }
        }
    }
}
