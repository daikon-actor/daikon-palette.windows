using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DaikonPalette
{
    /// <summary>
    /// 画面の左端に「AppBar」として登録する。
    /// タスクバーと同じ仕組みで、他のアプリを最大化してもこの領域を避けるようになる。
    /// </summary>
    internal sealed class AppBar
    {
        private readonly Form _form;
        private uint _callbackMessage;
        private bool _registered;
        private int _width;

        /// <summary>全画面のアプリが開いた（true）・閉じた（false）</summary>
        public event Action<bool> FullscreenChanged;

        public AppBar(Form form)
        {
            _form = form;
        }

        public void Register(int width)
        {
            _width = width;
            _callbackMessage = NativeMethods.RegisterWindowMessage("DaikonPalette.AppBarMessage");
            NativeMethods.APPBARDATA data = NewData();
            data.uCallbackMessage = _callbackMessage;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref data);
            _registered = true;
            SetPosition();
        }

        /// <summary>幅を変えて位置を合わせ直す</summary>
        public void Resize(int width)
        {
            _width = width;
            SetPosition();
        }

        public void SetPosition()
        {
            if (!_registered)
            {
                return;
            }
            Screen screen = Screen.PrimaryScreen;
            NativeMethods.APPBARDATA data = NewData();
            data.uEdge = NativeMethods.ABE_LEFT;
            data.rc.Left = screen.Bounds.Left;
            data.rc.Top = screen.Bounds.Top;
            data.rc.Right = screen.Bounds.Left + _width;
            data.rc.Bottom = screen.Bounds.Bottom;

            // 他の AppBar（左に置いたタスクバーなど）と重ならない位置をシステムに聞く
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref data);
            data.rc.Right = data.rc.Left + _width;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref data);

            // 縦方向は下のタスクバーに重ならないよう、作業領域に合わせる
            Rectangle work = screen.WorkingArea;
            _form.Bounds = Rectangle.FromLTRB(data.rc.Left, work.Top, data.rc.Right, work.Bottom);
        }

        /// <summary>AppBar の通知なら処理して true を返す</summary>
        public bool HandleMessage(ref Message m)
        {
            if (!_registered || m.Msg != (int)_callbackMessage)
            {
                return false;
            }
            long code = m.WParam.ToInt64();
            if (code == NativeMethods.ABN_POSCHANGED)
            {
                SetPosition();
            }
            else if (code == NativeMethods.ABN_FULLSCREENAPP && FullscreenChanged != null)
            {
                // 全画面のアプリ（スライドショー・動画など）が開いたら lParam が 0 以外
                FullscreenChanged(m.LParam != IntPtr.Zero);
            }
            return true;
        }

        public void Unregister()
        {
            if (!_registered)
            {
                return;
            }
            NativeMethods.APPBARDATA data = NewData();
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref data);
            _registered = false;
        }

        private NativeMethods.APPBARDATA NewData()
        {
            NativeMethods.APPBARDATA data = new NativeMethods.APPBARDATA();
            data.cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA));
            data.hWnd = _form.Handle;
            return data;
        }
    }
}
