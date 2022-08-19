// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Core.Features.Security;

namespace Microsoft.Health.Fhir.R4.ResourceParser.Code
{
    public class MockClaimsExtractor : IClaimsExtractor
    {
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public Func<IReadOnlyCollection<KeyValuePair<string, string>>> ExtractImpl { get; set; }
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix

        public IReadOnlyCollection<KeyValuePair<string, string>> Extract()
        {
            if (ExtractImpl == null)
            {
                return Array.Empty<KeyValuePair<string, string>>();
            }

            return ExtractImpl();
        }
    }
}
