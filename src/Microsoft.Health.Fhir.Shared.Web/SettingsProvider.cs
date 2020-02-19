// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.IO;
using EnsureThat;
using Microsoft.Health.Fhir.Core.Features.Settings;
using File = System.IO.File;

namespace Microsoft.Health.Fhir.Web
{
    public class FileProvider : IFileProvider
    {
        public Stream GetSettingsFile(string name)
        {
            EnsureArg.IsNotEmptyOrWhitespace(name, nameof(name));

            return File.OpenRead(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location), name));
        }
    }
}
