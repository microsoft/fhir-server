// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Configs;

public class FhirPackageConfiguration
{
    /// <summary>
    /// True if the names of the default FHIR packages should be included automatically.
    /// hl7.fhir.[R4] and hl7.fhir.[R4].expansions.
    /// </summary>
    public bool IncludeDefaultPackages { get; set; }

    /// <summary>
    /// Specify the package server if different to http://packages.fhir.org.
    /// </summary>
    public string PackageSource { get; set; }

    /// <summary>
    /// Names of the packages to be included for validation.
    /// </summary>
    public ICollection<string> PackageNames { get; } = new HashSet<string>();
}
