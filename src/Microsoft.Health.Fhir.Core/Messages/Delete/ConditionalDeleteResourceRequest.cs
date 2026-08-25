// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Features.Search;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Messages.Delete
{
    public sealed class ConditionalDeleteResourceRequest : ConditionalResourceRequest<DeleteResourceResponse>, IRequireCapability
    {
        private static readonly string[] Capabilities = new string[2] { "conditionalDelete.exists()", "conditionalDelete != 'not-supported'" };

        public ConditionalDeleteResourceRequest(
            string resourceType,
            IReadOnlyList<Tuple<string, string>> conditionalParameters,
            DeleteOperation deleteOperation,
            int? maxDeleteCount,
            BundleResourceContext bundleResourceContext = null,
            bool deleteAll = false,
            ResourceVersionType versionType = ResourceVersionType.Latest,
            bool allowPartialSuccess = false,
            bool isIncludesRequest = false,
            bool removeReferences = false,
            WeakETag weakETag = null)
            : base(resourceType, conditionalParameters, bundleResourceContext)
        {
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            DeleteOperation = deleteOperation;
            MaxDeleteCount = maxDeleteCount;
            DeleteAll = deleteAll;
            VersionType = versionType;
            AllowPartialSuccess = allowPartialSuccess;
            IsIncludesRequest = isIncludesRequest;
            RemoveReferences = removeReferences;
            WeakETag = weakETag;
        }

        public DeleteOperation DeleteOperation { get; }

        public int? MaxDeleteCount { get; }

        public bool DeleteAll { get; }

        public ResourceVersionType VersionType { get; }

        public bool AllowPartialSuccess { get; }

        public bool IsIncludesRequest { get; set; }

        public bool RemoveReferences { get; set; }

        public WeakETag WeakETag { get; }

        /// <summary>
        /// Gets or sets a value indicating whether this request was resolved to one match before include processing.
        /// </summary>
        public bool IsSingleResourceConditionalDelete { get; set; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;

        public ConditionalDeleteResourceRequest Clone()
        {
            var clone = new ConditionalDeleteResourceRequest(
                ResourceType,
                new List<Tuple<string, string>>(ConditionalParameters),
                DeleteOperation,
                MaxDeleteCount,
                BundleResourceContext,
                DeleteAll,
                VersionType,
                AllowPartialSuccess,
                IsIncludesRequest,
                RemoveReferences,
                WeakETag);

            clone.IsSingleResourceConditionalDelete = IsSingleResourceConditionalDelete;
            return clone;
        }
    }
}
