// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Operation.PatientEverything
{
    public class GetPatientEverythingResponse : IOperationFhirResponse
    {
        public GetPatientEverythingResponse(IResourceElement bundle)
        {
            Response = bundle;
        }

        public IResourceElement Response { get; }

        public HttpStatusCode StatusCode => HttpStatusCode.OK;
    }
}
