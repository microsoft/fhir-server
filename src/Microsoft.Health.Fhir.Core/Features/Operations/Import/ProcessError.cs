// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ProcessError
    {
        public ProcessError(long lineNumber, long resourceSurrogatedId, string errorMessage)
        {
            LineNumber = lineNumber;
            ErrorMessage = errorMessage;
            ResourceSurrogatedId = resourceSurrogatedId;
        }

        public long LineNumber { get; set; }

        public string ErrorMessage { get; set; }

        public long ResourceSurrogatedId { get; set; }
    }
}
