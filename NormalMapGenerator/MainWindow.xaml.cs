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

namespace NormalMapGenerator;

public partial class MainWindow : Window
{
    private const double DefaultStrength = 5.0;
    private const double DefaultLevel = 1.0;
    private const double DefaultBlurSharp = 0.0;

    private BitmapSource? _sourceImage;
    private BitmapSource? _normalMap;
    private string? _sourceFilePath;
    private Image? _sourcePreviewImage;
    private Image? _normalPreviewImage;
    private Button? _exportNormalMapButton;
    private Slider? _strengthSlider;
    private TextBox? _strengthValueText;
    private Slider? _levelSlider;
    private TextBox? _levelValueText;
    private Slider? _blurSharpSlider;
    private TextBox? _blurSharpValueText;
    private CheckBox? _invertXCheckBox;
    private CheckBox? _invertYCheckBox;
    private CheckBox? _useHeightmapAlbedoCheckBox;
    private ComboBox? _previewShapeComboBox;
    private ContentControl? _preview3DHost;
    private Viewport3DX? _previewViewport3D;
    private TextBlock? _preview3DStatusText;
    private DefaultEffectsManager? _previewEffectsManager;
    private MeshGeometryModel3D? _previewModel;
    private PhongMaterial? _previewMaterial;
    private bool _is3DPreviewAvailable;
    private PreviewRenderRequest? _pendingPreviewRequest;
    private bool _isPreviewWorkerRunning;
    private int _previewUpdateVersion;

