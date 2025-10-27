// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Api.Features.Binders;
using Microsoft.Health.Fhir.Api.OpenIddict.Extensions;

namespace Microsoft.Health.Fhir.Web
{
    internal static class Program
    {
        public static async Task Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, builder) =>
                {
                    builder.Sources.Add(new GenericConfigurationSource(() => new DictionaryExpansionConfigurationProvider(new EnvironmentVariablesConfigurationProvider())));

                    var builtConfig = builder.Build();

                    var keyVaultEndpoint = builtConfig["KeyVault:Endpoint"];
                    if (!string.IsNullOrEmpty(keyVaultEndpoint))
                    {
                        var credential = new ManagedIdentityCredential();
                        builder.AddAzureKeyVault(new System.Uri(keyVaultEndpoint), credential);
                    }

                    builder.AddDevelopmentAuthEnvironmentIfConfigured(builtConfig);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder
                        .UseContentRoot(Path.GetDirectoryName(typeof(Program).Assembly.Location))
                        .UseStartup<Startup>();
                })
                .Build();

            await host.RunAsync();
        }
    }
}
