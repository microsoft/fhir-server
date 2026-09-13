// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Context;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Context;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Security
{
    /// <summary>
    /// Validates that operations disallowed for SMART fine-grained access-control requests are not invoked.
    /// </summary>
    internal static class SmartFineGrainedAccessControlRejection
    {
        /// <summary>
        /// Ensures the current request is allowed when a SMART operation restriction is enabled.
        /// </summary>
        /// <param name="enabled">Whether the operation-specific SMART restriction is enabled.</param>
        /// <param name="requestContextAccessor">The current FHIR request context accessor.</param>
        /// <param name="operationName">The operation name to include in the forbidden diagnostics.</param>
        /// <exception cref="UnauthorizedFhirActionException">Thrown when fine-grained access control applies and the restriction is enabled.</exception>
        internal static void EnsureAllowed(bool enabled, RequestContextAccessor<IFhirRequestContext> requestContextAccessor, string operationName)
        {
            if (enabled &&
                requestContextAccessor.RequestContext?.AccessControlContext?.ApplyFineGrainedAccessControl == true)
            {
                throw new UnauthorizedFhirActionException(string.Format(Core.Resources.SmartFineGrainedAccessControlOperationForbidden, operationName));
            }
        }
    }
}
