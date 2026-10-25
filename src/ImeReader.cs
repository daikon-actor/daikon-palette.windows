using System;
using System.Runtime.InteropServices;

namespace DaikonPalette
{
    internal enum ImeKind
    {
        Japanese,
        English,
        Unknown,
    }

    internal sealed class ImeState
    {
        public static readonly ImeState Unknown = new ImeState(ImeKind.Unknown, "?", "入力モードを取得できません");

        public ImeState(ImeKind kind, string shortText, string longText)
        {
            Kind = kind;
            Short = shortText;
            Long = longText;
        }

        public ImeKind Kind { get; private set; }
        public string Short { get; private set; }
        public string Long { get; private set; }
    }

    /// <summary>
    /// 前面アプリの IME の状態を読み取る（変更はしない）。
    /// WM_IME_CONTROL を IME の既定ウィンドウに送って問い合わせる。
    /// </summary>
    internal static class ImeReader
    {
        private const int NATIVE = 0x1;
        private const int KATAKANA = 0x2;
        private const int FULLSHAPE = 0x8;

        public static ImeState Query(IntPtr foreground)
        {
            if (foreground == IntPtr.Zero)
            {
                return ImeState.Unknown;
            }
            uint pid;
            uint tid = NativeMethods.GetWindowThreadProcessId(foreground, out pid);
            NativeMethods.GUITHREADINFO gui = new NativeMethods.GUITHREADINFO();
            gui.cbSize = Marshal.SizeOf(typeof(NativeMethods.GUITHREADINFO));
            IntPtr target = foreground;
            if (NativeMethods.GetGUIThreadInfo(tid, ref gui) && gui.hwndFocus != IntPtr.Zero)
            {
                target = gui.hwndFocus;
            }
            IntPtr ime = NativeMethods.ImmGetDefaultIMEWnd(target);
            if (ime == IntPtr.Zero)
            {
                return ImeState.Unknown;
            }
            IntPtr open;
            if (NativeMethods.SendMessageTimeout(ime, NativeMethods.WM_IME_CONTROL,
                    new IntPtr(NativeMethods.IMC_GETOPENSTATUS), IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG, 200, out open) == IntPtr.Zero)
            {
                return ImeState.Unknown;
            }
            IntPtr conv;
            if (NativeMethods.SendMessageTimeout(ime, NativeMethods.WM_IME_CONTROL,
                    new IntPtr(NativeMethods.IMC_GETCONVERSIONMODE), IntPtr.Zero,
                    NativeMethods.SMTO_ABORTIFHUNG, 200, out conv) == IntPtr.Zero)
            {
                return ImeState.Unknown;
            }
            return Interpret(open != IntPtr.Zero, (int)conv.ToInt64());
        }

        public static ImeState Interpret(bool open, int conv)
        {
            if (!open)
            {
                return new ImeState(ImeKind.English, "A", "半角英数（IME オフ）");
            }
            if ((conv & NATIVE) != 0)
            {
                if ((conv & KATAKANA) != 0)
                {
                    return (conv & FULLSHAPE) != 0
                        ? new ImeState(ImeKind.Japanese, "カ", "日本語入力（全角カタカナ）")
                        : new ImeState(ImeKind.Japanese, "ｶ", "日本語入力（半角カタカナ）");
                }
                return new ImeState(ImeKind.Japanese, "あ", "日本語入力（ひらがな）");
            }
            return (conv & FULLSHAPE) != 0
                ? new ImeState(ImeKind.English, "Ａ", "全角英数")
                : new ImeState(ImeKind.English, "A", "半角英数（IME オン・直接入力）");
        }
    }
}
