// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ValidateCodeOperationRequest : IRequest<ValidateCodeOperationResponse>, IRequest
    {
        public ValidateCodeOperationRequest(Resource resource, string resourceID, string code, string system = null, string display = null)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(code, nameof(code));

            Resource = resource;
            System = system;
            ResourceID = resourceID;
            Code = code;
            Display = display;
        }

        public ValidateCodeOperationRequest(Resource resource)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));

            Resource = resource;
        }

        public Resource Resource { get; }

        public string System { get; }

        public string ResourceID { get; }

        public string Code { get; }

        public string Display { get; }
    }
}
