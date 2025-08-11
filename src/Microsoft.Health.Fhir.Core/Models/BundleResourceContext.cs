// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Model;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Core.Models
{
    public sealed class BundleResourceContext
    {
        public BundleResourceContext(BundleType? bundleType, BundleProcessingLogic processingLogic, Bundle.HTTPVerb httpVerb, string persistedId, Guid bundleOperationId)
        {
            BundleType = bundleType;
            ProcessingLogic = processingLogic;
            HttpVerb = httpVerb;
            PersistedId = persistedId;
            BundleOperationId = bundleOperationId;
        }

        /// <summary>
        /// Bundle Type (batch, transaction) of the bundle being processed.
        /// </summary>
        public BundleType? BundleType { get; }

        /// <summary>
        /// Processing logic assigned to the bundle being processed.
        /// </summary>
        public BundleProcessingLogic ProcessingLogic { get; }

        /// <summary>
        /// HTTP Verb of the inner request being processed.
        /// </summary>
        public Bundle.HTTPVerb HttpVerb { get; }

        /// <summary>
        /// Persisted ID generated at the time of bundle was received.
        /// </summary>
        public string PersistedId { get; }

        /// <summary>
        /// Bundle Parallel Operation ID for which the inner request is being processed.
        /// </summary>
        public Guid BundleOperationId { get; }

        public bool IsParallelBundle
        {
            get { return ProcessingLogic == BundleProcessingLogic.Parallel; }
        }
    }
}
