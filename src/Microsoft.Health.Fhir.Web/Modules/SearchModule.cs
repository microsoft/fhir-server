// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Features.Search;
using Microsoft.Health.Fhir.CosmosDb.Features.Search.Queries;

namespace Microsoft.Health.Fhir.Web.Modules
{
    /// <summary>
    /// Registration of search components.
    /// </summary>
    public class SearchModule : IStartupModule
    {
        /// <inheritdoc />
        public void Load(IServiceCollection services)
        {
            EnsureArg.IsNotNull(services, nameof(services));

            services.Add<CosmosSearchService>()
                .Scoped()
                .AsSelf()
                .AsImplementedInterfaces();

            services.AddSingleton<IQueryBuilder, QueryBuilder>();
        }
    }
}
