// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class AuditHeaderException : FhirException
    {
        public AuditHeaderException(string headerName, int size)
            : base(string.Format(Resources.CustomAuditHeaderTooLarge, headerName, size, AuditConstants.MaximumLengthOfCustomHeader))
        {
        }

        public AuditHeaderException(int size)
            : base(string.Format(Resources.TooManyCustomAuditHeaders, AuditConstants.MaximumNumberOfCustomHeaders, size))
        {
        }
    }
}
