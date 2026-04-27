using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using HelixToolkit;
using HelixToolkit.Maths;
using HelixToolkit.SharpDX;
using HelixToolkit.Wpf.SharpDX;
using Microsoft.Win32;
using NormalMapGenerator.ImageProcessing;
using Media3D = System.Windows.Media.Media3D;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace NormalMapGenerator;

public partial class MainWindow : Window
{
    private const double DefaultStrength = 5.0;
    private const double DefaultLevel = 1.0;
    private const double DefaultBlurSharp = 0.0;
    private const double DefaultDisplacementLevel = 1.0;
    private const double DefaultDisplacementBlurSharp = 0.0;
    private const double DefaultDisplacementHeightScale = 0.10;
    private const int PreviewPlaneSubdivisions = 64;
    private const int PreviewCubeSubdivisions = 32;
    private const int PreviewSphereLatitudeSegments = 32;
    private const int PreviewSphereLongitudeSegments = 64;
    private const int PreviewCylinderSegments = 64;
    private const int PreviewCylinderHeightSegments = 32;
    private const int PreviewCylinderCapRings = 16;
    private const float PreviewHardEdgeFadeWidth = 0.08f;
    private const int MaxPreviewBitmapDimension = 1024;

    private BitmapSource? _sourceFullResolutionImage;
    private BitmapSource? _sourceImage;
    private BitmapSource? _normalMap;
    private BitmapSource? _displacementMap;
    private string? _sourceFilePath;
    private Image? _sourcePreviewImage;
    private Image? _generatedMapPreviewImage;
    private TextBlock? _generatedMapPreviewTitle;
    private Button? _exportMapButton;
    private TabControl? _mapSettingsTabControl;
    private Slider? _strengthSlider;
    private TextBox? _strengthValueText;
    private Slider? _levelSlider;
    private TextBox? _levelValueText;
    private Slider? _blurSharpSlider;
    private TextBox? _blurSharpValueText;
    private ComboBox? _channelSourceComboBox;
    private ComboBox? _edgeModeComboBox;
    private CheckBox? _invertXCheckBox;
    private CheckBox? _invertYCheckBox;
    private Slider? _displacementLevelSlider;
    private TextBox? _displacementLevelValueText;
    private Slider? _displacementBlurSharpSlider;
    private TextBox? _displacementBlurSharpValueText;
    private Slider? _displacementHeightScaleSlider;
    private TextBox? _displacementHeightScaleValueText;
    private ComboBox? _displacementChannelSourceComboBox;
    private ComboBox? _displacementEdgeModeComboBox;
    private CheckBox? _invertDisplacementCheckBox;
    private CheckBox? _useNormalMapCheckBox;
    private CheckBox? _useDisplacementMapCheckBox;
    private CheckBox? _useHeightmapAlbedoCheckBox;
    private ComboBox? _previewShapeComboBox;
    private ContentControl? _preview3DHost;
    private Viewport3DX? _previewViewport3D;
    private TextBlock? _preview3DStatusText;
    private DefaultEffectsManager? _previewEffectsManager;
    private MeshGeometryModel3D? _previewModel;
    private PhongMaterial? _previewMaterial;
    private bool _is3DPreviewAvailable;
    private NormalPreviewRenderRequest? _pendingNormalPreviewRequest;
    private DisplacementPreviewRenderRequest? _pendingDisplacementPreviewRequest;
    private bool _isNormalPreviewWorkerRunning;
    private bool _isDisplacementPreviewWorkerRunning;
    private PreviewGeometryRenderRequest? _pendingPreviewGeometryRequest;
    private bool _isPreviewGeometryWorkerRunning;
    private int _normalPreviewUpdateVersion;
    private int _displacementPreviewUpdateVersion;
    private int _previewGeometryUpdateVersion;

    public MainWindow()
    {
        InitializeComponent();
        BindNamedControls();
        Initialize3DPreview();
        UpdateStrengthText();
        UpdateLevelText();
        UpdateBlurSharpText();
        UpdateDisplacementLevelText();
        UpdateDisplacementBlurSharpText();
        UpdateDisplacementHeightScaleText();
        UpdateActiveMapPreview();
    }

