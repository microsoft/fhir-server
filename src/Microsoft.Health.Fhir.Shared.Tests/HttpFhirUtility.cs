// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Web;

namespace Microsoft.Health.Fhir.Shared.Tests
{
    public static class HttpFhirUtility
    {
        /// <summary>
        /// Encodes an URI string.
        /// </summary>
        /// <param name="url">Uri.</param>
        /// <returns>Encoded uri.</returns>
        public static string EncodeUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            // The following logic does not encode '&' and '=' characters.
            // It follows the same approach as other client libraries in the market do.

            string[] parameters = url.Split('&');
            StringBuilder finalUri = new StringBuilder();

            for (int i = 0; i < parameters.Length; i++)
            {
                string[] keyValue = parameters[i].Split('=');

                finalUri.Append(HttpUtility.UrlEncode(keyValue[0]));
                if (keyValue.Length > 1)
                {
                    finalUri.Append("=");
                    finalUri.Append(HttpUtility.UrlEncode(keyValue[1]));
                }

                if (i < (parameters.Length - 1))
                {
                    finalUri.Append("&");
                }
            }

            return finalUri.ToString();
        }
    }
}
