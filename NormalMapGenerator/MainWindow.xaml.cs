using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NormalMapGenerator.ImageProcessing;

namespace NormalMapGenerator;

public partial class MainWindow : Window
{
    private BitmapSource? _sourceImage;
    private BitmapSource? _normalMap;
    private string? _sourceFilePath;

    public MainWindow()
    {
        InitializeComponent();
        UpdateStrengthText();
    }

    private void LoadHeightmapButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dialog = new()
        {
            Title = "Load Heightmap",
            Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|PNG Files (*.png)|*.png|JPEG Files (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        if (!IsSupportedImageFile(dialog.FileName))
        {
            ShowError("Unsupported file format. Please choose a PNG, JPG, or JPEG file.");
            return;
        }

        try
        {
            BitmapImage image = LoadBitmap(dialog.FileName);
            _sourceImage = image;
            _sourceFilePath = dialog.FileName;
            SourcePreviewImage.Source = image;
            RegenerateNormalMap();
        }
        catch (Exception exception) when (IsImageLoadException(exception))
        {
            ShowError($"The selected image could not be loaded.\n\n{exception.Message}");
        }
    }

    private void ExportNormalMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_normalMap is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = "Export Normal Map",
            Filter = "PNG Files (*.png)|*.png",
            AddExtension = true,
            DefaultExt = ".png",
            FileName = BuildDefaultExportFileName()
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(_normalMap));

            using FileStream stream = new(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            ShowError($"The normal map could not be exported.\n\n{exception.Message}");
        }
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        UpdateStrengthText();
        RegenerateNormalMap();
    }

    private void RegenerateNormalMap()
    {
        if (_sourceImage is null)
        {
            if (ExportNormalMapButton is not null)
            {
                ExportNormalMapButton.IsEnabled = false;
            }

            return;
        }

        try
        {
            _normalMap = ImageProcessing.NormalMapGenerator.Generate(
                _sourceImage,
                StrengthSlider.Value,
                InvertXCheckBox.IsChecked == true,
                InvertYCheckBox.IsChecked == true);

            NormalPreviewImage.Source = _normalMap;
            ExportNormalMapButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            _normalMap = null;
            NormalPreviewImage.Source = null;
            ExportNormalMapButton.IsEnabled = false;
            ShowError($"The normal map could not be generated.\n\n{exception.Message}");
        }
    }

    private void UpdateStrengthText()
    {
        if (StrengthValueText is null || StrengthSlider is null)
        {
            return;
        }

        StrengthValueText.Text = StrengthSlider.Value.ToString("0.0", CultureInfo.InvariantCulture);
    }

    private string BuildDefaultExportFileName()
    {
        if (string.IsNullOrWhiteSpace(_sourceFilePath))
        {
            return "normal_map.png";
        }

        return $"{Path.GetFileNameWithoutExtension(_sourceFilePath)}_normal.png";
    }

    private static BitmapImage LoadBitmap(string filePath)
    {
        BitmapImage image = new();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(filePath, UriKind.Absolute);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private static bool IsSupportedImageFile(string filePath)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImageLoadException(Exception exception)
    {
        return exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or InvalidOperationException
            or FileFormatException;
    }

    private static void ShowError(string message)
    {
        MessageBox.Show(message, "NormalMapGenerator", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
