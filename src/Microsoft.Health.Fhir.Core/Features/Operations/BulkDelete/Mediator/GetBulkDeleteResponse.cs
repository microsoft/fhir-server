// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Net;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.BulkDelete.Mediator
{
    public class GetBulkDeleteResponse
    {
        public GetBulkDeleteResponse(
            IEnumerable<Tuple<string, Base>> results,
            IList<OperationOutcomeIssue> issues,
            HttpStatusCode httpStatusCode)
        {
            Results = results;
            Issues = issues;
            HttpStatusCode = httpStatusCode;
        }

        public IEnumerable<Tuple<string, Base>> Results { get; }

        public IList<OperationOutcomeIssue> Issues { get; }

        public HttpStatusCode HttpStatusCode { get; }
    }
}