    public MainWindow()
    {
        InitializeComponent();
        BindNamedControls();
        Initialize3DPreview();
        UpdateStrengthText();
        UpdateLevelText();
        UpdateBlurSharpText();
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
        UpdateLevelText();
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

    private void ScheduleNormalMapRegeneration()
    {
        if (_sourceImage is null)
        {
            _normalMap = null;
            Update3DPreview(null);

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
            || _levelSlider is null
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
        double level = _levelSlider.Value;
        double blurSharp = _blurSharpSlider.Value;
        bool invertX = _invertXCheckBox.IsChecked == true;
        bool invertY = _invertYCheckBox.IsChecked == true;
        int version = Interlocked.Increment(ref _previewUpdateVersion);

        _exportNormalMapButton.IsEnabled = false;

        _pendingPreviewRequest = new PreviewRenderRequest(
            version,
            sourceImage,
            strength,
            level,
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
                    request.Level,
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
            Update3DPreview(normalMap);
            _exportNormalMapButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            if (request.Version != _previewUpdateVersion || !ReferenceEquals(_sourceImage, request.SourceImage))
            {
                return;
            }

            _normalMap = null;
            Update3DPreview(null);

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

    private void UseHeightmapAlbedoChanged(object sender, RoutedEventArgs e)
    {
        Update3DPreview(_normalMap);
    }

    private void PreviewShapeChanged(object sender, RoutedEventArgs e)
    {
        if (_previewModel is null)
        {
            return;
        }

        _previewModel.Geometry = CreatePreviewGeometry(GetSelectedPreviewShape());
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
                Geometry = CreatePreviewGeometry(GetSelectedPreviewShape()),
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

    private void Update3DPreview(BitmapSource? normalMap)
    {
        if (!_is3DPreviewAvailable || _previewMaterial is null || _previewModel is null)
        {
            return;
        }

        try
        {
            if (normalMap is null)
            {
                _previewMaterial.NormalMap = null;
                _previewMaterial.RenderNormalMap = false;
                _previewMaterial.DiffuseMap = null;
                _previewMaterial.RenderDiffuseMap = false;
                _previewModel.Visibility = Visibility.Hidden;
                return;
            }

            _previewMaterial.NormalMap = CreateTextureModel(normalMap);
            _previewMaterial.RenderNormalMap = true;

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
            RenderNormalMap = false
        };
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

    private static MeshGeometry3D CreatePreviewGeometry(PreviewShape shape)
    {
        return shape switch
        {
            PreviewShape.Plane => CreatePreviewPlaneGeometry(),
            PreviewShape.Sphere => CreatePreviewSphereGeometry(),
            PreviewShape.Cylinder => CreatePreviewCylinderGeometry(),
            _ => CreatePreviewCubeGeometry()
        };
    }

    private static MeshGeometry3D CreatePreviewPlaneGeometry()
    {
        return new MeshGeometry3D
        {
            Positions = new Vector3Collection
            {
                new(-1.0f, -1.0f, 0.0f),
                new(1.0f, -1.0f, 0.0f),
                new(1.0f, 1.0f, 0.0f),
                new(-1.0f, 1.0f, 0.0f)
            },
            TextureCoordinates = new Vector2Collection
            {
                new(0.0f, 1.0f),
                new(1.0f, 1.0f),
                new(1.0f, 0.0f),
                new(0.0f, 0.0f)
            },
            Indices = new IntCollection { 0, 1, 2, 0, 2, 3 },
            Normals = new Vector3Collection
            {
                new(0.0f, 0.0f, 1.0f),
                new(0.0f, 0.0f, 1.0f),
                new(0.0f, 0.0f, 1.0f),
                new(0.0f, 0.0f, 1.0f)
            },
            Tangents = new Vector3Collection
            {
                new(1.0f, 0.0f, 0.0f),
                new(1.0f, 0.0f, 0.0f),
                new(1.0f, 0.0f, 0.0f),
                new(1.0f, 0.0f, 0.0f)
            },
            BiTangents = new Vector3Collection
            {
                new(0.0f, 1.0f, 0.0f),
                new(0.0f, 1.0f, 0.0f),
                new(0.0f, 1.0f, 0.0f),
                new(0.0f, 1.0f, 0.0f)
            }
        };
    }

    private static MeshGeometry3D CreatePreviewCubeGeometry()
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
            new System.Numerics.Vector3(0.0f, 0.0f, halfSize),
            new System.Numerics.Vector3(0.0f, 0.0f, 1.0f),
            new System.Numerics.Vector3(1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 1.0f, 0.0f),
            halfSize);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new System.Numerics.Vector3(0.0f, 0.0f, -halfSize),
            new System.Numerics.Vector3(0.0f, 0.0f, -1.0f),
            new System.Numerics.Vector3(-1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 1.0f, 0.0f),
            halfSize);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new System.Numerics.Vector3(halfSize, 0.0f, 0.0f),
            new System.Numerics.Vector3(1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 0.0f, -1.0f),
            new System.Numerics.Vector3(0.0f, 1.0f, 0.0f),
            halfSize);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new System.Numerics.Vector3(-halfSize, 0.0f, 0.0f),
            new System.Numerics.Vector3(-1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 0.0f, 1.0f),
            new System.Numerics.Vector3(0.0f, 1.0f, 0.0f),
            halfSize);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new System.Numerics.Vector3(0.0f, halfSize, 0.0f),
            new System.Numerics.Vector3(0.0f, 1.0f, 0.0f),
            new System.Numerics.Vector3(1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 0.0f, -1.0f),
            halfSize);
        AddCubeFace(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            new System.Numerics.Vector3(0.0f, -halfSize, 0.0f),
            new System.Numerics.Vector3(0.0f, -1.0f, 0.0f),
            new System.Numerics.Vector3(1.0f, 0.0f, 0.0f),
            new System.Numerics.Vector3(0.0f, 0.0f, 1.0f),
            halfSize);

        return geometry;
    }

