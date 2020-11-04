// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Threading;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Operations.Routing
{
    public class OperationRequestContent : IOperationRequestContent
    {
        private readonly AsyncLocal<ResourceElement> _resourceCurrent = new AsyncLocal<ResourceElement>();

        public ResourceElement Resource
        {
            get => _resourceCurrent.Value;

            set => _resourceCurrent.Value = value;
        }
    }
}
