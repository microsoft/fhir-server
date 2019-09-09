// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Api.Features.Audit
{
    public class MissingAuditEventTypeMappingException : Exception
    {
        public MissingAuditEventTypeMappingException(string controllerName, string actionName)
        : base(string.Format(Resources.MissingAuditInformation, controllerName, actionName))
        {
        }
    }
}
