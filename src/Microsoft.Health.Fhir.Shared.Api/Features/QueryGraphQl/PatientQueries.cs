// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using System.Threading.Tasks;
using Hl7.Fhir.Model;
using HotChocolate.Types;
using Microsoft.Health.Fhir.Api.Features.QueryGraphQl;

namespace Microsoft.Health.Fhir.Shared.Api.Features.QueryGraphQl
{
    [ExtendObjectType(Name = "Query")]
#pragma warning disable CA1041 // Provide ObsoleteAttribute message
    [System.Obsolete]
#pragma warning restore CA1041 // Provide ObsoleteAttribute message
    public class PatientQueries
    {
        public Task<Patient> GetPatientByIdAsync(
            string id,
            PatientByIdDataLoader patientByIdDataLoader,
            CancellationToken cancellationToken) => patientByIdDataLoader.LoadAsync(id, cancellationToken);
    }

    /*public class Query
    {
        private IMediator _mediator;

        public Query(IMediator mediator)
        {
            _mediator = mediator;
        }

        public async Task<Patient> GetPatient(string id, CancellationToken cancellationToken)
        {
            var queries = new List<Tuple<string, string>> { Tuple.Create("id", id) };
            cancellationToken.Equals(CancellationToken.None);
            SearchResourceResponse response = await _mediator.Send(new SearchResourceRequest("Patient", queries), cancellationToken);

            // RawResourceElement response = await _mediator.GetResourceAsync(new ResourceKey("Patient", id), CancellationToken.None);
            return response.Bundle.ToPoco<Patient>();
        }
    }*/

    /*public class Query
    {
        public Patient Patient(string id) => new Patient { Id = id, Active = true };
    }*/
}