    protected override void OnClosed(EventArgs e)
    {
        _previewEffectsManager?.Dispose();
        base.OnClosed(e);
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
            BitmapSource previewImage = CreatePreviewBitmap(image);
            _sourceFullResolutionImage = image;
            _sourceImage = previewImage;
            _sourceFilePath = dialog.FileName;
            _normalMap = null;
            _displacementMap = null;
            if (_sourcePreviewImage is not null)
            {
                _sourcePreviewImage.Source = previewImage;
            }

            RefreshPreviewGeometry();
            UpdateActiveMapPreview();
            Update3DPreview();
            ScheduleNormalMapRegeneration();
            ScheduleDisplacementMapRegeneration();
        }
        catch (Exception exception) when (IsImageLoadException(exception))
        {
            ShowError($"The selected image could not be loaded.\n\n{exception.Message}");
        }
    }

    private async void ExportMapButton_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceFullResolutionImage is null || GetActiveGeneratedMap() is null)
        {
            return;
        }

        SaveFileDialog dialog = new()
        {
            Title = IsDisplacementTabActive() ? "Export Displacement Map" : "Export Normal Map",
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
            if (_exportMapButton is not null)
            {
                _exportMapButton.IsEnabled = false;
            }

            BitmapSource sourceImage = _sourceFullResolutionImage;
            bool exportDisplacement = IsDisplacementTabActive();
            NormalMapGenerationSettings? normalSettings = exportDisplacement ? null : CaptureNormalMapGenerationSettings();
            DisplacementMapGenerationSettings? displacementSettings = exportDisplacement ? CaptureDisplacementMapGenerationSettings() : null;

            BitmapSource? map = await Task.Run(() =>
            {
                if (exportDisplacement)
                {
                    return displacementSettings.HasValue
                        ? GenerateDisplacementMap(sourceImage, displacementSettings.Value)
                        : null;
                }

                return normalSettings.HasValue
                    ? GenerateNormalMap(sourceImage, normalSettings.Value)
                    : null;
            });

            if (map is null)
            {
                ShowError("The generated map could not be exported because the current settings are incomplete.");
                return;
            }

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(map));

            using FileStream stream = new(dialog.FileName, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or ArgumentException)
        {
            ShowError($"The generated map could not be exported.\n\n{exception.Message}");
        }
        finally
        {
            UpdateActiveMapPreview();
        }
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        UpdateStrengthText();
        UpdateLevelText();
        UpdateBlurSharpText();
        ScheduleNormalMapRegeneration();
    }

    private void DisplacementSettingsChanged(object sender, RoutedEventArgs e)
    {
        UpdateDisplacementLevelText();
        UpdateDisplacementBlurSharpText();
        ScheduleDisplacementMapRegeneration();
    }

    private void DisplacementHeightScaleChanged(object sender, RoutedEventArgs e)
    {
        UpdateDisplacementHeightScaleText();
        SchedulePreviewGeometryRefresh();
    }

    private void MapSettingsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.Source == _mapSettingsTabControl)
        {
            UpdateActiveMapPreview();
        }
    }

    private void PreviewMapUsageChanged(object sender, RoutedEventArgs e)
    {
        RefreshPreviewGeometry();
        Update3DPreview();
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

    private void ResetStrengthButton_Click(object sender, RoutedEventArgs e)
    {
        if (_strengthSlider is not null)
        {
            _strengthSlider.Value = DefaultStrength;
        }
    }

    private void DecreaseLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_levelSlider is not null)
        {
            AdjustSlider(_levelSlider, -GetSliderStep());
        }
    }

    private void IncreaseLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_levelSlider is not null)
        {
            AdjustSlider(_levelSlider, GetSliderStep());
        }
    }

    private void ResetLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_levelSlider is not null)
        {
            _levelSlider.Value = DefaultLevel;
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

    private void ResetBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_blurSharpSlider is not null)
        {
            _blurSharpSlider.Value = DefaultBlurSharp;
        }
    }

    private void DecreaseDisplacementLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementLevelSlider is not null)
        {
            AdjustSlider(_displacementLevelSlider, -GetSliderStep());
        }
    }

    private void IncreaseDisplacementLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementLevelSlider is not null)
        {
            AdjustSlider(_displacementLevelSlider, GetSliderStep());
        }
    }

    private void ResetDisplacementLevelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementLevelSlider is not null)
        {
            _displacementLevelSlider.Value = DefaultDisplacementLevel;
        }
    }

    private void DecreaseDisplacementBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementBlurSharpSlider is not null)
        {
            AdjustSlider(_displacementBlurSharpSlider, -GetSliderStep());
        }
    }

    private void IncreaseDisplacementBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementBlurSharpSlider is not null)
        {
            AdjustSlider(_displacementBlurSharpSlider, GetSliderStep());
        }
    }

    private void ResetDisplacementBlurSharpButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementBlurSharpSlider is not null)
        {
            _displacementBlurSharpSlider.Value = DefaultDisplacementBlurSharp;
        }
    }

    private void DecreaseDisplacementHeightScaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementHeightScaleSlider is not null)
        {
            AdjustSlider(_displacementHeightScaleSlider, -GetSliderStep());
        }
    }

    private void IncreaseDisplacementHeightScaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementHeightScaleSlider is not null)
        {
            AdjustSlider(_displacementHeightScaleSlider, GetSliderStep());
        }
    }

    private void ResetDisplacementHeightScaleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_displacementHeightScaleSlider is not null)
        {
            _displacementHeightScaleSlider.Value = DefaultDisplacementHeightScale;
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

    private void LevelValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitLevelText();
    }

    private void LevelValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitLevelText();
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

    private void DisplacementLevelValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDisplacementLevelText();
    }

    private void DisplacementLevelValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDisplacementLevelText();
            e.Handled = true;
        }
    }

    private void DisplacementBlurSharpValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDisplacementBlurSharpText();
    }

    private void DisplacementBlurSharpValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDisplacementBlurSharpText();
            e.Handled = true;
        }
    }

    private void DisplacementHeightScaleValueText_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitDisplacementHeightScaleText();
    }

    private void DisplacementHeightScaleValueText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitDisplacementHeightScaleText();
            e.Handled = true;
        }
    }

    private void ScheduleNormalMapRegeneration()
    {
        if (_sourceImage is null)
        {
            _normalMap = null;
            RefreshPreviewGeometry();
            UpdateActiveMapPreview();
            Update3DPreview();
            return;
        }

        if (_strengthSlider is null
            || _levelSlider is null
            || _blurSharpSlider is null
            || _channelSourceComboBox is null
            || _edgeModeComboBox is null
            || _invertXCheckBox is null
            || _invertYCheckBox is null)
        {
            return;
        }

        BitmapSource sourceImage = _sourceImage;
        double strength = _strengthSlider.Value;
        double level = _levelSlider.Value;
        double blurSharp = _blurSharpSlider.Value;
        HeightChannelSource channelSource = GetSelectedHeightChannelSource();
        NormalMapEdgeMode edgeMode = GetSelectedEdgeMode();
        bool invertX = _invertXCheckBox.IsChecked == true;
        bool invertY = _invertYCheckBox.IsChecked == true;
        int version = Interlocked.Increment(ref _normalPreviewUpdateVersion);

        if (!IsDisplacementTabActive() && _exportMapButton is not null)
        {
            _exportMapButton.IsEnabled = false;
        }

        _pendingNormalPreviewRequest = new NormalPreviewRenderRequest(
            version,
            sourceImage,
            strength,
            level,
            blurSharp,
            channelSource,
            edgeMode,
            invertX,
            invertY);

        if (!_isNormalPreviewWorkerRunning)
        {
            _isNormalPreviewWorkerRunning = true;
            _ = RunNormalPreviewWorkerAsync();
        }
    }

    private void ScheduleDisplacementMapRegeneration()
    {
        if (_sourceImage is null)
        {
            _displacementMap = null;
            RefreshPreviewGeometry();
            UpdateActiveMapPreview();
            Update3DPreview();
            return;
        }

        if (_displacementLevelSlider is null
            || _displacementBlurSharpSlider is null
            || _displacementChannelSourceComboBox is null
            || _displacementEdgeModeComboBox is null
            || _invertDisplacementCheckBox is null)
        {
            return;
        }

        BitmapSource sourceImage = _sourceImage;
        double level = _displacementLevelSlider.Value;
        double blurSharp = _displacementBlurSharpSlider.Value;
        HeightChannelSource channelSource = GetSelectedDisplacementHeightChannelSource();
        NormalMapEdgeMode edgeMode = GetSelectedDisplacementEdgeMode();
        bool invert = _invertDisplacementCheckBox.IsChecked == true;
        int version = Interlocked.Increment(ref _displacementPreviewUpdateVersion);

        if (IsDisplacementTabActive() && _exportMapButton is not null)
        {
            _exportMapButton.IsEnabled = false;
        }

        _pendingDisplacementPreviewRequest = new DisplacementPreviewRenderRequest(
            version,
            sourceImage,
            level,
            blurSharp,
            channelSource,
            edgeMode,
            invert);

        if (!_isDisplacementPreviewWorkerRunning)
        {
            _isDisplacementPreviewWorkerRunning = true;
            _ = RunDisplacementPreviewWorkerAsync();
        }
    }

    private NormalMapGenerationSettings? CaptureNormalMapGenerationSettings()
    {
        if (_strengthSlider is null
            || _levelSlider is null
            || _blurSharpSlider is null
            || _invertXCheckBox is null
            || _invertYCheckBox is null)
        {
            return null;
        }

        return new NormalMapGenerationSettings(
            _strengthSlider.Value,
            _levelSlider.Value,
            _blurSharpSlider.Value,
            GetSelectedHeightChannelSource(),
            GetSelectedEdgeMode(),
            _invertXCheckBox.IsChecked == true,
            _invertYCheckBox.IsChecked == true);
    }

    private DisplacementMapGenerationSettings? CaptureDisplacementMapGenerationSettings()
    {
        if (_displacementLevelSlider is null
            || _displacementBlurSharpSlider is null
            || _invertDisplacementCheckBox is null)
        {
            return null;
        }

        return new DisplacementMapGenerationSettings(
            _displacementLevelSlider.Value,
            _displacementBlurSharpSlider.Value,
            GetSelectedDisplacementHeightChannelSource(),
            GetSelectedDisplacementEdgeMode(),
            _invertDisplacementCheckBox.IsChecked == true);
    }

    private static BitmapSource? GenerateNormalMap(
        BitmapSource sourceImage,
        NormalMapGenerationSettings settings)
    {
        return ImageProcessing.NormalMapGenerator.Generate(
            sourceImage,
            settings.Strength,
            settings.Level,
            settings.BlurSharp,
            settings.InvertX,
            settings.InvertY,
            settings.ChannelSource,
            settings.EdgeMode);
    }

    private static BitmapSource? GenerateDisplacementMap(
        BitmapSource sourceImage,
        DisplacementMapGenerationSettings settings)
    {
        return ImageProcessing.NormalMapGenerator.GenerateDisplacement(
            sourceImage,
            settings.Level,
            settings.BlurSharp,
            settings.Invert,
            settings.ChannelSource,
            settings.EdgeMode);
    }

    private async Task RunNormalPreviewWorkerAsync()
    {
        while (true)
        {
            NormalPreviewRenderRequest? request = _pendingNormalPreviewRequest;
            if (request is null)
            {
                _isNormalPreviewWorkerRunning = false;
                return;
            }

            _pendingNormalPreviewRequest = null;

            await RenderNormalPreviewAsync(request.Value);

            if (_pendingNormalPreviewRequest is not null)
            {
                await Task.Yield();
            }
        }
    }

    private async Task RunDisplacementPreviewWorkerAsync()
    {
        while (true)
        {
            DisplacementPreviewRenderRequest? request = _pendingDisplacementPreviewRequest;
            if (request is null)
            {
                _isDisplacementPreviewWorkerRunning = false;
                return;
            }

            _pendingDisplacementPreviewRequest = null;

            await RenderDisplacementPreviewAsync(request.Value);

            if (_pendingDisplacementPreviewRequest is not null)
            {
                await Task.Yield();
            }
        }
    }

    private async Task RenderNormalPreviewAsync(NormalPreviewRenderRequest request)
    {
        try
        {
            BitmapSource? normalMap = await Task.Run(
                () => ImageProcessing.NormalMapGenerator.Generate(
                    request.SourceImage,
                    request.Strength,
                    request.Level,
                    request.BlurSharp,
                    request.InvertX,
                    request.InvertY,
                    request.ChannelSource,
                    request.EdgeMode));

            if (normalMap is null || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _normalMap = normalMap;
            UpdateActiveMapPreview();
            Update3DPreview();
        }
        catch (Exception exception)
        {
            if (request.Version != _normalPreviewUpdateVersion || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _normalMap = null;
            UpdateActiveMapPreview();
            Update3DPreview();

            ShowError($"The normal map could not be generated.\n\n{exception.Message}");
        }
    }

    private async Task RenderDisplacementPreviewAsync(DisplacementPreviewRenderRequest request)
    {
        try
        {
            BitmapSource? displacementMap = await Task.Run(
                () => ImageProcessing.NormalMapGenerator.GenerateDisplacement(
                    request.SourceImage,
                    request.Level,
                    request.BlurSharp,
                    request.Invert,
                    request.ChannelSource,
                    request.EdgeMode));

            if (displacementMap is null || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _displacementMap = displacementMap;
            RefreshPreviewGeometry();
            UpdateActiveMapPreview();
            Update3DPreview();
        }
        catch (Exception exception)
        {
            if (request.Version != _displacementPreviewUpdateVersion || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _displacementMap = null;
            RefreshPreviewGeometry();
            UpdateActiveMapPreview();
            Update3DPreview();

            ShowError($"The displacement map could not be generated.\n\n{exception.Message}");
        }
    }

    private void UseHeightmapAlbedoChanged(object sender, RoutedEventArgs e)
    {
        Update3DPreview();
    }

    private void PreviewShapeChanged(object sender, RoutedEventArgs e)
    {
        RefreshPreviewGeometry();
    }

    private void Initialize3DPreview()
    {
        if (_preview3DHost is null)
        {
            return;
        }

        try
        {
            _previewViewport3D = new Viewport3DX
            {
                BackgroundColor = System.Windows.Media.Color.FromRgb(17, 24, 39),
                ShowCoordinateSystem = false,
                ShowFrameRate = false,
                ShowViewCube = false,
                IsShadowMappingEnabled = false
            };
            _previewEffectsManager = new DefaultEffectsManager();
            _previewMaterial = CreatePreviewMaterial();
            _previewModel = new MeshGeometryModel3D
            {
                Geometry = CreateCurrentPreviewGeometry(),
                Material = _previewMaterial,
                CullMode = SharpDX.Direct3D11.CullMode.Back,
                IsHitTestVisible = false
            };

            _previewViewport3D.EffectsManager = _previewEffectsManager;
            _previewViewport3D.Camera = new PerspectiveCamera
            {
                Position = new Media3D.Point3D(1.7, 1.35, 2.6),
                LookDirection = new Media3D.Vector3D(-1.7, -1.35, -2.6),
                UpDirection = new Media3D.Vector3D(0, 1, 0),
                FarPlaneDistance = 100,
                NearPlaneDistance = 0.01
            };

            _previewViewport3D.Items.Add(new AmbientLight3D
            {
                Color = System.Windows.Media.Color.FromRgb(132, 138, 146)
            });
            _previewViewport3D.Items.Add(new DirectionalLight3D
            {
                Color = System.Windows.Media.Color.FromRgb(255, 238, 210),
                Direction = new Media3D.Vector3D(-0.35, -0.55, -1.0)
            });
            _previewViewport3D.Items.Add(new DirectionalLight3D
            {
                Color = System.Windows.Media.Color.FromRgb(150, 172, 210),
                Direction = new Media3D.Vector3D(0.85, -0.2, -0.45)
            });
            _previewViewport3D.Items.Add(new DirectionalLight3D
            {
                Color = System.Windows.Media.Color.FromRgb(120, 142, 190),
                Direction = new Media3D.Vector3D(0.45, -0.15, 0.9)
            });
            _previewViewport3D.Items.Add(new DirectionalLight3D
            {
                Color = System.Windows.Media.Color.FromRgb(105, 115, 135),
                Direction = new Media3D.Vector3D(0.1, 0.85, -0.15)
            });
            _previewViewport3D.Items.Add(_previewModel);
            _preview3DHost.Content = _previewViewport3D;
            _is3DPreviewAvailable = true;
        }
        catch (Exception exception)
        {
            _is3DPreviewAvailable = false;
            if (_preview3DStatusText is not null)
            {
                _preview3DStatusText.Visibility = Visibility.Visible;
            }

            ShowError($"The 3D preview could not be initialized.\n\n{exception.Message}");
        }
    }

    private void Update3DPreview()
    {
        if (!_is3DPreviewAvailable || _previewMaterial is null || _previewModel is null)
        {
            return;
        }

        try
        {
            if (_sourceImage is null)
            {
                _previewModel.Visibility = Visibility.Hidden;
                return;
            }

            if (_useNormalMapCheckBox?.IsChecked == true && _normalMap is not null)
            {
                _previewMaterial.NormalMap = CreateTextureModel(_normalMap);
                _previewMaterial.RenderNormalMap = true;
            }
            else
            {
                _previewMaterial.NormalMap = null;
                _previewMaterial.RenderNormalMap = false;
            }

            _previewMaterial.DisplacementMap = null;
            _previewMaterial.RenderDisplacementMap = false;
            _previewMaterial.EnableTessellation = false;

            if (_useHeightmapAlbedoCheckBox?.IsChecked == true && _sourceImage is not null)
            {
                _previewMaterial.DiffuseMap = CreateTextureModel(_sourceImage);
                _previewMaterial.RenderDiffuseMap = true;
            }
            else
            {
                _previewMaterial.DiffuseMap = null;
                _previewMaterial.RenderDiffuseMap = false;
            }

            _previewModel.Visibility = Visibility.Visible;
        }
        catch (Exception exception)
        {
            _is3DPreviewAvailable = false;
            if (_preview3DStatusText is not null)
            {
                _preview3DStatusText.Visibility = Visibility.Visible;
            }

            ShowError($"The 3D preview could not be updated.\n\n{exception.Message}");
        }
    }

    private static PhongMaterial CreatePreviewMaterial()
    {
        return new PhongMaterial
        {
            AmbientColor = new Color4(0.22f, 0.22f, 0.22f, 1.0f),
            DiffuseColor = new Color4(0.62f, 0.62f, 0.62f, 1.0f),
            SpecularColor = new Color4(0.08f, 0.08f, 0.08f, 1.0f),
            SpecularShininess = 16,
            RenderDiffuseMap = false,
            RenderNormalMap = false,
            RenderDisplacementMap = false,
            EnableTessellation = false
        };
    }

    private void UpdateActiveMapPreview()
    {
        if (_generatedMapPreviewImage is null || _generatedMapPreviewTitle is null || _exportMapButton is null)
        {
            return;
        }

        if (IsDisplacementTabActive())
        {
            _generatedMapPreviewTitle.Text = "Generated Displacement Map";
            _generatedMapPreviewImage.Source = _displacementMap;
            _exportMapButton.Content = "Export Displacement Map";
            _exportMapButton.IsEnabled = _displacementMap is not null;
        }
        else
        {
            _generatedMapPreviewTitle.Text = "Generated Normal Map";
            _generatedMapPreviewImage.Source = _normalMap;
            _exportMapButton.Content = "Export Normal Map";
            _exportMapButton.IsEnabled = _normalMap is not null;
        }
    }

    private BitmapSource? GetActiveGeneratedMap()
    {
        return IsDisplacementTabActive() ? _displacementMap : _normalMap;
    }

    private bool IsDisplacementTabActive()
    {
        return _mapSettingsTabControl?.SelectedIndex == 1;
    }

    private PreviewShape GetSelectedPreviewShape()
    {
        if (_previewShapeComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return PreviewShape.Cube;
        }

        return (item.Tag as string) switch
        {
            "Plane" => PreviewShape.Plane,
            "Sphere" => PreviewShape.Sphere,
            "Cylinder" => PreviewShape.Cylinder,
            _ => PreviewShape.Cube
        };
    }

    private HeightChannelSource GetSelectedHeightChannelSource()
    {
        if (_channelSourceComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return HeightChannelSource.Luminance;
        }

        return (item.Tag as string) switch
        {
            "Red" => HeightChannelSource.Red,
            "Green" => HeightChannelSource.Green,
            "Blue" => HeightChannelSource.Blue,
            "Alpha" => HeightChannelSource.Alpha,
            _ => HeightChannelSource.Luminance
        };
    }

    private NormalMapEdgeMode GetSelectedEdgeMode()
    {
        if (_edgeModeComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return NormalMapEdgeMode.Clamp;
        }

        return string.Equals(item.Tag as string, "Wrap", StringComparison.OrdinalIgnoreCase)
            ? NormalMapEdgeMode.Wrap
            : NormalMapEdgeMode.Clamp;
    }

    private HeightChannelSource GetSelectedDisplacementHeightChannelSource()
    {
        if (_displacementChannelSourceComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return HeightChannelSource.Luminance;
        }

        return (item.Tag as string) switch
        {
            "Red" => HeightChannelSource.Red,
            "Green" => HeightChannelSource.Green,
            "Blue" => HeightChannelSource.Blue,
            "Alpha" => HeightChannelSource.Alpha,
            _ => HeightChannelSource.Luminance
        };
    }

    private NormalMapEdgeMode GetSelectedDisplacementEdgeMode()
    {
        if (_displacementEdgeModeComboBox?.SelectedItem is not ComboBoxItem item)
        {
            return NormalMapEdgeMode.Clamp;
        }

        return string.Equals(item.Tag as string, "Wrap", StringComparison.OrdinalIgnoreCase)
            ? NormalMapEdgeMode.Wrap
            : NormalMapEdgeMode.Clamp;
    }

    private double GetDisplacementHeightScale()
    {
        return _displacementHeightScaleSlider?.Value ?? DefaultDisplacementHeightScale;
    }

    private void RefreshPreviewGeometry()
    {
        if (!_is3DPreviewAvailable || _previewModel is null)
        {
            return;
        }

        try
        {
            _previewModel.Geometry = CreateCurrentPreviewGeometry();
        }
        catch (Exception exception)
        {
            _is3DPreviewAvailable = false;
            if (_preview3DStatusText is not null)
            {
                _preview3DStatusText.Visibility = Visibility.Visible;
            }

            ShowError($"The 3D preview geometry could not be updated.\n\n{exception.Message}");
        }
    }

    private void SchedulePreviewGeometryRefresh()
    {
        if (!_is3DPreviewAvailable || _previewModel is null)
        {
            return;
        }

        BitmapSource? displacementMap = _useDisplacementMapCheckBox?.IsChecked == true ? _displacementMap : null;
        double heightScale = displacementMap is not null ? GetDisplacementHeightScale() : 0.0;
        int version = Interlocked.Increment(ref _previewGeometryUpdateVersion);

        _pendingPreviewGeometryRequest = new PreviewGeometryRenderRequest(
            version,
            GetSelectedPreviewShape(),
            displacementMap,
            heightScale);

        if (!_isPreviewGeometryWorkerRunning)
        {
            _isPreviewGeometryWorkerRunning = true;
            _ = RunPreviewGeometryWorkerAsync();
        }
    }

    private async Task RunPreviewGeometryWorkerAsync()
    {
        while (true)
        {
            PreviewGeometryRenderRequest? request = _pendingPreviewGeometryRequest;
            if (request is null)
            {
                _isPreviewGeometryWorkerRunning = false;
                return;
            }

            _pendingPreviewGeometryRequest = null;
            await RenderPreviewGeometryAsync(request.Value);

            if (_pendingPreviewGeometryRequest is not null)
            {
                await Task.Yield();
            }
        }
    }

    private async Task RenderPreviewGeometryAsync(PreviewGeometryRenderRequest request)
    {
        try
        {
            MeshGeometry3D geometry = await Task.Run(() =>
            {
                DisplacementSampler? displacementSampler = request.DisplacementMap is not null && request.HeightScale > 0.0
                    ? new DisplacementSampler(request.DisplacementMap)
                    : null;

                return CreatePreviewGeometry(request.Shape, displacementSampler, request.HeightScale);
            });

            if (request.Version != _previewGeometryUpdateVersion || _previewModel is null)
            {
                return;
            }

            _previewModel.Geometry = geometry;
            Update3DPreview();
        }
        catch (Exception exception)
        {
            if (request.Version != _previewGeometryUpdateVersion)
            {
                return;
            }

            _is3DPreviewAvailable = false;
            if (_preview3DStatusText is not null)
            {
                _preview3DStatusText.Visibility = Visibility.Visible;
            }

            ShowError($"The 3D preview geometry could not be updated.\n\n{exception.Message}");
        }
    }

    private MeshGeometry3D CreateCurrentPreviewGeometry()
    {
        DisplacementSampler? displacementSampler = null;
        double heightScale = 0.0;
        if (_useDisplacementMapCheckBox?.IsChecked == true
            && _displacementMap is not null
            && GetDisplacementHeightScale() > 0.0)
        {
            displacementSampler = new DisplacementSampler(_displacementMap);
            heightScale = GetDisplacementHeightScale();
        }

        return CreatePreviewGeometry(GetSelectedPreviewShape(), displacementSampler, heightScale);
    }

    private static MeshGeometry3D CreatePreviewGeometry(
        PreviewShape shape,
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        return shape switch
        {
            PreviewShape.Plane => CreatePreviewPlaneGeometry(displacementSampler, heightScale),
            PreviewShape.Sphere => CreatePreviewSphereGeometry(displacementSampler, heightScale),
            PreviewShape.Cylinder => CreatePreviewCylinderGeometry(displacementSampler, heightScale),
            _ => CreatePreviewCubeGeometry(displacementSampler, heightScale)
        };
    }

    private static MeshGeometry3D CreatePreviewPlaneGeometry(
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        Vector3 normal = new(0.0f, 0.0f, 1.0f);
        Vector3 tangent = new(1.0f, 0.0f, 0.0f);
        Vector3 bitangent = new(0.0f, 1.0f, 0.0f);

        for (int y = 0; y <= PreviewPlaneSubdivisions; y++)
        {
            float fy = y / (float)PreviewPlaneSubdivisions;
            float v = 1.0f - fy;
            float py = -1.0f + (2.0f * fy);

            for (int x = 0; x <= PreviewPlaneSubdivisions; x++)
            {
                float u = x / (float)PreviewPlaneSubdivisions;
                float px = -1.0f + (2.0f * u);
                Vector3 position = ApplyDisplacement(new Vector3(px, py, 0.0f), normal, displacementSampler, heightScale, u, v);

                positions.Add(position);
                textureCoordinates.Add(new Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        AddGridIndices(indices, PreviewPlaneSubdivisions, PreviewPlaneSubdivisions, reverseWinding: false);

        return new MeshGeometry3D
        {
            Positions = positions,
            TextureCoordinates = textureCoordinates,
            Indices = indices,
            Normals = normals,
            Tangents = tangents,
            BiTangents = biTangents
        };
    }

    private static MeshGeometry3D CreatePreviewCubeGeometry(
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        MeshGeometry3D geometry = new()
        {
            Positions = positions,
            TextureCoordinates = textureCoordinates,
            Indices = indices,
            Normals = normals,
            Tangents = tangents,
            BiTangents = biTangents
        };

        const float halfSize = 0.8f;
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(0.0f, 0.0f, halfSize),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            halfSize,
            displacementSampler,
            heightScale);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(0.0f, 0.0f, -halfSize),
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            halfSize,
            displacementSampler,
            heightScale);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(halfSize, 0.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            halfSize,
            displacementSampler,
            heightScale);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(-halfSize, 0.0f, 0.0f),
            new Vector3(-1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            halfSize,
            displacementSampler,
            heightScale);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(0.0f, halfSize, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, -1.0f),
            halfSize,
            displacementSampler,
            heightScale);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new Vector3(0.0f, -halfSize, 0.0f),
            new Vector3(0.0f, -1.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            halfSize,
            displacementSampler,
            heightScale);

        return geometry;
    }

    private static void AddCubeFace(
        Vector3Collection positions,
        Vector2Collection textureCoordinates,
        IntCollection indices,
        Vector3Collection normals,
        Vector3Collection tangents,
        Vector3Collection biTangents,
        Vector3 center,
        Vector3 normal,
        Vector3 tangent,
        Vector3 bitangent,
        float halfSize,
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        int startIndex = positions.Count;

        for (int y = 0; y <= PreviewCubeSubdivisions; y++)
        {
            float fy = y / (float)PreviewCubeSubdivisions;
            float v = 1.0f - fy;
            float localY = -halfSize + (2.0f * halfSize * fy);

            for (int x = 0; x <= PreviewCubeSubdivisions; x++)
            {
                float u = x / (float)PreviewCubeSubdivisions;
                float localX = -halfSize + (2.0f * halfSize * u);
                Vector3 position = center + (tangent * localX) + (bitangent * localY);
                float edgeFade = CalculateUvEdgeFade(u, v, PreviewHardEdgeFadeWidth);

                positions.Add(ApplyDisplacement(position, normal, displacementSampler, heightScale, u, v, edgeFade));
                textureCoordinates.Add(new Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        AddGridIndices(indices, PreviewCubeSubdivisions, PreviewCubeSubdivisions, startIndex, reverseWinding: false);
    }

    private static MeshGeometry3D CreatePreviewSphereGeometry(
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        const float radius = 0.85f;

        for (int latitude = 0; latitude <= PreviewSphereLatitudeSegments; latitude++)
        {
            float v = latitude / (float)PreviewSphereLatitudeSegments;
            float theta = MathF.PI * v;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int longitude = 0; longitude <= PreviewSphereLongitudeSegments; longitude++)
            {
                float u = longitude / (float)PreviewSphereLongitudeSegments;
                float phi = MathF.Tau * u;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                Vector3 normal = new(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
                Vector3 tangent = new(-sinPhi, 0.0f, cosPhi);
                Vector3 bitangent = new(cosTheta * cosPhi, -sinTheta, cosTheta * sinPhi);
                Vector3 position = ApplyDisplacement(normal * radius, normal, displacementSampler, heightScale, u, v);

                positions.Add(position);
                textureCoordinates.Add(new Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        int rowStride = PreviewSphereLongitudeSegments + 1;
        for (int latitude = 0; latitude < PreviewSphereLatitudeSegments; latitude++)
        {
            for (int longitude = 0; longitude < PreviewSphereLongitudeSegments; longitude++)
            {
                int current = (latitude * rowStride) + longitude;
                int next = current + rowStride;

                indices.Add(current);
                indices.Add(current + 1);
                indices.Add(next);
                indices.Add(current + 1);
                indices.Add(next + 1);
                indices.Add(next);
            }
        }

        return new MeshGeometry3D
        {
            Positions = positions,
            TextureCoordinates = textureCoordinates,
            Indices = indices,
            Normals = normals,
            Tangents = tangents,
            BiTangents = biTangents
        };
    }

    private static MeshGeometry3D CreatePreviewCylinderGeometry(
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        const float radius = 0.72f;
        const float halfHeight = 0.82f;

        for (int y = 0; y <= PreviewCylinderHeightSegments; y++)
        {
            float fy = y / (float)PreviewCylinderHeightSegments;
            float v = 1.0f - fy;
            float py = -halfHeight + (2.0f * halfHeight * fy);

            for (int segment = 0; segment <= PreviewCylinderSegments; segment++)
            {
                float u = segment / (float)PreviewCylinderSegments;
                float phi = MathF.Tau * u;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                Vector3 normal = new(cosPhi, 0.0f, sinPhi);
                Vector3 tangent = new(-sinPhi, 0.0f, cosPhi);
                Vector3 bitangent = new(0.0f, 1.0f, 0.0f);
                float verticalEdgeFade = CalculateLinearEdgeFade(fy, PreviewHardEdgeFadeWidth);
                Vector3 position = ApplyDisplacement(
                    new Vector3(radius * cosPhi, py, radius * sinPhi),
                    normal,
                    displacementSampler,
                    heightScale,
                    u,
                    v,
                    verticalEdgeFade);

                positions.Add(position);
                textureCoordinates.Add(new Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        int rowStride = PreviewCylinderSegments + 1;
        for (int y = 0; y < PreviewCylinderHeightSegments; y++)
        {
            for (int segment = 0; segment < PreviewCylinderSegments; segment++)
            {
                int current = (y * rowStride) + segment;
                int nextRow = current + rowStride;

                indices.Add(current);
                indices.Add(nextRow);
                indices.Add(current + 1);
                indices.Add(current + 1);
                indices.Add(nextRow);
                indices.Add(nextRow + 1);
            }
        }

        AddCylinderCap(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            halfHeight,
            radius,
            isTop: true,
            displacementSampler,
            heightScale);
        AddCylinderCap(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            -halfHeight,
            radius,
            isTop: false,
            displacementSampler,
            heightScale);

        return new MeshGeometry3D
        {
            Positions = positions,
            TextureCoordinates = textureCoordinates,
            Indices = indices,
            Normals = normals,
            Tangents = tangents,
            BiTangents = biTangents
        };
    }

    private static void AddCylinderCap(
        Vector3Collection positions,
        Vector2Collection textureCoordinates,
        IntCollection indices,
        Vector3Collection normals,
        Vector3Collection tangents,
        Vector3Collection biTangents,
        float y,
        float radius,
        bool isTop,
        DisplacementSampler? displacementSampler,
        double heightScale)
    {
        int startIndex = positions.Count;
        Vector3 normal = isTop ? new Vector3(0.0f, 1.0f, 0.0f) : new Vector3(0.0f, -1.0f, 0.0f);
        Vector3 tangent = new(1.0f, 0.0f, 0.0f);
        Vector3 bitangent = isTop ? new Vector3(0.0f, 0.0f, -1.0f) : new Vector3(0.0f, 0.0f, 1.0f);

        for (int ring = 0; ring <= PreviewCylinderCapRings; ring++)
        {
            float ringRadius = ring / (float)PreviewCylinderCapRings;

            for (int segment = 0; segment <= PreviewCylinderSegments; segment++)
            {
                float angle = segment / (float)PreviewCylinderSegments;
                float phi = MathF.Tau * angle;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);
                float u = (cosPhi * ringRadius * 0.5f) + 0.5f;
                float v = (sinPhi * ringRadius * 0.5f) + 0.5f;
                Vector3 position = new(radius * ringRadius * cosPhi, y, radius * ringRadius * sinPhi);

                float edgeFade = CalculateRadialEdgeFade(ringRadius, PreviewHardEdgeFadeWidth);
                positions.Add(ApplyDisplacement(position, normal, displacementSampler, heightScale, u, v, edgeFade));
                textureCoordinates.Add(new Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        int rowStride = PreviewCylinderSegments + 1;
        for (int ring = 0; ring < PreviewCylinderCapRings; ring++)
        {
            for (int segment = 0; segment < PreviewCylinderSegments; segment++)
            {
                int innerCurrent = startIndex + (ring * rowStride) + segment;
                int innerNext = innerCurrent + 1;
                int outerCurrent = innerCurrent + rowStride;
                int outerNext = outerCurrent + 1;

                if (isTop)
                {
                    indices.Add(innerCurrent);
                    indices.Add(outerNext);
                    indices.Add(outerCurrent);
                    indices.Add(innerCurrent);
                    indices.Add(innerNext);
                    indices.Add(outerNext);
                }
                else
                {
                    indices.Add(innerCurrent);
                    indices.Add(outerCurrent);
                    indices.Add(outerNext);
                    indices.Add(innerCurrent);
                    indices.Add(outerNext);
                    indices.Add(innerNext);
                }
            }
        }
    }

    private static void AddGridIndices(
        IntCollection indices,
        int columns,
        int rows,
        int startIndex = 0,
        bool reverseWinding = false)
    {
        int rowStride = columns + 1;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < columns; x++)
            {
                int current = startIndex + (y * rowStride) + x;
                int next = current + rowStride;

                if (reverseWinding)
                {
                    indices.Add(current);
                    indices.Add(next + 1);
                    indices.Add(current + 1);
                    indices.Add(current);
                    indices.Add(next);
                    indices.Add(next + 1);
                }
                else
                {
                    indices.Add(current);
                    indices.Add(current + 1);
                    indices.Add(next + 1);
                    indices.Add(current);
                    indices.Add(next + 1);
                    indices.Add(next);
                }
            }
        }
    }

    private static Vector3 ApplyDisplacement(
        Vector3 position,
        Vector3 normal,
        DisplacementSampler? displacementSampler,
        double heightScale,
        float u,
        float v,
        float displacementWeight = 1.0f)
    {
        if (displacementSampler is null || heightScale <= 0.0 || displacementWeight <= 0.0f)
        {
            return position;
        }

        double height = displacementSampler.Sample(u, v);
        return position + (normal * (float)((height - 0.5) * heightScale * displacementWeight));
    }

    private static float CalculateUvEdgeFade(float u, float v, float fadeWidth)
    {
        return MathF.Min(
            CalculateLinearEdgeFade(u, fadeWidth),
            CalculateLinearEdgeFade(v, fadeWidth));
    }

    private static float CalculateLinearEdgeFade(float value, float fadeWidth)
    {
        if (fadeWidth <= 0.0f)
        {
            return 1.0f;
        }

        return Math.Clamp(MathF.Min(value, 1.0f - value) / fadeWidth, 0.0f, 1.0f);
    }

    private static float CalculateRadialEdgeFade(float radius, float fadeWidth)
    {
        if (fadeWidth <= 0.0f)
        {
            return 1.0f;
        }

        return Math.Clamp((1.0f - radius) / fadeWidth, 0.0f, 1.0f);
    }

    private sealed class DisplacementSampler
    {
        private readonly byte[] _pixels;
        private readonly int _width;
        private readonly int _height;
        private readonly int _stride;

        public DisplacementSampler(BitmapSource bitmap)
        {
            BitmapSource source = bitmap.Format == PixelFormats.Bgra32
                ? bitmap
                : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);

            _width = source.PixelWidth;
            _height = source.PixelHeight;
            _stride = _width * 4;
            _pixels = new byte[_stride * _height];
            source.CopyPixels(_pixels, _stride, 0);
        }

        public double Sample(float u, float v)
        {
            if (_width <= 0 || _height <= 0)
            {
                return 0.5;
            }

            double clampedU = Math.Clamp(u, 0.0f, 1.0f);
            double clampedV = Math.Clamp(v, 0.0f, 1.0f);
            int x = (int)Math.Round(clampedU * (_width - 1));
            int y = (int)Math.Round(clampedV * (_height - 1));
            int index = (y * _stride) + (x * 4);

            return _pixels[index] / 255.0;
        }
    }

    private readonly record struct NormalMapGenerationSettings(
        double Strength,
        double Level,
        double BlurSharp,
        HeightChannelSource ChannelSource,
        NormalMapEdgeMode EdgeMode,
        bool InvertX,
        bool InvertY);

    private readonly record struct DisplacementMapGenerationSettings(
        double Level,
        double BlurSharp,
        HeightChannelSource ChannelSource,
        NormalMapEdgeMode EdgeMode,
        bool Invert);

    private static TextureModel CreateTextureModel(BitmapSource bitmap)
    {
        MemoryStream stream = new();
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        stream.Position = 0;
        return new TextureModel(stream, autoCloseStream: true);
    }

    private readonly record struct NormalPreviewRenderRequest(
        int Version,
        BitmapSource SourceImage,
        double Strength,
        double Level,
        double BlurSharp,
        HeightChannelSource ChannelSource,
        NormalMapEdgeMode EdgeMode,
        bool InvertX,
        bool InvertY);

    private readonly record struct DisplacementPreviewRenderRequest(
        int Version,
        BitmapSource SourceImage,
        double Level,
        double BlurSharp,
        HeightChannelSource ChannelSource,
        NormalMapEdgeMode EdgeMode,
        bool Invert);

    private readonly record struct PreviewGeometryRenderRequest(
        int Version,
        PreviewShape Shape,
        BitmapSource? DisplacementMap,
        double HeightScale);

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

    private void UpdateLevelText()
    {
        if (_levelValueText is null || _levelSlider is null)
        {
            return;
        }

        if (_levelValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetLevelText(_levelSlider.Value);
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

    private void UpdateDisplacementLevelText()
    {
        if (_displacementLevelValueText is null || _displacementLevelSlider is null)
        {
            return;
        }

        if (_displacementLevelValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetDisplacementLevelText(_displacementLevelSlider.Value);
    }

    private void UpdateDisplacementBlurSharpText()
    {
        if (_displacementBlurSharpValueText is null || _displacementBlurSharpSlider is null)
        {
            return;
        }

        if (_displacementBlurSharpValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetDisplacementBlurSharpText(_displacementBlurSharpSlider.Value);
    }

    private void UpdateDisplacementHeightScaleText()
    {
        if (_displacementHeightScaleValueText is null || _displacementHeightScaleSlider is null)
        {
            return;
        }

        if (_displacementHeightScaleValueText.IsKeyboardFocusWithin)
        {
            return;
        }

        SetDisplacementHeightScaleText(_displacementHeightScaleSlider.Value);
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

    private void CommitLevelText()
    {
        if (_levelValueText is null || _levelSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_levelValueText.Text, out double level))
        {
            _levelSlider.Value = Math.Clamp(level, _levelSlider.Minimum, _levelSlider.Maximum);
        }

        SetLevelText(_levelSlider.Value);
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

    private void CommitDisplacementLevelText()
    {
        if (_displacementLevelValueText is null || _displacementLevelSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_displacementLevelValueText.Text, out double level))
        {
            _displacementLevelSlider.Value = Math.Clamp(level, _displacementLevelSlider.Minimum, _displacementLevelSlider.Maximum);
        }

        SetDisplacementLevelText(_displacementLevelSlider.Value);
    }

    private void CommitDisplacementBlurSharpText()
    {
        if (_displacementBlurSharpValueText is null || _displacementBlurSharpSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_displacementBlurSharpValueText.Text, out double blurSharp))
        {
            _displacementBlurSharpSlider.Value = Math.Clamp(blurSharp, _displacementBlurSharpSlider.Minimum, _displacementBlurSharpSlider.Maximum);
        }

        SetDisplacementBlurSharpText(_displacementBlurSharpSlider.Value);
    }

    private void CommitDisplacementHeightScaleText()
    {
        if (_displacementHeightScaleValueText is null || _displacementHeightScaleSlider is null)
        {
            return;
        }

        if (TryParseSliderValue(_displacementHeightScaleValueText.Text, out double heightScale))
        {
            _displacementHeightScaleSlider.Value = Math.Clamp(heightScale, _displacementHeightScaleSlider.Minimum, _displacementHeightScaleSlider.Maximum);
        }

        SetDisplacementHeightScaleText(_displacementHeightScaleSlider.Value);
    }

    private void SetStrengthText(double value)
    {
        if (_strengthValueText is null)
        {
            return;
        }

        _strengthValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetLevelText(double value)
    {
        if (_levelValueText is null)
        {
            return;
        }

        _levelValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetBlurSharpText(double value)
    {
        if (_blurSharpValueText is null)
        {
            return;
        }

        _blurSharpValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetDisplacementLevelText(double value)
    {
        if (_displacementLevelValueText is null)
        {
            return;
        }

        _displacementLevelValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetDisplacementBlurSharpText(double value)
    {
        if (_displacementBlurSharpValueText is null)
        {
            return;
        }

        _displacementBlurSharpValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void SetDisplacementHeightScaleText(double value)
    {
        if (_displacementHeightScaleValueText is null)
        {
            return;
        }

        _displacementHeightScaleValueText.Text = value.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private void BindNamedControls()
    {
        _sourcePreviewImage = FindRequiredControl<Image>("SourcePreviewImage");
        _generatedMapPreviewImage = FindRequiredControl<Image>("GeneratedMapPreviewImage");
        _generatedMapPreviewTitle = FindRequiredControl<TextBlock>("GeneratedMapPreviewTitle");
        _exportMapButton = FindRequiredControl<Button>("ExportMapButton");
        _mapSettingsTabControl = FindRequiredControl<TabControl>("MapSettingsTabControl");
        _strengthSlider = FindRequiredControl<Slider>("StrengthSlider");
        _strengthValueText = FindRequiredControl<TextBox>("StrengthValueText");
        _levelSlider = FindRequiredControl<Slider>("LevelSlider");
        _levelValueText = FindRequiredControl<TextBox>("LevelValueText");
        _blurSharpSlider = FindRequiredControl<Slider>("BlurSharpSlider");
        _blurSharpValueText = FindRequiredControl<TextBox>("BlurSharpValueText");
        _channelSourceComboBox = FindRequiredControl<ComboBox>("ChannelSourceComboBox");
        _edgeModeComboBox = FindRequiredControl<ComboBox>("EdgeModeComboBox");
        _invertXCheckBox = FindRequiredControl<CheckBox>("InvertXCheckBox");
        _invertYCheckBox = FindRequiredControl<CheckBox>("InvertYCheckBox");
        _displacementLevelSlider = FindRequiredControl<Slider>("DisplacementLevelSlider");
        _displacementLevelValueText = FindRequiredControl<TextBox>("DisplacementLevelValueText");
        _displacementBlurSharpSlider = FindRequiredControl<Slider>("DisplacementBlurSharpSlider");
        _displacementBlurSharpValueText = FindRequiredControl<TextBox>("DisplacementBlurSharpValueText");
        _displacementHeightScaleSlider = FindRequiredControl<Slider>("DisplacementHeightScaleSlider");
        _displacementHeightScaleValueText = FindRequiredControl<TextBox>("DisplacementHeightScaleValueText");
        _displacementChannelSourceComboBox = FindRequiredControl<ComboBox>("DisplacementChannelSourceComboBox");
        _displacementEdgeModeComboBox = FindRequiredControl<ComboBox>("DisplacementEdgeModeComboBox");
        _invertDisplacementCheckBox = FindRequiredControl<CheckBox>("InvertDisplacementCheckBox");
        _useNormalMapCheckBox = FindRequiredControl<CheckBox>("UseNormalMapCheckBox");
        _useDisplacementMapCheckBox = FindRequiredControl<CheckBox>("UseDisplacementMapCheckBox");
        _useHeightmapAlbedoCheckBox = FindRequiredControl<CheckBox>("UseHeightmapAlbedoCheckBox");
        _previewShapeComboBox = FindRequiredControl<ComboBox>("PreviewShapeComboBox");
        _preview3DHost = FindRequiredControl<ContentControl>("Preview3DHost");
        _preview3DStatusText = FindRequiredControl<TextBlock>("Preview3DStatusText");
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
        string suffix = IsDisplacementTabActive() ? "displacement" : "normal";
        if (string.IsNullOrWhiteSpace(_sourceFilePath))
        {
            return $"{suffix}_map.png";
        }

        return $"{Path.GetFileNameWithoutExtension(_sourceFilePath)}_{suffix}.png";
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

    private static BitmapSource CreatePreviewBitmap(BitmapSource source)
    {
        int largestDimension = Math.Max(source.PixelWidth, source.PixelHeight);
        if (largestDimension <= MaxPreviewBitmapDimension)
        {
            return source;
        }

        double scale = MaxPreviewBitmapDimension / (double)largestDimension;
        TransformedBitmap preview = new(source, new ScaleTransform(scale, scale));
        preview.Freeze();
        return preview;
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

    private enum PreviewShape
    {
        Plane,
        Cube,
        Sphere,
        Cylinder
    }
}
