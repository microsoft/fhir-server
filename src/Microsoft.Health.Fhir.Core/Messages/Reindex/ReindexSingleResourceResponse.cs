// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ReindexSingleResourceResponse
    {
        public ReindexSingleResourceResponse(string resourceId)
        {
            EnsureArg.IsNotNullOrWhiteSpace(resourceId, nameof(resourceId));
        }
    }
}
