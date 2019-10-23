﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Features.Conformance.Schema;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class OperationComponent
    {
        [SchemaConst]
        public string Name { get; set; }

        public string Definition { get; set; }
    }
}
