using CADHub.Pages;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;

namespace CADHub
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            InitializeComponent();
            try
            {
                DarkTitleBar(this);
            }
            catch { }
            var page = new LoginPage();
            _mainFrame.Navigate(page);
            page.DoneLogin += DoneLogin;
        }

        private void DoneLogin(object? sender, EventArgs e)
        {
            var mainApplication = new MainApplication();
            _mainFrame.Navigate(mainApplication);
        }

        // For making the title bar dark

        [DllImport("DwmApi")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, int[] attrValue, int attrSize);
        const int DWWMA_CAPTION_COLOR = 35;

        static void DarkTitleBar(Window window)
        {
            IntPtr hWnd = new WindowInteropHelper(window).EnsureHandle();
            int[] colorstr = new int[] { 0x1E1E1E };
            DwmSetWindowAttribute(hWnd, DWWMA_CAPTION_COLOR, colorstr, 4);
        }

    }
}



