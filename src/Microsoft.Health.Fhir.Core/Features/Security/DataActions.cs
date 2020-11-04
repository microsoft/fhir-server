// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Runtime.Serialization;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    [Flags]
#pragma warning disable CA1028 // Enum Storage should be Int32
    public enum DataActions : ulong
#pragma warning restore CA1028 // Enum Storage should be Int32
    {
        None = 0,

        Read = 1,
        Write = 1 << 1,
        Delete = 1 << 2,
        HardDelete = 1 << 3,
        Export = 1 << 4,
        ResourceValidate = 1 << 5,
        Reindex = 1 << 6,
        Operation = 1 << 7,

        [EnumMember(Value = "*")]
        All = (ResourceValidate << 1) - 1,
    }
}
