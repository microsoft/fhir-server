// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;

namespace Microsoft.Health.Fhir.Core.Extensions
{
    public static class KeyValuePairExtensions
    {
        public static IReadOnlyList<Tuple<T1, T2>> AsTuples<T1, T2>(this IEnumerable<KeyValuePair<T1, T2>> collection)
        {
            return collection.Select(x => Tuple.Create(x.Key, x.Value)).ToArray();
        }
    }
}
