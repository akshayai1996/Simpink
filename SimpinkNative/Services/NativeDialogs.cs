using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;

namespace SimpinkNative.Services
{
    public static class NativeDialogs
    {
        [DllImport("shell32.dll")]
        private static extern IntPtr SHBrowseForFolder(ref BROWSEINFO lpbi);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct BROWSEINFO
        {
            public IntPtr hwndOwner;
            public IntPtr pidlRoot;
            public IntPtr pszDisplayName;
            public string lpszTitle;
            public uint ulFlags;
            public IntPtr lpfn;
            public IntPtr lParam;
            public int iImage;
        }

        private const uint BIF_RETURNONLYFSDIRS = 0x0001;
        private const uint BIF_DONTGOBELOWDOMAIN = 0x0002;
        private const uint BIF_STATUSTEXT = 0x0004;
        private const uint BIF_RETURNFSANCESTORS = 0x0008;
        private const uint BIF_EDITBOX = 0x0010;
        private const uint BIF_VALIDATE = 0x0020;
        private const uint BIF_NEWDIALOGSTYLE = 0x0040;
        private const uint BIF_USENEWUI = BIF_NEWDIALOGSTYLE | BIF_EDITBOX;
        private const uint BIF_BROWSEINCLUDEFILES = 0x4000;
        private const uint BIF_BROWSEFORCOMPUTER = 0x1000;
        private const uint BIF_BROWSEFORPRINTER = 0x2000;
        private const uint BIF_SHAREABLE = 0x8000;

        private const uint BFFM_INITIALIZED = 1;
        private const uint BFFM_SELCHANGED = 2;
        private const uint BFFM_SETSELECTION = 0x466;
        private const uint BFFM_ENABLEOK = 0x468;
        private const uint BFFM_SETSTATUSTEXT = 0x464;

        public static string? BrowseForFolder(Window owner, string title = "Select Folder", string? initialPath = null)
        {
            var hwndOwner = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            
            var bi = new BROWSEINFO
            {
                hwndOwner = hwndOwner,
                lpszTitle = title,
                ulFlags = BIF_RETURNONLYFSDIRS | BIF_USENEWUI | BIF_EDITBOX
            };

            if (!string.IsNullOrEmpty(initialPath))
            {
                bi.lParam = Marshal.StringToHGlobalAuto(initialPath);
            }

            IntPtr pidl = SHBrowseForFolder(ref bi);
            
            if (bi.lParam != IntPtr.Zero)
                Marshal.FreeHGlobal(bi.lParam);

            if (pidl == IntPtr.Zero)
                return null;

            var path = new StringBuilder(260);
            if (SHGetPathFromIDList(pidl, path))
            {
                Marshal.FreeCoTaskMem(pidl);
                return path.ToString();
            }

            Marshal.FreeCoTaskMem(pidl);
            return null;
        }
    }
}