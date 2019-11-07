// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Linq;

namespace Microsoft.Health.Fhir.Api.Features.Resources.Bundle
{
    public static class BundleValidator
    {
        public static bool ValidateTransactionBundle(Hl7.Fhir.Model.Bundle bundle)
        {
            var distinctEntry = bundle.Entry.Where(r => r.FullUrl != null).Select(r => string.Join(r.FullUrl, r.Resource.Meta?.VersionId)).Distinct();

            return bundle.Entry.Count == distinctEntry.Count();
        }
    }
}
