// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.CreateTask;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.GetTask;
using Microsoft.Health.TaskManagement;
using Container = Microsoft.Azure.Cosmos.Container;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    public class CosmosdbTaskManager : ITaskManager
    {
        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private readonly ILogger _logger;

        private static readonly CreateTask _createTask = new CreateTask();
        private static readonly GetTask _getTask = new GetTask();

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosFhirOperationDataStore"/> class.
        /// </summary>
        /// <param name="containerScope">The factory for <see cref="Container"/>.</param>
        /// <param name="cosmosDataStoreConfiguration">The data store configuration.</param>
        /// <param name="namedCosmosCollectionConfigurationAccessor">The IOptions accessor to get a named version.</param>
        /// <param name="retryExceptionPolicyFactory">The retry exception policy factory.</param>
        /// <param name="queryFactory">The Query Factory</param>
        /// <param name="logger">The logger.</param>
        public CosmosdbTaskManager(
            IScoped<Container> containerScope,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosdbTaskManager> logger)
        {
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _containerScope = containerScope;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
        }

        public async Task<TaskInfo> CreateTaskAsync(TaskInfo task, bool isUniqueTaskByType, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(task, nameof(task));

            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _createTask.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        task.TaskId,
                        task.QueueId,
                        (ushort)task.TaskTypeId,
                        task.InputData,
                        isUniqueTaskByType,
                        (ushort)task.MaxRetryCount,
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

                _logger.LogError(dce, "Failed to create task.");
                throw;
            }
        }

        public async Task<TaskInfo> GetTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNullOrWhiteSpace(taskId, nameof(taskId));
            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _getTask.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        taskId,
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

                _logger.LogError(dce, "Failed to create task.");
                throw;
            }
        }

        public Task<TaskInfo> CancelTaskAsync(string taskId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
