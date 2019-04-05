// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Models
{
    public class CodeableConceptInfo
    {
        public CodeableConceptInfo(string system, string value)
        {
            System = system;
            Value = value;
        }

        public string System { get; }

        public string Value { get; }
    }
}
