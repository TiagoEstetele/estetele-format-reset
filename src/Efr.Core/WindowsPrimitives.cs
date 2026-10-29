using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Efr.Core;

public static class RegistryStore
{
    public static RegistryKey Root(string hive) => RegistryKey.OpenBaseKey(hive == "HKLM" ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
    public static RegValue Read(RegTarget target)
    {
        using var root = Root(target.Hive);
        using var key = root.OpenSubKey(target.Path);
        if (key == null || !key.GetValueNames().Contains(target.Name, StringComparer.OrdinalIgnoreCase)) return RegValue.Missing;
        var kind = key.GetValueKind(target.Name);
        var value = key.GetValue(target.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new(true, kind.ToString(), JsonSerializer.Serialize(value, value?.GetType() ?? typeof(object)));
    }
    public static void Write(RegTarget target, RegValue value)
    {
        using var root = Root(target.Hive);
        if (!value.Exists)
        {
            using var existing = root.OpenSubKey(target.Path, true);
            existing?.DeleteValue(target.Name, false);
            existing?.Flush();
            return;
        }
        var kind = Enum.Parse<RegistryValueKind>(value.Kind);
        object raw = kind switch
        {
            RegistryValueKind.DWord => JsonSerializer.Deserialize<int>(value.Data),
            RegistryValueKind.QWord => JsonSerializer.Deserialize<long>(value.Data),
            RegistryValueKind.Binary or RegistryValueKind.None => JsonSerializer.Deserialize<byte[]>(value.Data)!,
            RegistryValueKind.MultiString => JsonSerializer.Deserialize<string[]>(value.Data)!,
            RegistryValueKind.String or RegistryValueKind.ExpandString => JsonSerializer.Deserialize<string>(value.Data)!,
            _ => throw new NotSupportedException("Tipo de Registro não suportado: " + kind)
        };
        using var key = root.CreateSubKey(target.Path, true);
        key.SetValue(target.Name, raw, kind);
        key.Flush();
    }
    public static bool Exists(string hive, string path)
    {
        using var root = Root(hive);
        using var key = root.OpenSubKey(path);
        return key != null && (key.ValueCount > 0 || key.SubKeyCount > 0);
    }
    public static string Text(string hive, string path, string value)
    {
        using var root = Root(hive);
        using var key = root.OpenSubKey(path);
        return key?.GetValue(value)?.ToString() ?? "";
    }
}

public static class Native
{
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SystemParametersInfoW(uint action, uint param, IntPtr value, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    [DllImport("netapi32.dll", CharSet = CharSet.Unicode)] private static extern int NetGetJoinInformation(string? server, out IntPtr name, out int status);
    [DllImport("netapi32.dll")] private static extern int NetApiBufferFree(IntPtr memory);

    public static int[] Mouse()
    {
        var memory = Marshal.AllocHGlobal(12);
        try { Check(SystemParametersInfoW(3, 0, memory, 0)); var value = new int[3]; Marshal.Copy(memory, value, 0, 3); return value; }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public static void SetMouse(int[] value)
    {
        if (value.Length != 3) throw new ArgumentException("Estado do mouse inválido.");
        var memory = Marshal.AllocHGlobal(12);
        try { Marshal.Copy(value, 0, memory, 3); Check(SystemParametersInfoW(4, 0, memory, 3)); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public static int Sticky()
    {
        var memory = Marshal.AllocHGlobal(8);
        try { Marshal.WriteInt32(memory, 8); Check(SystemParametersInfoW(0x003A, 8, memory, 0)); return Marshal.ReadInt32(memory, 4); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    public static void SetSticky(int flags)
    {
        var memory = Marshal.AllocHGlobal(8);
        try { Marshal.WriteInt32(memory, 8); Marshal.WriteInt32(memory, 4, flags); Check(SystemParametersInfoW(0x003B, 8, memory, 3)); }
        finally { Marshal.FreeHGlobal(memory); }
    }
    private static void Check(bool success) { if (!success) throw new Win32Exception(Marshal.GetLastWin32Error()); }
    public static string ShellSid()
    {
        GetWindowThreadProcessId(GetShellWindow(), out var pid);
        if (pid == 0) return "";
        var process = OpenProcess(0x1000, false, pid);
        if (process == IntPtr.Zero) return "";
        try
        {
            if (!OpenProcessToken(process, 8, out var token)) return "";
            try { using var identity = new WindowsIdentity(token); return identity.User?.Value ?? ""; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
    public static MachineInfo Machine(string? expectedSid)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var sid = identity.User?.Value ?? "";
        var admin = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        const string version = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";
        int.TryParse(RegistryStore.Text("HKLM", version, "CurrentBuildNumber"), out var build);
        var managed = true;
        var detail = "Não foi possível verificar gerenciamento; alterações bloqueadas.";
        var code = NetGetJoinInformation(null, out var buffer, out var join);
        if (buffer != IntPtr.Zero) NetApiBufferFree(buffer);
        if (code == 0)
        {
            managed = join == 3 || RegistryStore.Exists("HKLM", @"SYSTEM\CurrentControlSet\Control\CloudDomainJoin\JoinInfo")
                || RegistryStore.Exists("HKLM", @"SOFTWARE\Microsoft\Provisioning\OMADM\Accounts")
                || HasLocalPolicy();
            detail = managed ? "Domínio, MDM ou política local configurada detectada; alterações bloqueadas neste aplicativo pessoal." : "Nenhum vínculo de domínio/MDM ou política local configurada detectado.";
        }
        return new(build, RegistryStore.Text("HKLM", version, "EditionID"), RegistryStore.Text("HKLM", version, "DisplayVersion"), sid, identity.Name,
            admin, sid.Length > 0 && sid == ShellSid() && (expectedSid == null || expectedSid == sid), managed,
            build >= 22000 && RuntimeInformation.OSArchitecture == Architecture.X64 && RegistryStore.Text("HKLM", version, "InstallationType") == "Client", detail);
    }
    private static bool HasLocalPolicy()
    {
        // GPO History keys also exist on unmanaged PCs. Their presence is not proof of management.
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        try
        {
            foreach (var relative in new[] { @"GroupPolicy\Machine\Registry.pol", @"GroupPolicy\User\Registry.pol" })
            {
                var file = new FileInfo(Path.Combine(system, relative));
                if (file.Exists && file.Length > 8) return true;
            }
            var users = Path.Combine(system, "GroupPolicyUsers");
            return Directory.Exists(users) && Directory.EnumerateFiles(users, "Registry.pol", SearchOption.AllDirectories).Any(p => new FileInfo(p).Length > 8);
        }
        catch (UnauthorizedAccessException) { return true; }
        catch (IOException) { return true; }
    }
}

public record CommandResult(int ExitCode, string Output, string Error);
public static class Commands
{
    public static async Task<CommandResult> RunAsync(string executable, IEnumerable<string> arguments, IReadOnlyDictionary<string, string>? environment = null, bool readOnly = false)
    {
        var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        if (environment != null) foreach (var kv in environment) info.Environment[kv.Key] = kv.Value;
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new IOException("Não foi possível iniciar " + Path.GetFileName(executable));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        if (readOnly)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("A consulta ao Windows excedeu 2 minutos."); }
        }
        else await process.WaitForExitAsync(); // Never terminate DISM/installers halfway through a write.
        return new(process.ExitCode, await output, await error);
    }
    public static string SystemTool(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    public static async Task<string> PowerShellAsync(string trustedScript, IReadOnlyDictionary<string, string>? environment = null, bool readOnly = false)
    {
        // Data enters through environment variables, never through executable script interpolation.
        var script = "[Console]::OutputEncoding=[System.Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue'; try { "
            + trustedScript + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        var result = await RunAsync(SystemTool(@"WindowsPowerShell\v1.0\powershell.exe"),
            ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", Convert.ToBase64String(Encoding.Unicode.GetBytes(script))], environment, readOnly);
        if (result.ExitCode != 0) throw new IOException($"Windows PowerShell retornou {result.ExitCode}: {result.Error.Trim()} {result.Output.Trim()}");
        return result.Output.Trim();
    }
}
