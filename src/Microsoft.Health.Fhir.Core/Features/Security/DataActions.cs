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
        ConvertData = 1 << 7,
        EditProfileDefinitions = 1 << 8, // Allows to Create/Update/Delete resources related to profile's resources.
        Import = 1 << 9,

        Smart = 1 << 30, // Do not include Smart in the '*' case.  We only want smart for a user if explicitly added to the role or user

        [EnumMember(Value = "*")]
        All = (Import << 1) - 1,
    }
}
