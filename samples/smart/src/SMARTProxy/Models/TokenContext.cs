using Microsoft.IdentityModel.Tokens;
using SMARTProxy.Extensions;
using System.Text.Json;
using System.Web;

namespace SMARTProxy.Models
{
    public abstract class TokenContext
    {
        public virtual string ToLogString()
        {
            return JsonSerializer.Serialize(this);
        }

        public abstract void Validate();

        public abstract FormUrlEncodedContent ToFormUrlEncodedContent();

        public static TokenContext FromFormUrlEncodedContent(string formData)
        {
            var formDataCollection = HttpUtility.ParseQueryString(formData);
            TokenContext? _tokenContext = null;

            // For public apps and confidential apps
            if (formDataCollection.AllKeys.Contains("grant_type") && formDataCollection["grant_type"] == "authorization_code")
            {
                if (formDataCollection.AllKeys.Contains("client_secret"))
                {
                    _tokenContext = new ConfidentialClientTokenContext()
                    {
                        GrantType = GrantType.authorization_code,
                        Code = formDataCollection["code"]!,
                        RedirectUri = formDataCollection["redirect_uri"]!,
                        ClientId = formDataCollection["client_id"]!,
                        ClientSecret = formDataCollection["client_secret"]!,
                        CodeVerifier = formDataCollection["code_verifier"]!,
                    };
                }
                else
                {
                    _tokenContext = new PublicClientTokenContext()
                    {
                        GrantType = GrantType.authorization_code,
                        Code = formDataCollection["code"]!,
                        RedirectUri = formDataCollection["redirect_uri"]!,
                        ClientId = formDataCollection["client_id"]!,
                        CodeVerifier = formDataCollection["code_verifier"]!,
                    };
                }
            }
            else if (formDataCollection.AllKeys.Contains("grant_type") && formDataCollection["grant_type"] == "refresh_token")
            {
                _tokenContext = new RefreshTokenContext()
                {
                    GrantType = GrantType.refresh_token,
                    ClientId = formDataCollection["client_id"]!,
                    Scope = formDataCollection.AllKeys.Contains("scope") && formDataCollection.AllKeys.Contains("client_id") ? formDataCollection["scope"]!.ParseScope(formDataCollection["client_id"]!)! : null,
                    RefreshToken = formDataCollection["refresh_token"]!,
                    ClientSecret = formDataCollection["client_secret"]!,
                };
            }
            // TODO - add backend services

            if (_tokenContext is null)
            {
                throw new ArgumentException("Invalid token content");
            }

            return _tokenContext;
        }
    }

    public class PublicClientTokenContext  : TokenContext
    {
        public GrantType GrantType { get; set; } = default!;
        public string Code { get; set; } = default!;
        public string RedirectUri { get; set; } = default!;
        public string ClientId { get; set; } = default!;

        public string? CodeVerifier { get; set; } = default!;

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new();

            formValues.Add(new KeyValuePair<string, string>("code", Code));
            formValues.Add(new KeyValuePair<string, string>("grant_type", GrantType.ToString()));
            formValues.Add(new KeyValuePair<string, string>("redirect_uri", RedirectUri));
            formValues.Add(new KeyValuePair<string, string>("client_id", ClientId));
            if (CodeVerifier is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("code_verifier", CodeVerifier));
            }

            return new FormUrlEncodedContent(formValues);
        }

        public override void Validate()
        {
            if (GrantType != GrantType.authorization_code ||
                string.IsNullOrEmpty(Code) ||
                string.IsNullOrEmpty(RedirectUri) ||
                string.IsNullOrEmpty(ClientId) ||
                
                // TODO - do we want to force PKCE?
                string.IsNullOrEmpty(CodeVerifier))
            {
                throw new ArgumentException("TokenContext invalid");
            }
        }
    }

    public class ConfidentialClientTokenContext : TokenContext
    {
        public GrantType GrantType { get; set; } = default!;
        public string Code { get; set; } = default!;
        public string RedirectUri { get; set; } = default!;
        public string ClientId { get; set; } = default!;
        public string ClientSecret { get; set; } = default!;
        public string? CodeVerifier { get; set; } = default!;

        public override string ToLogString()
        {
            ClientSecret = "***";
            return JsonSerializer.Serialize(this);
        }

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new();

            formValues.Add(new KeyValuePair<string, string>("code", Code));
            formValues.Add(new KeyValuePair<string, string>("grant_type", GrantType.ToString()));
            formValues.Add(new KeyValuePair<string, string>("redirect_uri", RedirectUri));
            formValues.Add(new KeyValuePair<string, string>("client_id", ClientId));
            formValues.Add(new KeyValuePair<string, string>("client_secret", ClientSecret));
            if (CodeVerifier is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("code_verifier", CodeVerifier));
            }

            return new FormUrlEncodedContent(formValues);
        }

        public override void Validate()
        {
            if (GrantType != GrantType.authorization_code ||
                string.IsNullOrEmpty(Code) ||
                string.IsNullOrEmpty(RedirectUri) ||
                string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(ClientSecret) ||

                // TODO - do we want to force PKCE?
                string.IsNullOrEmpty(CodeVerifier))
            {
                throw new ArgumentException("TokenContext invalid");
            }
        }
    }

    public class RefreshTokenContext : TokenContext
    {
        public GrantType GrantType { get; set; } = GrantType.refresh_token;
        public string RefreshToken { get; set; } = default!;
        public string? Scope { get; set; } = default!;
        public string ClientId { get; set; } = default!;
        public string? ClientSecret { get; set; } = default!;

        public override string ToLogString()
        {
            ClientSecret = "***";
            return JsonSerializer.Serialize(this);
        }

        public override FormUrlEncodedContent ToFormUrlEncodedContent()
        {
            List<KeyValuePair<string, string>> formValues = new()
            {
                new KeyValuePair<string, string>("client_id", ClientId),
                new KeyValuePair<string, string>("refresh_token", RefreshToken),
                new KeyValuePair<string, string>("grant_type", GrantType.ToString())
            };

            if (Scope is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("scope", Scope));
            }
            if (ClientSecret is not null)
            {
                formValues.Add(new KeyValuePair<string, string>("client_secret", ClientSecret));
            }

            return new FormUrlEncodedContent(formValues);
        }

        public override void Validate()
        {
            if (GrantType != GrantType.refresh_token ||
                string.IsNullOrEmpty(ClientId) ||
                string.IsNullOrEmpty(RefreshToken))
            {
                throw new ArgumentException("Refresh TokenContext invalid");
            }
        }
    }

    public enum GrantType
    {
        authorization_code,
        refresh_token
    }
}