// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Messages
{
    public class GetBulkDeleteResponse
    {
        public GetBulkDeleteResponse(
            ICollection<Parameters.ParameterComponent> results,
            ICollection<OperationOutcomeIssue> issues,
            HttpStatusCode httpStatusCode)
        {
            Results = results;
            Issues = issues;
            HttpStatusCode = httpStatusCode;
        }

        /// <summary>
        /// Results to put into the Parameters object returned for the request.
        /// </summary>C
        public ICollection<Parameters.ParameterComponent> Results { get; }

        public ICollection<OperationOutcomeIssue> Issues { get; }

        public HttpStatusCode HttpStatusCode { get; }
    }
}
