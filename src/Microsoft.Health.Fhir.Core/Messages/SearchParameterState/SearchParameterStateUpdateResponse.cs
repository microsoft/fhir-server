// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.SearchParameterState
{
    public class SearchParameterStateUpdateResponse
    {
        public SearchParameterStateUpdateResponse(ResourceElement updateStatus)
        {
            UpdateStatus = updateStatus;
        }

        public ResourceElement UpdateStatus { get; }
    }
}
