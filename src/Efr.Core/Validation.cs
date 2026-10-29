using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Efr.Core;

public enum CheckStatus { Pending, Running, Passed, Warning, Failed, Cancelled }
public record CheckResult(CheckStatus Status, string Detail)
{
    public static CheckResult Pass(string detail) => new(CheckStatus.Passed, detail);
    public static CheckResult Warn(string detail) => new(CheckStatus.Warning, detail);
}
public record ValidationStep(string Id, string Name, string Description, Func<Task<CheckResult>> Execute);
public record ValidationResult(string Id, string Name, CheckStatus Status, string Detail);
public record ValidationProgress(int Index, int Total, int Completed, ValidationResult Current)
{
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed / Total);
}
public record ValidationReport(string Fingerprint, DateTimeOffset CompletedAt, bool Cancelled, List<ValidationResult> Results)
{
    public bool Approved => !Cancelled && Results.Count > 0 && Results.All(r => r.Status is CheckStatus.Passed or CheckStatus.Warning);
    public bool IsCurrent(string fingerprint, DateTimeOffset now) => Approved && Fingerprint == fingerprint && now >= CompletedAt && now - CompletedAt <= TimeSpan.FromMinutes(10);
}

public static class ValidationRunner
{
    public static string Fingerprint(IEnumerable<Adjustment> items, ApplyOptions options, MachineInfo info)
    {
        var data = JsonSerializer.Serialize(new { Items = items.OrderBy(i => i.Id).Select(i => new {i.Id,i.Target,i.Kind,i.Values}), Options=options, info.Sid, info.Build, info.Edition, info.Admin, info.Managed, info.IdentityMatches, info.Supported });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(data)));
    }
    public static async Task<ValidationReport> RunAsync(IReadOnlyList<ValidationStep> steps, string fingerprint, IProgress<ValidationProgress>? progress = null, CancellationToken token = default)
    {
        List<ValidationResult> results = [];
        int completed = 0;
        for (int index = 0; index < steps.Count; index++)
        {
            var step = steps[index];
            if (token.IsCancellationRequested)
            {
                results.Add(new(step.Id,step.Name,CheckStatus.Cancelled,"Não executado: validação cancelada."));
                progress?.Report(new(index,steps.Count,completed,results[^1]));
                continue;
            }
            progress?.Report(new(index,steps.Count,completed,new(step.Id,step.Name,CheckStatus.Running,step.Description)));
            CheckResult result;
            try { result = await step.Execute(); }
            catch (Exception ex) { result = new(CheckStatus.Failed,ex.Message); }
            if (result.Status is not (CheckStatus.Passed or CheckStatus.Warning or CheckStatus.Failed)) result = new(CheckStatus.Failed,"O teste retornou um estado inconclusivo.");
            results.Add(new(step.Id,step.Name,result.Status,result.Detail));
            completed++;
            progress?.Report(new(index,steps.Count,completed,results[^1]));
        }
        return new(fingerprint,DateTimeOffset.Now,token.IsCancellationRequested,results);
    }
}

