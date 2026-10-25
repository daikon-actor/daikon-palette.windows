using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaikonPalette
{
    /// <summary>前面のアプリにキー入力を送る</summary>
    internal static class InputSender
    {
        private static readonly int InputSize = Marshal.SizeOf(typeof(NativeMethods.INPUT));

        /// <summary>
        /// 文字列をキーボードで打ったのと同じように送る（クリップボードは使わない）。
        /// 改行は Enter、shiftEnter が true なら Shift+Enter で送る。
        /// </summary>
        public static void TypeText(string text, bool shiftEnter)
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            foreach (char c in NormalizeNewlines(text))
            {
                if (c == '\n')
                {
                    if (shiftEnter)
                    {
                        inputs.Add(Key(NativeMethods.VK_SHIFT, false));
                        inputs.Add(Key(NativeMethods.VK_RETURN, false));
                        inputs.Add(Key(NativeMethods.VK_RETURN, true));
                        inputs.Add(Key(NativeMethods.VK_SHIFT, true));
                    }
                    else
                    {
                        inputs.Add(Key(NativeMethods.VK_RETURN, false));
                        inputs.Add(Key(NativeMethods.VK_RETURN, true));
                    }
                }
                else if (c == '\t')
                {
                    inputs.Add(Key(NativeMethods.VK_TAB, false));
                    inputs.Add(Key(NativeMethods.VK_TAB, true));
                }
                else
                {
                    // サロゲートペアも 1 コードユニットずつ送ればアプリ側で結合される
                    inputs.Add(Unicode(c, false));
                    inputs.Add(Unicode(c, true));
                }
            }
            Send(inputs);
        }

        /// <summary>Ctrl+V を送る</summary>
        public static void PressPaste()
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            inputs.Add(Key(NativeMethods.VK_CONTROL, false));
            inputs.Add(Key(NativeMethods.VK_V, false));
            inputs.Add(Key(NativeMethods.VK_V, true));
            inputs.Add(Key(NativeMethods.VK_CONTROL, true));
            Send(inputs);
        }

        /// <summary>← キーを count 回送る</summary>
        public static void PressLeft(int count)
        {
            List<NativeMethods.INPUT> inputs = new List<NativeMethods.INPUT>();
            for (int i = 0; i < count; i++)
            {
                inputs.Add(Key(NativeMethods.VK_LEFT, false));
                inputs.Add(Key(NativeMethods.VK_LEFT, true));
            }
            Send(inputs);
        }

        /// <summary>カーソルを戻すのに必要な ← の回数（改行は 1 回、サロゲートペアも 1 回）</summary>
        public static int CaretSteps(string text)
        {
            int count = 0;
            foreach (char c in NormalizeNewlines(text))
            {
                if (!char.IsLowSurrogate(c))
                {
                    count++;
                }
            }
            return count;
        }

        public static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static void Send(List<NativeMethods.INPUT> inputs)
        {
            if (inputs.Count == 0)
            {
                return;
            }
            NativeMethods.SendInput((uint)inputs.Count, inputs.ToArray(), InputSize);
        }

        private static NativeMethods.INPUT Key(ushort vk, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.U.ki.wVk = vk;
            input.U.ki.dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0u;
            return input;
        }

        private static NativeMethods.INPUT Unicode(char c, bool up)
        {
            NativeMethods.INPUT input = new NativeMethods.INPUT();
            input.type = NativeMethods.INPUT_KEYBOARD;
            input.U.ki.wVk = 0;
            input.U.ki.wScan = c;
            input.U.ki.dwFlags = NativeMethods.KEYEVENTF_UNICODE | (up ? NativeMethods.KEYEVENTF_KEYUP : 0u);
            return input;
        }
    }
}
