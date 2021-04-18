// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import
{
    public class ProcessError
    {
        public ProcessError(long lineNumber, string errorMessage)
        {
            LineNumber = lineNumber;
            ErrorMessage = errorMessage;
        }

        public long LineNumber { get; set; }

        public string ErrorMessage { get; set; }
    }
}
