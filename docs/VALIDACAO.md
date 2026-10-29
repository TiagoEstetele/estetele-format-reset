# Validação e limitações conhecidas

Ambiente de desenvolvimento: Windows x64. SDK .NET 10.0.403 obtido da Microsoft em uma pasta local; arquivo validado pelo SHA-512 dos metadados oficiais. Aplicativo destinado a Windows 11 x64.

## Verificações executadas

- Compilação Release com warnings tratados como erros.
- Publicação `win-x64`, self-contained, single-file, runtime incluído.
- Executável publicado executado com saída 0 no teste offline. No sandbox, foi necessário apontar `DOTNET_BUNDLE_EXTRACT_BASE_DIR` para uma pasta gravável do workspace; a primeira tentativa na pasta temporária padrão teve falha de extração. Em uso normal, o runtime precisa de permissão de escrita na pasta temporária para extrair as bibliotecas nativas.
- **49/49 testes isolados passaram** na revisão de 29/10/2026: estados, idempotência, backup anterior à escrita, ausência/tipos/conteúdo originais, falha de disco, rollback, falha de validação, conflitos externos, identidade/SID, permissões, interrupção, recuperação parcial, ciclos arquivados, persistência, corrupção, lock, lista de origens oficiais, defaults pedidos, energia simulada, cobertura de todos os itens da pré-validação, expiração/fingerprint, cancelamento, progresso real, ressalvas e pós-validação.
- Os 15 testes adicionais reproduziram seis falhas/lacunas antes da correção: contagem de cancelados como concluídos, reaplicação interrompida sobrescrevendo mudança externa, aprovação de seleção vazia, fingerprint sem contexto completo, coleções nulas no histórico e URLs com credenciais/portas inesperadas. O conjunto inicialmente teve 43/49; após as correções, 49/49. Também verifica concorrência, falha ao persistir resultado, schema/máquina divergentes, versão anterior do histórico e limites temporais.
- Diagnóstico nativo **somente de leitura**, Windows 11 Pro build 26200 sem elevação: zero falhas nas consultas acessíveis; dez consultas de recursos/capacidades identificadas como pendentes por exigir administrador. Registro, APIs de mouse/acessibilidade, energia, detecção de programas e inventário Appx foram consultados. A primeira execução no sandbox teve acesso negado a Appx; repetição fora do sandbox confirmou o inventário. Nenhuma aplicação, remoção, instalação ou restauração foi executada.
- A primeira interface foi aberta em `--demo` e seu layout conferido. O controle de aplicativos foi interrompido por Escape; nenhuma alteração real do Windows foi feita.
- O tema final possui uma verificação WPF offline via `--self-test-ui`: renderiza seis telas do próprio aplicativo em PNG (incluindo validação), verifica os defaults, bloqueio/liberação/invalidação do botão de início, aplica e desfaz duas operações em memória. Não captura a área de trabalho nem usa entrada no computador.
- Notepad++ 8.9.8.1 foi baixado do repositório oficial somente para inspeção: Authenticode válido, publicador `NOTEPAD++`. O instalador **não foi executado**. A aplicação resolve a versão estável na hora de instalar, exige hash SHA-256 e assinatura válida.

Resultados locais ficam em `artifacts/test-results.txt`, `artifacts/native-readonly.txt` e `artifacts/ui-check/`. O resultado anterior às correções foi preservado em `artifacts/test-results-before-review-fixes.txt`. Esses arquivos são ignorados pelo Git. Duas telas sintéticas são copiadas para `docs/images/` para ilustrar o README. A geração dos PNGs não demonstra compatibilidade das alterações com o Windows real.

O script `tools/build.ps1` executa a suíte, publica, testa o executável pelo fixture WPF e gera ZIP/SHA-256. O workflow do GitHub repete esse fluxo; sua execução remota depende do envio do repositório e não foi presumida como aprovada pelo teste local.

## O que não foi validado em uma VM

Não foram aplicados ajustes reais, removidos aplicativos, instalados Firefox/Notepad++/VC++, habilitado .NET 3.5, criado ponto de restauração, elevado o aplicativo com outra conta nem alterado plano de energia no computador de desenvolvimento. Não houve teste em máquina virtual. Esses caminhos têm implementação, guardas e verificação posterior, mas ainda exigem validação prática numa VM descartável antes de distribuição ampla.

