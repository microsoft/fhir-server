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
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ReindexMediatorExtensions
    {
        public static async Task<ResourceElement> CreateReindexJobAsync(
            this IMediator mediator,
            ushort? maximumConcurrency,
            uint? maxResourcesPerQuery,
            int? queryDelay,
            ushort? targetDataStoreResourcePercentage,
            string targetResourceTypesString,
            string targetSearchParamTypesString,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var targetResourceTypes = new List<string>();

            if (!string.IsNullOrEmpty(targetResourceTypesString))
            {
                targetResourceTypes.AddRange(targetResourceTypesString.Split(",").Select(s => s.Trim()));
                foreach (var resourceType in targetResourceTypes)
                {
                    if (!VersionSpecificResourceTypeValidator.IsValidResourceType(resourceType))
                    {
                        throw new RequestNotValidException(string.Format(Resources.ResourceNotSupported, resourceType));
                    }
                }
            }

            var targetSearchParamTypes = new List<string>();

            if (!string.IsNullOrEmpty(targetSearchParamTypesString))
            {
                targetSearchParamTypes.AddRange(targetSearchParamTypesString.Split(",").Select(s => s.Trim()));
            }

            if (targetResourceTypes.Count > 0 && targetSearchParamTypes.Count > 0)
            {
                throw new RequestNotValidException(Resources.ReindexMultipleTargetsNotSupported);
            }

            var request = new CreateReindexRequest(targetResourceTypes, targetSearchParamTypes, maximumConcurrency, maxResourcesPerQuery, queryDelay, targetDataStoreResourcePercentage);

            CreateReindexResponse response = await mediator.Send(request, cancellationToken);
            return response.Job.ToParametersResourceElement();
        }

        public static async Task<ReindexSingleResourceResponse> SendReindexSingleResourceRequestAsync(
            this IMediator mediator,
            string httpMethod,
            string resourceType,
            string resourceId,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new ReindexSingleResourceRequest(httpMethod, resourceType, resourceId);

            ReindexSingleResourceResponse response = await mediator.Send(request, cancellationToken);
            return response;
        }

        public static async Task<ResourceElement> GetReindexJobAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetReindexRequest(jobId);

            GetReindexResponse response = await mediator.Send(request, cancellationToken);
            return response.Job.ToParametersResourceElement();
        }

        public static async Task<ResourceElement> CancelReindexAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelReindexRequest(jobId);

            var response = await mediator.Send(request, cancellationToken);

            return response.Job.ToParametersResourceElement();
        }

        public static Task<ResourceElement> GetReindexJobsAsync(this IMediator mediator)
        {
            throw new NotImplementedException();
        }
    }
}
