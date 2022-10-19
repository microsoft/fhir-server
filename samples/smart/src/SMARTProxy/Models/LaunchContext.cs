// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace SMARTProxy.Models
{
    public class LaunchContext
    {
        public string ResponseType { get; set; } = default!;

        public string ClientId { get; set; } = default!;

        public Uri RedirectUri { get; set; } = default!;

        public string Scope { get; set; } = default!;

        public string State { get; set; } = default!;

        public string Aud { get; set; } = default!;

        public string? CodeChallenge { get; set; } = default!;

        public string? CodeChallengeMethod { get; set; } = default!;
    }
}
