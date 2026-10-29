using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace Efr.Core;

public record PowerPlan(string Id, string Name);
public static class PowerPlans
{
    public const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    public const string ManagedUltimate = "8f7d9310-b065-4a21-92df-28f4da3abca1";
    public sealed record SavedState(string Active, bool ManagedPlanExisted);
    [DllImport("powrprof.dll")] private static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerDeleteScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] private static extern uint PowerEnumerate(IntPtr root, IntPtr scheme, IntPtr subgroup, uint access, uint index, byte[] buffer, ref uint size);
    [DllImport("powrprof.dll")] private static extern uint PowerReadFriendlyName(IntPtr root, ref Guid scheme, IntPtr subgroup, IntPtr setting, byte[]? buffer, ref uint size);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static string Active()
    {
        Check(PowerGetActiveScheme(IntPtr.Zero, out var pointer));
        try { return Marshal.PtrToStructure<Guid>(pointer).ToString(); }
        finally { LocalFree(pointer); }
    }
    public static List<PowerPlan> List()
    {
        List<PowerPlan> result = [];
        for (uint i = 0; ; i++)
        {
            uint size = 16;
            var buffer = new byte[16];
            var code = PowerEnumerate(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 16, i, buffer, ref size);
            if (code == 259) break;
            Check(code);
            var guid = new Guid(buffer);
            uint textSize = 0;
            var nameCode = PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, null, ref textSize);
            if (nameCode != 0 && nameCode != 234) Check(nameCode);
            var text = new byte[textSize];
            Check(PowerReadFriendlyName(IntPtr.Zero, ref guid, IntPtr.Zero, IntPtr.Zero, text, ref textSize));
            result.Add(new(guid.ToString(), Encoding.Unicode.GetString(text).TrimEnd('\0')));
        }
        return result;
    }
    public static void Set(string id)
    {
        if (!List().Any(p => p.Id == id)) throw new InvalidOperationException("O plano de energia não está mais disponível.");
        var guid = Guid.Parse(id);
        Check(PowerSetActiveScheme(IntPtr.Zero, ref guid));
        if (Active() != id) throw new IOException("O Windows não confirmou o plano de energia.");
    }
    public static string? ExistingUltimate() => List().OrderBy(p => p.Id == UltimateTemplate ? 0 : p.Id == ManagedUltimate ? 1 : 2)
        .FirstOrDefault(p => p.Id is UltimateTemplate or ManagedUltimate || p.Name.Equals("Desempenho Máximo", StringComparison.OrdinalIgnoreCase)
            || p.Name.Equals("Máximo Desempenho", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Ultimate Performance", StringComparison.OrdinalIgnoreCase))?.Id;
    public static bool IsUltimateActive() => Active() == ExistingUltimate();
    public static async Task ActivateUltimateAsync()
    {
        var existing = ExistingUltimate();
        if (existing != null) { Set(existing); return; }
        var plans = List();
        if (!plans.Any(p => p.Id == ManagedUltimate))
        {
            var result = await Commands.RunAsync(Commands.SystemTool("powercfg.exe"), ["/duplicatescheme", UltimateTemplate, ManagedUltimate]);
            if (result.ExitCode != 0 || !List().Any(p => p.Id == ManagedUltimate))
                throw new InvalidOperationException($"O Windows não disponibilizou Desempenho Máximo (código {result.ExitCode}). O hardware/Modern Standby pode limitar planos. Nenhuma política de hardware será contornada. {result.Error.Trim()}");
        }
        Set(ManagedUltimate);
    }
    public static void RestoreUltimate(SavedState state)
    {
        Set(state.Active);
        if (!state.ManagedPlanExisted && List().Any(p => p.Id == ManagedUltimate))
        {
            var guid = Guid.Parse(ManagedUltimate);
            Check(PowerDeleteScheme(IntPtr.Zero, ref guid));
        }
    }
    private static void Check(uint code) { if (code != 0) throw new Win32Exception((int)code); }
}
