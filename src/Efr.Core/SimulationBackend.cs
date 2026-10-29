namespace Efr.Core;

// Contains no calls to Windows, processes, registry or network. Used by tests and --demo.
public sealed class SimulationBackend : IBackend
{
    public MachineInfo Info { get; set; } = new(26200, "Professional", "25H2", "SIMULATION", "Simulação local", true, true, false, true, "Ambiente simulado; nenhum ajuste do Windows é executado.");
    public Dictionary<string, RegValue> Values { get; } = [];
    public Dictionary<string,string> NativeValues { get; } = [];
    public HashSet<string> Applied { get; } = [];
    public int Writes { get; private set; }
    public int FailAfterWrites { get; set; } = -1;
    public bool FailValidation { get; set; }
    public bool FailRestore { get; set; }
    public Task RefreshAsync() => Task.CompletedTask;
    public void AssertCanWrite(Adjustment item)
    {
        if (!Info.IdentityMatches) throw new UnauthorizedAccessException("Usuário incorreto.");
        if (Info.Managed) throw new UnauthorizedAccessException("Computador gerenciado.");
        if (item.Admin && !Info.Admin) throw new UnauthorizedAccessException("Administrador necessário.");
        if (!Info.Supported || item.Kind == OperationKind.Guide) throw new InvalidOperationException("Indisponível.");
    }
    public Task<Detection> DetectAsync(Adjustment item)
    {
        if (item.Kind == OperationKind.Guide) return Task.FromResult(new Detection(State.Unavailable, "Orientação manual; simulação não abre links."));
        bool applied = item.Kind == OperationKind.Registry
            ? item.Values!.All(t => Values.GetValueOrDefault(Engine.Key(t), RegValue.Missing) == t.Desired)
            : Applied.Contains(item.Id);
        if (FailValidation) applied = false;
        return Task.FromResult(new Detection(applied ? State.Applied : State.NotApplied, "Estado simulado; nada foi consultado ou alterado no Windows."));
    }
    public Task<Snapshot> CaptureAsync(Adjustment item) => Task.FromResult(new Snapshot(
        item.Values?.ToDictionary(Engine.Key, t => Values.GetValueOrDefault(Engine.Key(t), RegValue.Missing)) ?? [], NativeValues.GetValueOrDefault(item.Id, "original")));
    public Task<OperationResult> ApplyAsync(Adjustment item, ApplyOptions options)
    {
        AssertCanWrite(item);
        if (item.Values != null) foreach (var t in item.Values)
        {
            Values[Engine.Key(t)] = t.Desired;
            Writes++;
            if (FailAfterWrites == Writes) throw new IOException("Falha injetada após escrita parcial.");
        }
        else Writes++;
        if (item.Kind != OperationKind.Registry) NativeValues[item.Id] = "applied";
        Applied.Add(item.Id);
        return Task.FromResult(new OperationResult(true, "SIMULAÇÃO: operação concluída em memória."));
    }
    public Task RestoreAsync(Adjustment item, Snapshot snapshot)
    {
        AssertCanWrite(item);
        if (FailRestore) throw new IOException("Falha injetada de restauração.");
        foreach (var v in snapshot.Registry)
        {
            if (v.Value.Exists) Values[v.Key] = v.Value;
            else Values.Remove(v.Key);
        }
        NativeValues[item.Id] = snapshot.Native;
        Applied.Remove(item.Id);
        return Task.CompletedTask;
    }
}
