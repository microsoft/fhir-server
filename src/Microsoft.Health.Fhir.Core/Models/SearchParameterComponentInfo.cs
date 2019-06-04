// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core.Models
{
    public class SearchParameterComponentInfo
    {
        public SearchParameterComponentInfo(Uri definitionUrl = null, string expression = null)
        {
            DefinitionUrl = definitionUrl;
            Expression = expression;
        }

        public Uri DefinitionUrl { get; }

        public string Expression { get; }
    }
}
