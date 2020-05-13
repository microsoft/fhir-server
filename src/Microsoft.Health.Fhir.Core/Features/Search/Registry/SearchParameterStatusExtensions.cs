// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Registry
{
    public static class SearchParameterStatusExtensions
    {
        public static bool IsSupported(this SearchParameterStatus status)
        {
            return status != SearchParameterStatus.Disabled;
        }

        public static bool IsSearchable(this SearchParameterStatus status)
        {
            return status == SearchParameterStatus.Enabled;
        }

        public static SearchParameterStatus FromSearchParameter(this SearchParameterInfo info)
        {
            if (info.IsSearchable)
            {
                return SearchParameterStatus.Enabled;
            }

            if (info.IsSupported)
            {
                return SearchParameterStatus.Supported;
            }

            return SearchParameterStatus.Disabled;
        }
    }
}
