// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Exceptions
{
    /// <summary>
    /// Exception raised when the service does not have sufficient permissions to execute an action.
    /// </summary>
    public class InsufficientAccessException : FhirException
    {
        public InsufficientAccessException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
