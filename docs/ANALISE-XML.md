# Análise do autounattend.xml

Referência lida integralmente: configurações de instalação, ExtractScript e todos os 14 arquivos incorporados em Extensions. O XML não é distribuído nem executado pelo aplicativo. A análise abaixo precedeu a implementação. Consulta às fontes: 28/10/2026.

| Modificação encontrada | Finalidade / classe | Pós-instalação e adaptação adotada | Reversão / limites | Dependências / restrições |
|---|---|---|---|---|
| Idioma pt-BR, teclado ABNT2, fuso, edição/chave, EULA, OOBE, conta Usuario, senha vazia, autologon | Instalação / conta | Excluídos; nenhum reset ou formatação é executado | Não se aplica | Exclusivos do fluxo de instalação ou fora do escopo |
| ExtractScript, Specialize, DefaultUser, UserOnce, FirstLogon, RunOnce | Orquestração | Substituídos por operações individuais; sem escrever em Setup/Scripts ou carregar NTUSER.DAT | Histórico próprio | Usuário real identificado por SID; sem Sysprep |
| HideFileExt, Hidden, LaunchTo | Explorador / personalização | Valores do usuário atual; ocultos opcional | Valor, tipo e ausência originais | Preferências do Explorer; pode exigir nova sessão |
| TaskbarAl, ShowTaskViewButton | Interface | Alinhamento e mostrar/ocultar Visão de Tarefas individualmente | Backup original | Métodos internos da barra: oferecidos como orientação nas Configurações até validação em VM por build |
| TaskbarEndTask | Interface | Orientação para Configurações avançadas | Manual | Disponibilidade varia por build; não inferir somente pela existência do Registro |
| AllowNewsAndInterests | Widgets / política | Política documentada, apenas edição compatível e computador não gerenciado | Backup original; mostrar novamente por desfazer | Pro/Enterprise/Education; política prévia de terceiros bloqueia alteração |
| DisableSearchBoxSuggestions | Pesquisa | Não é tratado como bloqueio universal de Bing. Adaptado para ConnectedSearchUseWeb onde suportado | Backup original | Bloqueio de web suportado em Enterprise/Education/IoT, não Home/Pro |
| DisableWindowsConsumerFeatures e 17 valores ContentDeliveryManager | Sugestões / privacidade | Política documentada para experiências do consumidor em edições compatíveis; demais flags internas substituídas por orientação | Backup da política; demais mudanças manuais | Enterprise/Education; não promete eliminar toda publicidade |
| MouseSpeed, MouseThreshold1/2 | Entrada | API SystemParametersInfo para desativar aceleração | Estado API anterior e valores originais do Registro | Usuário interativo correto; preferência, não ganho de desempenho |
| StickyKeys Flags=10 | Acessibilidade | Desativa recurso e atalho mantendo os demais bits via API | Estado API anterior e valor original | Seleção explícita; pode afetar acessibilidade |
| LongPathsEnabled | Compatibilidade | Valor documentado do computador | Backup original | Programas precisam aderir a longPathAware; limites de componentes permanecem |
| HideFirstRunExperience, BackgroundModeEnabled, StartupBoostEnabled | Edge / preferências | Políticas documentadas separadas, sem tocar SmartScreen | Backup original | Edge instalado; não substituir política existente; reiniciar Edge manualmente |
| 32 seletores de Appx + Copilot Provider | Remoção opcional | Lista individual; somente conta atual; bloqueio de frameworks, recursos e pacotes não removíveis | Reinstalação pela Store; dados/configurações do aplicativo podem se perder | Sem remoção de provisionamento nem outros usuários; variantes ausentes não são erro |
| OneDrive: apagar instaladores, atalhos e Run | Remoção problemática | Automação excluída; item individual orienta conferir sincronização, baixar arquivos e desfazer backup de pastas antes de usar Configurações | Reinstalação/reconfiguração manual | Não apaga arquivos; redirecionamento de Documentos/Área de Trabalho precisa de revisão humana |
| Handwriting, MathRecognizer, OneSync, QuickAssist, Speech, TextToSpeech, StepsRecorder | Capacidades opcionais | Remoções individuais por identidade exata descoberta; sem curingas fornecidos pelo usuário | Recuperação manual via Recursos opcionais, pode exigir rede/mídia | Administrador; DISM detecta presença; impactos descritos |
| Hello.Face.* | Biometria / segurança | Excluído | Não se aplica | Windows Hello preservado |
| Recall, Microsoft-RemoteDesktopConnection | Recursos opcionais | Desabilitação individual, sem remover payload | Reabilitar em Recursos do Windows; pode exigir rede/reinicialização | Apenas se detectados; RDP desmarcado por padrão |
| Firefox via WinGet | Instalação | Instalador completo oficial Mozilla com Authenticode válido; não exige WinGet | Desinstalação manual | Internet explícita; detecta instalação; x64 |
| Visual C++ x64 | Dependência | Download Microsoft, assinatura e publicador verificados; código de saída e detecção final | Desinstalação manual não recomendada: runtime compartilhado | Não remove versões existentes; internet explícita |
| NetFx3 buscando sources/sxs em todos os discos | Dependência | Até 25H2: recurso DISM com Windows Update ou fonte escolhida; 26H1+: orientação ao instalador oficial específico | Recuperação manual | Build >=28000 não usa NetFx3/DISM; fonte deve corresponder à imagem, DISM faz a validação final |
| DuplicateScheme Desempenho Máximo | Energia | Conforme pedido posterior: pré-selecionado; reutiliza plano reconhecido ou tenta uma cópia com GUID fixo; não duplica cópias do EFR | Restaura GUID original e remove somente a cópia criada nesta aplicação | Firmware/Modern Standby limita planos; consumo e temperatura podem aumentar |
| Menu clássico via CLSID | Interface interna | Indisponível; sem método validado por build neste projeto | Não se aplica | Hack não documentado; atualizações podem quebrar |
| Photo Viewer: associações e comandos rundll32 | Interface legada | Excluído; manter Fotos ou escolher app padrão manualmente | Não se aplica | Não forçar associações nem registrar componente legado |
| ConfigureStartPins vazio em PolicyManager | Menu Iniciar | Excluído; editar armazenamento interno de políticas não é API de gerenciamento | Não se aplica | Configuração suportada exige política e edição compatível; não necessária para objetivo |
| Desativar SAC, SmartScreen, AppHost/WTDS e ocultar ícone Defender | Segurança | Excluído integralmente | Não se aplica | Proteções preservadas |
| PreventDeviceEncryption | Criptografia | Excluído | Não se aplica | BitLocker não é alterado |
| IntegratedServicesRegionPolicySet.json | Remoção Edge | Excluído | Não se aplica | Nenhum arquivo interno alterado |
| RemoteSigned global, maxpwage ilimitado | Segurança / conta | Excluídos | Não se aplica | Sem alterar política de execução ou senhas |
| DevHomeUpdate/OutlookUpdate, ConfigureChatAutoInstall | Instalação / atualizações | Excluídos | Não se aplica | Não interferir no Windows Update/orquestrador |
| disableLastAccess=1, Compact=false | Sistema de arquivos | Excluídos | Não se aplica | Sem evidência de benefício universal; trade-offs de compatibilidade/espaço |

