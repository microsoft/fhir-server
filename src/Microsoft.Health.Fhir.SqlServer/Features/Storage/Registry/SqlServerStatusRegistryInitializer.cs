// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using EnsureThat;
using Microsoft.Health.Abstractions.Features.Transactions;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Features.Search.Registry;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage.Registry
{
    internal class SqlServerStatusRegistryInitializer : IStartable
    {
        private readonly ISearchParameterRegistryDataStore _filebasedRegistry;
        private readonly Func<IScoped<SqlServerStatusRegistryDataStore>> _searchParameterRegistryFactory;
        private readonly ITransactionHandler _transactionHandler;

        public SqlServerStatusRegistryInitializer(
            FilebasedSearchParameterRegistryDataStore.Resolver filebasedRegistry,
            Func<IScoped<SqlServerStatusRegistryDataStore>> searchParameterRegistryFactory,
            ITransactionHandler transactionHandler)
        {
            EnsureArg.IsNotNull(filebasedRegistry, nameof(filebasedRegistry));
            EnsureArg.IsNotNull(searchParameterRegistryFactory, nameof(searchParameterRegistryFactory));
            EnsureArg.IsNotNull(transactionHandler, nameof(transactionHandler));

            _filebasedRegistry = filebasedRegistry.Invoke();
            _searchParameterRegistryFactory = searchParameterRegistryFactory;
            _transactionHandler = transactionHandler;
        }

        public async void Start()
        {
            using (IScoped<SqlServerStatusRegistryDataStore> registry = _searchParameterRegistryFactory.Invoke())
            {
                // Wrap the SQL calls in a transaction to ensure the read and insert operations are atomic.
                using (var transaction = _transactionHandler.BeginTransaction())
                {
                    if (await registry.Value.IsSearchParameterRegistryEmpty(CancellationToken.None))
                    {
                        // The registry has yet to be initialized, so get the search parameter statuses from file and add them to the registry.
                        IReadOnlyCollection<ResourceSearchParameterStatus> readonlyStatuses = await _filebasedRegistry.GetSearchParameterStatuses();
                        var statuses = new List<ResourceSearchParameterStatus>(readonlyStatuses);

                        await registry.Value.BulkInsert(statuses, CancellationToken.None);
                    }

                    transaction.Complete();
                }
            }
        }
    }
}
