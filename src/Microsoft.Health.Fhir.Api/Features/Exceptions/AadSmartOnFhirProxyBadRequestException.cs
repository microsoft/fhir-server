// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Exceptions
{
    public class AadSmartOnFhirProxyBadRequestException : Exception
    {
        public AadSmartOnFhirProxyBadRequestException(string message, ArgumentNullException innerException)
            : base(message, innerException)
        {
            message = string.Format(Resources.ValueCannotBeNull, innerException.ParamName);
        }

        public AadSmartOnFhirProxyBadRequestException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
