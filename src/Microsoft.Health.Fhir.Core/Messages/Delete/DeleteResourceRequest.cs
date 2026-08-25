// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using EnsureThat;
using Medino;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Bundle;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public class DeleteResourceRequest : BaseBundleInnerRequest, IRequest<DeleteResourceResponse>, IRequireCapability
    {
        public DeleteResourceRequest(ResourceKey resourceKey, DeleteOperation deleteOperation, BundleResourceContext bundleResourceContext = null, bool allowPartialSuccess = false, WeakETag weakETag = null, string comparedVersion = null)
            : base(bundleResourceContext)
        {
            EnsureArg.IsNotNull(resourceKey, nameof(resourceKey));

            ResourceKey = resourceKey;
            DeleteOperation = deleteOperation;
            AllowPartialSuccess = allowPartialSuccess;
            WeakETag = weakETag;
            ComparedVersion = comparedVersion;
        }

        public DeleteResourceRequest(string type, string id, DeleteOperation deleteOperation, BundleResourceContext bundleResourceContext = null, bool allowPartialSuccess = false, WeakETag weakETag = null, string comparedVersion = null)
            : base(bundleResourceContext)
        {
            EnsureArg.IsNotNull(type, nameof(type));
            EnsureArg.IsNotNull(id, nameof(id));

            ResourceKey = new ResourceKey(type, id);
            DeleteOperation = deleteOperation;
            AllowPartialSuccess = allowPartialSuccess;
            WeakETag = weakETag;
            ComparedVersion = comparedVersion;
        }

        public ResourceKey ResourceKey { get; }

        public DeleteOperation DeleteOperation { get; }

        public bool AllowPartialSuccess { get; }

        public WeakETag WeakETag { get; }

        /// <summary>
        /// Gets the resource version observed while resolving a single-match conditional delete. This is an
        /// internal guard distinct from <see cref="WeakETag"/> (the client-supplied If-Match) and is never set for
        /// a genuinely regular, non-conditional delete request.
        /// </summary>
        public string ComparedVersion { get; }

        public IEnumerable<CapabilityQuery> RequiredCapabilities()
        {
            yield return new CapabilityQuery($"CapabilityStatement.rest.resource.where(type = '{ResourceKey.ResourceType}').interaction.where(code = 'delete').exists()");
        }
    }
}
