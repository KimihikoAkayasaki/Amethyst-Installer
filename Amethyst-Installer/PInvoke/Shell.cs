using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace amethyst_installer_gui.PInvoke {
    public static class Shell {
        
        [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint SHGetKnownFolderItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint flags, IntPtr hToken, ref Guid riid, ref IntPtr ppv);
        
        public static readonly Guid FOLDERID_AppsFolder = new Guid(0x1e87508d, 0x89c2, 0x42f0, 0x8a, 0x7e, 0x64, 0x5a, 0x0f, 0x50, 0xca, 0x58);

        [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint SHCreateItemFromRelativeName(IntPtr psiParent, string pszName, IntPtr pbc, ref Guid riid, ref IntPtr ppv);

        [DllImport("Shell32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern uint SHGetIDListFromObject(IntPtr punk, out IntPtr ppidl);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr ILFindLastID(IntPtr pidl);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr ILClone(IntPtr pidl);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern Boolean ILRemoveLastID(IntPtr pidl);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern void ILFree(IntPtr pidl);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "#740")]
        public static extern uint SHCreateFileDataObject(IntPtr pidlFolder, uint cidl, IntPtr[] apidl, System.Runtime.InteropServices.ComTypes.IDataObject pdtInner, out System.Runtime.InteropServices.ComTypes.IDataObject ppdtobj);

        [DllImport("Shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "#172")]
        public static extern uint SHCreateLinks(IntPtr hwnd, string pszDir, System.Runtime.InteropServices.ComTypes.IDataObject pDataObj, uint fFlags, IntPtr ppidl);
        
        [DllImport("shell32.dll", SetLastError = true)]
        private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, uint cidl, [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, uint dwFlags);

        [DllImport("shell32.dll", SetLastError = true)]
        private static extern uint SHParseDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr bindingContext, [Out] out IntPtr pidl, uint sfgaoIn, [Out] out uint psfgaoOut);

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern uint SHGetNameFromIDList(IntPtr pidl, SIGDN sigdnName, [Out] out IntPtr ppszName);

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteFile(string name);

        private enum SIGDN : uint {
            NORMALDISPLAY = 0x00000000,
            PARENTRELATIVEPARSING = 0x80018001,
            DESKTOPABSOLUTEPARSING = 0x80028000,
            PARENTRELATIVEEDITING = 0x80031001,
            DESKTOPABSOLUTEEDITING = 0x8004c000,
            FILESYSPATH = 0x80058000,
            URL = 0x80068000,
            PARENTRELATIVEFORADDRESSBAR = 0x8007c001,
            PARENTRELATIVE = 0x80080001
        }

        // An alternative to:
        //      Process.Start("explorer.exe", $"/select,{filePath}");
        // P/Invoke version allows us to do the same task without spawning a new instance of explorer.exe
        // If the WINAPI function fails for whatever reason we'll spawn a new explorer instance instead
        // (the WINAPI function fails in multi-account setups :)
        public static void OpenFolderAndSelectItem(string filePath) {

            filePath = Path.GetFullPath(filePath); // Resolve absolute path
            string folderPath = Path.GetDirectoryName(filePath);
            string file = Path.GetFileName(filePath);

            IntPtr nativeFolder;
            uint psfgaoOut;


            SHParseDisplayName(folderPath, IntPtr.Zero, out nativeFolder, 0, out psfgaoOut);

            if ( nativeFolder == IntPtr.Zero ) {
                Logger.Fatal($"Failed to find directory {filePath}!");
                return;
            }

            IntPtr nativeFile;
            SHParseDisplayName(Path.Combine(folderPath, file), IntPtr.Zero, out nativeFile, 0, out psfgaoOut);

            IntPtr[] fileArray;
            if ( nativeFile == IntPtr.Zero ) {
                // Open the folder without the file selected if we can't find the file
                fileArray = new IntPtr[] { nativeFolder };
            } else {
                fileArray = new IntPtr[] { nativeFile };
            }

            // #define FAILED(hr) (((HRESULT)(hr)) < 0)
            if (SHOpenFolderAndSelectItems(nativeFolder, ( uint ) fileArray.Length, fileArray, 0) < 0) {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }

            Marshal.FreeCoTaskMem(nativeFolder);
            if ( nativeFile != IntPtr.Zero ) {
                Marshal.FreeCoTaskMem(nativeFile);
            }
        }

        public static string GetDriveLabel(string driveNameAsLetterColonBackslash) {
            IntPtr pidl;
            uint dummy;
            IntPtr ppszName;
            if ( SHParseDisplayName(driveNameAsLetterColonBackslash, IntPtr.Zero, out pidl, 0, out dummy) == 0
                && SHGetNameFromIDList(pidl, SIGDN.PARENTRELATIVEEDITING, out ppszName) == 0
                && ppszName != null ) {
                // Prevent memory leak
                var tmp = Marshal.PtrToStringUni(ppszName);
                Marshal.FreeCoTaskMem(ppszName);
                return tmp;
            }
            return null;
        }

        /// <summary>
        /// Unblocks a file from smartscreen
        /// </summary>
        /// <param name="fileName"></param>
        public static bool Unblock(string fileName) {
            return DeleteFile(fileName + ":Zone.Identifier");
        }
    }
}
