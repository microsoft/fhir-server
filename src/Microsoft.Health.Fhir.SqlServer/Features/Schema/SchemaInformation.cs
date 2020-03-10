// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.SqlServer.Features.Schema;

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SchemaInformation : ISchemaInformation
    {
        public SchemaInformation()
        {
            MinimumSupportedVersion = (int)SchemaVersion.V1;
            MaximumSupportedVersion = (int)SchemaVersion.V2;
        }

        public Type SchemaVersionEnumType => typeof(SchemaVersion);

        public int MinimumSupportedVersion { get; }

        public int MaximumSupportedVersion { get; }

        public int? Current { get; set; }
    }
}
