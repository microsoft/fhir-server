// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.CosmosDb.Features.Storage;
using Microsoft.Health.TaskManagement;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    public class CosmosDbTaskContextUpdaterFactory : IContextUpdaterFactory
    {
        private readonly IScoped<Container> _containerScope;
        private readonly RetryExceptionPolicyFactory _retryExceptionPolicyFactory;
        private ILoggerFactory _loggerFactory;

        public CosmosDbTaskContextUpdaterFactory(
            IScoped<Container> containerScope,
            RetryExceptionPolicyFactory retryExceptionPolicyFactory,
            ILoggerFactory loggerFactory)
        {
            EnsureArg.IsNotNull(containerScope, nameof(containerScope));
            EnsureArg.IsNotNull(retryExceptionPolicyFactory, nameof(retryExceptionPolicyFactory));
            EnsureArg.IsNotNull(loggerFactory, nameof(loggerFactory));

            _containerScope = containerScope;
            _retryExceptionPolicyFactory = retryExceptionPolicyFactory;
            _loggerFactory = loggerFactory;
        }

        public IContextUpdater CreateContextUpdater(string taskId, string runId)
        {
            EnsureArg.IsNotEmptyOrWhiteSpace(taskId, nameof(taskId));
            EnsureArg.IsNotEmptyOrWhiteSpace(runId, nameof(runId));

            return new CosmosDbTaskContextUpdater(taskId, runId, _containerScope, _retryExceptionPolicyFactory, _loggerFactory.CreateLogger<CosmosDbTaskContextUpdater>());
        }
    }
}
