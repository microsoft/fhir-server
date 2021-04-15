// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.CapabilityStatement
{
    public sealed class RebuildCapabilityStatement : INotification
    {
        public RebuildCapabilityStatement(RebuildPart part)
        {
            Part = part;
        }

        public RebuildPart Part { get; }
    }
}
