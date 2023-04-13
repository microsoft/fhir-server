// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.SearchParameterState
{
    internal class SearchParameterInfoEqualityComparer : IEqualityComparer<SearchParameterInfo>
    {
        public bool Equals(SearchParameterInfo x, SearchParameterInfo y)
        {
            return x.Url.AbsoluteUri.Equals(y.Url.AbsoluteUri, StringComparison.Ordinal);
        }

        public int GetHashCode([DisallowNull] SearchParameterInfo obj)
        {
            int hashCode = obj.Url?.AbsoluteUri.GetHashCode(StringComparison.Ordinal) ?? 0;

            hashCode ^= obj.Code?.GetHashCode(StringComparison.Ordinal) ?? 0;
            hashCode ^= obj.Type.GetHashCode();
            hashCode ^= obj.IsPartiallySupported.GetHashCode();
            hashCode ^= obj.IsSupported.GetHashCode();
            hashCode ^= obj.Name?.GetHashCode(StringComparison.Ordinal) ?? 0;

            return hashCode.GetHashCode();
        }
    }
}
