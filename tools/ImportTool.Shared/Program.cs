// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Mono.Options;

namespace ImportTool
{
    public static class Program
    {
        private static ILogger _logger = GetLogger();

        private static ILogger GetLogger()
        {
            using (var factory = LoggerFactory.Create(builder => builder.AddConsole()))
            {
                return factory.CreateLogger(typeof(Program).FullName);
            }
        }

        public static async Task Main(string[] args)
        {
            Config config = new Config();

            bool showHelp = false;
            bool generateRequest = false;
            bool splitFile = false;
            string prefix = string.Empty;

            var option = new OptionSet()
            {
                {
                    "c|connectionString=", "the {CONNECTIONSTRING} of source blob.",
                    value =>
                    {
                        if (value != null)
                        {
                            config.StorageConnectionString = value;
                        }
                    }
                },
                {
                    "s|splitSizeInMb=", "the {SIZE} of splited file.",
                    (long value) =>
                    {
                        if (value > 0)
                        {
                            config.SplitSizeInBytes = value;
                        }
                    }
                },
                {
                    "n|maxFileNumber=", "the {MaxNumber} of files to be used.",
                    (int value) =>
                    {
                        if (value > 0)
                        {
                            config.MaxFileNumber = value;
                        }
                    }
                },
                {
                    "generate",  "generate the request from the given path",
                    value => generateRequest = value != null
                },
                {
                    "split",  "split the file from given path",
                    value => splitFile = value != null
                },
                {
                    "p|prefix=",  "the {PREFIX} of input or splited files",
                    value =>
                    {
                        if (value != null)
                        {
                            prefix = value;
                        }
                    }
                },
                {
                    "h|help",  "show this message and exit",
                    value => showHelp = value != null
                },
            };

            try
            {
                option.Parse(args);

                if (generateRequest)
                {
                    await RequestGenerator.GenerateImportRequest(config.StorageConnectionString, prefix, config.MaxFileNumber);
                }
            }
            catch (OptionException oe)
            {
                _logger.LogError("Failed to reslove arguments due to {0}", oe.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to generate request or split file due to {0}", ex.Message);
            }
        }
    }
}
