// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;
using Hl7.Fhir.Model;
using Microsoft.Health.Fhir.Core.Models;

namespace Microsoft.Health.Fhir.Core.Features.Persistence;

public static class ImportBundleFhirPathExtension
{
    /// <summary>
    /// Return true if this resource contains the ImportBundle profile in meta data
    /// </summary>
    public static bool HasImportBundleProfile(this Resource resource)
    {
        return resource.Meta?.Profile != null && resource.Meta.Profile.Contains(KnownFhirPaths.ImportBundleProfileExtensionUrl);
    }
}
