// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Health.Internal.Fhir.RegisterAndMonitorImport
{
    public static class Program
    {
        public static async Task Main()
        {
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            RegisterAndMonitorConfiguration registerAndMonitorConfiguration = new();
            configuration.GetSection(RegisterAndMonitorConfiguration.SectionName).Bind(registerAndMonitorConfiguration);

            var import = new RegisterAndMonitorImport(registerAndMonitorConfiguration);
            await import.Run();
        }
    }
}
