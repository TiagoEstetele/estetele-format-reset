using System.Text.Json;
using Microsoft.Win32;
using System.Security.AccessControl;

namespace Efr.Core;

public static class WindowsChecks
{
    public static async Task<CheckResult> ToolsAsync(IReadOnlyList<Adjustment> items)
    {
        var commands = new List<string>();
        if (items.Any(i=>i.Kind==OperationKind.Appx)) commands.AddRange(["Get-AppxPackage","Remove-AppxPackage"]);
        if (items.Any(i=>i.Kind is OperationKind.Feature or OperationKind.NetFx)) commands.AddRange(["Get-WindowsOptionalFeature","Enable-WindowsOptionalFeature","Disable-WindowsOptionalFeature"]);
        if (items.Any(i=>i.Kind==OperationKind.Capability)) commands.AddRange(["Get-WindowsCapability","Remove-WindowsCapability"]);
        if (items.Any(i=>i.Kind==OperationKind.Install)) commands.Add("Get-AuthenticodeSignature");
        if (commands.Count>0) await Commands.PowerShellAsync("foreach($name in ($env:EFR_COMMANDS -split ';')) { $null = Get-Command -Name $name -ErrorAction Stop }; 'Ferramentas disponíveis.'",
            new Dictionary<string,string>{["EFR_COMMANDS"]=string.Join(';',commands)},true);
        if (items.Any(i=>i.Kind is OperationKind.Feature or OperationKind.Capability or OperationKind.NetFx or OperationKind.Install))
        {
            using var machine = RegistryStore.Root("HKLM");
            using var servicing = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            using var update = machine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            if (servicing != null || update != null) throw new InvalidOperationException("O Windows tem reinicialização pendente. Reinicie manualmente antes de instalar/remover componentes e valide novamente.");
        }
        return CheckResult.Pass(commands.Count == 0 ? "Esta seleção usa APIs locais; nenhuma ferramenta externa é necessária." : "Cmdlets necessários disponíveis. Nenhuma reinicialização impeditiva detectada para a seleção.");
    }
    public static async Task<CheckResult> ItemAsync(IBackend backend, Adjustment item, ApplyOptions options)
    {
        if (item.Reversible)
        {
            var snapshot = await backend.CaptureAsync(item);
            var roundTrip = JsonSerializer.Deserialize<Snapshot>(JsonSerializer.Serialize(snapshot)) ?? throw new IOException("Snapshot não serializável.");
            if (!Engine.Same(snapshot,roundTrip)) throw new IOException("O snapshot perdeu informações de tipo/conteúdo.");
            if (item.Values != null) foreach(var target in item.Values) CheckRegistryAccess(target);
        }
        var state = await backend.DetectAsync(item);
        if (state.State==State.Applied) return CheckResult.Pass("Estado já satisfeito; nenhuma alteração, remoção ou instalação adicional será executada.");
        if (item.Kind==OperationKind.Install)
        {
            var prepared = await Installers.PrepareAsync(item.Target);
            return CheckResult.Pass($"Instalador oficial baixado e verificado. Publicador: {prepared.Publisher}. SHA-256: {prepared.Hash}. Arquivo em cache para a aplicação; ainda não executado.");
        }
        if (item.Kind==OperationKind.NetFx)
        {
            if (backend.Info.Build >= 28000) throw new InvalidOperationException("Esta build exige instalador separado; consulte a orientação oficial.");
            if (!string.IsNullOrWhiteSpace(options.NetFxSource))
            {
                var source=Path.GetFullPath(options.NetFxSource);
                if (!options.SourceConfirmed || !Directory.Exists(source)) throw new IOException("Fonte NetFx não confirmada ou inacessível.");
                var files=Directory.GetFiles(source,"*netfx3*.cab");
                if (files.Length==0) throw new IOException("A fonte não contém pacotes NetFx3 CAB.");
                foreach(var file in files) { using var stream=File.OpenRead(file); if(stream.Length==0) throw new IOException("Pacote CAB vazio: "+file); }
                return CheckResult.Warn("Fonte e CABs acessíveis. A compatibilidade exata com o servicing stack só é confirmada pelo DISM ao habilitar; o teste não instala pacotes.");
            }
            await Commands.PowerShellAsync("""
                $service = Get-Service -Name wuauserv -ErrorAction Stop
                if ($service.StartType -eq 'Disabled') { throw 'Windows Update desabilitado. Habilite-o pelas opções administrativas suportadas ou escolha uma fonte local compatível.' }
                $session = New-Object -ComObject Microsoft.Update.Session
                $searcher = $session.CreateUpdateSearcher()
                $searcher.Online = $true
                $null = $searcher.Search("IsInstalled=0 and Type='Software'")
                """, readOnly:true);
            return CheckResult.Warn("Consulta online ao serviço de atualização concluída, sem baixar/instalar atualizações. O fornecimento do payload NetFx3 só pode ser confirmado ao habilitar o recurso.");
        }
        if (item.Kind==OperationKind.UltimatePower && PowerPlans.ExistingUltimate()==null)
        {
            var result = await Commands.RunAsync(Commands.SystemTool("powercfg.exe"),["/query",PowerPlans.UltimateTemplate],readOnly:true);
            return result.ExitCode==0
                ? CheckResult.Pass("Modelo de energia consultado e plano anterior capturado. A ativação será conferida após aplicar.")
                : CheckResult.Warn("Plano máximo ainda não existe e a consulta não confirmou o modelo. A criação só pode ser testada aplicando. O Windows pode recusá-la; nesse caso o plano anterior será preservado. Nenhuma configuração de hardware foi alterada.");
        }
        return CheckResult.Pass(item.Reversible
            ? "Snapshot completo serializado sem perdas; valores/API consultados e acesso às chaves verificado sem modificá-las. Efeito final será conferido após aplicar."
            : "Identidade exata e estado do componente verificados. Cmdlets conferidos no teste global. A recuperação desta remoção é manual; nenhuma remoção foi executada no teste.");
    }
    private static void CheckRegistryAccess(RegTarget target)
    {
        using var root=RegistryStore.Root(target.Hive);
        var path=target.Path;
        while(true)
        {
            using var existing=root.OpenSubKey(path);
            if(existing!=null)
            {
                using var access=root.OpenSubKey(path,RegistryKeyPermissionCheck.ReadWriteSubTree,RegistryRights.SetValue|RegistryRights.CreateSubKey);
                if(access==null) throw new UnauthorizedAccessException("Sem acesso de escrita à chave "+path);
                return;
            }
            int last=path.LastIndexOf('\\');
            if(last<0) throw new UnauthorizedAccessException("Nenhuma chave pai acessível: "+target.Path);
            path=path[..last];
        }
    }
}
