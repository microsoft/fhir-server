// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Everything
{
    public class EverythingOperationContinuationToken
    {
        public EverythingOperationContinuationToken(int phase, string internalContinuationToken)
        {
            Phase = phase;
            InternalContinuationToken = internalContinuationToken;

            SeeAlsoLinks = new List<string>();
            CurrentSeeAlsoLinkIndex = -1;
            CurrentSeeAlsoLinkId = null;
        }

        [JsonProperty]
        private List<string> SeeAlsoLinks { get; }

        [JsonProperty]
        private int CurrentSeeAlsoLinkIndex { get; set; }

        [JsonProperty]
        public string CurrentSeeAlsoLinkId { get; private set; }

        public int Phase { get; set; }

        public string InternalContinuationToken { get; set; }

        public bool ProcessingSeeAlsoLink
        {
            get
            {
                return !string.IsNullOrEmpty(CurrentSeeAlsoLinkId);
            }
        }

        public bool MoreSeeAlsoLinksToProcess
        {
            get
            {
                return CurrentSeeAlsoLinkIndex < SeeAlsoLinks.Count - 1;
            }
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this);
        }

        public static EverythingOperationContinuationToken FromString(string json)
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

        public void AddSeeAlsoLink(string link)
        {
            // Safe to linear search on addition as it isn't common for a patient to have many "seealso" links
            if (!SeeAlsoLinks.Contains(link))
            {
                SeeAlsoLinks.Add(link);
            }
        }

        public void ProcessNextSeeAlsoLink()
        {
            CurrentSeeAlsoLinkIndex++;
            CurrentSeeAlsoLinkId = SeeAlsoLinks[CurrentSeeAlsoLinkIndex];
        }
    }
}
