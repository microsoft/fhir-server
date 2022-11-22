// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTCustomOperations.AzureAuth.Models
{
    public class AppConsentScope
    {
        public string? Name { get; set; }

        public string? Id { get; set; }

        public string? UserDescription { get; set; }

        public string? ResourceId { get; set; }

        public bool AlreadyConsented { get; set; } = false;

        public string? ConsentId { get; set; }
    }
}
