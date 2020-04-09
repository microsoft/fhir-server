// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FhirSchemaManager.Model;

namespace FhirSchemaManager
{
    public interface IInvokeAPI
    {
        Task<List<CurrentVersion>> GetCurrentVersionInformation(Uri serverUri);

        Task<string> GetScript(Uri scriptUri);

        Task<CompatibleVersion> GetCompatibility(Uri serverUri);

        Task<List<AvailableVersion>> GetAvailability(Uri serverUri);
    }
}
