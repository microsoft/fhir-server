// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Hl7.Fhir.Model;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Operation
{
    public class LookUpOperationRequest : IRequest<LookUpOperationResponse>, IRequest
    {
        public LookUpOperationRequest(string code, string system, string display = null)
        {
            EnsureArg.IsNotNull(system, nameof(system));
            EnsureArg.IsNotNull(code, nameof(code));
            System = system;
            Code = code;
        }

        public LookUpOperationRequest(Parameters parameter)
        {
            EnsureArg.IsNotNull(parameter, nameof(parameter));

            Parameter = parameter;
        }

        public Parameters Parameter { get; }

        public string System { get; }

        public string Code { get; }
    }
}
