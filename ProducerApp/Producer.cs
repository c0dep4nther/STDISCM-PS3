using System;
using System.IO;
using System.Net.Sockets;
using System.Text;

namespace ProducerApp
{
    public class Producer
    {
        private string folderPath;
        private string serverIp;
        private int serverPort;

        public Producer(string folderPath, string serverIp, int serverPort)
        {
            this.folderPath = folderPath;
            this.serverIp = serverIp;
            this.serverPort = serverPort;
        }

        public void Start()
        {
            foreach (var file in Directory.GetFiles(folderPath, "*.mp4"))
            {
                try
                {
                    UploadVideo(file);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR] Failed to upload {file}: {ex.Message}");
                }
            }
        }

        private void UploadVideo(string filePath)
        {
            Console.WriteLine($"[INFO] Uploading: {Path.GetFileName(filePath)}");

            byte[] fileBytes = File.ReadAllBytes(filePath);
            byte[] fileNameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(filePath));
            byte[] fileNameLength = BitConverter.GetBytes(fileNameBytes.Length);

            using TcpClient client = new TcpClient(serverIp, serverPort);
            using NetworkStream stream = client.GetStream();
            {
                stream.Write(fileNameLength, 0, 4);
                stream.Write(fileNameBytes, 0, fileNameBytes.Length);
                stream.Write(fileBytes, 0, fileBytes.Length);
            }

            Console.WriteLine($"[SUCCESS] Uploaded: {Path.GetFileName(filePath)}");
        }
    }
}
