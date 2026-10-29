using System.Text.Json;

namespace Efr.Core;

public sealed class Journal : IJournal, IDisposable
{
    private readonly string path;
    private readonly FileStream ownership;
    public JournalData Data { get; }
    public string DirectoryPath { get; }
    public Journal(string directory, string sid)
    {
        DirectoryPath = directory;
        Directory.CreateDirectory(directory);
        ownership = new FileStream(Path.Combine(directory, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        path = Path.Combine(directory, "history.json");
        try
        {
            Data = File.Exists(path)
                ? JsonSerializer.Deserialize<JournalData>(File.ReadAllText(path)) ?? throw new IOException("Histórico vazio ou inválido.")
                : new JournalData { Sid = sid };
            if (Data.Schema != 1 || Data.Sid != sid || Data.Machine != Environment.MachineName)
                throw new IOException("O histórico pertence a outro usuário/computador ou tem formato incompatível. Não será sobrescrito.");
            if (Data.Backups == null || Data.ArchivedBackups == null || Data.History == null
                || Data.Backups.Any(kv => kv.Value == null || kv.Key != kv.Value.Id || !ValidBackup(kv.Value, sid))
                || Data.ArchivedBackups.Any(b => b == null || !ValidBackup(b, sid))
                || Data.History.Any(h => h == null))
                throw new IOException("O histórico contém registros inválidos. Não será sobrescrito.");
        }
        catch { ownership.Dispose(); throw; }
    }
    private static bool ValidBackup(Backup backup, string sid) => backup.Sid == sid
        && !string.IsNullOrWhiteSpace(backup.Id) && ValidSnapshot(backup.Original)
        && (backup.Expected == null || ValidSnapshot(backup.Expected));
    private static bool ValidSnapshot(Snapshot? snapshot) => snapshot?.Registry != null && snapshot.Native != null
        && snapshot.Registry.All(kv => kv.Value != null && kv.Value.Kind != null && kv.Value.Data != null);
    public void Save()
    {
        var temporary = path + ".pending";
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, Data, new JsonSerializerOptions { WriteIndented = true });
            stream.Flush(true);
        }
        if (File.Exists(path)) File.Replace(temporary, path, path + ".previous", true);
        else File.Move(temporary, path);
    }
    public void Dispose() => ownership.Dispose();
}
