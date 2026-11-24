// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Linq;
using Microsoft.Health.Fhir.Core.Models;
using Microsoft.Health.Fhir.SqlServer.Features.Search;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.SqlServer.Features.Storage
{
    internal static class SearchParameterTargetTable
    {
        internal const string Resource = "Resource";
        internal const string Unsupported = "Unsupported";
        internal const string Vector = "VectorSearchParam";

        internal static string Get(SearchParameterInfo parameter)
        {
            if (parameter.VectorConfig != null)
            {
                return Vector;
            }

            if (parameter.ColumnLocation().HasFlag(SearchParameterColumnLocation.ResourceTable))
            {
                return Resource;
            }

            return parameter.Type switch
            {
                SearchParamType.Token => "TokenSearchParam",
                SearchParamType.Reference => "ReferenceSearchParam",
                SearchParamType.Date => "DateTimeSearchParam",
                SearchParamType.String => "StringSearchParam",
                SearchParamType.Number => "NumberSearchParam",
                SearchParamType.Quantity => "QuantitySearchParam",
                SearchParamType.Uri => "UriSearchParam",
                SearchParamType.Composite => GetComposite(parameter),
                _ => Unsupported,
            };
        }

        private static string GetComposite(SearchParameterInfo parameter)
        {
            if (parameter.Component == null || parameter.Component.Any(c => c.ResolvedSearchParameter == null))
            {
                return Unsupported;
            }

            var types = parameter.Component.Select(c => c.ResolvedSearchParameter.Type).ToArray();
            return types switch
            {
                [SearchParamType.Token, SearchParamType.Quantity] => "TokenQuantityCompositeSearchParam",
                [SearchParamType.Reference, SearchParamType.Token] => "ReferenceTokenCompositeSearchParam",
                [SearchParamType.Token, SearchParamType.Token] => "TokenTokenCompositeSearchParam",
                [SearchParamType.Token, SearchParamType.Date] => "TokenDateTimeCompositeSearchParam",
                [SearchParamType.Token, SearchParamType.String] => "TokenStringCompositeSearchParam",
                [SearchParamType.Token, SearchParamType.Number, SearchParamType.Number] => "TokenNumberNumberCompositeSearchParam",
                _ => Unsupported,
            };
        }
    }
}
