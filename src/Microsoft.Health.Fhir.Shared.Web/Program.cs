// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using Azure.Identity;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Health.Fhir.Api.Features.Binders;

namespace Microsoft.Health.Fhir.Web
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            var host = WebHost.CreateDefaultBuilder(args)
                .UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location))
                .ConfigureAppConfiguration((hostContext, builder) =>
                {
                    builder.Sources.Add(new GenericConfigurationSource<EnvironmentVariablesDictionaryConfigurationProvider>());

                    var builtConfig = builder.Build();

                    var keyVaultEndpoint = builtConfig["KeyVault:Endpoint"];
                    if (!string.IsNullOrEmpty(keyVaultEndpoint))
                    {
                        var credential = new DefaultAzureCredential();
                        builder.AddAzureKeyVault(new System.Uri(keyVaultEndpoint), credential);
                    }

                    builder.AddDevelopmentAuthEnvironmentIfConfigured(builtConfig);
                })
                .UseStartup<Startup>()
                .Build();

            host.Run();
        }
    }
}
