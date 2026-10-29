using System.Text.Json;

namespace Efr.Core;

public enum State { Applied, NotApplied, Unavailable, Unknown }
public enum OperationKind { Registry, Mouse, StickyKeys, Appx, Capability, Feature, NetFx, Power, UltimatePower, Install, Guide }
public record Detection(State State, string Detail);
public record RegTarget(string Hive, string Path, string Name, RegValue Desired);
public record RegValue(bool Exists, string Kind, string Data)
{
    public static RegValue Missing => new(false, "None", "");
    public static RegValue Dword(int value) => new(true, "DWord", JsonSerializer.Serialize(value));
    public static RegValue String(string value) => new(true, "String", JsonSerializer.Serialize(value));
}
public record Adjustment(string Id, string Name, string Category, string Description, OperationKind Kind,
    bool Admin = false, bool Conservative = false, string Restart = "Nenhum", string Undo = "Restaura o estado anterior.",
    bool Internet = false, string Target = "", IReadOnlyList<RegTarget>? Values = null,
    string? GuideUrl = null, string? UnavailableReason = null, bool EnterpriseOnly = false, bool ProOnly = false, bool Preselect = true)
{
    public string Scope => Admin ? "Computador inteiro" : "Usuário atual";
    public bool Reversible => Kind is OperationKind.Registry or OperationKind.Mouse or OperationKind.StickyKeys or OperationKind.Power or OperationKind.UltimatePower;
    public string Details => $"{Scope} · Administrador: {(Admin ? "sim" : "não")} · Efeito: {Restart}\nDesfazer: {Undo}";
}
public record MachineInfo(int Build, string Edition, string DisplayVersion, string Sid, string UserName,
    bool Admin, bool IdentityMatches, bool Managed, bool Supported, string ManagementDetail);
public record Snapshot(Dictionary<string, RegValue> Registry, string Native = "");
public class Backup
{
    public required string Id { get; init; }
    public required string Sid { get; init; }
    public required Snapshot Original { get; init; }
    public Snapshot? Expected { get; set; }
    public bool Restored { get; set; }
    public DateTimeOffset Created { get; init; } = DateTimeOffset.Now;
}
public record HistoryEntry(DateTimeOffset Time, string RunId, string OperationId, string Action, string Result, string Detail);
public class JournalData
{
    public int Schema { get; set; } = 1;
    public string Machine { get; set; } = Environment.MachineName;
    public string Sid { get; set; } = "";
    public Dictionary<string, Backup> Backups { get; set; } = [];
    public List<Backup> ArchivedBackups { get; set; } = [];
    public List<HistoryEntry> History { get; set; } = [];
}
public record OperationResult(bool Success, string Message, bool RebootRequired = false);
public record ApplyOptions(string? NetFxSource = null, bool SourceConfirmed = false);

public interface IBackend
{
    MachineInfo Info { get; }
    Task RefreshAsync();
    Task<Detection> DetectAsync(Adjustment item);
    Task<Snapshot> CaptureAsync(Adjustment item);
    Task<OperationResult> ApplyAsync(Adjustment item, ApplyOptions options);
    Task RestoreAsync(Adjustment item, Snapshot snapshot);
    void AssertCanWrite(Adjustment item);
}

public interface IJournal
{
    JournalData Data { get; }
    void Save();
}
