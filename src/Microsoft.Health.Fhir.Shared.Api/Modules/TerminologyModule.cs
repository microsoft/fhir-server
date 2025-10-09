// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Shared.Core.Features.Conformance;

namespace Microsoft.Health.Fhir.Api.Modules
{
    public class TerminologyModule : IStartupModule
    {
        public void Load(IServiceCollection services)
        {
            services.AddSingleton<IAsyncResourceResolver>(
                _ =>
                {
                    return new MultiResolver(new CachedResolver(ZipSource.CreateValidationSource()));
                });

            services.AddSingleton<ITerminologyService, LocalTerminologyService>();
            services.AddSingleton<ITerminologyServiceProxy, FirelyTerminologyServiceProxy>();
        }
    }
}
