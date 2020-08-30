// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.SqlServer.Features.Search
{
    public static class SearchParameterInfoExtensions
    {
        private static readonly IList<string> SupportedSortParameters = new List<string>
            {
                KnownQueryParameterNames.LastUpdated,
            };

        static SearchParameterInfoExtensions()
        {
            Array.ForEach(SupportedSortParameterNames.Names, item => SupportedSortParameters.Add(item));
        }

        public static bool IsSortSupported(this SearchParameterInfo searchParameterInfo)
        {
            EnsureArg.IsNotNull(searchParameterInfo, nameof(searchParameterInfo));

            return SupportedSortParameters.Contains(searchParameterInfo.Name);
        }
    }
}
