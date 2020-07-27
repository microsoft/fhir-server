// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Messages.Search
{
    public class RawSearchBundle
    {
        public JRaw Result { get; set; }
    }
}
