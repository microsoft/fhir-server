// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Api.Features.Context
{
    [AttributeUsage(AttributeTargets.Method)]
    public class AuditEventSubTypeAttribute : Attribute
    {
        public AuditEventSubTypeAttribute(string requestSubType)
        {
            EnsureArg.IsNotNull(requestSubType, nameof(requestSubType));
            AuditEventType = requestSubType;
        }

        public string AuditEventType { get; }
    }
}
