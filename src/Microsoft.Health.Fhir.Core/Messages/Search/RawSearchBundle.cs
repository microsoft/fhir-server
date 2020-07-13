// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class RawSearchBundle
    {
        public static readonly string ResourceType = "Bundle";

        public static readonly string Type = "searchset";

        public string Id { get; set; }

        public List<string> Entry { get; } = new List<string>();
    }
}
