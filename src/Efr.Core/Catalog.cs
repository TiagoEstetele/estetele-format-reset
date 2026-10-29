namespace Efr.Core;

public static class Catalog
{
    public const string Explorer = "Explorador e interface", Privacy = "Privacidade e sugestões", Apps = "Aplicativos opcionais",
        Features = "Recursos do Windows", Energy = "Energia", Install = "Instalação de programas";
    public static readonly string[] Categories = [Explorer, Privacy, Apps, Features, Energy, Install, "Histórico e restauração"];
    public static List<Adjustment> Create()
    {
        const string advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string edge = @"SOFTWARE\Policies\Microsoft\Edge";
        var items = new List<Adjustment>
        {
            new("power.ultimate", "Ativar Desempenho Máximo", Energy, "Pré-selecionado conforme o XML e sua preferência. Reutiliza o plano quando disponível; caso contrário, tenta criar uma única cópia identificada do modelo oficial. Pode aumentar consumo, temperatura e reduzir autonomia. Alguns notebooks/Modern Standby não aceitam este plano.", OperationKind.UltimatePower, true, Restart:"Imediato", Undo:"Restaura o plano anterior e remove somente a cópia criada pelo EFR nesta aplicação."),
            Reg("explorer.extensions", "Exibir extensões de arquivos", Explorer, "Mostra .exe, .pdf e outras extensões para ajudar a identificar arquivos.", "HKCU", advanced, "HideFileExt", 0, conservative:true),
            Reg("explorer.thispc", "Abrir o Explorador em Este Computador", Explorer, "Altera a pasta inicial de novas janelas do Explorador.", "HKCU", advanced, "LaunchTo", 1, conservative:true),
            Reg("explorer.hidden", "Exibir arquivos ocultos", Explorer, "Mostra arquivos ocultos; arquivos protegidos do sistema continuam protegidos.", "HKCU", advanced, "Hidden", 1),
            Guide("taskbar.left", "Alinhar a barra à esquerda", Explorer, "Em Comportamentos da barra de tarefas, escolha Esquerda. Alteração interna automática aguarda validação por build.", "ms-settings:taskbar"),
            Guide("taskbar.taskview", "Mostrar ou ocultar Visão de Tarefas", Explorer, "Use o controle Visão de Tarefas nas Configurações da barra.", "ms-settings:taskbar"),
            Guide("taskbar.endtask", "Habilitar Finalizar tarefa na barra", Explorer, "Em Configurações avançadas / Para desenvolvedores, habilite Finalizar tarefa se disponível. Pode encerrar apps sem salvar.", "ms-settings:developers"),
            Guide("explorer.classic", "Menu de contexto clássico", Explorer, "Indisponível: o CLSID do XML não é uma API documentada e não foi validado por build. Use Mostrar mais opções ou Shift+F10.", "ms-settings:about"),
            new("input.mouse", "Desativar aceleração do mouse", Explorer, "Desativa Aprimorar precisão do ponteiro. Preferência de controle; não aumenta desempenho.", OperationKind.Mouse, Restart:"Imediato", Values: MouseValues()),
            new("input.sticky", "Desativar Teclas de Aderência e seu atalho", Explorer, "Impede ativação ao pressionar Shift cinco vezes. Pode prejudicar quem depende deste recurso de acessibilidade.", OperationKind.StickyKeys, Restart:"Imediato", Values:[new("HKCU", @"Control Panel\Accessibility\StickyKeys", "Flags", RegValue.String("10"))]),
            Reg("system.longpaths", "Habilitar caminhos longos", Features, "Remove o limite clássico somente em APIs e aplicativos compatíveis com longPathAware. Não corrige limites de todos os programas.", "HKLM", @"SYSTEM\CurrentControlSet\Control\FileSystem", "LongPathsEnabled", 1, restart:"Reiniciar o computador manualmente"),
            Reg("privacy.widgets", "Desabilitar Widgets", Privacy, "Desabilita o recurso para o computador. Desfaça para voltar à política anterior; exibição do botão também depende da preferência do usuário.", "HKLM", @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0, pro:true),
            Reg("privacy.websearch", "Desativar resultados da web na pesquisa", Privacy, "Política documentada somente para Enterprise, Education e IoT Enterprise. Home/Pro ficam indisponíveis.", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\Windows Search", "ConnectedSearchUseWeb", 0, enterprise:true),
            Reg("privacy.consumer", "Desativar experiências de consumidor", Privacy, "Reduz sugestões e instalações promocionais cobertas pela política. Não elimina toda publicidade.", "HKLM", @"SOFTWARE\Policies\Microsoft\Windows\CloudContent", "DisableWindowsConsumerFeatures", 1, enterprise:true),
            Guide("privacy.suggestions", "Revisar sugestões e publicidade", Privacy, "Nas Configurações de privacidade, revise ID de publicidade, conteúdo sugerido e experiências personalizadas. Flags internas do XML não são aplicadas em lote.", "ms-settings:privacy-general"),
            Reg("edge.startup", "Desativar inicialização acelerada do Edge", Privacy, "Evita pré-carregar processos do Edge no logon. A abertura do navegador pode ficar mais lenta.", "HKLM", edge, "StartupBoostEnabled", 0, restart:"Fechar e abrir o Edge manualmente"),
            Reg("edge.background", "Desativar Edge em segundo plano", Privacy, "Evita manter aplicativos e extensões do Edge ativos após fechar o navegador.", "HKLM", edge, "BackgroundModeEnabled", 0, restart:"Fechar e abrir o Edge manualmente"),
            Reg("edge.first", "Ocultar boas-vindas do Edge", Privacy, "Suprime a experiência de primeiro uso. Não desativa proteções do navegador.", "HKLM", edge, "HideFirstRunExperience", 1, restart:"Próxima abertura do Edge"),
            Guide("app.onedrive", "OneDrive — remoção assistida", Apps, "Antes de desinstalar: confirme sincronização, baixe arquivos somente online e revise o backup de Área de Trabalho/Documentos/Imagens. Desvincule a conta e use Aplicativos instalados. O EFR não apaga arquivos nem altera redirecionamento de pastas.", "ms-settings:appsfeatures"),
            new("feature.rdp", "Desabilitar cliente de Área de Trabalho Remota", Features, "Remove acesso pelo cliente clássico se este recurso existir. Não altera o serviço de acesso remoto.", OperationKind.Feature, true, Restart:"Pode exigir reinicialização manual", Undo:"Reabilitar manualmente em Recursos do Windows; pode exigir internet.", Target:"Microsoft-RemoteDesktopConnection"),
            new("feature.recall", "Desabilitar Recall", Features, "Desabilita apenas se o recurso estiver presente; não remove payload deliberadamente.", OperationKind.Feature, true, Restart:"Pode exigir reinicialização manual", Undo:"Reabilitar manualmente em Recursos do Windows.", Target:"Recall"),
            new("feature.netfx", "Habilitar .NET Framework 3.5", Features, "Até Windows 11 25H2: usa Windows Update ou uma pasta sources/sxs compatível escolhida por você. Build 28000+: instalador oficial separado.", OperationKind.NetFx, true, Restart:"Pode exigir reinicialização manual", Undo:"Desabilitação manual; aplicativos antigos podem depender deste recurso.", Internet:true, Target:"NetFx3", GuideUrl:"https://learn.microsoft.com/en-us/dotnet/framework/install/dotnet-35-windows-11"),
            new("install.firefox", "Instalar Mozilla Firefox x64", Install, "Baixa o instalador completo do servidor oficial Mozilla e verifica assinatura/publicador. Se já estiver instalado, não altera sua versão.", OperationKind.Install, true, Undo:"Desinstalação manual em Aplicativos instalados.", Internet:true, Target:"Firefox", GuideUrl:"https://www.mozilla.org/pt-BR/firefox/new/"),
            new("install.notepadpp", "Instalar Notepad++ x64", Install, "Adicionado a seu pedido. Baixa a versão estável do repositório oficial, verifica SHA-256 e assinatura NOTEPAD++. Não altera automaticamente associações de arquivos.", OperationKind.Install, true, Undo:"Desinstalação manual em Aplicativos instalados.", Internet:true, Target:"NotepadPlusPlus", GuideUrl:"https://notepad-plus-plus.org/downloads/"),
            new("install.vcredist", "Instalar Visual C++ Redistributable x64", Install, "Baixa da Microsoft e verifica assinatura/publicador. Presença do runtime v14 evita reinstalação; atualizar é uma decisão separada.", OperationKind.Install, true, Restart:"Pode exigir reinicialização manual", Undo:"Dependência compartilhada: desinstalação manual pode afetar outros programas.", Internet:true, Target:"VisualCpp", GuideUrl:"https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist")
        };
        string[][] packages = [
            ["Microsoft.Microsoft3DViewer", "Visualizador 3D"], ["Microsoft.BingSearch", "Bing Search"], ["Clipchamp.Clipchamp", "Clipchamp"],
            ["Microsoft.549981C3F5F10", "Cortana"], ["Microsoft.Windows.DevHome", "Dev Home"], ["MicrosoftCorporationII.MicrosoftFamily", "Microsoft Family"],
            ["Microsoft.WindowsFeedbackHub", "Hub de Comentários"], ["Microsoft.GetHelp", "Obter Ajuda"], ["Microsoft.Getstarted", "Dicas / Introdução"],
            ["microsoft.windowscommunicationsapps", "Email e Calendário"], ["Microsoft.WindowsMaps", "Mapas"], ["Microsoft.MixedReality.Portal", "Portal de Realidade Mista"],
            ["Microsoft.BingNews", "Notícias"], ["Microsoft.WindowsNotepad", "Bloco de Notas"], ["Microsoft.MicrosoftOfficeHub", "Microsoft 365 (hub)"],
            ["Microsoft.Office.OneNote", "OneNote (Store)"], ["Microsoft.OutlookForWindows", "Outlook para Windows"], ["Microsoft.MSPaint", "Paint 3D (pacote legado)"],
            ["Microsoft.People", "Pessoas"], ["Microsoft.Windows.Photos", "Fotos"], ["Microsoft.PowerAutomateDesktop", "Power Automate"],
            ["MicrosoftCorporationII.QuickAssist", "Assistência Rápida"], ["Microsoft.SkypeApp", "Skype"], ["Microsoft.MicrosoftSolitaireCollection", "Solitaire"],
            ["MicrosoftTeams", "Teams (legado)"], ["MSTeams", "Microsoft Teams"], ["Microsoft.Todos", "Microsoft To Do"], ["Microsoft.Wallet", "Carteira"],
            ["Microsoft.BingWeather", "Clima"], ["Microsoft.YourPhone", "Vincular ao Celular"], ["Microsoft.ZuneMusic", "Media Player / Música"],
            ["Microsoft.ZuneVideo", "Filmes e TV"], ["Microsoft.Windows.Ai.Copilot.Provider", "Copilot Provider (legado do XML)"], ["Microsoft.Copilot", "Microsoft Copilot"]
        ];
        foreach (var p in packages)
            items.Add(new("app." + p[0].ToLowerInvariant(), "Remover " + p[1], Apps,
                "Remove somente o pacote desta conta. Não remove provisionamento para futuras contas nem instalações de outros usuários. Salve/exporte os dados do aplicativo antes de remover.",
                OperationKind.Appx, Undo:"Reinstalar pela Microsoft Store; configurações e dados internos do app podem não voltar.", Target:p[0]));
        string[][] capabilities = [
            ["Language.Handwriting", "Reconhecimento de escrita", "Afeta escrita à mão nos idiomas instalados."],
            ["MathRecognizer", "Reconhecimento matemático", "Afeta entrada matemática manuscrita."],
            ["OneCoreUAP.OneSync", "Sincronização OneSync", "Pode afetar sincronização de email, contatos e calendário."],
            ["App.Support.QuickAssist", "Assistência Rápida (capacidade legada)", "Afeta suporte remoto pelo componente antigo."],
            ["Language.Speech", "Reconhecimento de fala", "Afeta reconhecimento de voz dos idiomas instalados."],
            ["Language.TextToSpeech", "Texto para fala", "Pode afetar leitores de tela e acessibilidade. Revise antes de remover."],
            ["App.StepsRecorder", "Gravador de Passos", "Remove o gravador legado se estiver instalado."]
        ];
        foreach (var c in capabilities)
            items.Add(new("cap." + c[0].ToLowerInvariant(), "Remover " + c[1], Features,
                c[2] + " Abrange as variantes/idiomas detectados deste componente.", OperationKind.Capability, true,
                Restart:"Pode exigir reinicialização manual", Undo:"Reinstalar manualmente em Recursos opcionais; pode exigir download ou mídia compatível.", Target:c[0]));
        return items;
    }
    private static Adjustment Reg(string id, string name, string category, string description, string hive, string path, string key, int value,
        bool conservative = false, string restart = "Nova sessão do Windows / novas janelas", bool enterprise = false, bool pro = false)
        => new(id, name, category, description, OperationKind.Registry, hive == "HKLM", conservative, restart,
            Values:[new(hive, path, key, RegValue.Dword(value))], EnterpriseOnly:enterprise, ProOnly:pro);
    private static Adjustment Guide(string id, string name, string category, string description, string url)
        => new(id, name, category, description, OperationKind.Guide, Undo:"Alteração manual nas Configurações.", GuideUrl:url, UnavailableReason:"Automação indisponível; orientação manual disponível.");
    public static RegTarget[] MouseValues() => [
        new("HKCU", @"Control Panel\Mouse", "MouseSpeed", RegValue.String("0")),
        new("HKCU", @"Control Panel\Mouse", "MouseThreshold1", RegValue.String("0")),
        new("HKCU", @"Control Panel\Mouse", "MouseThreshold2", RegValue.String("0"))];
}
