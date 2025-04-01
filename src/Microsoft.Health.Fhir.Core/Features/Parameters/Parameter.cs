// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Features.Parameters
{
    public class Parameter
    {
        public string Name { get; set; }

        public string CharValue { get; set; }

        public double NumberValue { get; set; }

        public long LongValue { get; set; }

        public DateTime DateValue { get; set; }

        public bool BooleanValue { get; set; }

        public DateTime UpdatedOn { get; set; }

        public string UpdatedBy { get; set; }
    }
}
