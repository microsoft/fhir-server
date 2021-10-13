// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Configs;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.CompleteTaskInfo;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.GetNextTask;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.KeepAliveTask;
using Microsoft.Health.TaskManagement;
using Newtonsoft.Json;
using Container = Microsoft.Azure.Cosmos.Container;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosDbTaskConsumer : ITaskConsumer
    {
        private TaskHostingConfiguration _taskHostingConfiguration;
        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger _logger;

        private static readonly CompleteTaskInfo _completeTask = new CompleteTaskInfo();
        private static readonly KeepAliveTask _keepAliveTask = new KeepAliveTask();
        private static readonly GetNextTask _getNextTask = new GetNextTask();

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosFhirOperationDataStore"/> class.
        /// </summary>
        /// <param name="containerScope">The factory for <see cref="Container"/>.</param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="taskHostingConfiguration">The TaskHosting Configuration</param>
        /// <param name="logger">The logger.</param>
        public CosmosDbTaskConsumer(
            IOptions<TaskHostingConfiguration> taskHostingConfiguration,
            IScoped<Container> containerScope,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosdbTaskManager> logger)
        {
            EnsureArg.IsNotNull(taskHostingConfiguration, nameof(taskHostingConfiguration));
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskHostingConfiguration = taskHostingConfiguration.Value;
            _containerScope = containerScope;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
        }

        public async Task<TaskInfo> CompleteAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(taskId, nameof(taskId));
            EnsureArg.IsNotNull(taskResultData, nameof(taskResultData));
            EnsureArg.IsNotNullOrEmpty(runId, nameof(runId));

            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _completeTask.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        taskId,
                        JsonConvert.SerializeObject(taskResultData),
                        runId,
                        ct),
                    cancellationToken);

                return response.Resource.TaskInfo;
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(dce, "Failed to complete task.");
                throw;
            }
        }

        public async Task<TaskInfo> KeepAliveAsync(string taskId, string runId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrEmpty(taskId, nameof(taskId));
            EnsureArg.IsNotNullOrEmpty(runId, nameof(runId));

            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _keepAliveTask.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        taskId,
                        runId,
                        ct),
                    cancellationToken);

                return response.Resource.TaskInfo;
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(dce, "Failed to keep alive task.");
                throw;
            }
        }

        public async Task<IReadOnlyCollection<TaskInfo>> GetNextMessagesAsync(short count, int taskHeartbeatTimeoutThresholdInSeconds, CancellationToken cancellationToken)
        {
            try
            {
                string queueId = _taskHostingConfiguration.QueueId;
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _getNextTask.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        queueId,
                        (ushort)count,
                        taskHeartbeatTimeoutThresholdInSeconds,
                        ct),
                    cancellationToken);

                return response.Resource.Select(wrapper => wrapper.TaskInfo).ToList();
            }
            catch (CosmosException dce)
            {
                if (dce.IsRequestEntityTooLarge())
                {
                    throw;
                }

                _logger.LogError(dce, "Failed to get next task.");
                throw;
            }
        }

        public Task<TaskInfo> ResetAsync(string taskId, TaskResultData taskResultData, string runId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
