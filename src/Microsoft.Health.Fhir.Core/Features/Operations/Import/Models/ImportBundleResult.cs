// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Import.Models
{
    public class ImportBundleResult
    {
        public ImportBundleResult(int loaded, int failed, IList<string> errors)
        {
            LoadedResources = loaded;
            FailedResources = failed;
            Errors = errors;
        }

        [JsonConstructor]
        private ImportBundleResult()
        {
        }

        [JsonProperty("loadedResources")]
        public int LoadedResources { get; private set; }

        [JsonProperty("failedResources")]
        public int FailedResources { get; private set; }

        [JsonProperty("errors")]
        public IList<string> Errors { get; private set; }
    }
}
