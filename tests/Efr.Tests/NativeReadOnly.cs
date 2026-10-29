using Efr.Core;

internal static class NativeReadOnly
{
    public static async Task<int> RunAsync()
    {
        List<string> lines=[];int failures=0;
        try
        {
            var backend=new WindowsBackend(null,_=>false);
            await backend.RefreshAsync();
            lines.Add($"Windows build {backend.Info.Build}; edição {backend.Info.Edition}; admin {backend.Info.Admin}; suportado {backend.Info.Supported}.");
            // Never persist SID, username, snapshots or package identities in this report.
            foreach(var item in Catalog.Create())
            {
                try
                {
                    var detection=await backend.DetectAsync(item);
                    bool requiresElevation=!backend.Info.Admin && item.Kind is OperationKind.Feature or OperationKind.Capability or OperationKind.NetFx;
                    if(detection.State==State.Unknown && !requiresElevation) {failures++;lines.Add("FAIL "+item.Id+": leitura inconclusiva.");}
                    else lines.Add((requiresElevation ? "SKIP " : "READ ")+item.Id+": "+detection.State);
                    if(item.Reversible && detection.State is State.Applied or State.NotApplied)
                    {
                        var snapshot=await backend.CaptureAsync(item);
                        if(snapshot.Registry==null) throw new IOException("Snapshot inválido.");
                    }
                }
                catch(Exception ex) {failures++;lines.Add("FAIL "+item.Id+": "+ex.GetType().Name);}
            }
            var plans=PowerPlans.List();
            if(!plans.Any(p=>p.Id==PowerPlans.Active())) {failures++;lines.Add("FAIL plano ativo fora do inventário.");}
            else lines.Add("READ plano ativo e enumeração de energia consistentes.");
        }
        catch(Exception ex) {failures++;lines.Add("FAIL inicialização: "+ex.GetType().Name);}
        lines.Add($"Falhas de leitura: {failures}. Aplicações, instalações, remoções e restaurações executadas: ZERO. SKIP exige elevação; indisponíveis não são testes de aplicação aprovados.");
        Directory.CreateDirectory("artifacts");File.WriteAllLines("artifacts/native-readonly.txt",lines);
        foreach(var line in lines) Console.WriteLine(line);
        return failures==0 ? 0 : 1;
    }
}
