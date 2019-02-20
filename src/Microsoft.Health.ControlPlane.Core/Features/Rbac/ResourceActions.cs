// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResourceAction
    {
        /// <summary>
        /// Read
        /// </summary>
        [EnumMember(Value = "read")]
        Read,

        /// <summary>
        /// Write
        /// </summary>
        [EnumMember(Value = "write")]
        Write,

        /// <summary>
        /// HardDelete
        /// </summary>
        [EnumMember(Value = "hardDelete")]
        HardDelete,
    }
}
