// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using EnsureThat;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class SearchParameterComponentInfo
    {
        public SearchParameterComponentInfo(Uri definitionUrl)
        {
            EnsureArg.IsNotNull(definitionUrl, nameof(definitionUrl));

            DefinitionUrl = definitionUrl;
        }

        public Uri DefinitionUrl { get; }
    }
}
