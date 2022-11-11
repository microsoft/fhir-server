// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Health.Fhir.Core.Features.Operations;

namespace Microsoft.Health.Fhir.Api.Features.ActionResults
{
    public class OperationSmartConfigurationResult : JsonResult
    {
        public OperationSmartConfigurationResult(SmartConfigurationResult result)
            : base(result)
        {
            EnsureArg.IsNotNull(result, nameof(result));
        }

        public static OperationSmartConfigurationResult Ok(SmartConfigurationResult result)
        {
            return new OperationSmartConfigurationResult(result);
        }
    }
}
