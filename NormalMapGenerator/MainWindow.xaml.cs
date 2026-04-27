using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using NormalMapGenerator.ImageProcessing;

namespace NormalMapGenerator;

public partial class MainWindow : Window
{
    private BitmapSource? _sourceImage;
    private BitmapSource? _normalMap;
    private string? _sourceFilePath;
    private Image? _sourcePreviewImage;
    private Image? _normalPreviewImage;
    private Button? _exportNormalMapButton;
    private Slider? _strengthSlider;
    private TextBox? _strengthValueText;
    private Slider? _blurSharpSlider;
    private TextBox? _blurSharpValueText;
    private CheckBox? _invertXCheckBox;
    private CheckBox? _invertYCheckBox;
    private PreviewRenderRequest? _pendingPreviewRequest;
    private bool _isPreviewWorkerRunning;
    private int _previewUpdateVersion;

    public MainWindow()
    {
        InitializeComponent();
        BindNamedControls();
        UpdateStrengthText();
        UpdateBlurSharpText();
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
            if (_sourcePreviewImage is not null)
            {
                _sourcePreviewImage.Source = image;
            }

            ScheduleNormalMapRegeneration();
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
        UpdateBlurSharpText();
        ScheduleNormalMapRegeneration();
    }

    private void DecreaseStrengthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strengthSlider is not null)
        {
            AdjustSlider(_strengthSlider, -GetSliderStep());
        }
    }

    private void IncreaseStrengthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strengthSlider is not null)
        {
            AdjustSlider(_strengthSlider, GetSliderStep());
        }
    }

    private void DecreaseBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_blurSharpSlider is not null)
        {
            AdjustSlider(_blurSharpSlider, -GetSliderStep());
        }
    }

    private void IncreaseBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_blurSharpSlider is not null)
        {
            AdjustSlider(_blurSharpSlider, GetSliderStep());
        }
    }

    private void StrengthValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitStrengthText();
    }

    private void StrengthValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitStrengthText();
            e.Handled = true;
        }
    }

    private void BlurSharpValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitBlurSharpText();
    }

    private void BlurSharpValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitBlurSharpText();
            e.Handled = true;
        }
    }

    private void ScheduleNormalMapRegeneration()
    {
        if (_sourceImage is null)
        {
            _normalMap = null;

            if (_normalPreviewImage is not null)
            {
                _normalPreviewImage.Source = null;
            }

            if (_exportNormalMapButton is not null)
            {
                _exportNormalMapButton.IsEnabled = false;
            }

            return;
        }

        if (_strengthSlider is null
            || _blurSharpSlider is null
            || _invertXCheckBox is null
            || _invertYCheckBox is null
            || _normalPreviewImage is null
            || _exportNormalMapButton is null)
        {
            return;
        }

        BitmapSource sourceImage = _sourceImage;
        double strength = _strengthSlider.Value;
        double blurSharp = _blurSharpSlider.Value;
        bool invertX = _invertXCheckBox.IsChecked == true;
        bool invertY = _invertYCheckBox.IsChecked == true;
        int version = Interlocked.Increment(ref _previewUpdateVersion);

        _exportNormalMapButton.IsEnabled = false;

        _pendingPreviewRequest = new PreviewRenderRequest(
            version,
            sourceImage,
            strength,
            blurSharp,
            invertX,
            invertY);

        if (!_isPreviewWorkerRunning)
        {
            _isPreviewWorkerRunning = true;
            _ = RunPreviewWorkerAsync();
        }
    }

    private async Task RunPreviewWorkerAsync()
    {
        while (true)
        {
            PreviewRenderRequest? request = _pendingPreviewRequest;
            if (request is null)
            {
                _isPreviewWorkerRunning = false;
                return;
            }

            _pendingPreviewRequest = null;

            await RenderPreviewAsync(request.Value);

            if (_pendingPreviewRequest is not null)
            {
                await Task.Yield();
            }
        }
    }

    private async Task RenderPreviewAsync(PreviewRenderRequest request)
    {
        try
        {
            BitmapSource? normalMap = await Task.Run(
                () => ImageProcessing.NormalMapGenerator.Generate(
                    request.SourceImage,
                    request.Strength,
                    request.BlurSharp,
                    request.InvertX,
                    request.InvertY));

            if (normalMap is null || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            if (_normalPreviewImage is null || _exportNormalMapButton is null)
            {
                return;
            }

            _normalMap = normalMap;
            _normalPreviewImage.Source = normalMap;
            _exportNormalMapButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            if (request.Version != _previewUpdateVersion || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _normalMap = null;

            if (_normalPreviewImage is not null)
            {
                _normalPreviewImage.Source = null;
            }

            if (_exportNormalMapButton is not null)
            {
                _exportNormalMapButton.IsEnabled = false;
            }

            ShowError($"The normal map could not be generated.\n\n{exception.Message}");
        }
    }

    private readonly record struct PreviewRenderRequest(
        int Version,
        BitmapSource SourceImage,
        double Strength,
        double BlurSharp,
        bool InvertX,
        bool InvertY);

    private void UpdateStrengthText()
    {
        if (_strengthValueText is null || _strengthSlider is null)
        {
            return;
        }

        if (_strengthValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetStrengthText(_strengthSlider.Value);
    }

    private void UpdateBlurSharpText()
    {
        if (_blurSharpValueText is null || _blurSharpSlider is null)
        {
            return;
        }

        if (_blurSharpValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetBlurSharpText(_blurSharpSlider.Value);
    }

    private static void AdjustSlider(Slider slider, double delta)
    {
        slider.Value = Math.Clamp(
            Math.Round((slider.Value + delta) * 100.0) / 100.0,
            slider.Minimum,
            slider.Maximum);
    }

    private static double GetSliderStep()
    {
        return Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 0.1 : 0.01;
    }

    private void CommitStrengthText()
    {
        if (_strengthValueText is null || _strengthSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_strengthValueText.Text, out double strength))
        {
            _strengthSlider.Value = Math.Clamp(strength, _strengthSlider.Minimum, _strengthSlider.Maximum);
        }

        SetStrengthText(_strengthSlider.Value);
    }

    private void CommitBlurSharpText()
    {
        if (_blurSharpValueText is null || _blurSharpSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_blurSharpValueText.Text, out double blurSharp))
        {
            _blurSharpSlider.Value = Math.Clamp(blurSharp, _blurSharpSlider.Minimum, _blurSharpSlider.Maximum);
        }

        SetBlurSharpText(_blurSharpSlider.Value);
    }

    private void SetStrengthText(double value)
    {
        if (_strengthValueText is null)
        {
            return;
        }

        _strengthValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetBlurSharpText(double value)
    {
        if (_blurSharpValueText is null)
        {
            return;
        }

        _blurSharpValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void BindNamedControls()
    {
        _sourcePreviewImage = FindRequiredControl<Image>("SourcePreviewImage");
        _normalPreviewImage = FindRequiredControl<Image>("NormalPreviewImage");
        _exportNormalMapButton = FindRequiredControl<Button>("ExportNormalMapButton");
        _strengthSlider = FindRequiredControl<Slider>("StrengthSlider");
        _strengthValueText = FindRequiredControl<TextBox>("StrengthValueText");
        _blurSharpSlider = FindRequiredControl<Slider>("BlurSharpSlider");
        _blurSharpValueText = FindRequiredControl<TextBox>("BlurSharpValueText");
        _invertXCheckBox = FindRequiredControl<CheckBox>("InvertXCheckBox");
        _invertYCheckBox = FindRequiredControl<CheckBox>("InvertYCheckBox");
    }

    private T FindRequiredControl<T>(string name)
        where T : class
    {
        return FindName(name) as T
            ?? throw new InvalidOperationException($"The control '{name}' could not be found.");
    }

    private static bool TryParseSliderValue(string text, out double value)
    {
        const NumberStyles decimalStyle =
            NumberStyles.AllowLeadingWhite
            | NumberStyles.AllowTrailingWhite
            | NumberStyles.AllowLeadingSign
            | NumberStyles.AllowDecimalPoint;

        if (double.TryParse(text, decimalStyle, CultureInfo.CurrentCulture, out value))
        {
            return true;
        }

        if (double.TryParse(text, decimalStyle, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        string normalized = text.Replace(',', '.');
        return double.TryParse(normalized, decimalStyle, CultureInfo.InvariantCulture, out value);
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
