using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using MediaUploadService.ConsumerBackend.Models;

namespace MediaUploadService.ConsumerBackend.Server
{
    public class UploadQueue
    {
        private readonly BlockingCollection<VideoUploadItem> _queue;
        private readonly int _maxSize;

        public UploadQueue(int maxSize)
        {
            _maxSize = maxSize;
            _queue = new BlockingCollection<VideoUploadItem>(new ConcurrentQueue<VideoUploadItem>(), maxSize);
        }

        public bool TryAddToQueue(VideoUploadItem item)
        {
            // Implements the leaky bucket algorithm - drop items if queue is full
            return _queue.TryAdd(item);
        }

        public VideoUploadItem Take(CancellationToken cancellationToken)
        {
            return _queue.Take(cancellationToken);
        }

        public bool IsEmpty => _queue.Count == 0;

        public bool IsFull => _queue.Count >= _maxSize;

        public int Count => _queue.Count;

        public int MaxSize => _maxSize;
    }
}