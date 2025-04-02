using System;
using System.Threading;
using System.Threading.Tasks;
using MediaUploadService.ConsumerBackend.Server;
using MediaUploadService.ConsumerBackend.Storage;

namespace MediaUploadService.ConsumerBackend
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Media Upload Service - Consumer Backend");
            
            // Parse command-line arguments
            int consumerThreads = 4;
            int maxQueueLength = 100;
            string storagePath = "./uploads";
            
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "-c" && i + 1 < args.Length)
                    consumerThreads = int.Parse(args[i + 1]);
                else if (args[i] == "-q" && i + 1 < args.Length)
                    maxQueueLength = int.Parse(args[i + 1]);
                else if (args[i] == "-p" && i + 1 < args.Length)
                    storagePath = args[i + 1];
            }
            
            Console.WriteLine($"Starting with {consumerThreads} threads, queue size {maxQueueLength}, storage path: {storagePath}");
            
            // Create necessary directories
            System.IO.Directory.CreateDirectory(storagePath);
            
            // Initialize core components
            var storageManager = new StorageManager(storagePath);
            var metadataManager = new VideoMetadataManager(System.IO.Path.Combine(storagePath, "metadata.json"));
            var uploadQueue = new UploadQueue(maxQueueLength);
            var cancellationTokenSource = new CancellationTokenSource();
            
            // Start consumer threads
            var workerService = new WorkerService(
                uploadQueue, 
                storageManager, 
                metadataManager, 
                consumerThreads
            );
            
            // Start socket server
            var uploadServer = new UploadServer(
                uploadQueue, 
                metadataManager, 
                9000
            );
            
            try
            {
                workerService.Start();
                await uploadServer.StartAsync(cancellationTokenSource.Token);
                
                Console.WriteLine("Press Ctrl+C to stop the server");
                
                // Wait for cancellation
                var tcs = new TaskCompletionSource<bool>();
                Console.CancelKeyPress += (sender, e) => {
                    e.Cancel = true;
                    tcs.TrySetResult(true);
                };
                
                await tcs.Task;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Shutdown
                Console.WriteLine("Shutting down...");
                cancellationTokenSource.Cancel();
                uploadServer.Stop();
                workerService.Stop();
                
                Console.WriteLine("Server stopped");
            }
        }
    }
}