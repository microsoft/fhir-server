// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class OperationDefinitionRequest : IRequest<OperationDefinitionResponse>
    {
        public OperationDefinitionRequest(string operationName)
        {
            EnsureArg.IsNotNullOrWhiteSpace(operationName, nameof(operationName));

            OperationName = operationName;
        }

        public string OperationName { get; }
    }
}