public sealed class Preflight(IBackend backend, IJournal journal, string journalDirectory, bool simulation)
{
    public IReadOnlyList<ValidationStep> Build(IReadOnlyList<Adjustment> items, ApplyOptions options)
    {
        bool contextPassed = false;
        List<ValidationStep> steps = [
            new("system.inventory","Windows, arquitetura e inventário","Atualizando build, edição, aplicativos e recursos instalados.", async () => {
                await backend.RefreshAsync();
                if (!backend.Info.Supported) throw new InvalidOperationException("O destino precisa ser Windows 11 x64.");
                return CheckResult.Pass($"Windows {backend.Info.DisplayVersion}, build {backend.Info.Build}, {backend.Info.Edition}. Inventário consultado; cada componente será verificado abaixo.");
            }),
            new("system.identity","Usuário, permissões e políticas","Conferindo SID da sessão, elevação e gerenciamento administrativo.", () => {
                var i = backend.Info;
                if (items.Count == 0) throw new InvalidOperationException("Selecione pelo menos uma configuração para validar.");
                if (!i.IdentityMatches) throw new UnauthorizedAccessException("A conta do aplicativo não corresponde ao shell. Reabra na conta correta.");
                if (i.Managed) throw new UnauthorizedAccessException(i.ManagementDetail);
                if (items.Any(x=>x.Admin) && !i.Admin) throw new UnauthorizedAccessException("A seleção exige administrador. Reabra como administrador com a mesma conta e valide novamente.");
                if (items.Count(x=>x.Kind is OperationKind.Power or OperationKind.UltimatePower)>1) throw new InvalidOperationException("Selecione somente um plano de energia.");
                contextPassed = true;
                return Task.FromResult(CheckResult.Pass("SID correto, privilégios necessários presentes e nenhum gerenciamento impeditivo detectado."));
            }),
            new("system.history","Histórico, backup e espaço livre","Testando gravação atômica do histórico do EFR e espaço disponível.", () => {
                journal.Save();
                if (!simulation) {
                    var persisted=JsonSerializer.Deserialize<JournalData>(File.ReadAllText(Path.Combine(journalDirectory,"history.json"))) ?? throw new IOException("Histórico não pôde ser relido.");
                    if(persisted.Sid!=backend.Info.Sid) throw new IOException("Histórico pertence a outra conta.");
                    var drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(journalDirectory))!);
                    long minimum = items.Any(i=>i.Kind is OperationKind.Install or OperationKind.NetFx) ? 2L*1024*1024*1024 : 10L*1024*1024;
                    if (drive.AvailableFreeSpace < minimum) throw new IOException("Espaço insuficiente para a margem de trabalho: " + minimum/(1024*1024) + " MB livres necessários.");
                }
                return Task.FromResult(CheckResult.Pass("Histórico gravado e relido pelo armazenamento; margem de espaço verificada. Não substitui backup de arquivos pessoais."));
            }),
            new("system.tools","Ferramentas do Windows e reinicialização","Conferindo ferramentas nativas e operações de sistema pendentes.", async () => simulation
                ? CheckResult.Pass("SIMULAÇÃO: ferramentas e reinicialização representadas em memória.")
                : await WindowsChecks.ToolsAsync(items))
        ];
        foreach (var item in items)
        {
            bool itemReady = false;
            steps.Add(new(item.Id+".state", item.Name+" · estado e compatibilidade", "Consultando o estado real, a presença e as restrições deste item.", async () => {
                backend.AssertCanWrite(item);
                var state = await backend.DetectAsync(item);
                if (state.State is State.Unknown or State.Unavailable) throw new InvalidOperationException(state.Detail);
                itemReady = true;
                return CheckResult.Pass((state.State == State.Applied ? "Já aplicado; será preservado sem repetir alterações. " : "Disponível para alteração. ")+state.Detail);
            }));
            steps.Add(new(item.Id+".method", item.Name+(item.Reversible ? " · backup e acesso" : " · método e dependências"),
                item.Kind == OperationKind.Install ? "Baixando o instalador oficial, verificando integridade e assinatura. Nenhum instalador será executado nesta fase." : "Verificando o método e os pré-requisitos sem aplicar a alteração.", async () => {
                    if (!contextPassed || !itemReady) throw new InvalidOperationException("Teste bloqueado por falha de contexto ou compatibilidade acima. Corrija e valide novamente.");
                    if (simulation) {
                        if (item.Reversible) _ = await backend.CaptureAsync(item);
                        return CheckResult.Pass("SIMULAÇÃO: método validado pelo backend em memória. Nenhuma verificação do Windows ou download real.");
                    }
                    return await WindowsChecks.ItemAsync(backend,item,options);
                }));
        }
        return steps;
    }
    public IReadOnlyList<ValidationStep> PostChecks(IEnumerable<Adjustment> items, bool pendingRestart) => items.Select(item =>
        new ValidationStep(item.Id+".result",item.Name+" · resultado efetivo","Lendo novamente o estado final do Windows após a aplicação.",async () => {
            var state = await backend.DetectAsync(item);
            if (state.State == State.Applied) return CheckResult.Pass(state.Detail);
            if (pendingRestart && state.Detail.Contains("pendente",StringComparison.OrdinalIgnoreCase)) return CheckResult.Warn("Confirmação definitiva pendente de reinicialização. "+state.Detail);
            return new(CheckStatus.Failed,"O resultado esperado não foi confirmado: "+state.Detail);
        })).ToList();
}
