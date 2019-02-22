// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Net.Http;
using EnsureThat;
using Newtonsoft.Json;

namespace Microsoft.Health.ControlPlane.Core.Features.Rbac
{
    public class IdentityProvider : IValidatableObject
    {
        [JsonConstructor]
        protected IdentityProvider()
        {
        }

        public IdentityProvider(string name, string authority, IReadOnlyList<string> audience)
            : this(name, authority, audience, null)
        {
        }

        internal IdentityProvider(string name, string authority, IReadOnlyList<string> audience, string eTag)
        {
            EnsureArg.IsNotNull(name, nameof(name));
            EnsureArg.IsNotNull(authority, nameof(authority));
            EnsureArg.IsNotNull(audience, nameof(audience));

            Name = name;
            Authority = authority;
            Audience = audience;
            VersionTag = eTag;
        }

        [JsonProperty("name")]
        public virtual string Name { get; protected set; }

        [JsonProperty("authority")]
        public virtual string Authority { get; protected set; }

        [JsonProperty("audience")]
        public virtual IReadOnlyList<string> Audience { get; protected set; }

        [JsonProperty("etag")]
        public virtual string VersionTag { get; protected set; }

        public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                yield return new ValidationResult(Resources.IdentityProviderNameEmpty);
            }

            if (string.IsNullOrWhiteSpace(Authority))
            {
                yield return new ValidationResult(Resources.IdentityProviderAuthorityEmpty);
            }

            if (Audience == null)
            {
                yield return new ValidationResult(Resources.IdentityProviderAudienceIsNull);
            }

            foreach (var audience in Audience)
            {
                if (string.IsNullOrWhiteSpace(audience))
                {
                    yield return new ValidationResult(Resources.IdentityProviderInvalidAudience);
                    break;
                }
            }

            foreach (ValidationResult result in ValidateAuthority())
            {
                yield return result;
            }
        }

        public virtual IEnumerable<ValidationResult> ValidateAuthority()
        {
            using (var handler = new HttpClientHandler())
            {
                var client = new HttpClient(handler);
                var uri = new Uri(Authority.TrimEnd('/') + "/.well-known/openid-configuration");

                HttpResponseMessage response = client.GetAsync(uri).GetAwaiter().GetResult();

                if (!response.IsSuccessStatusCode)
                {
                    yield return new ValidationResult(string.Format(Resources.IdentityProviderInvalidMetadataUrl, Authority, uri.OriginalString));
                }
                else
                {
                    var json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    Dictionary<string, object> metadataDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(json);

                    var requiredMetadata = new List<string> { "issuer", "authorization_endpoint", "token_endpoint" };

                    foreach (var metaProp in requiredMetadata)
                    {
                        if (!metadataDict.ContainsKey(metaProp))
                        {
                            yield return new ValidationResult(string.Format(Resources.IdentityProviderMissingMetaProp, Authority, metaProp, uri.OriginalString));
                        }
                    }
                }
            }
        }
    }
}
