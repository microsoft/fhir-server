// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Context
{
    /// <summary>
    /// Compares KeyValuePair&lt;string, string&gt; with case-insensitive key and ordinal value comparison.
    /// </summary>
    public class KeyValuePairComparer : IEqualityComparer<KeyValuePair<string, string>>
    {
        public bool Equals(KeyValuePair<string, string> x, KeyValuePair<string, string> y)
        {
            return string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(x.Value, y.Value, StringComparison.Ordinal);
        }

        public int GetHashCode(KeyValuePair<string, string> obj)
        {
            return StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key) ^
                   StringComparer.Ordinal.GetHashCode(obj.Value);
        }
    }
}