Nenhum ganho de desempenho é prometido. Desligar atividade de fundo do Edge muda seu ciclo de vida e pode aumentar o tempo de abertura. Remover aplicativos é uma escolha de uso e espaço, não uma garantia de velocidade.

Atualizações solicitadas após a análise: tema preto/roxo e logo Efr; perfil inicial com itens viáveis do XML pré-selecionados (inclusive remoção do Bloco de Notas); instalação do Notepad++, que não constava no XML; Desempenho Máximo pré-selecionado. Confirmação antes de aplicar permanece obrigatória. O perfil conservador continua disponível.

## Referências oficiais

- [Distribuição .NET em arquivo único](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/)
- [Caminhos longos e requisito longPathAware](https://learn.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation)
- [Personalização da barra](https://support.microsoft.com/en-us/windows/experience/personalization/customize-the-taskbar-in-windows) e [configurações avançadas](https://learn.microsoft.com/en-us/windows/advanced-settings)
- [Políticas da barra e Widgets](https://learn.microsoft.com/en-us/windows/configuration/taskbar/policy-settings)
- [Search: DoNotUseWebResults e edições](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-search#donotusewebresults)
- [Experience: AllowWindowsConsumerFeatures](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-experience#allowwindowsconsumerfeatures)
- [SystemParametersInfoW](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-systemparametersinfow)
- [Edge: StartupBoostEnabled](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/startupboostenabled), [BackgroundModeEnabled](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/backgroundmodeenabled), [HideFirstRunExperience](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-policies/hidefirstrunexperience)
- [Remove-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/remove-appxpackage) e [Remove-AppxProvisionedPackage](https://learn.microsoft.com/en-us/powershell/module/dism/remove-appxprovisionedpackage)
- [.NET 3.5 por versão do Windows 11](https://learn.microsoft.com/en-us/dotnet/framework/install/dotnet-35-windows-11)
- [Visual C++ oficial](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist)
- [Instalação silenciosa do Firefox](https://support.mozilla.org/en-US/kb/silently-install-uninstall-firefox-enterprise)
- [Checkpoint-Computer](https://learn.microsoft.com/en-us/powershell/module/microsoft.powershell.management/checkpoint-computer?view=powershell-5.1)
- [Opções powercfg](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/powercfg-command-line-options)
- [Notepad++: repositório e releases oficiais](https://github.com/notepad-plus-plus/notepad-plus-plus/releases) e [opções de instalação](https://npp-user-manual.org/docs/command-prompt/)
