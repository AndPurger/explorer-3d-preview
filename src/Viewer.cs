// Visualizador avulso (abre o arquivo numa janela) e gerador de imagens de teste.
//   Viewer.exe arquivo.3mf
//   Viewer.exe --snapshot arquivo.3mf saida.png [plate]
using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Bambu3DPreview
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length >= 3 && args[0] == "--snapshot")
            {
                try
                {
                    int plate = args.Length >= 4 ? int.Parse(args[3]) - 1 : 0;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    var view = new PreviewView(Theme.Dark());
                    view.Measure(new Size(520, 640));
                    view.Arrange(new Rect(0, 0, 520, 640));
                    view.UpdateLayout();
                    view.LoadSync(args[1], plate);
                    view.UpdateLayout();
                    var bmp = new RenderTargetBitmap(520, 640, 96, 96, PixelFormats.Pbgra32);
                    bmp.Render(view);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(bmp));
                    using (var f = File.Create(args[2])) enc.Save(f);
                    File.WriteAllText(args[2] + ".txt", "ok " + sw.ElapsedMilliseconds + " ms");
                    return 0;
                }
                catch (Exception ex) { File.WriteAllText(args[2] + ".txt", ex.ToString()); return 1; }
            }

            var app = new Application();
            var v = new PreviewView();
            var w = new Window { Title = "Prévia 3D", Width = 640, Height = 720, Content = v };
            if (args.Length > 0)
            {
                w.Title = Path.GetFileName(args[0]) + " — Prévia 3D";
                v.LoadAsync(args[0], null);
            }
            return app.Run(w);
        }
    }
}
