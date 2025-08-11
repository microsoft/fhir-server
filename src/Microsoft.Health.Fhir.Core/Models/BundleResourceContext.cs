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
        public BundleResourceContext(BundleType? bundleType, BundleProcessingLogic processingLogic, Bundle.HTTPVerb httpVerb, Guid bundleOperationId)
        {
            BundleType = bundleType;
            ProcessingLogic = processingLogic;
            HttpVerb = httpVerb;
            BundleOperationId = bundleOperationId;
        }

        public BundleType? BundleType { get; }

        public BundleProcessingLogic ProcessingLogic { get; }

        public Bundle.HTTPVerb HttpVerb { get; }

        public Guid BundleOperationId { get; }

        public bool IsParallelBundle
        {
            get { return ProcessingLogic == BundleProcessingLogic.Parallel; }
        }
    }
}
