// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    internal static class SmartFineGrainedAccessControlRejection
    {
        private const string SmartFineGrainedAccessControlForbidden = "SMART fine-grained access control does not permit this operation.";

        public static void Check(bool enabled, RequestContextAccessor<IFhirRequestContext> requestContextAccessor)
        {
            if (enabled &&
                requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
            {
                throw new UnauthorizedFhirActionException(SmartFineGrainedAccessControlForbidden);
            }
        }
    }
}
