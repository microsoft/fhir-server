// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    public static class SearchParameterInfoExtensions
    {
        private static IList<string> supportedSortParameters = new List<string>
            {
                KnownQueryParameterNames.LastUpdated,
            };

        public static void AppendSearchParameterInfoExtensions(string[] parameters)
        {
            Array.ForEach(parameters, item => supportedSortParameters.Add(item));
        }

        public static bool IsSortSupported(this SearchParameterInfo searchParameterInfo)
        {
            EnsureArg.IsNotNull(searchParameterInfo, nameof(searchParameterInfo));

            return supportedSortParameters.Contains(searchParameterInfo.Name);
        }
    }
}
