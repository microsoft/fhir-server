// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Generic;

namespace Microsoft.Health.Fhir.Core.Features.Conformance.Models
{
    internal class DefaultOptionHashSet<T> : HashSet<T>, IDefaultOption
    {
        public DefaultOptionHashSet(T defaultOption)
        {
            DefaultOption = defaultOption;
        }

        public DefaultOptionHashSet(T defaultOption, IEqualityComparer<T> comparer)
            : base(comparer)
        {
            DefaultOption = defaultOption;
        }

        public T DefaultOption { get; set; }

        object IDefaultOption.DefaultOption
        {
            get
            {
                return DefaultOption;
            }
        }
    }
}
