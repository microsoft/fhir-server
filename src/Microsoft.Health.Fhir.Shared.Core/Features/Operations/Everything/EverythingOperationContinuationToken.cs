// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    internal class EverythingOperationContinuationToken
    {
        [JsonConstructor]
        internal EverythingOperationContinuationToken()
        {
            Phase = 0;
            InternalContinuationToken = null;

            SeeAlsoLinks = new List<string>();
            CurrentSeeAlsoLinkIndex = -1;
        }

        [JsonProperty]
        internal List<string> SeeAlsoLinks { get; }

        [JsonProperty]
        internal int Phase { get; set; }

        [JsonProperty]
        internal string InternalContinuationToken { get; set; }

        internal bool MoreSeeAlsoLinksToProcess
        {
            get
            {
                return CurrentSeeAlsoLinkIndex < SeeAlsoLinks.Count - 1;
            }
        }

        internal string CurrentSeeAlsoLinkId
        {
            get
            {
                return SeeAlsoLinks[CurrentSeeAlsoLinkIndex];
            }
        }

        internal bool IsProcessingSeeAlsoLink
        {
            get
            {
                return CurrentSeeAlsoLinkIndex > -1;
            }
        }

        [JsonProperty]
        private int CurrentSeeAlsoLinkIndex { get; set; }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        internal static EverythingOperationContinuationToken FromString(string json)
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

        internal void ProcessNextSeeAlsoLink()
        {
            CurrentSeeAlsoLinkIndex++;
        }
    }
}
