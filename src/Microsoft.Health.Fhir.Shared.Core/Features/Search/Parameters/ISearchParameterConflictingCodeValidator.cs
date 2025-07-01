// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.ObjectModel;
using FluentValidation.Results;
using Hl7.Fhir.Model;

namespace Microsoft.Health.Fhir.Shared.Core.Features.Search.Parameters
{
    public interface ISearchParameterConflictingCodeValidator
    {
        Uri CheckForConflictingCodeValue(SearchParameter searchParam, Collection<ValidationFailure> validationFailures);
    }
}
