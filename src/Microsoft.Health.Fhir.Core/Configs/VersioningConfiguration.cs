// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Configs
{
    public class VersioningConfiguration
    {
        private string _default = ResourceVersionPolicy.Versioned;

        public string Default
        {
            get => _default;

            // If null is provided, use "versioned" as the default value.
            // #TODO in main - Consider whether we want to throw an exception instead of silently defaulting to "versioned" if null is provided.
            set => _default = (value ?? ResourceVersionPolicy.Versioned).ToLowerInvariant();
        }

        public Dictionary<string, string> ResourceTypeOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
    }
}
