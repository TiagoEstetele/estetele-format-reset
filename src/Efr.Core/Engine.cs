namespace Efr.Core;

// All mutation paths pass through this coordinator, including simulation.
public sealed class Engine(IBackend backend, IJournal journal)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public JournalData History => journal.Data;
    public async Task<Detection> DetectAsync(Adjustment item)
    {
        try { return await backend.DetectAsync(item); }
        catch (Exception ex) { return new(State.Unknown, ex.Message); }
    }
    public bool HasBackup(string id) => journal.Data.Backups.TryGetValue(id, out var b) && !b.Restored;
    public async Task<OperationResult> ApplyAsync(Adjustment item, ApplyOptions options, string run)
    {
        await gate.WaitAsync();
        Snapshot? before = null;
        bool started = false;
        try
        {
            backend.AssertCanWrite(item);
            var state = await backend.DetectAsync(item);
            if (state.State is State.Unknown or State.Unavailable) throw new InvalidOperationException(state.Detail);
            if (state.State == State.Applied)
            {
                Log(run, item, "Aplicar", "Sem alteração", "Já estava aplicado; backup original preservado.");
                return new(true, "Já aplicado. Nenhuma alteração necessária.");
            }
            if (item.Reversible)
            {
                before = await backend.CaptureAsync(item);
                if (!journal.Data.Backups.TryGetValue(item.Id, out var existing) || existing.Restored)
                {
                    if (existing != null) journal.Data.ArchivedBackups.Add(existing);
                    journal.Data.Backups[item.Id] = new Backup { Id = item.Id, Sid = backend.Info.Sid, Original = before };
                }
                else if (existing.Expected != null && !Same(before, existing.Expected))
                    throw new InvalidOperationException("O estado mudou fora do aplicativo. Restaure/revise o histórico antes de reaplicar; backup preservado.");
                else if (existing.Expected == null && !SafeInterrupted(item, before, existing.Original))
                    throw new InvalidOperationException("Operação anterior interrompida com estado divergente. Reaplicação bloqueada; revise o histórico para preservar mudanças externas.");
            }
            // A durable intent + backup must exist BEFORE any system write.
            Log(run, item, "Aplicar", "Iniciado", item.Description);
            started = true;
            var result = await backend.ApplyAsync(item, options);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            await backend.RefreshAsync();
            var after = await backend.DetectAsync(item);
            if (after.State != State.Applied && !result.RebootRequired)
                throw new InvalidOperationException("A validação final não confirmou o ajuste: " + after.Detail);
            if (item.Reversible) journal.Data.Backups[item.Id].Expected = await backend.CaptureAsync(item);
            Log(run, item, "Aplicar", result.RebootRequired ? "Pendente de reinicialização" : "Concluído", result.Message);
            return result;
        }
        catch (Exception ex)
        {
            var message = ex.Message;
            if (started && before != null)
            {
                try
                {
                    await backend.RestoreAsync(item, before);
                    if (!Same(before, await backend.CaptureAsync(item))) throw new IOException("Rollback não confirmado.");
                    journal.Data.Backups[item.Id].Expected = before;
                    message += " Estado anterior à tentativa recuperado.";
                }
                catch (Exception rollback) { message += " Falha ao recuperar: " + rollback.Message + ". Use o histórico para revisar."; }
            }
            try { Log(run, item, "Aplicar", "Falhou", message); }
            catch (Exception save) { message += " Histórico não pôde ser salvo: " + save.Message; }
            return new(false, message);
        }
        finally { gate.Release(); }
    }
    public async Task<OperationResult> UndoAsync(Adjustment item, string run)
    {
        await gate.WaitAsync();
        try
        {
            backend.AssertCanWrite(item);
            if (!journal.Data.Backups.TryGetValue(item.Id, out var backup) || backup.Restored)
                return new(false, "Nenhum backup ativo deste ajuste.");
            if (backup.Sid != backend.Info.Sid) throw new InvalidOperationException("SID do backup não corresponde ao usuário atual.");
            var now = await backend.CaptureAsync(item);
            // Recover interrupted writes only if every field is original or a value this operation owns.
            if (backup.Expected != null && !Same(now, backup.Expected) && !Same(now, backup.Original) && !SafePartialRestore(now, backup.Original, backup.Expected))
                throw new InvalidOperationException("O ajuste foi alterado fora do aplicativo. Restauração bloqueada para preservar essa mudança.");
            if (backup.Expected == null && !SafeInterrupted(item, now, backup.Original))
                throw new InvalidOperationException("Operação interrompida com estado divergente. Revise history.json antes de recuperar manualmente.");
            Log(run, item, "Desfazer", "Iniciado", "Restaurando backup original.");
            await backend.RestoreAsync(item, backup.Original);
            if (!Same(await backend.CaptureAsync(item), backup.Original)) throw new IOException("Restauração não confirmada pela leitura final.");
            backup.Restored = true;
            Log(run, item, "Desfazer", "Concluído", "Estado original restaurado. " + item.Restart);
            return new(true, "Estado original restaurado. Efeito: " + item.Restart);
        }
        catch (Exception ex)
        {
            try { Log(run, item, "Desfazer", "Falhou", ex.Message); } catch { /* Original journal remains intact. */ }
            return new(false, ex.Message);
        }
        finally { gate.Release(); }
    }
    public static bool Same(Snapshot a, Snapshot b) => a.Native == b.Native && a.Registry.Count == b.Registry.Count
        && a.Registry.All(kv => b.Registry.TryGetValue(kv.Key, out var value) && value == kv.Value);
    private static bool SafePartialRestore(Snapshot now, Snapshot original, Snapshot expected)
        => now.Registry.Count == original.Registry.Count && (now.Native == original.Native || now.Native == expected.Native)
            && now.Registry.All(kv => original.Registry.TryGetValue(kv.Key, out var o) && expected.Registry.TryGetValue(kv.Key, out var e) && (kv.Value == o || kv.Value == e));
    private static bool SafeInterrupted(Adjustment item, Snapshot now, Snapshot original)
    {
        if (Same(now, original)) return true;
        if (item.Kind != OperationKind.Registry || item.Values == null) return false;
        return item.Values.All(v => now.Registry.TryGetValue(Key(v), out var n) &&
            (n == v.Desired || (original.Registry.TryGetValue(Key(v), out var o) && n == o)));
    }
    public static string Key(RegTarget target) => $"{target.Hive}\\{target.Path}|{target.Name}";
    private void Log(string run, Adjustment item, string action, string result, string detail)
    {
        journal.Data.History.Add(new(DateTimeOffset.Now, run, item.Id, action, result, detail));
        journal.Save();
    }
}
