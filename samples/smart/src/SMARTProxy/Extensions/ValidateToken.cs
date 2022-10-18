using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SMARTProxy.Models;
using System.Text;

namespace SMARTProxy.Extensions
{
    public static class ValidateCode
    {

        public static bool IsNullOrEmpty(this JToken token)
        {
            return (token == null) ||
            (token.Type == JTokenType.Array && !token.HasValues) ||
            (token.Type == JTokenType.Object && !token.HasValues) ||
            (token.Type == JTokenType.String && token.ToString() == string.Empty) ||
            (token.Type == JTokenType.Null);
        }

        public static string EncodeBase64(this string value)
        {
            var valueBytes = Encoding.UTF8.GetBytes(value);
            return Convert.ToBase64String(valueBytes);
        }

        public static string DecodeBase64(this string value)
        {
            // Fix invalid base64 from inferno
            if (!value.EndsWith("="))
            {
                value = value + "=";
            }
            var valueBytes = System.Convert.FromBase64String(value);
            return Encoding.UTF8.GetString(valueBytes);
        }
    }
}