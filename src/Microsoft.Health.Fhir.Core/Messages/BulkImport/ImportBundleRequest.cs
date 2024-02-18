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

namespace Microsoft.Health.Fhir.Core.Messages.Import
{
    public class ImportBundleRequest : IRequest<ImportBundleResponse>
    {
        public ImportBundleRequest(string bundle)
        {
            Bundle = bundle;
        }

        /// <summary>
        /// Import bundle
        /// </summary>
        public string Bundle { get; }
    }
}
