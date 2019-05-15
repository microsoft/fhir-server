// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class CodingInfo
    {
        public CodingInfo(string system, string code)
        {
            EnsureArg.IsNotNullOrEmpty(system, nameof(system));
            EnsureArg.IsNotNullOrEmpty(code, nameof(code));

            System = system;
            Code = code;
        }

        public string System { get; set; }

        public string Code { get; set; }
    }
}
