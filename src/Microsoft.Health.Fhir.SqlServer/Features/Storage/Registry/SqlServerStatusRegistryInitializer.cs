// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistryInitializer : IStartable
    {
        private readonly ISearchParameterRegistry _filebasedRegistry;
        private readonly SqlServerStatusRegistry _sqlServerStatusRegistry;

        public SqlServerStatusRegistryInitializer(
            FilebasedSearchParameterRegistry.Resolver filebasedRegistry,
            SqlServerStatusRegistry sqlServerStatusRegistry)
        {
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(sqlServerStatusRegistry, nameof(sqlServerStatusRegistry));

            _filebasedRegistry = filebasedRegistry.Invoke();
            _sqlServerStatusRegistry = sqlServerStatusRegistry;
        }

        public async void Start() // TODO: Should a start method be async?
        {
            if (await _sqlServerStatusRegistry.GetIsSearchParameterRegistryEmpty())
            {
                IReadOnlyCollection<ResourceSearchParameterStatus> readonlyStatuses = await _filebasedRegistry.GetSearchParameterStatuses();
                var statuses = new List<ResourceSearchParameterStatus>(readonlyStatuses);

                await _sqlServerStatusRegistry.BulkInsert(statuses);
            }
        }
    }
}
