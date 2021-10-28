// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Diagnostics;
using System.IO;

namespace Microsoft.Health.Fhir.Core.Features.Version
{
    public sealed class ProductVersionInfo
    {
        public static FileVersionInfo Version => FileVersionInfo.GetVersionInfo(typeof(ProductVersionInfo).Assembly.Location);

        public static DateTimeOffset CreationTime => File.GetCreationTime(typeof(ProductVersionInfo).Assembly.Location).ToUniversalTime();
    }
}
