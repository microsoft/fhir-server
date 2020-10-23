// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Create;
using Microsoft.Health.Fhir.Core.Messages.Upsert;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// Intercepts ConditionalCreateResourceRequests and wraps them in a CosmosDbDistributedLock
    /// </summary>
    public class ConditionalCreateLockingBehavior
        : IPipelineBehavior<ConditionalCreateResourceRequest, UpsertResourceResponse>,
            IPipelineBehavior<ConditionalUpsertResourceRequest, UpsertResourceResponse>
    {
        private readonly ICosmosDbDistributedLockFactory _lockFactory;

        public ConditionalCreateLockingBehavior(ICosmosDbDistributedLockFactory lockFactory)
        {
            EnsureArg.IsNotNull(lockFactory, nameof(lockFactory));

            _lockFactory = lockFactory;
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalCreateResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(request.Resource.InstanceType, request.ConditionalParameters, next);
        }

        public async Task<UpsertResourceResponse> Handle(ConditionalUpsertResourceRequest request, CancellationToken cancellationToken, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNull(request, nameof(request));
            EnsureArg.IsNotNull(next, nameof(next));

            return await Execute(request.Resource.InstanceType, request.ConditionalParameters, next);
        }

        private async Task<UpsertResourceResponse> Execute(string resourceType, IReadOnlyList<Tuple<string, string>> conditions, RequestHandlerDelegate<UpsertResourceResponse> next)
        {
            EnsureArg.IsNotNullOrEmpty(resourceType, nameof(resourceType));
            EnsureArg.IsNotNull(conditions, nameof(conditions));
            EnsureArg.IsNotNull(next, nameof(next));

            var hashBuilder = new StringBuilder();
            conditions.Aggregate(hashBuilder, (builder, tuple) => builder.AppendFormat("{0}={1}&", tuple.Item1, tuple.Item2));

            var conditionalHash = hashBuilder.ToString().ComputeHash();
            ICosmosDbDistributedLock lockDocument = _lockFactory.Create($"ConditionalCreate:{resourceType}:{conditionalHash}");

            UpsertResourceResponse response;
            try
            {
                if (await lockDocument.TryAcquireLock() == false)
                {
                    // This is a fast-fail lock acquisition, if a lock cannot be obtained, return a conflict
                    throw new ResourceConflictException(Core.Resources.ResourceConflict);
                }

                response = await next();
            }
            finally
            {
                await lockDocument.DisposeAsync();
            }

            return response;
        }
    }
}
