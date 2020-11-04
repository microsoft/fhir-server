// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features.Operations.Routing;

namespace Microsoft.Health.Fhir.Api.Features.Routing.Operations
{
    public interface IOperationActionResultResponse : IOperationResponse
    {
        IActionResult Response { get; }
    }
}
