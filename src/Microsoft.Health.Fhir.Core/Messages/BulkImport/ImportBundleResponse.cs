// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class ImportBundleResponse
    {
        public ImportBundleResponse(int loaded, IReadOnlyList<string> errors)
        {
            LoadedResources = loaded;
            Errors = errors;
        }

        public int LoadedResources { get; private set; }

        public IReadOnlyList<string> Errors { get; private set; }
    }
}
