// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Security;

namespace Microsoft.Health.Fhir.Stu3.Api.Modules
{
    /// <summary>
    /// Registration of data persistence components
    /// </summary>
    /// <seealso cref="IStartupModule" />
    public class PersistenceModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.AddSingleton<IRawResourceFactory, RawResourceFactory>();
            services.AddSingleton<IResourceWrapperFactory, ResourceWrapperFactory>();
            services.AddSingleton<IClaimsExtractor, PrincipalClaimsExtractor>();
        }
    }
}
