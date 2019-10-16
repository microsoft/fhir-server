// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Microsoft.Health.Fhir.Core.Features
{
    /// <summary>
    /// Type-safe enum for the _total parameter values.
    /// </summary>
    /// <remarks>Taken from https://stackoverflow.com/questions/424366/string-representation-of-an-enum and adapted.</remarks>
    public sealed class TotalType
    {
        private readonly string name;
        private readonly int value;

        public static readonly TotalType Accurate = new TotalType(1, "accurate");
        public static readonly TotalType None = new TotalType(2, "none");
        public static readonly TotalType Estimate = new TotalType(3, "estimate");

        private TotalType(int value, string name)
        {
            this.name = name;
            this.value = value;
        }

        public override string ToString()
        {
            return name;
        }
    }
}
