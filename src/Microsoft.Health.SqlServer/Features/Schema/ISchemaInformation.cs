// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;

namespace Microsoft.Health.SqlServer.Features.Schema
{
    public interface ISchemaInformation
    {
        public int MinimumSupportedVersion { get; }

        public int MaximumSupportedVersion { get; }

        public int? Current { get; set; }
    }
}