## Limitações

- A pré-validação real roda no computador do usuário ao clicar no botão correspondente. Os testes de desenvolvimento usam simulação; não foi executado o perfil real no computador de desenvolvimento. Validação não executa remoções/instalações para “provar” que elas funcionarão. O resultado final é checado após aplicar.
- Erros obrigatórios bloqueiam o início. Ressalvas (por exemplo criação de plano inexistente ou validação definitiva do payload NetFx) são explicitadas, mas não bloqueiam por si só. O usuário as revê na confirmação final. Instalação/servicing ainda pode falhar por alterações externas, rede, políticas, arquivos em uso ou estado futuro do Windows.

- Alinhamento da barra, Visão de Tarefas, Finalizar tarefa, sugestões gerais e OneDrive oferecem orientação manual. Menu clássico não é automatizado. Essas ausências são intencionais enquanto não houver método validado por build.
- .NET 3.5 na build 28000+ é encaminhado à documentação do instalador separado; a instalação automática deste formato não está implementada.
- Remoção Appx atende somente o usuário atual. Pacotes provisionados, outras contas e versões Win32 permanecem. Recuperação pode exigir Store/download e não recupera automaticamente dados internos do aplicativo.
- Preferências de Registro mostram o estado gravado; isso não prova que toda interface já mudou. Algumas opções exigem nova sessão; GPOs e mudanças de versão podem prevalecer.
- Gerenciamento é detectado de forma conservadora. Domínio/MDM/política local configurada bloqueia escrita; políticas preexistentes não são substituídas. Chaves de histórico de GPO, sozinhas, não geram bloqueio. Não há gerenciamento empresarial/RSoP completo.
- Elevação com outra conta é bloqueada. Usuário padrão sem administrador da mesma conta pode aplicar somente ajustes de usuário.
- O lock protege o histórico de uma conta. Não execute mudanças simultâneas por contas diferentes ou por outros utilitários; o programa detecta divergências na restauração, mas não controla todo o sistema.
- Desempenho Máximo pode não existir ou ser recusado em Modern Standby. Planos equivalentes renomeados não são identificados com certeza. Uma falha não autoriza alterar firmware ou desabilitar Modern Standby.
- O perfil do XML pré-seleciona remoções e dependências conforme pedido posterior. Revise os idiomas de fala/escrita e impactos de acessibilidade antes de confirmar. O perfil conservador continua disponível.
- Não se promete ganho de desempenho. Energia máxima pode elevar consumo e temperatura. Não há benchmark neste projeto.
- Não há cancelamento forçado de DISM/instaladores. A fila para entre operações. Falhas podem deixar remoções parciais de componentes não reversíveis; consulte o resultado por operação.
- Ponto de restauração depende da proteção do sistema e limites do Windows. Backup do EFR não equivale a imagem do disco; arquivos pessoais não são copiados.
- Binário sem assinatura de distribuição. Se o Windows exigir uma política de confiança/certificado, isso deve ser resolvido por distribuição assinada, sem desativar proteções.

## Roteiro recomendado para VM descartável

1. Snapshot limpo; testar Home e Pro 24H2/25H2, Enterprise para políticas específicas e build 28000+ para .NET.
2. Conta padrão, administrador da mesma conta e elevação com credenciais diferentes; confirmar bloqueio da conta incorreta.
3. Aplicar/restaurar cada ajuste reversível, incluindo valores inexistentes e tipos diferentes; abrir o Windows para observar o efeito real.
4. Testar aplicativos instalados e ausentes, capacidade inexistente, DISM pendente, fonte NetFx incompatível e rede indisponível.
5. Testar criação/reutilização/recusa do plano máximo e retorno ao plano original, inclusive encerramento abrupto entre criação e ativação.
6. Testar downloads válidos, assinatura inválida, hash inválido, códigos não zero e 3010; verificar que falha do Notepad++ preserva Bloco de Notas quando ambos foram selecionados.
7. Simular encerramento abrupto e histórico corrompido; revisar recuperação sem sobrescrever mudanças externas.
