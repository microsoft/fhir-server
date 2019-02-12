// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Exceptions;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class AuditException : FhirException
    {
        public AuditException(string controllerName, string actionName)
            : base(string.Format(Resources.MissingAuditInformation, controllerName, actionName))
        {
        }
    }
}
