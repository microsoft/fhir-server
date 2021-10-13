// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage.StoredProcedures.UpdateTaskContext;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class CosmosDbTaskContextUpdater : IContextUpdater
    {
        private string _taskId;
        private string _runId;

        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private ILogger<CosmosDbTaskContextUpdater> _logger;

        private static readonly UpdateTaskContext _updateTaskContext = new UpdateTaskContext();

        public CosmosDbTaskContextUpdater(
            string taskId,
            string runId,
            IScoped<Container> containerScope,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILogger<CosmosDbTaskContextUpdater> logger)
        {
            EnsureArg.IsNotNullOrEmpty(taskId, nameof(taskId));
            EnsureArg.IsNotNullOrEmpty(runId, nameof(runId));
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(logger, nameof(logger));

            _taskId = taskId;
            _runId = runId;
            _containerScope = containerScope;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _logger = logger;
        }

        public async Task UpdateContextAsync(string context, CancellationToken cancellationToken)
        {
            try
            {
                var response = await _retryExceptionPolicyFactory.RetryPolicy.ExecuteAsync(
                    async ct => await _updateTaskContext.ExecuteAsync(
                        _containerScope.Value.Scripts,
                        _taskId,
                        _runId,
                        context,
                        ct),
                    cancellationToken);
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
    }
}
