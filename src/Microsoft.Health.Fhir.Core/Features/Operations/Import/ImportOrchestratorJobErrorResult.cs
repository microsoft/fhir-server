// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ImportOrchestratorJobErrorResult
    {
        /// <summary>
        /// Err http status code
        /// </summary>
        public HttpStatusCode HttpStatusCode { get; set; }

        /// <summary>
        /// Error message
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Inner error if there're multiple errors
        /// </summary>
        public ImportOrchestratorJobErrorResult InnerError { get; set; }

        /// <summary>
        /// Details
        /// </summary>
        public string ErrorDetails { get; set; }
    }
}
