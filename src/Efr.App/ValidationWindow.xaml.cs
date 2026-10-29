using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Efr.Core;

namespace Efr.App;

public sealed class CheckViewModel(int number, ValidationStep step) : INotifyPropertyChanged
{
    public int Number { get; }=number;
    public string Name { get; }=step.Name;
    public string Detail { get; private set; }=step.Description;
    public CheckStatus Status { get; private set; }=CheckStatus.Pending;
    public string StatusText => Status switch { CheckStatus.Pending=>"A SEGUIR",CheckStatus.Running=>"TESTANDO",CheckStatus.Passed=>"APROVADO",CheckStatus.Warning=>"RESSALVA",CheckStatus.Failed=>"BLOQUEADO",_=>"CANCELADO" };
    public Brush StatusBrush => new SolidColorBrush((Color)ColorConverter.ConvertFromString(Status switch { CheckStatus.Passed=>"#99DEBE",CheckStatus.Warning=>"#E8C58B",CheckStatus.Failed=>"#FF9FAE",CheckStatus.Running=>"#C9ABFF",_=>"#A99ABD" }));
    public void Set(ValidationResult result) { Status=result.Status; Detail=result.Detail; Changed(nameof(StatusText));Changed(nameof(StatusBrush));Changed(nameof(Detail)); }
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName]string? name=null)=>PropertyChanged?.Invoke(this,new(name));
}

public partial class ValidationWindow : Window
{
    private readonly IReadOnlyList<ValidationStep> steps;
    private readonly string fingerprint;
    private readonly bool post;
    private readonly List<CheckViewModel> checks;
    private readonly CancellationTokenSource cancellation=new();
    private readonly Stopwatch stopwatch=new();
    private bool running;
    public ValidationReport? Report { get; private set; }
    public ValidationWindow(IReadOnlyList<ValidationStep> steps,string fingerprint,bool simulation,bool post=false)
    {
        InitializeComponent(); this.steps=steps;this.fingerprint=fingerprint;this.post=post;
        checks=steps.Select((s,i)=>new CheckViewModel(i+1,s)).ToList(); TestsList.ItemsSource=checks;
        Heading.Text=post ? "Verificação dos resultados" : "Validação da configuração";
        Subtitle.Text=simulation ? "MODO SIMULAÇÃO · resultados sintéticos; nenhum ajuste ou download real." : post ? "Conferindo o estado efetivo de cada operação executada." : "Leitura real do Windows, permissões e dependências. Instaladores selecionados são baixados para verificação, sem instalar.";
        PendingCount.Text=$"{steps.Count} próximos";
        SourceInitialized+=(_,_)=>WindowTheme.Apply(this);
        Loaded+=async (_,_)=> { if(Report==null && !running) await RunTestsAsync(); };
        Closing+=(_,e)=> { if(running) { cancellation.Cancel(); e.Cancel=true;Conclusion.Text="Cancelamento solicitado; aguardando o teste atual terminar com segurança."; } };
        Closed+=(_,_)=>cancellation.Dispose();
    }
    public async Task RunTestsAsync()
    {
        running=true;stopwatch.Start();
        var timer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
        timer.Tick+=(_,_)=>Elapsed.Text=stopwatch.Elapsed.ToString(@"mm\:ss");timer.Start();
        try
        {
            var progress=new Progress<ValidationProgress>(Update);
            Report=await Task.Run(()=>ValidationRunner.RunAsync(steps,fingerprint,progress,cancellation.Token));
            for(int i=0;i<Report.Results.Count;i++) checks[i].Set(Report.Results[i]);
            if(!Report.Cancelled) { TotalProgress.Value=100;Percentage.Text="100%"; }
            Counts(); Counter.Text=$"{Report.Results.Count(r=>r.Status!=CheckStatus.Cancelled)}/{steps.Count} testes executados";
            bool warnings=Report.Results.Any(r=>r.Status==CheckStatus.Warning);
            Phase.Text=Report.Cancelled ? "VALIDAÇÃO CANCELADA" : Report.Approved ? "VALIDAÇÃO CONCLUÍDA" : "CORREÇÕES NECESSÁRIAS";
            CurrentTest.Text=Report.Approved ? warnings ? "Concluída com ressalvas" : "Todos os testes obrigatórios passaram" : "Configuração ainda não liberada";
            CurrentDetail.Text=Report.Approved ? "Consulte os detalhes e ressalvas de cada item abaixo." : "Corrija os itens bloqueados ou desmarque-os, depois valide novamente.";
            Conclusion.Text=post ? "Esta é a leitura final. Itens pendentes de reinicialização exigem nova consulta depois de reiniciar manualmente." : Report.Approved ? "Iniciar configurações principais será liberado ao voltar. A aprovação vale por 10 minutos e apenas para esta seleção/fonte. Ressalvas não são garantias de execução." : "O botão de início continuará bloqueado. Nada foi aplicado por esta validação.";
        }
        catch(Exception ex) { Conclusion.Text="A validação falhou: "+ex.Message; }
        finally { timer.Stop();stopwatch.Stop();Elapsed.Text=stopwatch.Elapsed.ToString(@"mm\:ss");running=false;Activity.IsIndeterminate=false;Activity.Visibility=Visibility.Hidden;CancelButton.IsEnabled=false;FinishButton.IsEnabled=true; }
    }
    private void Update(ValidationProgress p)
    {
        checks[p.Index].Set(p.Current);TotalProgress.Value=p.Percent;Percentage.Text=p.Percent+"%";
        CurrentTest.Text=p.Current.Name;CurrentDetail.Text=p.Current.Detail;Counter.Text=$"{p.Completed}/{p.Total} testes concluídos";
        Counts(); if(IsVisible) TestsList.ScrollIntoView(checks[p.Index]);
    }
    private void Counts()
    {
        PassedCount.Text=$"{checks.Count(c=>c.Status==CheckStatus.Passed)} aprovados";
        WarningCount.Text=$"{checks.Count(c=>c.Status==CheckStatus.Warning)} ressalvas";
        FailedCount.Text=$"{checks.Count(c=>c.Status==CheckStatus.Failed)} bloqueados";
        PendingCount.Text=$"{checks.Count(c=>c.Status==CheckStatus.Pending)} próximos";
    }
    private void FinishClick(object sender,RoutedEventArgs e)=>Close();
    private void CancelClick(object sender,RoutedEventArgs e) { cancellation.Cancel();CancelButton.IsEnabled=false;Conclusion.Text="Cancelamento solicitado. Aguardando o teste atual; os demais serão cancelados."; }
}