    private static void AddCubeFace(
        Vector3Collection positions,
        Vector2Collection textureCoordinates,
        IntCollection indices,
        Vector3Collection normals,
        Vector3Collection tangents,
        Vector3Collection biTangents,
        System.Numerics.Vector3 center,
        System.Numerics.Vector3 normal,
        System.Numerics.Vector3 tangent,
        System.Numerics.Vector3 bitangent,
        float halfSize)
    {
        int startIndex = positions.Count;
        positions.Add(center - (tangent * halfSize) - (bitangent * halfSize));
        positions.Add(center + (tangent * halfSize) - (bitangent * halfSize));
        positions.Add(center + (tangent * halfSize) + (bitangent * halfSize));
        positions.Add(center - (tangent * halfSize) + (bitangent * halfSize));

        textureCoordinates.Add(new System.Numerics.Vector2(0.0f, 1.0f));
        textureCoordinates.Add(new System.Numerics.Vector2(1.0f, 1.0f));
        textureCoordinates.Add(new System.Numerics.Vector2(1.0f, 0.0f));
        textureCoordinates.Add(new System.Numerics.Vector2(0.0f, 0.0f));

        indices.Add(startIndex);
        indices.Add(startIndex + 1);
        indices.Add(startIndex + 2);
        indices.Add(startIndex);
        indices.Add(startIndex + 2);
        indices.Add(startIndex + 3);

        for (int i = 0; i < 4; i++)
        {
            normals.Add(normal);
            tangents.Add(tangent);
            biTangents.Add(bitangent);
        }
    }

    private static MeshGeometry3D CreatePreviewSphereGeometry()
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        const float radius = 0.85f;
        const int latitudeSegments = 24;
        const int longitudeSegments = 48;

        for (int latitude = 0; latitude <= latitudeSegments; latitude++)
        {
            float v = latitude / (float)latitudeSegments;
            float theta = MathF.PI * v;
            float sinTheta = MathF.Sin(theta);
            float cosTheta = MathF.Cos(theta);

            for (int longitude = 0; longitude <= longitudeSegments; longitude++)
            {
                float u = longitude / (float)longitudeSegments;
                float phi = MathF.Tau * u;
                float sinPhi = MathF.Sin(phi);
                float cosPhi = MathF.Cos(phi);

                System.Numerics.Vector3 normal = new(sinTheta * cosPhi, cosTheta, sinTheta * sinPhi);
                System.Numerics.Vector3 tangent = new(-sinPhi, 0.0f, cosPhi);
                System.Numerics.Vector3 bitangent = new(cosTheta * cosPhi, -sinTheta, cosTheta * sinPhi);

                positions.Add(normal * radius);
                textureCoordinates.Add(new System.Numerics.Vector2(u, v));
                normals.Add(normal);
                tangents.Add(tangent);
                biTangents.Add(bitangent);
            }
        }

