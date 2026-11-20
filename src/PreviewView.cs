// Interface WPF: visualizador 3D com navegação entre plates.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;

namespace Bambu3DPreview
{
    public class Theme
    {
        public Color Back, Panel, Text, Muted, Accent, Hover;
        public static Theme Detect()
        {
            bool light = false;
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) light = (int)v != 0;
                }
            }
            catch { }
            return light ? Light() : Dark();
        }
        public static Theme Dark()
        {
            return new Theme
            {
                Back = Color.FromRgb(0x1C, 0x1C, 0x1C), Panel = Color.FromRgb(0x2A, 0x2A, 0x2A),
                Text = Color.FromRgb(0xF0, 0xF0, 0xF0), Muted = Color.FromRgb(0x9A, 0x9A, 0x9A),
                Accent = Color.FromRgb(0x00, 0xAE, 0x42), Hover = Color.FromRgb(0x3A, 0x3A, 0x3A)
            };
        }
        public static Theme Light()
        {
            return new Theme
            {
                Back = Color.FromRgb(0xF7, 0xF7, 0xF7), Panel = Color.FromRgb(0xE9, 0xE9, 0xE9),
                Text = Color.FromRgb(0x1A, 0x1A, 0x1A), Muted = Color.FromRgb(0x66, 0x66, 0x66),
                Accent = Color.FromRgb(0x00, 0x8F, 0x36), Hover = Color.FromRgb(0xD6, 0xD6, 0xD6)
            };
        }
    }

    public class PlateGeometry
    {
        public Model3DGroup Model;
        public Rect3D Bounds;
        public int Triangles;
        public bool Decimated;
    }

    public class PreviewView : Grid
    {
        public const int MaxTriangles = 800000;

        readonly Theme theme;
        readonly Viewport3D viewport = new Viewport3D();
        readonly PerspectiveCamera camera = new PerspectiveCamera { FieldOfView = 40, UpDirection = new Vector3D(0, 0, 1) };
        readonly ModelVisual3D modelVisual = new ModelVisual3D();
        readonly DirectionalLight headLight = new DirectionalLight(Color.FromRgb(0xB8, 0xB8, 0xB8), new Vector3D(0, 0, -1));
        readonly Image image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(8) };
        readonly TextBlock status = new TextBlock();
        readonly TextBlock plateLabel = new TextBlock();
        readonly TextBlock info = new TextBlock();
        readonly TextBlock objects = new TextBlock();
        readonly Border prevBtn, nextBtn, modeBtn;
        readonly Grid stage = new Grid();

        SceneData scene;
        int plateIndex;
        bool showImage;
        volatile int generation;
        readonly Dictionary<int, PlateGeometry> cache = new Dictionary<int, PlateGeometry>();

        // câmera orbital
        Point3D target; double distance = 100, azimuth = -55, elevation = 28;
        Point3D homeTarget; double homeDistance = 100;
        Point lastMouse; bool rotating, panning;

        public PreviewView() : this(Theme.Detect()) { }

        public PreviewView(Theme t)
        {
            theme = t;
            Background = new SolidColorBrush(t.Back);
            TextElement.SetForeground(this, new SolidColorBrush(t.Text));
            TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
            TextElement.SetFontSize(this, 12.5);
            SnapsToDevicePixels = true;

            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // ---- barra superior ----
            var top = new Grid { Background = new SolidColorBrush(t.Panel) };
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            prevBtn = MakeButton("◀", () => ChangePlate(-1));
            nextBtn = MakeButton("▶", () => ChangePlate(1));
            modeBtn = MakeButton("Miniatura", ToggleMode);
            modeBtn.Margin = new Thickness(10, 4, 6, 4);
            plateLabel.FontWeight = FontWeights.SemiBold;
            plateLabel.FontSize = 13.5;
            plateLabel.HorizontalAlignment = HorizontalAlignment.Center;
            plateLabel.VerticalAlignment = VerticalAlignment.Center;
            plateLabel.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetColumn(prevBtn, 0); Grid.SetColumn(plateLabel, 1); Grid.SetColumn(nextBtn, 2); Grid.SetColumn(modeBtn, 3);
            top.Children.Add(prevBtn); top.Children.Add(plateLabel); top.Children.Add(nextBtn); top.Children.Add(modeBtn);
            Grid.SetRow(top, 0);
            Children.Add(top);

            // ---- área 3D ----
            viewport.Camera = camera;
            var lights = new Model3DGroup();
            lights.Children.Add(new AmbientLight(Color.FromRgb(0x58, 0x58, 0x58)));
            lights.Children.Add(headLight);
            lights.Children.Add(new DirectionalLight(Color.FromRgb(0x50, 0x50, 0x50), new Vector3D(0.4, 0.6, -1)));
            viewport.Children.Add(new ModelVisual3D { Content = lights });
            viewport.Children.Add(modelVisual);
            RenderOptions.SetEdgeMode(viewport, EdgeMode.Unspecified);

            stage.Background = Brushes.Transparent; // captura o mouse
            stage.ClipToBounds = true;
            stage.Children.Add(viewport);
            image.Visibility = Visibility.Collapsed;
            stage.Children.Add(image);
            status.HorizontalAlignment = HorizontalAlignment.Center;
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Foreground = new SolidColorBrush(t.Muted);
            status.FontSize = 14;
            status.TextWrapping = TextWrapping.Wrap;
            status.TextAlignment = TextAlignment.Center;
            status.Margin = new Thickness(16);
            stage.Children.Add(status);
            var hint = new TextBlock
            {
                Text = "arrastar: girar · botão direito: mover · roda: zoom · duplo clique: centralizar",
                Foreground = new SolidColorBrush(Color.FromArgb(0x90, t.Muted.R, t.Muted.G, t.Muted.B)),
                FontSize = 10.5, Margin = new Thickness(8, 0, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stage.Children.Add(hint);
            stage.MouseDown += OnMouseDown;
            stage.MouseUp += OnMouseUp;
            stage.MouseMove += OnMouseMove;
            stage.MouseWheel += OnWheel;
            Grid.SetRow(stage, 1);
            Children.Add(stage);

            // ---- rodapé ----
            var bottom = new StackPanel { Background = new SolidColorBrush(t.Panel) };
            info.Margin = new Thickness(10, 6, 10, 0);
            info.TextTrimming = TextTrimming.CharacterEllipsis;
            objects.Margin = new Thickness(10, 2, 10, 7);
            objects.Foreground = new SolidColorBrush(t.Muted);
            objects.TextWrapping = TextWrapping.Wrap;
            objects.MaxHeight = 52;
            objects.TextTrimming = TextTrimming.CharacterEllipsis;
            bottom.Children.Add(info);
            bottom.Children.Add(objects);
            Grid.SetRow(bottom, 2);
            Children.Add(bottom);

            Focusable = true;
            KeyDown += OnKey;
            UpdateCamera();
            SetPlateControls();
        }

        Border MakeButton(string text, Action click)
        {
            var b = new Border
            {
                CornerRadius = new CornerRadius(4), Padding = new Thickness(12, 5, 12, 5), Margin = new Thickness(4),
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                Child = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center }
            };
            var hover = new SolidColorBrush(theme.Hover);
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = Brushes.Transparent;
            b.MouseLeftButtonUp += (s, e) => { if (b.IsEnabled) click(); e.Handled = true; };
            return b;
        }

        static void SetButtonText(Border b, string text) { ((TextBlock)b.Child).Text = text; }

        // ---------------- carregamento ----------------
        public void Cancel() { generation++; }

        public void LoadAsync(string path, byte[] data)
        {
            int gen = ++generation;
            scene = null; cache.Clear(); plateIndex = 0;
            ShowStatus("Carregando…");
            SetPlateControls();
            var th = new Thread(() =>
            {
                try
                {
                    var sc = ModelLoader.Load(path, data, () => gen != generation);
                    if (gen != generation) return;
                    // primeiro mostra a miniatura enquanto monta a malha 3D
                    Dispatcher.BeginInvoke(new Action(() => { if (gen == generation) OnSceneLoaded(sc); }));
                    var g = BuildPlate(sc.Plates[0]);
                    Dispatcher.BeginInvoke(new Action(() => { if (gen == generation) OnGeometry(0, g); }));
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Dispatcher.BeginInvoke(new Action(() => { if (gen == generation) ShowStatus("Não foi possível ler o arquivo:\n" + ex.Message); }));
                }
            });
            th.IsBackground = true;
            th.SetApartmentState(ApartmentState.STA);
            th.Start();
        }

        /// <summary>Carrega de forma síncrona (usado para gerar imagens de teste).</summary>
        public void LoadSync(string path, int plate)
        {
            var sc = ModelLoader.Load(path, null, () => false);
            OnSceneLoaded(sc);
            plateIndex = Math.Max(0, Math.Min(plate, sc.Plates.Count - 1));
            SetPlateControls();
            OnGeometry(plateIndex, BuildPlate(sc.Plates[plateIndex]));
        }

        void OnSceneLoaded(SceneData sc)
        {
            scene = sc;
            SetPlateControls();
            ShowCurrent();
        }

        void OnGeometry(int index, PlateGeometry g)
        {
            cache[index] = g;
            if (index == plateIndex) ShowCurrent();
        }

        void ChangePlate(int delta)
        {
            if (scene == null || scene.Plates.Count < 2) return;
            int n = scene.Plates.Count;
            plateIndex = (plateIndex + delta + n) % n;
            SetPlateControls();
            ShowCurrent();
            if (!cache.ContainsKey(plateIndex))
            {
                int gen = generation, idx = plateIndex;
                var plate = scene.Plates[idx];
                var th = new Thread(() =>
                {
                    try
                    {
                        var g = BuildPlate(plate);
                        Dispatcher.BeginInvoke(new Action(() => { if (gen == generation) OnGeometry(idx, g); }));
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.BeginInvoke(new Action(() => { if (gen == generation) ShowStatus("Erro: " + ex.Message); }));
                    }
                });
                th.IsBackground = true;
                th.Start();
            }
        }

        void ToggleMode()
        {
            showImage = !showImage;
            ShowCurrent();
        }

        void SetPlateControls()
        {
            int n = scene == null ? 0 : scene.Plates.Count;
            var vis = n > 1 ? Visibility.Visible : Visibility.Hidden;
            prevBtn.Visibility = vis; nextBtn.Visibility = vis;
            if (n == 0) { plateLabel.Text = ""; return; }
            var p = scene.Plates[plateIndex];
            string label = n > 1 ? "Plate " + p.Number + " de " + n : (scene.Format == "3MF" ? "Plate única" : scene.Format);
            if (!string.IsNullOrEmpty(p.Name)) label += " · " + p.Name;
            plateLabel.Text = label;
        }

        void ShowCurrent()
        {
            if (scene == null) return;
            var plate = scene.Plates[plateIndex];
            BitmapImage thumb = plate.Thumbnail != null ? Decode(plate.Thumbnail) : null;
            modeBtn.Visibility = thumb != null ? Visibility.Visible : Visibility.Collapsed;
            SetButtonText(modeBtn, showImage ? "Ver em 3D" : "Miniatura");

            var names = new List<string>();
            foreach (var o in plate.Objects) names.Add(o.Name);
            objects.Text = names.Count == 0 ? "Plate vazia" : string.Join(", ", names);

            PlateGeometry g;
            bool has3d = cache.TryGetValue(plateIndex, out g);
            if (has3d)
            {
                var b = g.Bounds;
                string dims = b.IsEmpty ? "" : string.Format(CultureInfo.GetCultureInfo("pt-BR"), " · {0:0.#} × {1:0.#} × {2:0.#} mm", b.SizeX, b.SizeY, b.SizeZ);
                info.Text = plate.Objects.Count + (plate.Objects.Count == 1 ? " objeto" : " objetos") +
                    " · " + g.Triangles.ToString("N0", CultureInfo.GetCultureInfo("pt-BR")) + " triângulos" + dims +
                    (g.Decimated ? " (simplificado)" : "");
            }
            else info.Text = plate.Objects.Count + (plate.Objects.Count == 1 ? " objeto" : " objetos");

            bool wantImage = (showImage && thumb != null) || (!has3d && thumb != null);
            image.Source = thumb;
            image.Visibility = wantImage ? Visibility.Visible : Visibility.Collapsed;
            viewport.Visibility = !wantImage && has3d ? Visibility.Visible : Visibility.Hidden;

            if (has3d && !wantImage)
            {
                modelVisual.Content = g.Model;
                if (g.Triangles == 0) ShowStatus("Nenhuma peça nesta plate");
                else { status.Visibility = Visibility.Collapsed; }
                FitCamera(g.Bounds);
            }
            else if (!has3d && !showImage) ShowStatus(thumb != null ? "" : "Montando visualização 3D…");
            else status.Visibility = Visibility.Collapsed;
            if (!has3d && thumb != null && !showImage) { status.Visibility = Visibility.Collapsed; }
        }

        void ShowStatus(string text)
        {
            status.Text = text;
            status.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            if (!string.IsNullOrEmpty(text)) { viewport.Visibility = Visibility.Hidden; image.Visibility = Visibility.Collapsed; }
        }

        static BitmapImage Decode(byte[] png)
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(png);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch { return null; }
        }

        // ---------------- geometria ----------------
        public static PlateGeometry BuildPlate(PlateData plate)
        {
            var result = new PlateGeometry { Model = new Model3DGroup(), Bounds = Rect3D.Empty };
            int total = 0;
            foreach (var o in plate.Objects) foreach (var p in o.Parts) total += p.Mesh.TriangleCount;

            // agrupa por cor para reduzir o número de malhas
            var groups = new Dictionary<Color, List<Placement>>();
            var order = new List<Color>();
            foreach (var o in plate.Objects)
                foreach (var p in o.Parts)
                {
                    List<Placement> l;
                    if (!groups.TryGetValue(p.Color, out l)) { l = new List<Placement>(); groups[p.Color] = l; order.Add(p.Color); }
                    l.Add(p);
                }

            // transforma os vértices de cada peça uma única vez
            var transformed = new Dictionary<Placement, Point3D[]>();
            double gx0 = double.MaxValue, gy0 = double.MaxValue, gz0 = double.MaxValue, gx1 = double.MinValue, gy1 = double.MinValue, gz1 = double.MinValue;
            foreach (var o in plate.Objects)
                foreach (var p in o.Parts)
                {
                    var V = p.Mesh.V; var m = p.Matrix;
                    int nv = V.Count / 3;
                    var tv = new Point3D[nv];
                    for (int i = 0; i < nv; i++)
                    {
                        tv[i] = m.Transform(new Point3D(V[i * 3], V[i * 3 + 1], V[i * 3 + 2]));
                        Grow(tv[i], ref gx0, ref gy0, ref gz0, ref gx1, ref gy1, ref gz1);
                    }
                    transformed[p] = tv;
                }

            // malhas muito grandes: simplificação por agrupamento de vértices (mantém a superfície fechada)
            if (total > MaxTriangles && gx1 > gx0)
            {
                result.Decimated = true;
                double diag = Math.Sqrt((gx1 - gx0) * (gx1 - gx0) + (gy1 - gy0) * (gy1 - gy0) + (gz1 - gz0) * (gz1 - gz0));
                double res = 700;
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    double cell = diag / res;
                    int kept = 0;
                    var snapped = new Dictionary<Placement, Point3D[]>();
                    foreach (var kv in transformed)
                    {
                        var map = new Dictionary<long, Point3D>();
                        var src = kv.Value;
                        var dst = new Point3D[src.Length];
                        for (int i = 0; i < src.Length; i++)
                        {
                            long key = CellKey(src[i], gx0, gy0, gz0, cell);
                            Point3D rep;
                            if (!map.TryGetValue(key, out rep)) { rep = src[i]; map[key] = rep; }
                            dst[i] = rep;
                        }
                        snapped[kv.Key] = dst;
                        var T = kv.Key.Mesh.T;
                        for (int t = 0; t + 2 < T.Count; t += 3)
                        {
                            int a = T[t], b = T[t + 1], c = T[t + 2];
                            if (a < 0 || b < 0 || c < 0 || a >= dst.Length || b >= dst.Length || c >= dst.Length) continue;
                            if (dst[a] != dst[b] && dst[b] != dst[c] && dst[a] != dst[c]) kept++;
                        }
                    }
                    if (kept <= MaxTriangles || attempt == 5) { transformed = snapped; break; }
                    res *= 0.7 * Math.Sqrt(MaxTriangles / (double)kept);
                }
            }

            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (var color in order)
            {
                var list = groups[color];
                int est = 0;
                foreach (var p in list) est += Math.Min(p.Mesh.TriangleCount, MaxTriangles) + 1;
                var pts = new Point3DCollection(est * 3);
                foreach (var p in list)
                {
                    var T = p.Mesh.T;
                    var tv = transformed[p];
                    int nv = tv.Length;
                    for (int t = 0; t + 2 < T.Count; t += 3)
                    {
                        int a = T[t], b = T[t + 1], c = T[t + 2];
                        if (a >= nv || b >= nv || c >= nv || a < 0 || b < 0 || c < 0) continue;
                        var pa = tv[a]; var pb = tv[b]; var pc = tv[c];
                        if (result.Decimated && (pa == pb || pb == pc || pa == pc)) continue;
                        pts.Add(pa); pts.Add(pb); pts.Add(pc);
                        Grow(pa, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                        Grow(pb, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                        Grow(pc, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
                    }
                }
                if (pts.Count == 0) continue;
                result.Triangles += pts.Count / 3;
                pts.Freeze();
                var mesh = new MeshGeometry3D { Positions = pts };
                mesh.Freeze();
                var mat = new MaterialGroup();
                mat.Children.Add(new DiffuseMaterial(new SolidColorBrush(color)));
                mat.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF)), 40));
                mat.Freeze();
                var back = new DiffuseMaterial(new SolidColorBrush(Color.FromRgb((byte)(color.R / 2), (byte)(color.G / 2), (byte)(color.B / 2))));
                back.Freeze();
                var gm = new GeometryModel3D(mesh, mat) { BackMaterial = back };
                gm.Freeze();
                result.Model.Children.Add(gm);
            }

            if (result.Triangles > 0)
            {
                result.Bounds = new Rect3D(minX, minY, minZ, maxX - minX, maxY - minY, maxZ - minZ);
                result.Model.Children.Add(MakeFloor(plate, result.Bounds));
            }
            result.Model.Freeze();
            return result;
        }

        static long CellKey(Point3D p, double x0, double y0, double z0, double cell)
        {
            long ix = (long)((p.X - x0) / cell), iy = (long)((p.Y - y0) / cell), iz = (long)((p.Z - z0) / cell);
            return (ix & 0x1FFFFF) | ((iy & 0x1FFFFF) << 21) | ((iz & 0x1FFFFF) << 42);
        }

        static void Grow(Point3D p, ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ)
        {
            if (p.X < minX) minX = p.X; if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y; if (p.Y > maxY) maxY = p.Y;
            if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z;
        }

        static Model3D MakeFloor(PlateData plate, Rect3D b)
        {
            double x0, y0, w, h;
            double cx = b.X + b.SizeX / 2, cy = b.Y + b.SizeY / 2;
            var bed = plate.Bed;
            bool useBed = !bed.IsEmpty && cx >= bed.X - 20 && cx <= bed.X + bed.SizeX + 20 && cy >= bed.Y - 20 && cy <= bed.Y + bed.SizeY + 20;
            if (useBed) { x0 = bed.X; y0 = bed.Y; w = bed.SizeX; h = bed.SizeY; }
            else
            {
                double s = Math.Max(Math.Max(b.SizeX, b.SizeY) * 1.5, 20);
                s = Math.Ceiling(s / 10) * 10;
                x0 = Math.Floor((cx - s / 2) / 10) * 10; y0 = Math.Floor((cy - s / 2) / 10) * 10; w = s; h = s;
            }
            double z = b.Z - 0.05;
            var mesh = new MeshGeometry3D();
            mesh.Positions = new Point3DCollection { new Point3D(x0, y0, z), new Point3D(x0 + w, y0, z), new Point3D(x0 + w, y0 + h, z), new Point3D(x0, y0 + h, z) };
            mesh.TriangleIndices = new Int32Collection { 0, 1, 2, 0, 2, 3 };
            mesh.TextureCoordinates = new PointCollection { new Point(0, 1), new Point(1, 1), new Point(1, 0), new Point(0, 0) };
            mesh.Normals = new Vector3DCollection { new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1), new Vector3D(0, 0, 1) };
            mesh.Freeze();

            // textura de grade de 10 mm
            double cell = 10;
            var grid = new DrawingGroup();
            var lineBrush = new SolidColorBrush(Color.FromArgb(0x70, 0x80, 0x80, 0x80));
            var fill = new SolidColorBrush(Color.FromRgb(0x3C, 0x3C, 0x40));
            grid.Children.Add(new GeometryDrawing(fill, null, new RectangleGeometry(new Rect(0, 0, cell, cell))));
            grid.Children.Add(new GeometryDrawing(null, new Pen(lineBrush, 0.25), new RectangleGeometry(new Rect(0, 0, cell, cell))));
            var brush = new DrawingBrush(grid)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, cell / w, cell / h),
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox,
                Stretch = Stretch.Fill
            };
            var mat = new DiffuseMaterial(brush);
            mat.Freeze();
            var gm = new GeometryModel3D(mesh, mat) { BackMaterial = mat };
            gm.Freeze();
            return gm;
        }

        // ---------------- câmera ----------------
        void FitCamera(Rect3D b)
        {
            if (b.IsEmpty) return;
            target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            double r = Math.Sqrt(b.SizeX * b.SizeX + b.SizeY * b.SizeY + b.SizeZ * b.SizeZ) / 2;
            if (r < 1) r = 1;
            double aspect = stage.ActualWidth > 0 && stage.ActualHeight > 0 ? stage.ActualWidth / stage.ActualHeight : 1;
            double half = camera.FieldOfView / 2 * Math.PI / 180;
            double fitH = r / Math.Sin(half);                 // pelo campo horizontal
            double fitV = r / Math.Sin(Math.Atan(Math.Tan(half) / aspect));
            distance = Math.Max(fitH, fitV) * 1.05;
            azimuth = -55; elevation = 28;
            homeTarget = target; homeDistance = distance;
            UpdateCamera();
        }

        void UpdateCamera()
        {
            double az = azimuth * Math.PI / 180, el = elevation * Math.PI / 180;
            var dir = new Vector3D(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
            var pos = target + dir * distance;
            camera.Position = pos;
            camera.LookDirection = -dir * distance;
            camera.NearPlaneDistance = Math.Max(0.01, distance * 0.01);
            camera.FarPlaneDistance = distance * 50;
            var right = Vector3D.CrossProduct(-dir, new Vector3D(0, 0, 1));
            right.Normalize();
            headLight.Direction = -dir - right * 0.35 - new Vector3D(0, 0, 0.3);
        }

        void OnMouseDown(object s, MouseButtonEventArgs e)
        {
            Focus();
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2)
            {
                target = homeTarget; distance = homeDistance; azimuth = -55; elevation = 28; UpdateCamera();
                return;
            }
            lastMouse = e.GetPosition(stage);
            rotating = e.ChangedButton == MouseButton.Left;
            panning = e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle;
            stage.CaptureMouse();
        }

        void OnMouseUp(object s, MouseButtonEventArgs e)
        {
            rotating = panning = false;
            stage.ReleaseMouseCapture();
            e.Handled = true;
        }

        void OnMouseMove(object s, MouseEventArgs e)
        {
            if (!rotating && !panning) return;
            var p = e.GetPosition(stage);
            double dx = p.X - lastMouse.X, dy = p.Y - lastMouse.Y;
            lastMouse = p;
            if (rotating)
            {
                azimuth -= dx * 0.5;
                elevation = Math.Max(-89, Math.Min(89, elevation + dy * 0.5));
            }
            else
            {
                double az = azimuth * Math.PI / 180, el = elevation * Math.PI / 180;
                var dir = new Vector3D(Math.Cos(el) * Math.Cos(az), Math.Cos(el) * Math.Sin(az), Math.Sin(el));
                var right = Vector3D.CrossProduct(new Vector3D(0, 0, 1), dir); right.Normalize();
                var up = Vector3D.CrossProduct(dir, right); up.Normalize();
                double k = distance * 2 * Math.Tan(camera.FieldOfView / 2 * Math.PI / 180) / Math.Max(1, stage.ActualHeight);
                target += right * dx * k + up * dy * k;
            }
            UpdateCamera();
        }

        void OnWheel(object s, MouseWheelEventArgs e)
        {
            distance *= Math.Pow(0.9, e.Delta / 120.0);
            distance = Math.Max(0.5, distance);
            UpdateCamera();
            e.Handled = true;
        }

        void OnKey(object s, KeyEventArgs e)
        {
            if (e.Key == Key.PageDown || e.Key == Key.OemPeriod) { ChangePlate(1); e.Handled = true; }
            else if (e.Key == Key.PageUp || e.Key == Key.OemComma) { ChangePlate(-1); e.Handled = true; }
        }
    }
}
