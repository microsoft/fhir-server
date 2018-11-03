// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using Microsoft.Azure.Documents;
using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.CosmosDb.Features.Storage
{
    /// <summary>
    /// A <see cref="IDocumentClient"/> wrapper that handles exception for each request.
    /// </summary>
    internal partial class DocumentClientWithExceptionHandler
    {
        private void ProcessException(Exception ex)
        {
            if (ex is DocumentClientException dce &&
                dce.StatusCode == (HttpStatusCode)429)
            {
                throw new RequestRateExceededException(dce.RetryAfter);
            }
        }
    }
}
