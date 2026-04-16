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

        // Legacy permissions (maintained for backward compatibility)
        Read = 1 << 0, // Legacy read permission (includes search capability for SMART v1 compatibility)
        Write = 1 << 1, // Legacy write permission (kept for backward compatibility)
        Delete = 1 << 2,
        HardDelete = 1 << 3,
        Export = 1 << 4,
        ResourceValidate = 1 << 5,
        Reindex = 1 << 6,
        ConvertData = 1 << 7,
        EditProfileDefinitions = 1 << 8, // Allows to Create/Update/Delete resources related to profile's resources.
        Import = 1 << 9,
        SearchParameter = 1 << 10,
        BulkOperator = 1 << 11,

        // SMART v2 granular permissions
        Search = 1 << 12, // SMART v2 search permission
        ReadById = 1 << 13, // SMART v2 read permission (read-only, no search)
        Create = 1 << 14, // SMART v2 create permission
        Update = 1 << 15, // SMART v2 update permission

        Smart = 1 << 30, // Do not include Smart in the '*' case.  We only want smart for a user if explicitly added to the role or user

        [EnumMember(Value = "*")]
        All = (Update << 1) - 1,
    }
}
