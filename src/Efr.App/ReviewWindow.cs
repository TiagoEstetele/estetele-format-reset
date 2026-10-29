using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Efr.Core;
using Microsoft.Win32;

namespace Efr.App;

public sealed class ReviewWindow : Window
{
    private readonly TextBox source = new() { MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly CheckBox sourceConfirmed = new() { Content = "Confirmei que a fonte corresponde à build, arquitetura x64 e idioma deste Windows.", Margin = new(0,8,0,0) };
    public ApplyOptions Options => new(string.IsNullOrWhiteSpace(source.Text) ? null : source.Text.Trim(), sourceConfirmed.IsChecked == true);
    public ReviewWindow(IReadOnlyList<Adjustment> items, bool demo, bool validation = false, ApplyOptions? lockedOptions = null, IEnumerable<string>? caveats = null)
    {
        Title = "Revisar alterações · Efr"; Width = 720; Height = 680; MinHeight = 500; MinWidth = 600; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)Application.Current.Resources["Canvas"]; Foreground = (Brush)Application.Current.Resources["Ink"];
        SourceInitialized += (_,_) => WindowTheme.Apply(this);
        var root = new DockPanel { Margin = new(24) }; Content = root;
        var header = new TextBlock { Text = validation ? "Confira o que será validado" : demo ? "Revisão da simulação" : "Confira antes de aplicar", FontSize = 24, FontWeight = FontWeights.SemiBold, Margin = new(0,0,0,14) };
        DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
        var footer = new StackPanel { Margin = new(0,16,0,0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var acknowledge = new CheckBox { Content = new TextBlock { Text = "Revisei todos os itens, inclusive as remoções pré-selecionadas, e seus limites de desfazer." }, Margin = new(0,0,0,14) };
        footer.Children.Add(acknowledge);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = new Button { Content = "Voltar", IsCancel = true };
        var apply = new Button { Content = validation ? "Validar seleção" : demo ? "Simular agora" : "Aplicar seleção", IsEnabled = false, Background = new SolidColorBrush(Color.FromRgb(132,69,223)), Foreground = Brushes.White };
        acknowledge.Checked += (_,_) => apply.IsEnabled = true; acknowledge.Unchecked += (_,_) => apply.IsEnabled = false;
        apply.Click += (_,_) =>
        {
            if (!string.IsNullOrWhiteSpace(source.Text) && sourceConfirmed.IsChecked != true)
            { MessageBox.Show(this, "Confirme a compatibilidade da fonte selecionada. O DISM fará a validação final; fontes incompatíveis podem falhar.", "Fonte .NET 3.5"); return; }
            DialogResult = true;
        };
        buttons.Children.Add(cancel); buttons.Children.Add(apply); footer.Children.Add(buttons);
        var list = new StackPanel(); root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        if(validation) list.Children.Add(new TextBlock { Text="A validação não aplica ajustes: consulta o Windows, testa permissões e verifica dependências. Se houver instaladores selecionados, baixa os arquivos oficiais e confere suas assinaturas. O início das mudanças só será liberado após essa etapa.", Margin=new(0,0,0,12), Foreground=(Brush)Application.Current.Resources["Accent"] });
        if(caveats!=null) foreach(var caveat in caveats) list.Children.Add(new TextBlock { Text="RESSALVA DA VALIDAÇÃO: "+caveat, Foreground=new SolidColorBrush(Color.FromRgb(232,197,139)),Margin=new(0,0,0,12) });
        list.Children.Add(new TextBlock { Text = "Nenhum reinício é automático. O cancelamento interrompe a fila entre operações; DISM, remoções e instaladores em andamento precisam terminar.", Foreground = (Brush)Application.Current.Resources["Muted"], Margin = new(0,0,0,12) });
        if (items.Any(i => i.Internet)) list.Children.Add(new TextBlock { Text = "INTERNET: a seleção inclui downloads oficiais ou consulta ao Windows Update. Instaladores têm assinatura/publicador verificados. Os termos do fabricante se aplicam à instalação.", Foreground = new SolidColorBrush(Color.FromRgb(221,188,255)), Margin = new(0,0,0,12) });
        foreach (var item in items)
        {
            var block = new StackPanel { Margin = new(12) };
            block.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeights.SemiBold, FontSize = 15 });
            block.Children.Add(new TextBlock { Text = item.Description, Margin = new(0,6,0,6) });
            block.Children.Add(new TextBlock { Text = item.Details, FontSize = 11, Foreground = (Brush)Application.Current.Resources["Muted"] });
            list.Children.Add(new Border { Child = block, Background = (Brush)Application.Current.Resources["Surface"], CornerRadius = new(7), Margin = new(0,0,8,10) });
        }
        if (items.Any(i => i.Kind == OperationKind.NetFx))
        {
            list.Children.Add(new TextBlock { Text = ".NET 3.5 — fonte opcional", FontWeight = FontWeights.SemiBold, Margin = new(0,8,0,8) });
            list.Children.Add(new TextBlock { Text = "Deixe vazio para Windows Update. Para trabalhar offline, selecione sources\\sxs de uma imagem compatível montada por você. A pasta não é procurada automaticamente em pendrives.", Margin = new(0,0,0,8) });
            list.Children.Add(source);
            var browse = new Button { Content = "Escolher pasta…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new(0,8,0,0) };
            browse.Click += (_,_) => { var dialog = new OpenFolderDialog { Title = "Selecionar sources\\sxs compatível" }; if (dialog.ShowDialog(this) == true) { source.Text = dialog.FolderName; sourceConfirmed.IsChecked = false; } };
            list.Children.Add(browse); list.Children.Add(sourceConfirmed);
            if(lockedOptions!=null) { source.Text=lockedOptions.NetFxSource??"";sourceConfirmed.IsChecked=lockedOptions.SourceConfirmed;source.IsReadOnly=true;sourceConfirmed.IsEnabled=false;browse.IsEnabled=false;list.Children.Add(new TextBlock { Text="Fonte bloqueada para manter a configuração validada. Volte e execute Validar configuração para alterá-la.", Margin=new(0,8,0,0) }); }
        }
    }
}
