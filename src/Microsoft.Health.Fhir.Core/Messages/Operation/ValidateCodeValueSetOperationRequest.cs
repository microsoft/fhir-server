// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class ValidateCodeValueSetOperationRequest : IRequest<ValidateCodeValueSetOperationResponse>, IRequest
    {
        public ValidateCodeValueSetOperationRequest(Resource resource, string valueSetID, string system, string code, string display = null)
        {
            EnsureArg.IsNotNull(resource, nameof(resource));
            EnsureArg.IsNotNull(system, nameof(system));
            EnsureArg.IsNotNull(code, nameof(code));

            Resource = resource;
            System = system;
            ValueSetID = valueSetID;
            Code = code;
            Display = display;
        }

        public Resource Resource { get; }

        public string System { get; }

        public string ValueSetID { get; }

        public string Code { get; }

        public string Display { get; }
    }
}
