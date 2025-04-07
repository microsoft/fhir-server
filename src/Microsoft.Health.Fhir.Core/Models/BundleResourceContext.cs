// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Hl7.Fhir.Rest;

namespace Microsoft.Health.Fhir.Core.Models
{
    public sealed class BundleResourceContext
    {
        public BundleResourceContext(BundleProcessingLogic processingLogic, HTTPVerb httpVerb, Guid bundleOperationId)
        {
            ProcessingLogic = processingLogic;
            HttpVerb = httpVerb;
            BundleOperationId = bundleOperationId;
        }

        public BundleProcessingLogic ProcessingLogic { get; }

        public HTTPVerb HttpVerb { get; }

        public Guid BundleOperationId { get; }

        public bool IsParallelBundle
        {
            get { return ProcessingLogic == BundleProcessingLogic.Parallel; }
        }
    }
}
