// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EnsureThat;
using GreenDonut;
using Hl7.Fhir.Model;
using HotChocolate.DataLoader;
using MediatR;
using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Shared.Web
{
    public class PatientBatchDataLoader : BatchDataLoader<string, Patient>
    {
        private readonly IMediator _mediator;

        public PatientBatchDataLoader(
            IMediator mediator,
            IBatchScheduler batchScheduler,
            DataLoaderOptions<string>? options = null)
            : base(batchScheduler, options)
        {
            EnsureArg.IsNotNull(mediator, nameof(mediator));
            _mediator = mediator;
        }

        protected override async Task<IReadOnlyDictionary<string, Patient>> LoadBatchAsync(
            IReadOnlyList<string> keys,
            CancellationToken cancellationToken)
        {
            // instead of fetching one person, we fetch multiple persons
            var response = await _mediator.Send(new SearchResourceRequest("Patient", keys), cancellationToken);
            return (IReadOnlyDictionary<string, Patient>)response.Bundle;
        }
    }
}
