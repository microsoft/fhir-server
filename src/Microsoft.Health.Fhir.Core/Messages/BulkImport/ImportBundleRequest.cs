// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using EnsureThat;
using MediatR;
using Microsoft.Health.Fhir.Core.Features.Operations.Import;
using Microsoft.Health.Fhir.Core.Features.Operations.Import.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class ImportBundleRequest : IRequest<ImportBundleResponse>
    {
        public ImportBundleRequest(IReadOnlyList<ImportResource> resources)
        {
            Resources = EnsureArg.IsNotNull(resources, nameof(resources));
        }

        /// <summary>
        /// Resources to import
        /// </summary>
        public IReadOnlyList<ImportResource> Resources { get; }
    }
}
