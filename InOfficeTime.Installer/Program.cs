using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Security.Principal;

namespace InOfficeTime.Installer;

internal static class Program
{
    private const string ServiceName = "InOfficeTime";
    private const string DisplayName = "InOffice Time Tracker";
    private const string ExeName = "InOfficeTime.exe";
    private const string PayloadResourceName = "payload.zip";

    // Files we must NOT overwrite on reinstall/upgrade so the admin's edits survive.
    private static readonly HashSet<string> PreserveOnUpgrade = new(StringComparer.OrdinalIgnoreCase)
    {
        "appsettings.json",
    };

    private static string InstallDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "InOfficeTime");

    public static int Main(string[] args)
    {
        try
        {
            EnsureAdmin();

            var uninstall = args.Any(a =>
                a.Equals("/u", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("-u", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase));

            var code = uninstall ? Uninstall() : Install();
            Pause();
            return code;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine("Installation FAILED: " + ex.Message);
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine(ex);
            Pause();
            return 1;
        }
    }

    private static int Install()
    {
        PrintHeader("InOffice Time Tracker - Setup");
        Console.WriteLine($"Install path : {InstallDir}");
        Console.WriteLine($"Service name : {ServiceName}  (\"{DisplayName}\")");
        Console.WriteLine($"Listening on : http://localhost:11000");
        Console.WriteLine($"Log folder   : C:\\ProgramData\\InOfficeTime");
        Console.WriteLine();

        StopAndDeleteServiceIfExists();
        ExtractPayload(InstallDir);

        var exePath = Path.Combine(InstallDir, ExeName);
        if (!File.Exists(exePath))
            throw new FileNotFoundException($"Service executable not found after extraction: {exePath}");

        CreateService(exePath);
        StartService();

        Console.WriteLine();
        PrintOk("Installation complete.");
        Console.WriteLine("  Open http://localhost:11000/time to query work time.");
        Console.WriteLine("  Lock/unlock your PC to generate the first session entry.");
        return 0;
    }

    private static int Uninstall()
    {
        PrintHeader("InOffice Time Tracker - Uninstall");

        StopAndDeleteServiceIfExists();

        if (Directory.Exists(InstallDir))
        {
            Console.WriteLine($"Removing {InstallDir}...");
            try
            {
                Directory.Delete(InstallDir, recursive: true);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  Warning: could not remove install folder: {ex.Message}");
            }
        }

        Console.WriteLine();
        PrintOk("Uninstall complete.");
        Console.WriteLine("  Log files in C:\\ProgramData\\InOfficeTime have been kept.");
        return 0;
    }

    private static void ExtractPayload(string target)
    {
        Console.WriteLine("Extracting files...");
        Directory.CreateDirectory(target);

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(PayloadResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{PayloadResourceName}' is missing from the installer.");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var targetRoot = Path.GetFullPath(target);

        foreach (var entry in archive.Entries)
        {
            var dest = Path.GetFullPath(Path.Combine(target, entry.FullName));
            if (!dest.StartsWith(targetRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Unsafe zip entry path: {entry.FullName}");

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(dest);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

            if (PreserveOnUpgrade.Contains(entry.FullName) && File.Exists(dest))
            {
                Console.WriteLine($"  Keeping existing {entry.FullName} (preserving admin edits).");
                continue;
            }

            using var es = entry.Open();
            using var fs = File.Create(dest);
            es.CopyTo(fs);
        }
    }

    private static void StopAndDeleteServiceIfExists()
    {
        if (!ServiceExists())
            return;

        Console.WriteLine($"Stopping existing service {ServiceName}...");
        Exec("sc.exe", ["stop", ServiceName]);
        WaitForServiceStopped(TimeSpan.FromSeconds(20));

        Console.WriteLine($"Removing existing service {ServiceName}...");
        Exec("sc.exe", ["delete", ServiceName]);
        Thread.Sleep(500);
    }

    private static bool ServiceExists()
    {
        var (exit, _) = Exec("sc.exe", ["query", ServiceName], silent: true);
        return exit == 0;
    }

    private static void WaitForServiceStopped(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var (exit, output) = Exec("sc.exe", ["query", ServiceName], silent: true);
            if (exit != 0)
                return;
            if (output.Contains("STOPPED", StringComparison.OrdinalIgnoreCase))
                return;
            Thread.Sleep(500);
        }
    }

    private static void CreateService(string exePath)
    {
        Console.WriteLine($"Registering service (binPath = \"{exePath}\")...");
        var (exit, output) = Exec("sc.exe",
            ["create", ServiceName,
             "binPath=", exePath,
             "start=", "auto",
             "DisplayName=", DisplayName]);

        if (exit != 0)
            throw new InvalidOperationException($"sc create failed (exit {exit}): {output}");

        Exec("sc.exe",
            ["description", ServiceName,
             "Tracks work session time and exposes a local HTTP API on port 11000."],
            silent: true);
    }

    private static void StartService()
    {
        Console.WriteLine("Starting service...");
        var (exit, output) = Exec("sc.exe", ["start", ServiceName]);
        if (exit != 0)
            throw new InvalidOperationException($"sc start failed (exit {exit}): {output}");
    }

    private static (int ExitCode, string Output) Exec(string file, IEnumerable<string> args, bool silent = false)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit();

        if (!silent && !string.IsNullOrWhiteSpace(output))
        {
            foreach (var line in output.Split(['\n'], StringSplitOptions.RemoveEmptyEntries))
                Console.WriteLine("  " + line.TrimEnd('\r'));
        }

        return (p.ExitCode, output);
    }

    private static void EnsureAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
            throw new UnauthorizedAccessException(
                "This installer must run as Administrator (right-click -> Run as administrator).");
    }

    private static void PrintHeader(string title)
    {
        Console.WriteLine(new string('=', title.Length + 4));
        Console.WriteLine($"  {title}  ");
        Console.WriteLine(new string('=', title.Length + 4));
        Console.WriteLine();
    }

    private static void PrintOk(string text)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(text);
        Console.ForegroundColor = prev;
    }

    private static void Pause()
    {
        Console.WriteLine();
        Console.WriteLine("Press any key to close...");
        try
        {
            Console.ReadKey(intercept: true);
        }
        catch
        {
            // input redirected / no console
        }
    }
}
