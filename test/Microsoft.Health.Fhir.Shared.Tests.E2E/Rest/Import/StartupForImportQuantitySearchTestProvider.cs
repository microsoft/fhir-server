// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Shared.Tests.E2E;
using Microsoft.Health.Fhir.Shared.Tests.E2E.Rest;

namespace Microsoft.Health.Fhir.Tests.E2E.Rest.Import
{
    [RequiresIsolatedDatabase]
    public class StartupForImportQuantitySearchTestProvider : StartupBaseForCustomProviders
    {
        public StartupForImportQuantitySearchTestProvider(IConfiguration configuration)
            : base(configuration)
        {
        }

        public override void ConfigureServices(IServiceCollection services)
        {
            base.ConfigureServices(services);
        }
    }
}