        int rowStride = longitudeSegments + 1;
        for (int latitude = 0; latitude < latitudeSegments; latitude++)
        {
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
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

    private static MeshGeometry3D CreatePreviewCylinderGeometry()
    {
        Vector3Collection positions = new();
        Vector2Collection textureCoordinates = new();
        IntCollection indices = new();
        Vector3Collection normals = new();
        Vector3Collection tangents = new();
        Vector3Collection biTangents = new();

        const float radius = 0.72f;
        const float halfHeight = 0.82f;
        const int segments = 48;

        for (int segment = 0; segment <= segments; segment++)
        {
            float u = segment / (float)segments;
            float phi = MathF.Tau * u;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            System.Numerics.Vector3 normal = new(cosPhi, 0.0f, sinPhi);
            System.Numerics.Vector3 tangent = new(-sinPhi, 0.0f, cosPhi);
            System.Numerics.Vector3 bitangent = new(0.0f, 1.0f, 0.0f);

            positions.Add(new System.Numerics.Vector3(radius * cosPhi, -halfHeight, radius * sinPhi));
            textureCoordinates.Add(new System.Numerics.Vector2(u, 1.0f));
            normals.Add(normal);
            tangents.Add(tangent);
            biTangents.Add(bitangent);

            positions.Add(new System.Numerics.Vector3(radius * cosPhi, halfHeight, radius * sinPhi));
            textureCoordinates.Add(new System.Numerics.Vector2(u, 0.0f));
            normals.Add(normal);
            tangents.Add(tangent);
            biTangents.Add(bitangent);
        }

        for (int segment = 0; segment < segments; segment++)
        {
            int currentBottom = segment * 2;
            int currentTop = currentBottom + 1;
            int nextBottom = currentBottom + 2;
            int nextTop = currentBottom + 3;

            indices.Add(currentBottom);
            indices.Add(currentTop);
            indices.Add(nextBottom);
            indices.Add(currentTop);
            indices.Add(nextTop);
            indices.Add(nextBottom);
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
            segments,
            isTop: true);
        AddCylinderCap(
            positions,
            textureCoordinates,
            indices,
            normals,
            tangents,
            biTangents,
            -halfHeight,
            radius,
            segments,
            isTop: false);

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
        int segments,
        bool isTop)
    {
        int centerIndex = positions.Count;
        System.Numerics.Vector3 normal = isTop
            ? new System.Numerics.Vector3(0.0f, 1.0f, 0.0f)
            : new System.Numerics.Vector3(0.0f, -1.0f, 0.0f);
        System.Numerics.Vector3 tangent = new(1.0f, 0.0f, 0.0f);
        System.Numerics.Vector3 bitangent = isTop
            ? new System.Numerics.Vector3(0.0f, 0.0f, -1.0f)
            : new System.Numerics.Vector3(0.0f, 0.0f, 1.0f);

        positions.Add(new System.Numerics.Vector3(0.0f, y, 0.0f));
        textureCoordinates.Add(new System.Numerics.Vector2(0.5f, 0.5f));
        normals.Add(normal);
        tangents.Add(tangent);
        biTangents.Add(bitangent);

        for (int segment = 0; segment <= segments; segment++)
        {
            float u = segment / (float)segments;
            float phi = MathF.Tau * u;
            float sinPhi = MathF.Sin(phi);
            float cosPhi = MathF.Cos(phi);

            positions.Add(new System.Numerics.Vector3(radius * cosPhi, y, radius * sinPhi));
            textureCoordinates.Add(new System.Numerics.Vector2((cosPhi * 0.5f) + 0.5f, (sinPhi * 0.5f) + 0.5f));
            normals.Add(normal);
            tangents.Add(tangent);
            biTangents.Add(bitangent);
        }

        for (int segment = 0; segment < segments; segment++)
        {
            int current = centerIndex + 1 + segment;
            int next = current + 1;

            indices.Add(centerIndex);
            if (isTop)
            {
                indices.Add(next);
                indices.Add(current);
            }
            else
            {
                indices.Add(current);
                indices.Add(next);
            }
        }
    }

    private static TextureModel CreateTextureModel(BitmapSource bitmap)
    {
        MemoryStream stream = new();
        PngBitmapEncoder encoder = new();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        encoder.Save(stream);
        stream.Position = 0;
        return new TextureModel(stream, autoCloseStream: true);
    }

    private readonly record struct PreviewRenderRequest(
        int Version,
        BitmapSource SourceImage,
        double Strength,
        double Level,
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

    private void BindNamedControls()
    {
        _sourcePreviewImage = FindRequiredControl<Image>("SourcePreviewImage");
        _normalPreviewImage = FindRequiredControl<Image>("NormalPreviewImage");
        _exportNormalMapButton = FindRequiredControl<Button>("ExportNormalMapButton");
        _strengthSlider = FindRequiredControl<Slider>("StrengthSlider");
        _strengthValueText = FindRequiredControl<TextBox>("StrengthValueText");
        _levelSlider = FindRequiredControl<Slider>("LevelSlider");
        _levelValueText = FindRequiredControl<TextBox>("LevelValueText");
        _blurSharpSlider = FindRequiredControl<Slider>("BlurSharpSlider");
        _blurSharpValueText = FindRequiredControl<TextBox>("BlurSharpValueText");
        _invertXCheckBox = FindRequiredControl<CheckBox>("InvertXCheckBox");
        _invertYCheckBox = FindRequiredControl<CheckBox>("InvertYCheckBox");
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

    private enum PreviewShape
    {
        Plane,
        Cube,
        Sphere,
        Cylinder
    }
}
