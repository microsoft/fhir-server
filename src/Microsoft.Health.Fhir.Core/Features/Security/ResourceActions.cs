// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.Serialization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.Health.Fhir.Core.Features.Security
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResourceAction
    {
        [EnumMember(Value = "read")]
        Read,

        [EnumMember(Value = "write")]
        Write,
    }
}
