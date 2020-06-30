// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class AnonymizationModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.Add<ExportAnonymizer>()
                    .Scoped()
                    .AsService<IAnonymizer>();
        }
    }
}
