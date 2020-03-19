// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Exceptions;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Bundle
{
    public class BundleRequest : IRequest<BundleResponse>, IRequest, IRequireCapability
    {
        public BundleRequest(ResourceElement bundle)
        {
            EnsureArg.IsNotNull(bundle, nameof(bundle));

            Bundle = bundle;
        }

        public ResourceElement Bundle { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            var bundleType = Bundle.Scalar<string>(KnownFhirPaths.BundleType);

            if (string.IsNullOrEmpty(bundleType))
            {
                throw new MethodNotAllowedException(Core.Resources.TypeNotPresent);
            }

            yield return new CapabilityQuery($"CapabilityStatement.rest.interaction.where(code = '{bundleType}').exists()");
        }
    }
}
