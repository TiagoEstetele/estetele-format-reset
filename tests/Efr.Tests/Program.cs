using System.Text.Json;
using Efr.Core;

// Native smoke is opt-in and reads only. The normal suite uses simulation exclusively.
if (args.Contains("--native-readonly")) return await NativeReadOnly.RunAsync();

// No native mutations are reachable from the default suite.
var results = new List<string>();
var failed = 0;
async Task Test(string name, Func<Task> test)
{
    try { await test(); results.Add("PASS " + name); }
    catch (Exception ex) { failed++; results.Add("FAIL " + name + ": " + ex.Message); }
}
void Assert(bool condition, string message = "Condição não atendida") { if (!condition) throw new Exception(message); }
Adjustment Item(params RegTarget[] values) => new("test.registry", "Teste", "Teste", "Isolado", OperationKind.Registry, Values:values);
RegTarget Target(string name, int desired = 1) => new("HKCU", @"Software\EFR-Test-Only-In-Memory", name, RegValue.Dword(desired));
(SimulationBackend, MemoryJournal, Engine) Setup() { var b = new SimulationBackend(); var j = new MemoryJournal(); return (b, j, new Engine(b, j)); }

await Test("Detecção diferencia valores ausentes, diferentes e aplicados", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t);
    Assert((await e.DetectAsync(i)).State == State.NotApplied);
    b.Values[Engine.Key(t)] = t.Desired; Assert((await e.DetectAsync(i)).State == State.Applied);
});
await Test("Aplicação idempotente preserva o backup original", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t);
    b.Values[Engine.Key(t)] = RegValue.Dword(7);
    Assert((await e.ApplyAsync(i, new(), "1")).Success);
    int writes = b.Writes;
    Assert((await e.ApplyAsync(i, new(), "2")).Success && b.Writes == writes);
    Assert(j.Data.Backups[i.Id].Original.Registry[Engine.Key(t)] == RegValue.Dword(7));
});
await Test("Backup durável existe antes da primeira escrita", async () => {
    var (b,j,e) = Setup(); j.OnSave = () => { if (j.Data.History.Count == 1) Assert(b.Writes == 0 && j.Data.Backups.Count == 1); };
    Assert((await e.ApplyAsync(Item(Target("A")), new(), "1")).Success);
});
await Test("Falha ao salvar backup impede qualquer escrita", async () => {
    var (b,j,e) = Setup(); j.FailSave = true;
    Assert(!(await e.ApplyAsync(Item(Target("A")), new(), "1")).Success && b.Writes == 0);
});
await Test("Desfazer remove valores originalmente ausentes", async () => {
    var (b,j,e) = Setup(); var t = Target("ausente"); var i = Item(t);
    await e.ApplyAsync(i, new(), "1"); Assert((await e.UndoAsync(i,"2")).Success); Assert(!b.Values.ContainsKey(Engine.Key(t)));
});
await Test("Restauração preserva tipo e conteúdo originais", async () => {
    RegValue[] originals = [RegValue.String("ação & 'teste' $()"), new(true,"ExpandString",JsonSerializer.Serialize("%USERPROFILE%\\á")),
        new(true,"Binary",JsonSerializer.Serialize(new byte[] {0,255,42})), new(true,"MultiString",JsonSerializer.Serialize(new[] {"á", "b"})),
        new(true,"QWord",JsonSerializer.Serialize(long.MaxValue)), RegValue.Dword(-1)];
    foreach (var original in originals) {
        var (b,j,e) = Setup(); var t = Target("tipo"); var i = Item(t); b.Values[Engine.Key(t)] = original;
        Assert((await e.ApplyAsync(i,new(),"1")).Success); Assert((await e.UndoAsync(i,"2")).Success); Assert(b.Values[Engine.Key(t)] == original);
    }
});
await Test("Falha parcial recupera os valores anteriores", async () => {
    var (b,j,e) = Setup(); var a = Target("A"); var c = Target("C"); var i = Item(a,c);
    b.Values[Engine.Key(a)] = RegValue.Dword(4); b.FailAfterWrites = 1;
    Assert(!(await e.ApplyAsync(i,new(),"1")).Success);
    Assert(b.Values[Engine.Key(a)] == RegValue.Dword(4) && !b.Values.ContainsKey(Engine.Key(c)));
});
await Test("Falha de validação não é reportada como sucesso", async () => {
    var (b,j,e) = Setup(); b.FailValidation = true; var t = Target("A");
    Assert(!(await e.ApplyAsync(Item(t),new(),"1")).Success); Assert(!b.Values.ContainsKey(Engine.Key(t)));
});
await Test("Falha de rollback fica explícita e mantém backup", async () => {
    var (b,j,e) = Setup(); b.FailAfterWrites = 1; b.FailRestore = true; var i = Item(Target("A"));
    var r = await e.ApplyAsync(i,new(),"1"); Assert(!r.Success && r.Message.Contains("Falha ao recuperar") && e.HasBackup(i.Id));
});
await Test("Mudança externa bloqueia restauração", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t); await e.ApplyAsync(i,new(),"1"); b.Values[Engine.Key(t)] = RegValue.Dword(99);
    Assert(!(await e.UndoAsync(i,"2")).Success); Assert(b.Values[Engine.Key(t)] == RegValue.Dword(99));
});
await Test("Mudança externa bloqueia reaplicação e preserva baseline", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t); await e.ApplyAsync(i,new(),"1"); b.Values[Engine.Key(t)] = RegValue.Dword(99);
    Assert(!(await e.ApplyAsync(i,new(),"2")).Success && b.Writes == 1); Assert(!j.Data.Backups[i.Id].Original.Registry[Engine.Key(t)].Exists);
});
await Test("Identidade diferente e credenciais de outro usuário bloqueiam escrita", async () => {
    var (b,j,e) = Setup(); b.Info = b.Info with { IdentityMatches = false };
    Assert(!(await e.ApplyAsync(Item(Target("A")),new(),"1")).Success && b.Writes == 0);
});
await Test("SID divergente do backup bloqueia restauração", async () => {
    var (b,j,e) = Setup(); var i = Item(Target("A")); await e.ApplyAsync(i,new(),"1"); b.Info = b.Info with { Sid = "OUTRO-SID" };
    Assert(!(await e.UndoAsync(i,"2")).Success);
});
await Test("Computador gerenciado e falta de administrador bloqueiam escrita", async () => {
    var (b,j,e) = Setup(); var i = Item(Target("A")) with { Admin = true }; b.Info = b.Info with { Admin = false };
    Assert(!(await e.ApplyAsync(i,new(),"1")).Success && b.Writes == 0); b.Info = b.Info with { Admin = true, Managed = true };
    Assert(!(await e.ApplyAsync(i,new(),"2")).Success && b.Writes == 0);
});
await Test("Recuperação de operação interrompida aceita só valores originais/desejados", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t); var original = await b.CaptureAsync(i);
    j.Data.Backups[i.Id] = new Backup { Id=i.Id, Sid=b.Info.Sid, Original=original }; b.Values[Engine.Key(t)] = t.Desired;
    Assert((await e.UndoAsync(i,"1")).Success);
});
await Test("Interrupção com valor estranho não é sobrescrita", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t);
    j.Data.Backups[i.Id] = new Backup { Id=i.Id, Sid=b.Info.Sid, Original=await b.CaptureAsync(i) }; b.Values[Engine.Key(t)] = RegValue.Dword(77);
    Assert(!(await e.UndoAsync(i,"1")).Success && b.Values[Engine.Key(t)] == RegValue.Dword(77));
});
await Test("Novos ciclos arquivam backups restaurados", async () => {
    var (b,j,e) = Setup(); var t = Target("A"); var i = Item(t); await e.ApplyAsync(i,new(),"1"); await e.UndoAsync(i,"2"); b.Values[Engine.Key(t)] = RegValue.Dword(4);
    await e.ApplyAsync(i,new(),"3"); Assert(j.Data.ArchivedBackups.Count == 1 && !j.Data.ArchivedBackups[0].Original.Registry[Engine.Key(t)].Exists);
    Assert(j.Data.Backups[i.Id].Original.Registry[Engine.Key(t)] == RegValue.Dword(4));
});
await Test("Catálogo tem IDs únicos e perfil sem remoções", () => {
    var catalog = Catalog.Create(); Assert(catalog.Select(i=>i.Id).Distinct().Count() == catalog.Count);
    Assert(catalog.Where(i=>i.Conservative).All(i=>i.Kind == OperationKind.Registry && !i.Admin));
    Assert(!catalog.Any(i=>i.Kind == OperationKind.Appx && (i.Target.Contains("WindowsStore") || i.Target.Contains("DesktopAppInstaller") || i.Target.Contains("WebView"))));
    Assert(catalog.Where(i=>i.Kind is OperationKind.Appx or OperationKind.Capability or OperationKind.Feature).All(i=>!i.Conservative));
    return Task.CompletedTask;
});
await Test("Preferências solicitadas: remoção do Bloco de Notas, Notepad++ e Desempenho Máximo pré-selecionados", () => {
    var catalog = Catalog.Create();
    Assert(catalog.Single(i=>i.Target == "Microsoft.WindowsNotepad").Preselect);
    Assert(catalog.Single(i=>i.Id == "install.notepadpp").Preselect);
    Assert(catalog.Single(i=>i.Id == "power.ultimate").Preselect);
    Assert(!catalog.Single(i=>i.Id == "power.ultimate").Conservative);
    return Task.CompletedTask;
});
await Test("Desempenho Máximo é idempotente e reversível no motor simulado", async () => {
    var (b,j,e) = Setup(); var i = Catalog.Create().Single(i=>i.Id == "power.ultimate");
    Assert((await e.ApplyAsync(i,new(),"1")).Success); int writes = b.Writes;
    Assert((await e.ApplyAsync(i,new(),"2")).Success && b.Writes == writes);
    Assert((await e.UndoAsync(i,"3")).Success && b.NativeValues[i.Id] == "original");
});
await Test("Downloads recusam HTTP, domínio parecido e repositório não oficial", () => {
    Assert(Installers.Allowed(new Uri("https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/v1/npp.exe"), "NotepadPlusPlus"));
    Assert(!Installers.Allowed(new Uri("http://download.mozilla.org/a.exe"), "Firefox"));
    Assert(!Installers.Allowed(new Uri("https://download.mozilla.org.attacker.example/a.exe"), "Firefox"));
    Assert(!Installers.Allowed(new Uri("https://github.com/other/notepad-plus-plus/releases/download/v1/npp.exe"), "NotepadPlusPlus"));
    Assert(!Installers.Allowed(new Uri("https://aka.ms/a.exe"), "Unknown"));
    return Task.CompletedTask;
});
await Test("Restauração parcial pode ser retomada sem aceitar valores externos", async () => {
    var (b,j,e) = Setup(); var a=Target("A"); var c=Target("C"); var i=Item(a,c);
    await e.ApplyAsync(i,new(),"1"); b.Values.Remove(Engine.Key(a));
    Assert((await e.UndoAsync(i,"2")).Success && !b.Values.ContainsKey(Engine.Key(c)));
});
await Test("Histórico atômico persiste em caminho com espaços, acentos e caracteres especiais", async () => {
    var directory = Path.Combine(Environment.CurrentDirectory,"artifacts","testes espaço ação & [x]",Guid.NewGuid().ToString("N"));
    var b = new SimulationBackend(); var i = Item(Target("A"));
    using (var j = new Journal(directory,b.Info.Sid)) { var e = new Engine(b,j); Assert((await e.ApplyAsync(i,new(),"1")).Success); }
    using (var j = new Journal(directory,b.Info.Sid)) { Assert(j.Data.Backups.Count == 1 && j.Data.History.Count == 2); }
    bool refused = false; try { using var invalid = new Journal(directory,"OUTRO"); } catch(IOException) { refused = true; } Assert(refused);
});
await Test("Histórico corrompido não é substituído", () => {
    var directory = Path.Combine(Environment.CurrentDirectory,"artifacts","invalid-history",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
    var path = Path.Combine(directory,"history.json"); File.WriteAllText(path,"{INVALID");
    bool refused = false; try { using var j = new Journal(directory,"SIMULATION"); } catch(JsonException) { refused = true; }
    Assert(refused && File.ReadAllText(path) == "{INVALID"); return Task.CompletedTask;
});
await Test("Trava de histórico impede instâncias simultâneas", () => {
    var directory = Path.Combine(Environment.CurrentDirectory,"artifacts","locked-history",Guid.NewGuid().ToString("N"));
    using var first = new Journal(directory,"SIMULATION"); bool refused = false;
    try { using var second = new Journal(directory,"SIMULATION"); } catch(IOException) { refused = true; }
    Assert(refused); return Task.CompletedTask;
});

await Test("Pré-validação cobre cada item selecionado sem executar alterações", async () => {
    var (b,j,e)=Setup(); var items=Catalog.Create().Where(i=>i.Kind!=OperationKind.Guide && i.Preselect).ToList();
    var preflight=new Preflight(b,j,"unused",true); var steps=preflight.Build(items,new());
    Assert(steps.Count==4+items.Count*2 && steps.Select(s=>s.Id).Distinct().Count()==steps.Count);
    var report=await ValidationRunner.RunAsync(steps,ValidationRunner.Fingerprint(items,new(),b.Info));
    Assert(report.Approved && b.Writes==0 && j.Data.Backups.Count==0);
});
await Test("Pré-validação bloqueia conta errada e preserva o Windows", async () => {
    var (b,j,e)=Setup(); b.Info=b.Info with {IdentityMatches=false}; var items=new[]{Item(Target("A"))};
    var report=await ValidationRunner.RunAsync(new Preflight(b,j,"unused",true).Build(items,new()),"test");
    Assert(!report.Approved && report.Results.Any(r=>r.Status==CheckStatus.Failed) && b.Writes==0);
});
await Test("Falha de armazenamento bloqueia liberação da configuração", async () => {
    var (b,j,e)=Setup();j.FailSave=true;var items=new[]{Item(Target("A"))};
    var report=await ValidationRunner.RunAsync(new Preflight(b,j,"unused",true).Build(items,new()),"test");
    Assert(!report.Approved && report.Results.Single(r=>r.Id=="system.history").Status==CheckStatus.Failed);
});
await Test("Fingerprint invalida seleção, fonte .NET, SID e privilégios diferentes", () => {
    var b=new SimulationBackend();var items=new[]{Item(Target("A"))};
    string original=ValidationRunner.Fingerprint(items,new(),b.Info);
    Assert(original!=ValidationRunner.Fingerprint([Item(Target("B"))],new(),b.Info));
    Assert(original!=ValidationRunner.Fingerprint(items,new("C:\\mídia\\sxs",true),b.Info));
    Assert(original!=ValidationRunner.Fingerprint(items,new(),b.Info with {Sid="OUTRO"}));
    Assert(original!=ValidationRunner.Fingerprint(items,new(),b.Info with {Admin=false}));return Task.CompletedTask;
});
await Test("Aprovação expira e não aceita fingerprint de outra seleção", () => {
    var now=DateTimeOffset.Now;var report=new ValidationReport("one",now,false,[new("test","teste",CheckStatus.Passed,"OK")]);
    Assert(report.IsCurrent("one",now.AddMinutes(9)));Assert(!report.IsCurrent("one",now.AddMinutes(11)));Assert(!report.IsCurrent("two",now));return Task.CompletedTask;
});
await Test("Cancelamento da validação não libera início nem executa próximos testes", async () => {
    using var cancel=new CancellationTokenSource();cancel.Cancel();int executions=0;
    ValidationStep[] steps=[new("one","teste","",()=>{executions++;return Task.FromResult(CheckResult.Pass("ok"));})];
    var report=await ValidationRunner.RunAsync(steps,"test",token:cancel.Token);
    Assert(!report.Approved && report.Cancelled && executions==0 && report.Results[0].Status==CheckStatus.Cancelled);
});
await Test("Progresso reflete testes concluídos e preserva testes seguintes após erro", async () => {
    List<ValidationProgress> progress=[];
    ValidationStep[] steps=[new("one","teste1","",()=>throw new IOException("falha")),new("two","teste2","",()=>Task.FromResult(CheckResult.Pass("ok")))];
    var report=await ValidationRunner.RunAsync(steps,"test",new InlineProgress(progress.Add));
    Assert(!report.Approved && report.Results[1].Status==CheckStatus.Passed);
    Assert(progress.Any(p=>p.Completed==1 && p.Percent==50) && progress.Last().Percent==100);
});
await Test("Ressalva é explícita; estado inconclusivo nunca é aprovado", async () => {
    var warning=await ValidationRunner.RunAsync([new("one","teste","",()=>Task.FromResult(CheckResult.Warn("Limite do teste sem aplicar")))],"test");
    Assert(warning.Approved && warning.Results[0].Status==CheckStatus.Warning);
    var invalid=await ValidationRunner.RunAsync([new("one","teste","",()=>Task.FromResult(new CheckResult(CheckStatus.Pending,"não testado")))],"test");
    Assert(!invalid.Approved);
});
await Test("Pós-validação confirma resultado e identifica ajuste não aplicado", async () => {
    var (b,j,e)=Setup();var item=Item(Target("A"));var preflight=new Preflight(b,j,"unused",true);
    Assert(!(await ValidationRunner.RunAsync(preflight.PostChecks([item],false),"before")).Approved);
    await e.ApplyAsync(item,new(),"1");Assert((await ValidationRunner.RunAsync(preflight.PostChecks([item],false),"after")).Approved);
});

await Test("Cancelamento intermediário não conta testes cancelados como executados", async () => {
    using var cancel=new CancellationTokenSource(); List<ValidationProgress> progress=[]; int executions=0;
    var steps=Enumerable.Range(0,4).Select(n=>new ValidationStep(n.ToString(),"teste","",()=> {
        executions++; cancel.Cancel(); return Task.FromResult(CheckResult.Pass("ok"));
    })).ToArray();
    var report=await ValidationRunner.RunAsync(steps,"test",new InlineProgress(progress.Add),cancel.Token);
    Assert(!report.Approved && executions==1 && report.Results.Count(r=>r.Status==CheckStatus.Cancelled)==3);
    Assert(progress.Last().Completed==1 && progress.Last().Percent==25,"Progresso contabilizou testes cancelados.");
});
await Test("Reaplicação interrompida recusa mudança externa sem sobrescrever", async () => {
    var (b,j,e)=Setup(); var t=Target("A");var i=Item(t);
    j.Data.Backups[i.Id]=new Backup { Id=i.Id,Sid=b.Info.Sid,Original=await b.CaptureAsync(i) };
    b.Values[Engine.Key(t)]=RegValue.Dword(77);
    Assert(!(await e.ApplyAsync(i,new(),"retry")).Success && b.Writes==0);
    Assert(b.Values[Engine.Key(t)]==RegValue.Dword(77));
});
await Test("Reaplicação interrompida aceita somente estado conhecido", async () => {
    var (b,j,e)=Setup();var a=Target("A");var c=Target("C");var i=Item(a,c);
    j.Data.Backups[i.Id]=new Backup { Id=i.Id,Sid=b.Info.Sid,Original=await b.CaptureAsync(i) };
    b.Values[Engine.Key(a)]=a.Desired;
    Assert((await e.ApplyAsync(i,new(),"retry")).Success);
    Assert((await e.UndoAsync(i,"undo")).Success && b.Values.Count==0);
});
await Test("Pré-validação vazia não autoriza aplicação", async () => {
    var (b,j,e)=Setup();var report=await ValidationRunner.RunAsync(new Preflight(b,j,"unused",true).Build([],new()),"empty");
    Assert(!report.Approved);
});
await Test("Pré-validação bloqueia dois planos de energia", async () => {
    var (b,j,e)=Setup();Adjustment[] items=[new("p1","P1","","",OperationKind.Power),new("p2","P2","","",OperationKind.UltimatePower)];
    var report=await ValidationRunner.RunAsync(new Preflight(b,j,"unused",true).Build(items,new()),"power");
    Assert(!report.Approved && b.Writes==0 && report.Results.Single(r=>r.Id=="system.identity").Status==CheckStatus.Failed);
});
await Test("Pré-validação bloqueia plataforma não suportada", async () => {
    var (b,j,e)=Setup();b.Info=b.Info with {Supported=false};
    var report=await ValidationRunner.RunAsync(new Preflight(b,j,"unused",true).Build([Item(Target("A"))],new()),"os");
    Assert(!report.Approved && b.Writes==0);
});
await Test("Fingerprint inclui gerenciamento e identidade da sessão", () => {
    var b=new SimulationBackend();var items=new[]{Item(Target("A"))};var original=ValidationRunner.Fingerprint(items,new(),b.Info);
    Assert(original!=ValidationRunner.Fingerprint(items,new(),b.Info with {Managed=true}));
    Assert(original!=ValidationRunner.Fingerprint(items,new(),b.Info with {IdentityMatches=false}));
    Assert(original!=ValidationRunner.Fingerprint(items,new(),b.Info with {Supported=false}));return Task.CompletedTask;
});
await Test("Fingerprint é estável ao reordenar seleção", () => {
    var b=new SimulationBackend();var a=Item(Target("A")) with {Id="a"};var c=Item(Target("C")) with {Id="c"};
    Assert(ValidationRunner.Fingerprint([a,c],new(),b.Info)==ValidationRunner.Fingerprint([c,a],new(),b.Info));return Task.CompletedTask;
});
await Test("Relatório vazio, relógio anterior e estados incompletos não liberam início", () => {
    var now=DateTimeOffset.Now;Assert(!new ValidationReport("x",now,false,[]).Approved);
    foreach(var status in new[]{CheckStatus.Pending,CheckStatus.Running,CheckStatus.Failed,CheckStatus.Cancelled})
        Assert(!new ValidationReport("x",now,false,[new("x","x",status,"")]).Approved);
    var good=new ValidationReport("x",now,false,[new("x","x",CheckStatus.Passed,"")]);
    Assert(!good.IsCurrent("x",now.AddSeconds(-1)) && good.IsCurrent("x",now.AddMinutes(10)) && !good.IsCurrent("x",now.AddMinutes(10).AddTicks(1)));
    return Task.CompletedTask;
});
await Test("Histórico rejeita máquina e schema diferentes sem alterar arquivo", () => {
    foreach(var data in new[]{new JournalData {Sid="SIMULATION",Machine="OTHER-MACHINE"},new JournalData {Sid="SIMULATION",Schema=99}}) {
        var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","journal-guards",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        var path=Path.Combine(directory,"history.json");var content=JsonSerializer.Serialize(data);File.WriteAllText(path,content);bool refused=false;
        try { using var j=new Journal(directory,"SIMULATION"); } catch(IOException) { refused=true; }
        Assert(refused && File.ReadAllText(path)==content);
    }return Task.CompletedTask;
});
await Test("Histórico com coleções nulas é recusado na abertura", () => {
    var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","null-history",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
    var data=new JournalData {Sid="SIMULATION",Backups=null!};var content=JsonSerializer.Serialize(data);var path=Path.Combine(directory,"history.json");File.WriteAllText(path,content);
    bool refused=false;try {using var j=new Journal(directory,"SIMULATION");} catch(IOException) {refused=true;}
    Assert(refused && File.ReadAllText(path)==content);return Task.CompletedTask;
});
await Test("Histórico conserva versão anterior e libera lock ao fechar", () => {
    var directory=Path.Combine(Environment.CurrentDirectory,"artifacts","previous-history",Guid.NewGuid().ToString("N"));
    using(var j=new Journal(directory,"SIMULATION")) {j.Save();j.Data.History.Add(new(DateTimeOffset.Now,"run","id","test","ok",""));j.Save();}
    var previous=JsonSerializer.Deserialize<JournalData>(File.ReadAllText(Path.Combine(directory,"history.json.previous")))!;
    using var reopened=new Journal(directory,"SIMULATION");Assert(previous.History.Count==0 && reopened.Data.History.Count==1);return Task.CompletedTask;
});
await Test("Falha na gravação da conclusão dispara rollback", async () => {
    var (b,j,e)=Setup();var t=Target("A");var i=Item(t);int saves=0;
    j.OnSave=()=>{if(++saves==2) throw new IOException("Falha ao persistir resultado");};
    var result=await e.ApplyAsync(i,new(),"run");Assert(!result.Success && !b.Values.ContainsKey(Engine.Key(t)) && e.HasBackup(i.Id));
});
await Test("Aplicações concorrentes do mesmo motor preservam idempotência", async () => {
    var (b,j,e)=Setup();var i=Item(Target("A"));
    var outcomes=await Task.WhenAll(Enumerable.Range(0,20).Select(n=>e.ApplyAsync(i,new(),n.ToString())));
    Assert(outcomes.All(r=>r.Success) && b.Writes==1 && j.Data.Backups.Count==1);
});
await Test("Downloads rejeitam credenciais na URL e portas não HTTPS padrão", () => {
    Assert(!Installers.Allowed(new Uri("https://someone:secret@download.mozilla.org/file.exe"),"Firefox"));
    Assert(!Installers.Allowed(new Uri("https://download.mozilla.org:8443/file.exe"),"Firefox"));
    Assert(!Installers.Allowed(new Uri("https://github.com/notepad-plus-plus/notepad-plus-plus/releases/download/../../other/file.exe"),"NotepadPlusPlus"));
    return Task.CompletedTask;
});

Directory.CreateDirectory("artifacts");
File.WriteAllLines(Path.Combine("artifacts","test-results.txt"), results.Append($"{results.Count-failed}/{results.Count} testes passaram; alterações no Windows: nenhuma."));
foreach(var line in results) Console.WriteLine(line);
Console.WriteLine($"{results.Count-failed}/{results.Count} testes passaram.");
return failed == 0 ? 0 : 1;

sealed class MemoryJournal : IJournal
{
    public JournalData Data { get; } = new() { Sid="SIMULATION" };
    public bool FailSave { get; set; }
    public Action? OnSave { get; set; }
    public void Save() { OnSave?.Invoke(); if(FailSave) throw new IOException("Falha de disco simulada."); }
}
sealed class InlineProgress(Action<ValidationProgress> action) : IProgress<ValidationProgress>
{
    public void Report(ValidationProgress value)=>action(value);
}
