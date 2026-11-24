using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

internal static class Host
{
    private const BindingFlags Static = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int Main(string[] args)
    {
        try
        {
            var gameDirectory = args.Length == 2 && args[0] == "--game"
                ? Path.GetDirectoryName(Path.GetFullPath(args[1])) : null;
            AssemblyLoadContext.Default.Resolving += (context, name) =>
            {
                if (name.Name is null || name.Name.StartsWith("System.", StringComparison.Ordinal))
                    return null;
                // Prepared game data and the pinned framework reference the original assembly identity.
                string filename = name.Name == "SOR4Bridge" ? "SORAGE4Bridge" : name.Name;
                foreach (string directory in new[] { AppContext.BaseDirectory, gameDirectory })
                {
                    if (directory == null)
                        continue;
                    string candidate = Path.Combine(directory, filename + ".dll");
                    if (File.Exists(candidate))
                        return context.LoadFromAssemblyPath(candidate);
                }
                return null;
            };
            AppDomain.CurrentDomain.AssemblyLoad += (_, loaded) =>
            {
                if (loaded.LoadedAssembly.GetName().Name == "MonoGame.Framework")
                    NativeLibrary.SetDllImportResolver(loaded.LoadedAssembly, ResolveFrameworkLibrary);
            };
            if (args.Length >= 2 && args[0] == "--run-dll")
            {
                var tool = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(args[1]));
                var entry = tool.EntryPoint ?? throw new InvalidOperationException("Setup tool has no entry point");
                return entry.Invoke(null, entry.GetParameters().Length == 0 ? null : new object[] { args[2..] })
                    is int status ? status : 0;
            }
            if (gameDirectory != null)
                return RunGame(args[1]);
            Console.Error.WriteLine("Usage: sorage4 --game SOR4.dll | --run-dll TOOL.dll [args]");
            return 2;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine("[game host] " + (error.InnerException ?? error));
            return 1;
        }
    }

    private static IntPtr ResolveFrameworkLibrary(string name, Assembly _, DllImportSearchPath? __)
    {
        // The pinned MonoGame build uses the original import name and symbol names.
        return name == "sor4astc"
            ? NativeLibrary.Load(Path.Combine(AppContext.BaseDirectory, "..", "libs.aarch64", "libsorage4astc.so"))
            : IntPtr.Zero;
    }

    private static int RunGame(string path)
    {
        var gameAssembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
        gameAssembly.GetType("CommonLib.utils", true)!.GetMethod("set_as_main_thread", Static)!.Invoke(null, null);
        gameAssembly.GetType("BeatThemAll.MetaGame.program", true)!.GetMethod("static_init", Static)!.Invoke(null, null);

        var reflection = gameAssembly.GetType("CommonLib.reflection", true)!;
        var activity = gameAssembly.GetType("SOR4.Android.MainActivity", true)!;
        foreach (string name in new[] { "delegate_serialize", "delegate_deserialize", "delegate_deep_clone" })
        {
            var field = reflection.GetField(name, Static)!;
            field.SetValue(null, Delegate.CreateDelegate(field.FieldType, activity.GetMethod(name, Static)!));
        }

        var xna = gameAssembly.GetType("CommonLib.xna", true)!;
        xna.GetMethod("CreateGame", Static)!.Invoke(null, null);
        var game = xna.GetField("game", Static)!.GetValue(null)!;
        Console.Error.WriteLine("[game host] Starting Streets of Rage 4");
        game.GetType().GetMethod("Run", Type.EmptyTypes)!.Invoke(game, null);
        return 0;
    }
}
