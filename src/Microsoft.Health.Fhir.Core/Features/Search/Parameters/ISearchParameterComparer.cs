// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Search.Parameters
{
    public interface ISearchParameterComparer<T> : IComparer<T>
        where T : class
    {
        int CompareBase(IEnumerable<string> x, IEnumerable<string> y);

        int CompareComponent(IEnumerable<(string definition, string expression)> x, IEnumerable<(string definition, string expression)> y);

        int CompareExpression(string x, string y, bool baseTypeExpression = false);
    }
}
