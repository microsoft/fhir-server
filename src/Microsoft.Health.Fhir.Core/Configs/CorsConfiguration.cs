// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class CorsConfiguration
    {
        public IList<string> Origins { get; } = new List<string>();

        public IList<string> Headers { get; } = new List<string>();

        public IList<string> Methods { get; } = new List<string>();

        public int? MaxAge { get; set; }

        public bool AllowCredentials { get; set; }
    }
}
