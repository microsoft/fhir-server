// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GreenDonut;
using Hl7.Fhir.Model;
using HotChocolate.DataLoader;
using MediatR;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Messages.GraphQl;

// using Microsoft.Health.Fhir.Core.Messages.Search;

namespace Microsoft.Health.Fhir.Api.Features.QueryGraphQl
{
    public class PatientByIdDataLoader : BatchDataLoader<string, Patient>
    {
        private readonly IMediator _mediator;

        public PatientByIdDataLoader(IBatchScheduler batchScheduler, IMediator mediator)
            : base(batchScheduler)
        {
            _mediator = mediator;
        }

        protected override async Task<IReadOnlyDictionary<string, Patient>> LoadBatchAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken)
        {
            // var queries = new List<Tuple<string, string>> { Tuple.Create ("id", keys)};
            var queries = new List<Tuple<string, string>>();
            int count = keys.Count;

            for (int i = 0; i < count; i++)
            {
                queries.Add(new Tuple<string, string>("_id", keys[i]));
            }

            // queries ("_id", "jasidufioasdfj")
            GraphQlResponse response = await _mediator.Send(new GraphQlRequest("Patient", queries), CancellationToken.None);

            // SearchResourceResponse response = await _mediator.Send(new SearchResourceRequest("Patient", queries), CancellationToken.None);
            var patient = response.Patient.ToPoco<Patient>();
            Dictionary<string, Patient> dict = new Dictionary<string, Patient>
            {
                { keys[0], patient },
            };

            return dict;

            // RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey("Patient", "d71c377d-741b-4ef1-a157-a3d3795016ba"), CancellationToken.None);
            // return (IReadOnlyDictionary<string, Patient>)response.RawResource.GetType();
        }
    }
}
