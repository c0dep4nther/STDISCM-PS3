using System;
using System.IO;
using System.Threading;
using ProducerApp; // 👈 Add this line

namespace ProducerApp
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length < 4)
            {
                Console.WriteLine("Usage: dotnet run -- <p> <server_ip> <server_port> <base_video_path>");
                return;
            }

            int p = int.Parse(args[0]);
            string serverIp = args[1];
            int serverPort = int.Parse(args[2]);
            string basePath = args[3];

            Thread[] threads = new Thread[p];

            for (int i = 0; i < p; i++)
            {
                string folderPath = Path.Combine(basePath, $"Producer{i + 1}");
                Producer producer = new Producer(folderPath, serverIp, serverPort);
                threads[i] = new Thread(new ThreadStart(producer.Start));
                threads[i].Start();
            }

            foreach (Thread t in threads)
            {
                t.Join();
            }

            Console.WriteLine("All producer threads completed.");
        }
    }
}
