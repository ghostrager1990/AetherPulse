using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;

namespace AppUI.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            SplashImage.Source = LoadEncryptedSplash();
        }

        private static BitmapImage? LoadEncryptedSplash()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                string? resName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("splash.dat", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrEmpty(resName)) return null;

                using var stream = assembly.GetManifestResourceStream(resName);
                if (stream == null) return null;

                byte[] key = new byte[] { 0x5A, 0xAF, 0x3C, 0x91 };
                using var ms = new MemoryStream();
                int b, i = 0;
                while ((b = stream.ReadByte()) != -1)
                {
                    ms.WriteByte((byte)(b ^ key[i % key.Length]));
                    i++;
                }
                ms.Position = 0;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public void UpdateStatus(string status)
        {
            // Status text is omitted to preserve unhindered presentation of splash artwork
        }
    }
}
