// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Health.Fhir.Core.Features.Search.SearchValues;
using Microsoft.Health.Fhir.ValueSets;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    internal static class SearchIndexEntryExtensions
    {
        public static void SetMinMaxValues(this IEnumerable<SearchIndexEntry> entries)
        {
            // determine min/max values
            foreach (var item in entries.GroupBy(x => x.SearchParameter.Name))
            {
                switch (item.First().SearchParameter.Type)
                {
                    case SearchParamType.String:
                    case SearchParamType.Reference:
                    case SearchParamType.Uri:
                        item.First(x => x.Value.ToString() == item.Min(y => y.Value.ToString())).IsMin = true;
                        item.First(x => x.Value.ToString() == item.Max(y => y.Value.ToString())).IsMax = true;
                        break;
                    case SearchParamType.Number:
                    case SearchParamType.Quantity:
                        var highNum = item.Max(y => ((IHighLowValues)y.Value).High);
                        var lowNum = item.Min(y => ((IHighLowValues)y.Value).Low);

                        var highNumItem = item.FirstOrDefault(x => ((IHighLowValues)x.Value).High == highNum);
                        var lowNumItem = item.FirstOrDefault(x => ((IHighLowValues)x.Value).Low == lowNum);

                        if (highNumItem != null)
                        {
                            highNumItem.IsMax = true;
                            highNumItem.IsMin = lowNumItem == null ? true : (bool?)null;
                        }

                        if (lowNumItem != null)
                        {
                            lowNumItem.IsMax = highNumItem?.IsMax ?? true;
                            lowNumItem.IsMin = true;
                        }

                        break;

                    case SearchParamType.Date:
                        DateTimeOffset highDate = item.Max(y => ((DateTimeSearchValue)y.Value).End);
                        DateTimeOffset lowDate = item.Min(y => ((DateTimeSearchValue)y.Value).Start);

                        SearchIndexEntry highDateItem = item.FirstOrDefault(x => ((DateTimeSearchValue)x.Value).End == highDate);
                        SearchIndexEntry lowDateItem = item.FirstOrDefault(x => ((DateTimeSearchValue)x.Value).Start == lowDate);

                        if (highDateItem != null)
                        {
                            highDateItem.IsMax = true;
                            highDateItem.IsMin = lowDateItem == null ? true : (bool?)null;
                        }

                        if (lowDateItem != null)
                        {
                            lowDateItem.IsMax = highDateItem?.IsMax ?? true;
                            lowDateItem.IsMin = true;
                        }

                        break;
                }
            }
        }
    }
}
