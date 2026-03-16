// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using Microsoft.Health.Fhir.Core.Models;
using static Hl7.Fhir.Model.Bundle;

namespace Microsoft.Health.Fhir.Core.Messages.Bundle
{
    public sealed class BundleResponseInfo
    {
        public BundleResponseInfo(TimeSpan executionTime, BundleType bundleType, BundleProcessingLogic processingLogic)
        {
            ExecutionTime = executionTime;
            BundleType = bundleType;
            ProcessingLogic = processingLogic;
        }

        public TimeSpan ExecutionTime { get; }

        public BundleProcessingLogic ProcessingLogic { get; }

        public BundleType BundleType { get; }
    }
}
