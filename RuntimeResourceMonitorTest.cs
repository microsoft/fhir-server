using System;
using Microsoft.Extensions.Logging;
using Microsoft.Health.Fhir.Core.Features.Threading;

namespace TestResourceMonitor
{
    class Program
    {
        static void Main(string[] args)
        {
            // Create a simple logger
            using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<RuntimeResourceMonitor>();

            // Test our new resource monitor
            using var monitor = new RuntimeResourceMonitor(logger);

            Console.WriteLine("Testing New RuntimeResourceMonitor Implementation");
            Console.WriteLine("================================================");

            try
            {
                var processorCount = monitor.GetCurrentProcessorCount();
                var availableMemoryMB = monitor.GetCurrentAvailableMemoryMB();
                var memoryUsagePercent = monitor.GetCurrentMemoryUsagePercentage();
                var isUnderPressure = monitor.IsUnderResourcePressure();

                Console.WriteLine($"Processor Count: {processorCount}");
                Console.WriteLine($"Available Memory: {availableMemoryMB} MB");
                Console.WriteLine($"Memory Usage: {memoryUsagePercent:F1}%");
                Console.WriteLine($"Under Pressure: {isUnderPressure}");

                Console.WriteLine("\nThis should now show more accurate system resource usage!");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }
    }
}
