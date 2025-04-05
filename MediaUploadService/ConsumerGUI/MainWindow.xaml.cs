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
    private readonly string videoFolder = @"C:\Users\alexia\Desktop\DLSU\02 STDISCM Distributed Computing\Problem Set 03\STDISCM-PS3\MediaUploadService\bin\Debug\net8.0\uploads"; // Change to your actual video storage path
    public ObservableCollection<string> VideoFiles { get; set; } = new ObservableCollection<string>();
    private DispatcherTimer previewTimer;

    public MainWindow()
    {
        MessageBox.Show("MainWindow loaded!");
        InitializeComponent();
        VideoList.ItemsSource = VideoFiles;
        LoadVideos();
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