using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaUploadService.ConsumerBackend.Models;
using MediaUploadService.ConsumerBackend.Storage;

namespace MediaUploadService.ConsumerBackend.Server
{
    public class WorkerService
    {
        private readonly UploadQueue _uploadQueue;
        private readonly StorageManager _storageManager;
        private readonly VideoMetadataManager _metadataManager;
        private readonly int _workerCount;
        private readonly List<Task> _workers = new List<Task>();
        private CancellationTokenSource _cancellationTokenSource;
        private bool _isRunning;

        public WorkerService(
            UploadQueue uploadQueue, 
            StorageManager storageManager, 
            VideoMetadataManager metadataManager, 
            int workerCount)
        {
            _uploadQueue = uploadQueue;
            _storageManager = storageManager;
            _metadataManager = metadataManager;
            _workerCount = workerCount;
        }

        public void Start()
        {
            if (_isRunning)
                return;

            _isRunning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            var token = _cancellationTokenSource.Token;

            for (int i = 0; i < _workerCount; i++)
            {
                int workerId = i;
                var task = Task.Run(() => WorkerLoop(workerId, token), token);
                _workers.Add(task);
                Console.WriteLine($"Started worker {workerId}");
            }
        }

        public void Stop()
        {
            if (!_isRunning)
                return;

            _isRunning = false;
            _cancellationTokenSource.Cancel();
            
            // Wait for workers to finish with timeout
            Task.WaitAll(_workers.ToArray(), 5000);
            Console.WriteLine("All workers stopped");
        }

        private async Task WorkerLoop(int workerId, CancellationToken cancellationToken)
        {
            Console.WriteLine($"Worker {workerId} started");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    VideoUploadItem item;
                    try
                    {
                        item = _uploadQueue.Take(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    Console.WriteLine($"Worker {workerId} processing video {item.VideoId}");

                    try
                    {
                        // Move from temp to final location
                        File.Move(item.TempPath, item.FinalPath, true);
                        
                        // Update metadata
                        var metadata = new Dictionary<string, object>(item.Metadata);
                        metadata["status"] = "processed";
                        metadata["processed_timestamp"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                        metadata["video_id"] = item.VideoId;
                        metadata["file_path"] = item.FinalPath;
                        
                        // Save metadata
                        _metadataManager.AddVideoMetadata(item.VideoId, metadata);
                        
                        Console.WriteLine($"Worker {workerId} completed processing video {item.VideoId}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Worker {workerId} error processing video {item.VideoId}: {ex.Message}");
                        // Clean up temp file if it exists
                        if (File.Exists(item.TempPath))
                        {
                            try
                            {
                                File.Delete(item.TempPath);
                            }
                            catch
                            {
                                // Ignore cleanup errors
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Worker {workerId} encountered error: {ex.Message}");
            }
            
            Console.WriteLine($"Worker {workerId} stopped");
        }
    }
}