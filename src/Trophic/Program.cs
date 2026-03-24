using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

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

        RunApp();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RunApp()
    {
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
