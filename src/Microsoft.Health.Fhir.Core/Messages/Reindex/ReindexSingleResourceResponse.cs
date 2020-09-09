// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Reindex
{
    public class ReindexSingleResourceResponse
    {
        public ReindexSingleResourceResponse(ResourceElement parameter)
        {
            EnsureArg.IsNotNull(parameter, nameof(parameter));

            ParameterResource = parameter;
        }

        public ResourceElement ParameterResource { get; }
    }
}
