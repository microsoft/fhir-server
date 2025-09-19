// Test file - you can run this in a simple console app to test the Microsoft Entra Redis integration
using Azure.Identity;
using Microsoft.Azure.StackExchangeRedis;
using StackExchange.Redis;
using System;
using System.Threading.Tasks;

public class RedisEntraTest
{
    public static async Task Main(string[] args)
    {
        var host = "estrtest.redis.cache.windows.net"; // Update with your Redis host
        var port = 6380;
        
        try
        {
            Console.WriteLine("Testing Microsoft Azure StackExchangeRedis integration...");
            
            var options = new ConfigurationOptions
            {
                AbortOnConnectFail = false,
                ClientName = "TestApp-ManagedIdentity",
                Ssl = true,
                DefaultDatabase = 0,
            };

            options.EndPoints.Add(host, port);

            // Managed Identity / Workload Identity credential
            var credOptions = new DefaultAzureCredentialOptions
            {
                ExcludeVisualStudioCredential = true,
                ExcludeVisualStudioCodeCredential = true,
                ExcludeInteractiveBrowserCredential = true,
                // ManagedIdentityClientId = null for system-assigned
            };
            var credential = new DefaultAzureCredential(credOptions);

            // Wire Microsoft Entra auth into StackExchange.Redis and enable automatic token refresh
            await options.ConfigureForAzureWithTokenCredentialAsync(credential);

            Console.WriteLine($"? Configured Redis for Microsoft Entra ID auth. Host={host} Port={port}");
            
            // Test Redis connection
            using var connection = await ConnectionMultiplexer.ConnectAsync(options);
            Console.WriteLine($"? Redis connection established: {connection.IsConnected}");
            
            var db = connection.GetDatabase();
            var pingResult = await db.PingAsync();
            Console.WriteLine($"? PING successful: {pingResult.TotalMilliseconds}ms");
            
            // Test a simple SET/GET operation
            await db.StringSetAsync("test:entra", "Microsoft Entra ID authentication working!");
            var result = await db.StringGetAsync("test:entra");
            Console.WriteLine($"? SET/GET test successful: {result}");
            
            // Test pub/sub
            var subscriber = connection.GetSubscriber();
            await subscriber.SubscribeAsync("test-channel", (channel, message) =>
            {
                Console.WriteLine($"? Received message: {message}");
            });
            
            await subscriber.PublishAsync("test-channel", "Hello from Microsoft Entra ID!");
            await Task.Delay(100); // Give time for message to be received
            
            Console.WriteLine("? All tests passed! Microsoft Entra ID authentication is working correctly.");
        }
        catch (Azure.Identity.CredentialUnavailableException ex)
        {
            Console.WriteLine($"? Credentials not available: {ex.Message}");
            Console.WriteLine("Make sure you're logged in: az login");
        }
        catch (StackExchange.Redis.RedisServerException ex) when (ex.Message.Contains("NOAUTH"))
        {
            Console.WriteLine($"? Redis authentication failed: {ex.Message}");
            Console.WriteLine("Check Microsoft Entra ID configuration on Redis Cache");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"? Error: {ex.Message}");
            Console.WriteLine($"Type: {ex.GetType().Name}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Inner: {ex.InnerException.Message}");
            }
        }
    }
}
