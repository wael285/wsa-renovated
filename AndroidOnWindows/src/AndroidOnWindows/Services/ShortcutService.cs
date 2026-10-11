using System.Runtime.InteropServices;

namespace AndroidOnWindows.Services;

/// <summary>Creates real Windows shortcuts so the app behaves like a normal program.</summary>
public static class ShortcutService
{
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszFile, int cch, IntPtr pfd, int fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszName, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszDir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszArgs, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder pszIconPath, int cch, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
        void Resolve(IntPtr hwnd, int fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string? pszFileName, bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
    }

    public static string CreateShortcut(string exePath, string args, string name, string? iconPath = null)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        var lnkPath = Path.Combine(programs, name + ".lnk");

        var link = (IShellLinkW)new ShellLink();
        link.SetPath(exePath);
        link.SetArguments(args);
        link.SetWorkingDirectory(Path.GetDirectoryName(exePath)!);
        link.SetDescription("Android on Windows");
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            link.SetIconLocation(iconPath, 0);

        ((IPersistFile)link).Save(lnkPath, true);
        Log.Info($"start menu shortcut created: {lnkPath}");
        return lnkPath;
    }

    /// <summary>Attempts to pin to the taskbar via the shell verb (may be blocked by Windows policy).</summary>
    public static bool TryPinToTaskbar(string lnkPath)
    {
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
            var folder = shell.NameSpace(Path.GetDirectoryName(lnkPath)!);
            var item = folder.ParseName(Path.GetFileName(lnkPath));
            foreach (dynamic verb in item.Verbs())
            {
                string vname = verb.Name.Replace("&", "");
                if (vname.Contains("taskbar", StringComparison.OrdinalIgnoreCase) ||
                    vname.Contains("Pin", StringComparison.OrdinalIgnoreCase))
                {
                    verb.DoIt();
                    return true;
                }
            }
        }
        catch (Exception ex) { Log.Warn($"taskbar pin failed: {ex.Message}"); }
        return false;
    }
}
