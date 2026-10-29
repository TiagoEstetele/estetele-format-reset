using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Text.Json;
using System.Windows.Threading;
using Efr.Core;

namespace Efr.App;

public partial class MainWindow : Window
{
    private readonly bool demo;
    private readonly IBackend backend;
    private readonly Journal journal;
    private readonly Engine engine;
    private readonly Preflight preflight;
    private ValidationReport? validated;
    private ApplyOptions validatedOptions = new();
    private readonly List<AdjustmentViewModel> rows;
    private bool busy, stopAfterCurrent;
    private bool initialized;
    private bool defaultsLoaded;
    public MainWindow(bool demo, string? expectedSid, string? demoDirectory = null)
    {
        InitializeComponent();
        SourceInitialized += (_,_) => WindowTheme.Apply(this);
        this.demo = demo;
        var info = demo ? new SimulationBackend().Info : Native.Machine(expectedSid);
        var directory = demo
            ? Path.Combine(demoDirectory ?? Path.Combine(AppContext.BaseDirectory, "demo-history"), DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6])
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "estetele-format-reset", "history-v1");
        journal = new Journal(directory, info.Sid);
        backend = demo ? new SimulationBackend() : new WindowsBackend(expectedSid, id => journal.Data.Backups.TryGetValue(id, out var b) && !b.Restored);
        engine = new Engine(backend, journal);
        preflight = new Preflight(backend,journal,journal.DirectoryPath,demo);
        rows = Catalog.Create().Select(i => new AdjustmentViewModel(i)).ToList();
        foreach (var row in rows) row.PropertyChanged += RowChanged;
        Categories.ItemsSource = Catalog.Categories;
        Categories.SelectedIndex = 0;
        ModeLabel.Text = demo ? "SIMULAÇÃO · SEM ALTERAR O WINDOWS" : "PERSONALIZE COM CONTROLE";
        AdminButton.Visibility = demo || info.Admin ? Visibility.Collapsed : Visibility.Visible;
        Loaded += async (_,_) => { if (!initialized) { initialized = true; await RefreshAll(); } };
        Closing += OnClosing;
        var expiryTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(15) };
        expiryTimer.Tick+=(_,_)=>UpdateSelection();expiryTimer.Start();
        Closed += (_,_) => { expiryTimer.Stop();Installers.Cleanup();journal.Dispose(); };
    }
    private void RowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AdjustmentViewModel.Selected)) { validated=null;UpdateSelection(); }
    }
    private void UpdateSelection()
    {
        int count = rows.Count(r => r.Selected);
        SelectionLabel.Text = count == 0 ? "Nenhum ajuste selecionado" : $"{count} ajuste(s) selecionado(s)";
        bool approved=validated?.IsCurrent(ValidationRunner.Fingerprint(ChosenRows().Select(r=>r.Item),validatedOptions,backend.Info),DateTimeOffset.Now)==true;
        ApplyButton.IsEnabled = count > 0 && !busy && approved;
        ValidateButton.IsEnabled=count>0 && !busy;
        ApplyButton.ToolTip=approved ? "Seleção validada. Clique para a confirmação final." : "Execute Validar configuração. Mudar a seleção ou exceder 10 minutos exige nova validação.";
    }
    private async Task RefreshAll()
    {
        validated=null;
        SetBusy(true, "Consultando aplicativos, permissões e recursos do Windows…", false);
        try
        {
            await Task.Run(backend.RefreshAsync);
            if (!rows.Any(r => r.Item.Kind == OperationKind.Power))
            {
                var plans = demo ? new List<PowerPlan> { new("381b4222-f694-41f0-9685-ff5bb260df2e", "Equilibrado"), new("a1841308-3541-4fab-bc81-f71556f20b4a", "Economia de energia") } : await Task.Run(PowerPlans.List);
                foreach (var plan in plans)
                {
                    var row = new AdjustmentViewModel(new("power." + plan.Id, "Usar plano " + plan.Name, Catalog.Energy,
                        "Seleciona um plano já existente. Planos de alto desempenho podem elevar consumo, temperatura e uso de bateria. Nenhum plano será criado.", OperationKind.Power, true,
                        Restart:"Imediato", Undo:"Restaura o plano anterior se ele ainda existir.", Target:plan.Id, Preselect:false));
                    row.PropertyChanged += RowChanged; rows.Add(row);
                }
            }
            foreach (var row in rows) row.SetDetection(await Task.Run(() => engine.DetectAsync(row.Item)));
            if (!defaultsLoaded) { SelectXmlProfile(); defaultsLoaded = true; }
            ShowMachine(); ShowCategory(); StatusLabel.Text = demo ? "Simulação pronta. Perfil do XML selecionado; nada será aplicado ao Windows." : "Perfil do XML pré-selecionado: inclui remoções, Notepad++ e Desempenho Máximo. Revise todos os itens.";
        }
        catch (Exception ex) { StatusLabel.Text = "Falha na consulta: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private void ShowMachine()
    {
        var i = backend.Info;
        MachineLabel.Text = $"Windows {i.DisplayVersion} · build {i.Build} · {i.Edition} · {i.UserName} · {(i.Admin ? "Administrador" : "Usuário padrão")}\nSID: {i.Sid}";
        var warnings = new List<string>();
        if (!i.Supported) warnings.Add("Sistema não suportado: requer Windows 11 x64.");
        if (!i.IdentityMatches) warnings.Add("Conta diferente da sessão do Explorador. Alterações bloqueadas; abra pela conta que será personalizada.");
        if (i.Managed) warnings.Add(i.ManagementDetail);
        if (journal.Data.History.Any(h => h.Result == "Iniciado" && !journal.Data.History.Any(end => end.RunId == h.RunId && end.OperationId == h.OperationId && end.Action == h.Action && end.Time > h.Time && end.Result != "Iniciado")))
            warnings.Add("Há uma operação interrompida no histórico. Consulte novamente e revise os backups antes de continuar.");
        WarningBox.Visibility = warnings.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        WarningLabel.Text = string.Join("\n", warnings);
    }
    private void ShowCategory()
    {
        if (rows == null) return;
        var category = Categories.SelectedItem as string ?? Catalog.Explorer;
        CategoryTitle.Text = category;
        bool history = category == "Histórico e restauração";
        AdjustmentsScroll.Visibility = history ? Visibility.Collapsed : Visibility.Visible;
        HistoryScroll.Visibility = history ? Visibility.Visible : Visibility.Collapsed;
        var filtered = rows.Where(r => r.Item.Category == category).ToList();
        AdjustmentsList.ItemsSource = filtered;
        CountLabel.Text = history ? "" : $"{filtered.Count} opções";
        if (history) UpdateHistory();
    }
    private void UpdateHistory()
    {
        BackupsList.ItemsSource = rows.Where(r => engine.HasBackup(r.Item.Id)).ToList();
        HistoryText.Text = journal.Data.History.Count == 0 ? "Nenhuma alteração registrada." : string.Join("\n\n", journal.Data.History.AsEnumerable().Reverse().Select(h => $"{h.Time:dd/MM/yyyy HH:mm:ss zzz} | {h.Action} | {h.OperationId}\n{h.Result}: {h.Detail}"));
    }
    private void CategoryChanged(object sender, SelectionChangedEventArgs e) { if (IsInitialized) ShowCategory(); }
    private void ConservativeClick(object sender, RoutedEventArgs e)
    {
        foreach (var row in rows) row.Selected = row.Item.Conservative && row.CanSelect;
        StatusLabel.Text = "Perfil conservador: extensões visíveis e Este Computador. Nenhuma remoção ou alteração de energia.";
    }
    private void SelectXmlProfile()
    {
        foreach (var row in rows) row.Selected = row.Item.Preselect && row.CanSelect && (row.NeedsChange || row.Item.Kind == OperationKind.UltimatePower);
    }
    private void XmlProfileClick(object sender, RoutedEventArgs e)
    {
        SelectXmlProfile();
        StatusLabel.Text = "Perfil do XML: ajustes disponíveis, remoções e instalações pré-selecionados. Revise todos os itens antes de aplicar.";
    }
    private void ClearClick(object sender, RoutedEventArgs e) { foreach (var row in rows) row.Selected = false; }
    private async void RefreshClick(object sender, RoutedEventArgs e) => await RefreshAll();
    private List<AdjustmentViewModel> ChosenRows()=>rows.Where(r=>r.Selected && r.CanSelect).OrderBy(r=>r.Item.Kind==OperationKind.Install ? 0 : 1).ToList();
    private void SaveValidation(ValidationReport report,string phase)
    {
        var directory=Path.Combine(journal.DirectoryPath,"validations");Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+phase+"-"+Guid.NewGuid().ToString("N")[..6]+".json");
        File.WriteAllText(file,JsonSerializer.Serialize(report,new JsonSerializerOptions { WriteIndented=true }));
        journal.Data.History.Add(new(DateTimeOffset.Now,Guid.NewGuid().ToString("N"),"validation."+phase,"Validar",report.Approved?"Aprovado":"Bloqueado",$"{report.Results.Count(r=>r.Status==CheckStatus.Passed)} aprovados; {report.Results.Count(r=>r.Status==CheckStatus.Warning)} ressalvas; {report.Results.Count(r=>r.Status==CheckStatus.Failed)} bloqueados. Relatório: {file}"));journal.Save();
    }
    private void ValidateClick(object sender,RoutedEventArgs e)
    {
        var selected=ChosenRows(); if(selected.Count==0) return;
        validated=null;UpdateSelection();
        var review=new ReviewWindow(selected.Select(r=>r.Item).ToList(),demo,validation:true) { Owner=this };
        if(review.ShowDialog()!=true) return;
        validatedOptions=review.Options;
        SetBusy(true,"Validando a seleção; nenhum ajuste será aplicado…");
        try
        {
            var items=selected.Select(r=>r.Item).ToList();
            var fingerprint=ValidationRunner.Fingerprint(items,validatedOptions,backend.Info);
            var window=new ValidationWindow(preflight.Build(items,validatedOptions),fingerprint,demo) { Owner=this };
            window.ShowDialog();
            if(window.Report!=null) { SaveValidation(window.Report,"preflight"); validated=window.Report; }
            StatusLabel.Text=validated?.Approved==true ? "Validação concluída. Consulte as ressalvas, se houver; Iniciar configurações principais está liberado para esta seleção por 10 minutos." : "Validação incompleta ou bloqueada. Corrija os testes indicados e valide novamente.";
            ShowMachine();UpdateHistory();
        }
        catch(Exception ex) { validated=null;StatusLabel.Text="Não foi possível concluir/salvar a validação: "+ex.Message; }
        finally { SetBusy(false); }
    }
    private async void ApplyClick(object sender, RoutedEventArgs e)
    {
        var selected = ChosenRows();
        if (selected.Count == 0) return;
        if(validated?.IsCurrent(ValidationRunner.Fingerprint(selected.Select(r=>r.Item),validatedOptions,backend.Info),DateTimeOffset.Now)!=true) { validated=null;UpdateSelection();MessageBox.Show(this,"Execute Validar configuração para esta seleção antes de iniciar.","Validação necessária");return; }
        if (selected.Count(r => r.Item.Kind is OperationKind.Power or OperationKind.UltimatePower) > 1) { MessageBox.Show(this, "Selecione apenas um plano de energia por aplicação. Desmarque Desempenho Máximo para escolher outro.", "Energia"); return; }
        if (selected.Any(r => r.Item.Admin) && !backend.Info.Admin) { MessageBox.Show(this, "A seleção inclui alterações do computador. Use Reabrir como administrador com a mesma conta e selecione novamente. Credenciais de outra conta não serão usadas para personalizar HKCU.", "Administrador necessário"); return; }
        var review = new ReviewWindow(selected.Select(r => r.Item).ToList(), demo,lockedOptions:validatedOptions,caveats:validated.Results.Where(r=>r.Status==CheckStatus.Warning).Select(r=>r.Name+": "+r.Detail)) { Owner = this };
        if (review.ShowDialog() != true) return;
        if(validated?.IsCurrent(ValidationRunner.Fingerprint(selected.Select(r=>r.Item),validatedOptions,backend.Info),DateTimeOffset.Now)!=true) { validated=null;UpdateSelection();StatusLabel.Text="A validação expirou durante a revisão. Valide novamente.";return; }
        validated=null;
        await RunSelection(selected, review.Options);
    }
    private async Task RunSelection(List<AdjustmentViewModel> selected, ApplyOptions options, bool showResults = true)
    {
        var run = Guid.NewGuid().ToString("N");
        SetBusy(true, "Iniciando operações…", true); stopAfterCurrent = false;
        int done = 0, failed = 0; bool reboot = false;
        bool notepadInstallFailed = false;
        try
        {
            foreach (var row in selected)
            {
                if (stopAfterCurrent) break;
                if (row.Item.Target == "Microsoft.WindowsNotepad" && notepadInstallFailed)
                {
                    journal.Data.History.Add(new(DateTimeOffset.Now, run, row.Item.Id, "Aplicar", "Ignorado", "Notepad++ não foi instalado; Bloco de Notas preservado nesta aplicação.")); journal.Save();
                    done++; failed++; row.Selected = false; Progress.Value = 100.0 * done / selected.Count; continue;
                }
                StatusLabel.Text = $"{done + 1}/{selected.Count} · {row.Item.Name}. A operação atual precisa terminar; você pode parar as próximas.";
                var result = await Task.Run(() => engine.ApplyAsync(row.Item, options, run));
                if (!result.Success) failed++;
                if (row.Item.Id == "install.notepadpp" && !result.Success) notepadInstallFailed = true;
                reboot |= result.RebootRequired;
                row.SetDetection(await Task.Run(() => engine.DetectAsync(row.Item)));
                row.Selected = false;
                done++; Progress.Value = 100.0 * done / selected.Count;
                StatusLabel.Text = result.Message;
            }
            // Energy choices and related rows can change together.
            foreach (var row in rows) row.SetDetection(await Task.Run(() => engine.DetectAsync(row.Item)));
            StatusLabel.Text = $"{done}/{selected.Count} operações concluídas; {failed} falha(s). " + (stopAfterCurrent ? "Restante da fila cancelado. " : "") + (reboot ? "Reinicie manualmente quando estiver pronto. " : "") + "Consulte os resultados no histórico.";
            UpdateHistory(); ShowMachine();
            if(showResults && done>0)
            {
                await Task.Run(backend.RefreshAsync);
                foreach(var row in rows) row.SetDetection(await Task.Run(()=>engine.DetectAsync(row.Item)));
                var checks=preflight.PostChecks(selected.Take(done).Select(r=>r.Item),reboot);
                var finalWindow=new ValidationWindow(checks,"post-"+run,demo,post:true) { Owner=this };
                finalWindow.ShowDialog();
                if(finalWindow.Report!=null) SaveValidation(finalWindow.Report,"resultados");
                UpdateHistory();
            }
        }
        catch (Exception ex) { StatusLabel.Text = "A fila foi interrompida: " + ex.Message + ". Consulte o histórico."; }
        finally { SetBusy(false); }
    }
    private void SetBusy(bool value, string? status = null, bool allowStop = false)
    {
        busy = value;
        Toolbar.IsEnabled = !value; AdjustmentsList.IsEnabled = !value; HistoryScroll.IsEnabled = !value;
        Progress.IsIndeterminate = value && !allowStop;
        if (value) Progress.Value = 0;
        StopButton.Visibility = value && allowStop ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = value;
        if (status != null) StatusLabel.Text = status;
        UpdateSelection();
    }
    private void StopClick(object sender, RoutedEventArgs e) { stopAfterCurrent = true; StopButton.IsEnabled = false; StatusLabel.Text = "Parada solicitada. A operação atual continuará até terminar; as próximas não serão iniciadas."; }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!busy) return;
        e.Cancel = true; stopAfterCurrent = true;
        StatusLabel.Text = "Aguarde a operação atual terminar. Fechar durante uma alteração poderia deixar um estado parcial.";
    }
    private void AdminClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var info = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            info.ArgumentList.Add("--expected-sid"); info.ArgumentList.Add(backend.Info.Sid);
            info.ArgumentList.Add("--wait-pid"); info.ArgumentList.Add(Environment.ProcessId.ToString());
            // Release this user's history lock before the new UI loads. Cancelled UAC leaves this window intact.
            using var process = Process.Start(info);
            if (process != null) Close();
        }
        catch (Win32Exception ex) { StatusLabel.Text = ex.NativeErrorCode == 1223 ? "Elevação cancelada. Nenhum ajuste foi aplicado." : ex.Message; }
    }
    private void GuideClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdjustmentViewModel row } || row.Item.GuideUrl == null) return;
        if (demo) { StatusLabel.Text = "SIMULAÇÃO: orientação disponível em " + row.Item.GuideUrl; return; }
        try { Process.Start(new ProcessStartInfo(row.Item.GuideUrl) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLabel.Text = "Não foi possível abrir: " + ex.Message; }
    }
    private async void UndoClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdjustmentViewModel row }) return;
        if (MessageBox.Show(this, $"Restaurar o estado anterior de {row.Item.Name}?\n\n{row.Item.Undo}\nEfeito: {row.Item.Restart}\nMudanças posteriores feitas fora do EFR bloqueiam a restauração.", "Restaurar backup original", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        SetBusy(true, "Restaurando e verificando o backup…");
        try
        {
            var result = await Task.Run(() => engine.UndoAsync(row.Item, Guid.NewGuid().ToString("N")));
            await Task.Run(backend.RefreshAsync);
            foreach (var item in rows) item.SetDetection(await Task.Run(() => engine.DetectAsync(item.Item)));
            StatusLabel.Text = result.Message; UpdateHistory();
        }
        catch (Exception ex) { StatusLabel.Text = "Falha ao restaurar: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private async void RestorePointClick(object sender, RoutedEventArgs e)
    {
        if (demo) { StatusLabel.Text = "SIMULAÇÃO: nenhum ponto de restauração foi criado."; return; }
        SetBusy(true, "Solicitando ponto de restauração; aguardando confirmação do Windows…");
        string message; bool success;
        try { message = await Task.Run(((WindowsBackend)backend).CreateRestorePointAsync); success = true; }
        catch (Exception ex) { message = "Ponto NÃO criado/confirmado: " + ex.Message; success = false; }
        try
        {
            journal.Data.History.Add(new(DateTimeOffset.Now, Guid.NewGuid().ToString("N"), "system.restorepoint", "Criar ponto", success ? "Concluído" : "Falhou", message)); journal.Save();
            StatusLabel.Text = message; UpdateHistory();
        }
        catch (Exception ex) { StatusLabel.Text = message + " Falha ao salvar histórico: " + ex.Message; }
        finally { SetBusy(false); }
    }
    private void OpenHistoryClick(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(journal.DirectoryPath) { UseShellExecute = true }); }
        catch (Exception ex) { StatusLabel.Text = ex.Message; }
    }
    // An offline WPF fixture: renders this application's visual tree to PNG, never captures the desktop.
    public async Task SelfTestUiAsync(string directory)
    {
        if (!demo) throw new InvalidOperationException("A verificação visual exige backend de simulação.");
        Directory.CreateDirectory(directory);
        initialized = true;
        await RefreshAll();
        var root = (FrameworkElement)Content;
        Content = null; // Isolate fixture layout from an unshown Window's unbounded measuring pass.
        if (!rows.Single(r => r.Item.Id == "power.ultimate").Selected || !rows.Single(r => r.Item.Id == "install.notepadpp").Selected
            || !rows.Single(r => r.Item.Target == "Microsoft.WindowsNotepad").Selected)
            throw new InvalidOperationException("Perfil inicial não corresponde às preferências solicitadas.");
        void Save(FrameworkElement view, string file, int width, int height, Brush background)
        {
            view.Measure(new Size(width,height)); view.Arrange(new Rect(0,0,width,height)); view.UpdateLayout();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen()) { context.DrawRectangle(background,null,new Rect(0,0,width,height)); }
            var bitmap = new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); bitmap.Render(drawing); bitmap.Render(view);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.Combine(directory,file)); encoder.Save(stream);
        }
        Save(root,"01-explorador.png",1220,790,Background);
        Categories.SelectedItem = Catalog.Energy;
        Save(root,"02-energia.png",1220,790,Background);
        Categories.SelectedItem = Catalog.Install;
        Save(root,"03-programas.png",1220,790,Background);
        var selected = rows.Where(r=>r.Item.Conservative).ToList();
        foreach(var row in rows) row.Selected=row.Item.Conservative;
        if(ApplyButton.IsEnabled) throw new InvalidOperationException("Início liberado sem validação.");
        var review = new ReviewWindow(selected.Select(r=>r.Item).ToList(),true);
        var reviewRoot = (FrameworkElement)review.Content; review.Content = null;
        Save(reviewRoot,"04-revisao.png",672,600,review.Background);
        var validationItems=selected.Select(r=>r.Item).ToList();
        var fingerprint=ValidationRunner.Fingerprint(validationItems,new(),backend.Info);
        var validationWindow=new ValidationWindow(preflight.Build(validationItems,new()),fingerprint,true);
        await validationWindow.RunTestsAsync();
        if(validationWindow.Report?.Approved!=true) throw new InvalidOperationException("Validação simulada não aprovou os ajustes.");
        validated=validationWindow.Report;validatedOptions=new();UpdateSelection();
        if(!ApplyButton.IsEnabled) throw new InvalidOperationException("Início não foi liberado após validação válida.");
        selected[0].Selected=false;
        if(ApplyButton.IsEnabled) throw new InvalidOperationException("Alterar seleção não invalidou a aprovação.");
        selected[0].Selected=true;
        var validationRoot=(FrameworkElement)validationWindow.Content;validationWindow.Content=null;
        Save(validationRoot,"06-validacao.png",1064,728,validationWindow.Background);
        await RunSelection(selected,new(),showResults:false);
        if (selected.Any(r => !engine.HasBackup(r.Item.Id))) throw new InvalidOperationException("Backup da aplicação simulada não foi encontrado.");
        foreach(var row in selected)
            if (!(await engine.UndoAsync(row.Item,"ui-test-undo")).Success) throw new InvalidOperationException("Restauração simulada falhou.");
        Categories.SelectedItem = "Histórico e restauração"; UpdateHistory();
        Save(root,"05-historico.png",1220,790,Background);
        File.WriteAllText(Path.Combine(directory,"ui-results.txt"),"PASS: inicialização WPF offline; perfil inicial; 6 telas renderizadas; bloqueio/liberação/invalidação do botão de início; pré-validação; aplicação e reversão de 2 operações em memória. Nenhuma API de ajuste do Windows executada.");
    }
}
