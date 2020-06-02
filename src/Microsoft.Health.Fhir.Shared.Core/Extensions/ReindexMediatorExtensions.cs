// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Reindex;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class ReindexMediatorExtensions
    {
        public static async Task<ResourceElement> CreateReindexJobAsync(
            this IMediator mediator,
            int? maximumConcurrency,
            string scope,
            CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new CreateReindexRequest(maximumConcurrency, scope);

            CreateReindexResponse response = await mediator.Send(request, cancellationToken);
            return response.Job.JobRecord.ToParametersResourceElement();
        }

        public static async Task<ResourceElement> GetReindexJobAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));

            var request = new GetReindexRequest(jobId);

            GetReindexResponse response = await mediator.Send(request, cancellationToken);
            return response.Job.JobRecord.ToParametersResourceElement();
        }

        public static async Task<ResourceElement> CancelReindexAsync(this IMediator mediator, string jobId, CancellationToken cancellationToken)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            EnsureArg.IsNotNullOrWhiteSpace(jobId, nameof(jobId));

            var request = new CancelReindexRequest(jobId);

            var response = await mediator.Send(request, cancellationToken);

            return response.Job.JobRecord.ToParametersResourceElement();
        }

        public static Task<ResourceElement> GetReindexJobsAsync(this IMediator mediator)
        {
            throw new NotImplementedException();
        }
    }
}
