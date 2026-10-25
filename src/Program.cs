// DaikonPalette - 画面左端に常駐し、クリックした単語・テンプレートを前面のアプリに入力するパレット
// Windows付属の csc.exe (C# 5) でビルドできるよう、C# 5 の範囲で書くこと。

using System;
using System.Threading;
using System.Windows.Forms;

namespace DaikonPalette
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            using (Mutex mutex = new Mutex(true, "DaikonPalette_SingleInstance_4C9E", out created))
            {
                if (!created) return; // 二重起動しない

                try { NativeMethods.SetProcessDPIAware(); }
                catch (EntryPointNotFoundException) { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new PaletteForm());
                GC.KeepAlive(mutex);
            }
        }
    }
}
