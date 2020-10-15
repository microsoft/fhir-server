// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class OperationDefinitionResponse
    {
        public OperationDefinitionResponse(ResourceElement operationDefinition)
        {
            EnsureArg.IsNotNull(operationDefinition, nameof(operationDefinition));

            OperationDefinition = operationDefinition;
        }

        public ResourceElement OperationDefinition { get; }
    }
}
