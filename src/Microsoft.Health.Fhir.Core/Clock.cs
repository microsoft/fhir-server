// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;

namespace Microsoft.Health.Fhir.Core
{
    public static class Clock
    {
        private static Func<DateTimeOffset> _utcNowFunc = () => DateTimeOffset.UtcNow;

        public static DateTimeOffset UtcNow => _utcNowFunc();

        internal static Func<DateTimeOffset> UtcNowFunc
        {
            get => _utcNowFunc;
            set => _utcNowFunc = value;
        }
    }
}