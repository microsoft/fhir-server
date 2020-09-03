// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.Health.Fhir.Core.Features.Search
{
    /// <summary>
    /// Represents the search continuation token.
    /// </summary>
    public class ContinuationToken
    {
        public long ResourceSourogateId { get; set; }

        public string SortExpr { get; set; }

        public string ToJson()
        {
            return JObject.FromObject(this).ToString();
        }

        public override string ToString()
        {
            return this.ToJson();
        }

        public static ContinuationToken FromString(string json)
        {
            try
            {
                return JsonConvert.DeserializeObject<ContinuationToken>(json);
            }
            catch
            {
                return null;
            }
        }
    }
}
