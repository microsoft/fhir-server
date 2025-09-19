// Test file - you can run this in a simple console app to debug token issues
using Azure.Core;
using Azure.Identity;
using System;
using System.Threading;
using System.Threading.Tasks;

public class RedisTokenTest
{
    public static async Task Main(string[] args)
    {
        try
        {
            var credential = new DefaultAzureCredential();
            var tokenRequestContext = new TokenRequestContext(new[] { "https://redis.azure.com/.default" });
            
            Console.WriteLine("Attempting to get Azure token for Redis...");
            var accessToken = await credential.GetTokenAsync(tokenRequestContext, CancellationToken.None);
            
            Console.WriteLine($"Token acquired successfully!");
            Console.WriteLine($"Token length: {accessToken.Token.Length}");
            Console.WriteLine($"Token expires: {accessToken.ExpiresOn}");
            Console.WriteLine($"Token prefix: {accessToken.Token.Substring(0, Math.Min(50, accessToken.Token.Length))}...");
            
            // Test Redis connection
            var redisConfig = new StackExchange.Redis.ConfigurationOptions
            {
                EndPoints = { "estrtest.redis.cache.windows.net:6380" },
                Ssl = true,
                Password = accessToken.Token,
                User = "Azure",
                ClientName = "TokenTest"
            };
            
            using var connection = await StackExchange.Redis.ConnectionMultiplexer.ConnectAsync(redisConfig);
            Console.WriteLine($"Redis connection established: {connection.IsConnected}");
            
            var db = connection.GetDatabase();
            var pingResult = await db.PingAsync();
            Console.WriteLine($"PING successful: {pingResult.TotalMilliseconds}ms");
            
            Console.WriteLine("? All tests passed!");
        }
        catch (Azure.Identity.CredentialUnavailableException ex)
        {
            Console.WriteLine($"? Credentials not available: {ex.Message}");
            Console.WriteLine("Make sure you're logged in: az login");
        }
        catch (StackExchange.Redis.RedisServerException ex) when (ex.Message.Contains("NOAUTH"))
        {
            Console.WriteLine($"? Redis authentication failed: {ex.Message}");
            Console.WriteLine("Check Azure AD configuration on Redis Cache");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
        }
    }
}
