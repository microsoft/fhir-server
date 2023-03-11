// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Health.Fhir.Core.Features.Operations
{
    [SuppressMessage("Design", "CA1028:Enum Storage should be Int32", Justification = "Byte is sufficient for this type.")]
    public enum QueueType : byte
    {
        Unknown = 0, // should not be used
        Export = 1,
        Import = 2,
        Defrag = 3,
        ConditionalDelete = 4,
    }
}
