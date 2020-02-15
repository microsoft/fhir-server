// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;

namespace Microsoft.Health.Fhir.Core.Features.Settings
{
    /// <summary>
    /// Provides access to the contents of files that are located in the process' bin directory.
    /// </summary>
    public interface IFileProvider
    {
        Stream ReadFile(string name);
    }
}
