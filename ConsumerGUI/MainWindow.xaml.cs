using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

using System.Collections.ObjectModel;
using System.Windows.Threading;
using System.IO;

namespace ConsumerGUI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly string videoFolder = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
        @"..\..\..\..\uploads"); // Relative path to uploads folder
    public ObservableCollection<string> VideoFiles { get; set; } = new ObservableCollection<string>();
    private DispatcherTimer previewTimer;
    private FileSystemWatcher fileWatcher;

    public MainWindow()
    {
        InitializeComponent();
        VideoList.ItemsSource = VideoFiles;
        LoadVideos();
        SetupFileWatcher();
    }

    private void SetupFileWatcher()
    {
        fileWatcher = new FileSystemWatcher(videoFolder)
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
            Filter = "*.mp4",
            EnableRaisingEvents = true
        };

        // Handle file creation events
        fileWatcher.Created += (sender, e) =>
        {
            // Use Dispatcher to update UI from different thread
            Dispatcher.Invoke(() =>
            {
                string fileName = System.IO.Path.GetFileName(e.FullPath);
                if (!VideoFiles.Contains(fileName))
                {
                    VideoFiles.Add(fileName);
                }
            });
        };

        // Handle file deletion events
        fileWatcher.Deleted += (sender, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                string fileName = System.IO.Path.GetFileName(e.FullPath);
                if (VideoFiles.Contains(fileName))
                {
                    VideoFiles.Remove(fileName);
                }
            });
        };

        // Handle file rename events
        fileWatcher.Renamed += (sender, e) =>
        {
            Dispatcher.Invoke(() =>
            {
                string oldFileName = System.IO.Path.GetFileName(e.OldFullPath);
                string newFileName = System.IO.Path.GetFileName(e.FullPath);

                if (VideoFiles.Contains(oldFileName))
                {
                    VideoFiles.Remove(oldFileName);
                }

                if (!VideoFiles.Contains(newFileName) && System.IO.Path.GetExtension(newFileName).ToLower() == ".mp4")
                {
                    VideoFiles.Add(newFileName);
                }
            });
        };
    }

    private void LoadVideos()
    {
        if (!Directory.Exists(videoFolder)) Directory.CreateDirectory(videoFolder);
        VideoFiles.Clear();
        foreach (var file in Directory.GetFiles(videoFolder, "*.mp4"))
        {
            VideoFiles.Add(System.IO.Path.GetFileName(file));
        }
    }

    private void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VideoList.SelectedItem is string fileName)
        {
            FullVideo.Source = new Uri(System.IO.Path.Combine(videoFolder, fileName));
            FullVideo.Play();
        }
    }

    private void VideoList_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is ListBoxItem item && item.Content is string fileName)
        {
            PreviewVideo.Source = new Uri(System.IO.Path.Combine(videoFolder, fileName));
            PreviewVideo.Position = TimeSpan.Zero;
            PreviewVideo.Play();

            previewTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(10)
            };
            previewTimer.Tick += (s, ev) =>
            {
                PreviewVideo.Stop();
                previewTimer.Stop();
            };
            previewTimer.Start();
        }
    }

    private void VideoList_MouseLeave(object sender, MouseEventArgs e)
    {
        PreviewVideo.Stop();
        previewTimer?.Stop();
    }
}