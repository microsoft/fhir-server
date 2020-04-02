// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;

namespace FhirSchemaManager.Model
{
    public class CompatibleVersion
    {
        public CompatibleVersion(int min, int max)
        {
            EnsureArg.IsNotNull<int>(min, nameof(min));
            EnsureArg.IsNotNull<int>(max, nameof(max));

            Min = min;
            Max = max;
        }

        public int Min { get; }

        public int Max { get; }
    }
}
