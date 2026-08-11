// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Mcp;

internal sealed class FhirRequestException : Exception
{
    internal FhirRequestException(string message, string captureDirectory, Exception? innerException = null)
        : base(message, innerException)
    {
        CaptureDirectory = captureDirectory;
    }

    internal string CaptureDirectory { get; }
}
