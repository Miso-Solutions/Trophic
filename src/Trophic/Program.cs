using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Trophic;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var libPath = Path.Combine(AppContext.BaseDirectory, "lib");

        if (Directory.Exists(libPath))
        {
            // Resolve managed assemblies (and satellite assemblies) from lib/
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                var asmName = new AssemblyName(args.Name);
                var name = asmName.Name;
                if (name == null) return null;

                string dllPath;
                if (asmName.CultureInfo != null && !string.IsNullOrEmpty(asmName.CultureInfo.Name))
                    dllPath = Path.Combine(libPath, asmName.CultureInfo.Name, name + ".dll");
                else
                    dllPath = Path.Combine(libPath, name + ".dll");

                return File.Exists(dllPath) ? Assembly.LoadFrom(dllPath) : null;
            };
        }

        // Playwright looks for its node driver relative to PLAYWRIGHT_DRIVER_SEARCH_PATH.
        // Without this, it searches the working directory instead of the app directory.
        Environment.SetEnvironmentVariable("PLAYWRIGHT_DRIVER_SEARCH_PATH", AppContext.BaseDirectory);

        // Strip Mark of the Web from Playwright driver files.
        // When extracted from a zip downloaded from the internet, Windows applies
        // Zone.Identifier alternate data streams that block node.exe from running.
        StripMotwFromPlaywright();

        RunApp();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFile(string lpFileName);

    private static void StripMotwFromPlaywright()
    {
        var playwrightDir = Path.Combine(AppContext.BaseDirectory, ".playwright");
        if (!Directory.Exists(playwrightDir)) return;

        try
        {
            foreach (var file in Directory.EnumerateFiles(playwrightDir, "*", SearchOption.AllDirectories))
            {
                // Delete the Zone.Identifier alternate data stream (MOTW)
                DeleteFile(file + ":Zone.Identifier");
            }
        }
        catch
        {
            // Non-critical — if stripping fails, Playwright will report its own error
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
