using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MediaUploadService.ConsumerBackend.Storage
{
    public class StorageManager
    {
        public string StoragePath { get; }

        public StorageManager(string storagePath)
        {
            StoragePath = storagePath;
            Directory.CreateDirectory(storagePath);
            Console.WriteLine($"Storage manager initialized with path: {storagePath}");
        }

        public StorageInfo GetStorageInfo()
        {
            try
            {
                var driveInfo = new DriveInfo(Path.GetFullPath(StoragePath));
                return new StorageInfo
                {
                    TotalBytes = driveInfo.TotalSize,
                    FreeBytes = driveInfo.AvailableFreeSpace,
                    UsedBytes = driveInfo.TotalSize - driveInfo.AvailableFreeSpace,
                    TotalGB = driveInfo.TotalSize / (1024.0 * 1024.0 * 1024.0),
                    FreeGB = driveInfo.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0),
                    UsedGB = (driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / (1024.0 * 1024.0 * 1024.0)
                };
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error getting storage info: {ex.Message}");
                return null;
            }
        }

        public string GetVideoPath(string videoId, string extension = ".mp4")
        {
            return Path.Combine(StoragePath, $"{videoId}{extension}");
        }

        public bool DeleteVideo(string videoPath)
        {
            try
            {
                if (File.Exists(videoPath))
                {
                    File.Delete(videoPath);
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error deleting video {videoPath}: {ex.Message}");
                return false;
            }
        }

        public int CleanupTempFiles()
        {
            try
            {
                int count = 0;
                foreach (var file in Directory.GetFiles(StoragePath, "temp_*"))
                {
                    File.Delete(file);
                    count++;
                }
                Console.WriteLine($"Cleaned up {count} temporary files");
                return count;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error cleaning up temp files: {ex.Message}");
                return 0;
            }
        }
    }

    public class StorageInfo
    {
        public long TotalBytes { get; set; }
        public long UsedBytes { get; set; }
        public long FreeBytes { get; set; }
        public double TotalGB { get; set; }
        public double UsedGB { get; set; }
        public double FreeGB { get; set; }
    }
}