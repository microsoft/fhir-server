// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    internal class EverythingOperationContinuationToken
    {
        // The $everything operation continuation token is used to retrieve the next phase of results when running the
        // Patient $everything operation. The information stored in this class is serialized, encoded and returned as
        // part of the "next" URL in the result set bundle.
        // The length of the serialized token must not exceed the limit of the URL (2083 characters), so we need to
        // avoid using the continuation token to store large amounts of data. This is why we only store the current
        // "seealso" link id in the token (opposed to all "seealso" links).
        [JsonConstructor]
        internal EverythingOperationContinuationToken()
        {
            Phase = 0;
            InternalContinuationToken = null;
        }

        [JsonProperty]
        internal int Phase { get; set; }

        [JsonProperty]
        internal string InternalContinuationToken { get; set; }

        [JsonProperty]
        internal string CurrentSeeAlsoLinkId { get; set; }

        [JsonProperty]
        internal string ParentPatientVersionId { get; set; }

        internal bool IsProcessingSeeAlsoLink
        {
            get
            {
                return CurrentSeeAlsoLinkId != null;
            }
        }

        public string ToJson()
        {
            return JsonConvert.SerializeObject(this);
        }

        internal static EverythingOperationContinuationToken FromJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            EverythingOperationContinuationToken token;

            try
            {
                token = JsonConvert.DeserializeObject<EverythingOperationContinuationToken>(json);
            }
            catch (JsonException)
            {
                return null;
            }

            return token;
        }
    }
}
