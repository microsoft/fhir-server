// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.SqlServer.Features.Schema
{
    public class SchemaInformation
    {
        public SchemaInformation()
        {
            Min = SchemaVersion.V1;
            Max = SchemaVersion.V1;
        }

        public SchemaVersion Min { get; }

        public SchemaVersion Max { get; }

        public SchemaVersion? Current { get; set; }
    }
}
