// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Conformance;
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
            bool maxParallelism = false,
            BundleResourceContext bundleResourceContext = null,
            bool deleteAll = false)
            : base(resourceType, conditionalParameters, maxParallelism, bundleResourceContext)
        {
            EnsureArg.IsNotNull(conditionalParameters, nameof(conditionalParameters));

            DeleteOperation = deleteOperation;
            MaxDeleteCount = maxDeleteCount;
            DeleteAll = deleteAll;
        }

        public DeleteOperation DeleteOperation { get; }

        public int? MaxDeleteCount { get; }

        public bool DeleteAll { get; }

        protected override IEnumerable<string> GetCapabilities() => Capabilities;
    }
}
