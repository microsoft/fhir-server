// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using MediatR;

namespace Microsoft.Health.Fhir.Core.Messages.Get
{
    public class GetSmartConfigurationRequest : IRequest<GetSmartConfigurationResponse>
    {
        private readonly Uri _baseUri;

        public GetSmartConfigurationRequest(Uri baseUri)
        {
            _baseUri = baseUri;
        }

        public Uri BaseUri => _baseUri;
    }
}
