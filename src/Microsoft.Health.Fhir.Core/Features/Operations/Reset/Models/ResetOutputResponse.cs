// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Reset.Models
{
    /// <summary>
    /// This is the output response that we send back to the client once bulk import is complete.
    /// Subset of <see cref="ResetFileInfo"/>.
    /// </summary>
    public class ResetOutputResponse
    {
        public ResetOutputResponse(string error)
        {
            Error = error;
        }

        [JsonConstructor]
        protected ResetOutputResponse()
        {
        }

        [JsonProperty(JobRecordProperties.Error)]
        public string Error { get; private set; }
    }
}
