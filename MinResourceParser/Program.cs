// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore;

namespace MinResourceParser
{
    public static class Program
    {
        public static void Main(string[] args)
        {
#pragma warning disable CS8604 // Possible null reference argument.
            var host = WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(contentRoot: Path.GetDirectoryName(typeof(Program).Assembly.Location))
                .ConfigureAppConfiguration((hostContext, builder) =>
                {
                    var builtConfig = builder.Build();
                })
                .UseStartup<Startup>()
                .Build();
#pragma warning restore CS8604 // Possible null reference argument.

            host.Run();
        }
    }
}
