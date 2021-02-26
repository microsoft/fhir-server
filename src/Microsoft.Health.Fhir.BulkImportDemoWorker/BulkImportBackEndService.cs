// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Task = System.Threading.Tasks.Task;

namespace Microsoft.Health.Fhir.BulkImportDemoWorker
{
    public class BulkImportBackEndService : BackgroundService
    {
        private const int MaxRunningTaskCount = 1;
        private ISearchIndexer _searchIndexer;
        private IRawResourceFactory _rawResourceFactory;
        private ResourceIdProvider _resourceIdProvider;
        private IConfiguration _configuration;

        public BulkImportBackEndService(
            ISearchIndexer searchIndexer,
            IRawResourceFactory rawResourceFactory,
            ResourceIdProvider resourceIdProvider,
            IConfiguration configuration)
        {
            _searchIndexer = searchIndexer;
            _rawResourceFactory = rawResourceFactory;
            _resourceIdProvider = resourceIdProvider;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string sqlConnectionString = _configuration["SqlConnectionString"];
            using SqlTaskConsumer consumer = new SqlTaskConsumer(sqlConnectionString, "WorkerQueue");

            TaskHosting hosting = new TaskHosting(
                consumer,
                (taskInfo) =>
                {
                    if (taskInfo.TaskTypeId == BulkImportTask.BulkImportTaskType)
                    {
                        return new BulkImportTask(_searchIndexer, _rawResourceFactory, _resourceIdProvider, _configuration, taskInfo.InputData);
                    }
                    else
                    {
                        return null;
                    }
                },
                maxRunningTaskCount: MaxRunningTaskCount);

            await hosting.StartAsync(stoppingToken);
            await hosting.StopAndWaitCompleteAsync();
        }
    }
}
