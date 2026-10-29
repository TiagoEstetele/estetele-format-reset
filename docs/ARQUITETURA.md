# Arquitetura e segurança operacional

**C# / .NET 10 + WPF**: GUI nativa, sem navegador embutido, dependências de runtime incluídas no executável e suporte a Windows 11 x64. Não há servidor, serviço ou tarefa agendada do EFR.

`Adjustment` identifica cada operação. `WindowsBackend` detecta, captura, executa e restaura. `Engine` coordena backup durável anterior à escrita, idempotência, validação e tratamento de falhas. `SimulationBackend` não usa APIs do Windows e é compartilhado pela demonstração e pelos testes.

`Preflight` cria uma lista explícita de testes: quatro testes globais e dois por item selecionado. `ValidationRunner` executa cada teste e informa início/fim, porcentagem calculada a partir das conclusões, erros e ressalvas. `ValidationWindow` apresenta todos os testes, incluindo os que ainda estão por vir. O relatório e o histórico persistem no diretório de dados do usuário.

A liberação do início depende de `ValidationReport.Approved`, fingerprint da seleção/fonte/SID/build/privilégios/gerenciamento/identidade/suporte e validade máxima de 10 minutos. Não é persistida como autorização reutilizável ao reiniciar. Alterar a seleção invalida o relatório na interface; aplicar revalida identidade, compatibilidade e resultado pelo motor. Depois da fila, um inventário novo alimenta outra tela de verificação dos efeitos. Testes cancelados não incrementam o percentual de execução; seleção vazia não recebe aprovação.

Operações são sequenciais, fora da thread da interface. Progresso é a contagem real de operações finalizadas, não porcentagem inventada de DISM/download. Enquanto uma operação individual demora, a interface mostra qual é. Parar afeta somente as próximas; fechar é bloqueado durante operações. Consultas têm timeout; processos que estão alterando o sistema não são terminados à força.

## Identidade e políticas

A aplicação inicia `asInvoker`. O SID do token é comparado com o SID do processo do shell, e a elevação carrega o SID de origem. Se não for possível provar a identidade, a escrita é bloqueada. Não se carrega perfil DefaultUser. Elevação com outro administrador não personaliza a conta errada.

Máquinas com domínio/MDM/política local configurada detectada ficam bloqueadas para escrita nesta versão voltada a uso pessoal. A mera existência de chaves de histórico de GPO não é tratada como gerenciamento: elas também existem em computadores pessoais. Políticas de Registro preexistentes não são assumidas pelo aplicativo, mesmo que o computador não esteja gerenciado. Não há tentativa de contornar ACLs, WDAC, AppLocker ou política de execução. Detecção de gerenciamento é conservadora; não é uma implementação completa de RSoP/Intune.

`RegistryStore` usa a visão x64 e não expande REG_EXPAND_SZ ao capturar. Valores originalmente inexistentes são removidos ao desfazer. Chaves vazias eventualmente criadas podem permanecer, evitando remover conteúdo não pertencente ao EFR. Estados nativos de mouse/StickyKeys são restaurados via API, seguidos dos valores persistidos originais.

## Histórico

`history.json` é escrito em arquivo temporário, recebe flush e é substituído atomicamente; a versão anterior fica em `.previous`. Histórico ilegível ou de outro SID/máquina causa recusa, não redefinição silenciosa. O lock de arquivo impede duas janelas da mesma conta de escreverem no mesmo histórico. Não editar o JSON enquanto o aplicativo está aberto.

Cada operação tem um backup original ativo. Reaplicar não troca esse original. Há registro de intenção antes das mudanças, sucesso/falha depois, e arquivos anteriores preservados. Falhas em ajustes reversíveis tentam voltar ao estado imediatamente anterior à tentativa. Falha do rollback é explicitada. Restauração recusa valores externos que não correspondam ao esperado/original; restaurações parciais de valores reconhecidos podem ser retomadas.

Histórico interrompido não prova sucesso. A reaplicação com resultado anterior desconhecido exige estado original ou valores reconhecidos da operação; mudanças externas bloqueiam escrita. A abertura rejeita coleções nulas e backups estruturalmente inválidos. Para operações nativas com snapshot incompleto ou divergente, pode ser necessária recuperação manual. Backups não substituem imagem completa do sistema nem backup de arquivos pessoais.

## Energia e instaladores

Desempenho Máximo reutiliza GUID conhecido ou nome padrão reconhecido em pt-BR/inglês. A cópia gerenciada pelo EFR usa `8f7d9310-b065-4a21-92df-28f4da3abca1`; não se duplica esse GUID. Desfazer volta ao plano anterior e só apaga a cópia gerenciada quando ela não existia no snapshot original. Planos personalizados renomeados não podem ser identificados com certeza como equivalentes. O Windows pode recusar o modelo em notebooks; não se alteram flags de Modern Standby.

Instaladores são baixados somente após confirmação. Redirecionamentos HTTPS usam lista fechada de hosts. Assinatura Authenticode precisa ser válida e corresponder ao publicador esperado. Notepad++ resolve a release estável diretamente na API do repositório oficial e exige SHA-256; não usa seu serviço de atualização. Arquivo fica bloqueado contra escrita entre verificação e execução. Downloads têm limite de tamanho e timeout. Códigos 0 e 3010 são tratados; presença final precisa ser confirmada. Erros não são tratados como instalação bem-sucedida.

Na pré-validação, o download e a verificação são realizados sem executar o instalador. O arquivo fica num diretório temporário exclusivo do EFR; a aplicação reaproveita esse cache, recalcula o hash e verifica a assinatura novamente. O cache desta sessão é limpo ao concluir a instalação ou fechar normalmente. A consulta online ao Windows Update para NetFx pesquisa disponibilidade sem instalar atualizações. A compatibilidade final do payload e a criação de plano inexistente são ressalvas explícitas, pois não têm equivalente seguro de teste completo sem modificar o sistema.

WinGet é detectado, mas não é necessário: o caminho alternativo oficial direto é o método adotado. Instaladores e recursos podem precisar de conectividade adicional do Windows para validação de confiança ou dependências. Ajustes locais não realizam downloads pelo EFR.

## Perfil inicial

O perfil inicial seleciona os itens viáveis inspirados no XML, remoção do Bloco de Notas, instalação do Notepad++ e Desempenho Máximo. O perfil conservador permanece disponível. Pré-seleção não executa alterações: são necessárias validação e confirmação explícitas. Nenhuma otimização foi aplicada no computador de desenvolvimento durante os testes.
