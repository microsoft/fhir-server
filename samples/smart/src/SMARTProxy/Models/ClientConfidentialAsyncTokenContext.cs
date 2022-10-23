// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Collections.Specialized;
using SMARTProxy.Services;

namespace SMARTProxy.Models
{
    public class ClientConfidentialAsyncTokenContext : TokenContext
    {
        /// <summary>
        /// Creates a RefreshTokenContext from the NameValueCollection from the HTTP request body.
        /// </summary>
        /// <param name="form">HTTP Form Encoded Body from Token Request.</param>
        /// <param name="audience">Azure Active Directory audience for the FHIR Server.</param>
        public ClientConfidentialAsyncTokenContext(NameValueCollection form, string audience)
        {
            if (form["grant_type"] != GrantType.client_credentials.ToString())
            {
                throw new ArgumentException("RefreshTokenContext requires the client_credentials grant type.");
            }

            GrantType = GrantType.client_credentials;

            // Since there is no user interaction involved, AAD only accepts the .default scope. It will give
            // the application the approved scopes.
            // AADSTS1002012
            Scope = $"{audience}/.default";
            ClientAssertionType = form["client_assertion_type"]!;
            ClientAssertion = form["client_assertion"]!;
        }

        public GrantType GrantType { get; }

        public string Scope { get; }

        public string ClientAssertionType { get; }

        public string ClientAssertion { get; }

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            throw new InvalidOperationException("ClientConfidentialAsync cannot be encoded to Form URL Content since this flow does not interact with Azure Active Directory.");
        }

        public override void Validate()
        {
            if (GrantType != GrantType.client_credentials ||
                string.IsNullOrEmpty(Scope) ||
                string.IsNullOrEmpty(ClientAssertionType) ||
                string.IsNullOrEmpty(ClientAssertion))
            {
                throw new ArgumentException("ClientConfidentialAsyncTokenContext invalid");
            }
        }

        public FormUrlEncodedContent ConvertToClientCredentialsFormUrlEncodedContent(ClientConfiguration client)
        {
            List<KeyValuePair<string, string>> formValues = new();

            formValues.Add(new KeyValuePair<string, string>("grant_type", "client_credentials"));
            formValues.Add(new KeyValuePair<string, string>("scope", Scope));
            formValues.Add(new KeyValuePair<string, string>("client_id", client.ClientId));
            formValues.Add(new KeyValuePair<string, string>("client_secret", client.ClientSecret));

            return new FormUrlEncodedContent(formValues);
        }
    }
}
