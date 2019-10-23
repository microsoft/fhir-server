// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    public class ListedContactPoint
    {
        public string System { get; set; }

        public string Use { get; set; }

        public string Value { get; set; }
    }
}
