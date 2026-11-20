// Preview handler COM para o painel de visualização do Explorer.
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Interop;
using System.Windows.Media;

namespace Bambu3DPreview
{
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG { public IntPtr hwnd; public int message; public IntPtr wParam; public IntPtr lParam; public int time; public int pt_x; public int pt_y; }

    [ComImport, Guid("8895b1c6-b41f-4c1c-a562-0d564250836f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPreviewHandler
    {
        void SetWindow(IntPtr hwnd, ref RECT rect);
        void SetRect(ref RECT rect);
        void DoPreview();
        void Unload();
        void SetFocus();
        void QueryFocus(out IntPtr phwnd);
        [PreserveSig] uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("196bf9a5-b346-4ef0-aa1e-5dcdb76768b1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPreviewHandlerVisuals
    {
        void SetBackgroundColor(uint color);
        void SetFont(IntPtr plf);
        void SetTextColor(uint color);
    }

    [ComImport, Guid("fec87aaf-35f9-447a-adb7-20234491401a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPreviewHandlerFrame
    {
        void GetWindowContext(IntPtr pinfo);
        [PreserveSig] uint TranslateAccelerator(ref MSG pmsg);
    }

    [ComImport, Guid("b7d14566-0509-4cce-a71f-0a554233bd9b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInitializeWithFile
    {
        void Initialize([MarshalAs(UnmanagedType.LPWStr)] string pszFilePath, uint grfMode);
    }

    [ComImport, Guid("b824b49d-22ac-4161-ac8a-9916e8fa3f7f"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IInitializeWithStream
    {
        void Initialize(IStream pstream, uint grfMode);
    }

    [ComImport, Guid("fc4801a3-2ba9-11cf-a229-00aa003d7352"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IObjectWithSite
    {
        void SetSite([MarshalAs(UnmanagedType.IUnknown)] object pUnkSite);
        void GetSite(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppvSite);
    }

    [ComImport, Guid("00000114-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow
    {
        void GetWindow(out IntPtr phwnd);
        void ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }

    [ComVisible(true)]
    [Guid(PreviewHandler.ClsidString)]
    [ClassInterface(ClassInterfaceType.None)]
    [ProgId("Bambu3DPreview.PreviewHandler")]
    public class PreviewHandler : IPreviewHandler, IPreviewHandlerVisuals, IInitializeWithFile, IInitializeWithStream, IObjectWithSite, IOleWindow
    {
        public const string ClsidString = "7A3E5B1D-4C2F-4E8A-9B61-3D0F5C2A8E47";

        IntPtr parent;
        RECT rect;
        string filePath;
        byte[] streamData;
        object site;
        HwndSource source;
        PreviewView view;
        Color? bg;

        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SetFocus(IntPtr hWnd);
        [DllImport("user32.dll")] static extern IntPtr GetFocus();
        [DllImport("user32.dll")] static extern IntPtr SetParent(IntPtr child, IntPtr parent);

        const int WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPCHILDREN = 0x02000000, WS_CLIPSIBLINGS = 0x04000000;

        // ---- inicialização ----
        void IInitializeWithFile.Initialize(string path, uint mode) { filePath = path; streamData = null; }

        void IInitializeWithStream.Initialize(IStream stream, uint mode)
        {
            filePath = null;
            try
            {
                System.Runtime.InteropServices.ComTypes.STATSTG st;
                stream.Stat(out st, 0);
                if (!string.IsNullOrEmpty(st.pwcsName)) filePath = st.pwcsName;
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[1 << 20];
                    IntPtr pRead = Marshal.AllocCoTaskMem(4);
                    try
                    {
                        while (true)
                        {
                            stream.Read(buf, buf.Length, pRead);
                            int n = Marshal.ReadInt32(pRead);
                            if (n <= 0) break;
                            ms.Write(buf, 0, n);
                        }
                    }
                    finally { Marshal.FreeCoTaskMem(pRead); }
                    streamData = ms.ToArray();
                }
            }
            finally { Marshal.ReleaseComObject(stream); }
        }

        // ---- janela ----
        void IPreviewHandler.SetWindow(IntPtr hwnd, ref RECT r)
        {
            parent = hwnd; rect = r;
            if (source != null && ui != null)
            {
                ui.Invoke(new Action(() => { SetParent(source.Handle, parent); Resize(); }));
            }
        }

        void IPreviewHandler.SetRect(ref RECT r) { rect = r; Resize(); }

        void Resize()
        {
            if (source == null) return;
            SetWindowPos(source.Handle, IntPtr.Zero, rect.left, rect.top, Math.Max(0, rect.right - rect.left), Math.Max(0, rect.bottom - rect.top), 0x0014 /*NOZORDER|NOACTIVATE*/);
        }

        // O WPF precisa de uma thread STA própria; a thread do prevhost nem sempre é.
        System.Windows.Threading.Dispatcher ui;

        void OnUi(Action a)
        {
            if (ui == null)
            {
                var ready = new System.Threading.ManualResetEvent(false);
                var th = new System.Threading.Thread(() =>
                {
                    ui = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                    ready.Set();
                    System.Windows.Threading.Dispatcher.Run();
                });
                th.SetApartmentState(System.Threading.ApartmentState.STA);
                th.IsBackground = true;
                th.Start();
                ready.WaitOne();
            }
            ui.Invoke(a);
        }

        void IPreviewHandler.DoPreview() { OnUi(DoPreviewUi); }

        void DoPreviewUi()
        {
            if (source == null)
            {
                // o Explorer informa fundo branco mesmo no modo escuro: segue o tema do Windows
                var theme = Theme.Detect();
                view = new PreviewView(theme);
                var p = new HwndSourceParameters("Bambu3DPreview")
                {
                    ParentWindow = parent,
                    WindowStyle = WS_CHILD | WS_VISIBLE | WS_CLIPCHILDREN | WS_CLIPSIBLINGS,
                    PositionX = rect.left,
                    PositionY = rect.top,
                    Width = Math.Max(1, rect.right - rect.left),
                    Height = Math.Max(1, rect.bottom - rect.top)
                };
                source = new HwndSource(p);
                source.SizeToContent = System.Windows.SizeToContent.Manual;
                source.RootVisual = view;
            }
            view.LoadAsync(filePath, streamData);
        }

        void IPreviewHandler.Unload()
        {
            if (ui != null)
            {
                ui.Invoke(new Action(() =>
                {
                    if (view != null) view.Cancel();
                    if (source != null) { source.RootVisual = null; source.Dispose(); }
                    source = null; view = null;
                }));
                ui.InvokeShutdown();
                ui = null;
            }
            streamData = null;
            GC.Collect();
        }

        void IPreviewHandler.SetFocus() { if (source != null) SetFocus(source.Handle); }
        void IPreviewHandler.QueryFocus(out IntPtr phwnd) { phwnd = GetFocus(); }

        uint IPreviewHandler.TranslateAccelerator(ref MSG msg)
        {
            var frame = site as IPreviewHandlerFrame;
            return frame != null ? frame.TranslateAccelerator(ref msg) : 1u; // S_FALSE
        }

        // ---- cores do Explorer ----
        void IPreviewHandlerVisuals.SetBackgroundColor(uint c) { bg = Color.FromRgb((byte)(c & 0xFF), (byte)((c >> 8) & 0xFF), (byte)((c >> 16) & 0xFF)); }
        void IPreviewHandlerVisuals.SetFont(IntPtr plf) { }
        void IPreviewHandlerVisuals.SetTextColor(uint c) { }

        // ---- site / janela ----
        void IObjectWithSite.SetSite(object s) { site = s; }
        void IObjectWithSite.GetSite(ref Guid riid, out object s) { s = site; }
        void IOleWindow.GetWindow(out IntPtr hwnd) { hwnd = source != null ? source.Handle : parent; }
        void IOleWindow.ContextSensitiveHelp(bool f) { throw new NotImplementedException(); }
    }
}
