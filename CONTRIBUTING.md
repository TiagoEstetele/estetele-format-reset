# Contribuir com o EFR

Obrigado por contribuir. Antes de propor novas otimizações, consulte o [escopo](README.md) e a [análise técnica](docs/ANALISE-XML.md).

## Desenvolvimento

1. Faça um fork e clone o repositório em Windows x64.
2. Instale o SDK indicado em `global.json`.
3. Crie uma branch: `git switch -c fix/descricao-curta`.
4. Execute `./tools/build.ps1`: testes isolados, compilação, publicação, verificação WPF simulada e pacote.
5. Abra um pull request com o problema, comportamento resultante e evidências de teste.

Os testes são um executável próprio: use `dotnet run --project tests/Efr.Tests/Efr.Tests.csproj -c Release`. `dotnet test` não executa essa suíte. Nunca use a máquina de trabalho como alvo de testes destrutivos.

## Regras de implementação

- Documente a API/fonte oficial e as builds/edições compatíveis.
- Cada operação deve ter detecção, impacto, requisitos, verificação posterior e limites de reversão.
- Alterações reversíveis precisam de backup durável antes da escrita, preservando tipo, valor e ausência originais.
- Preserve alterações externas. Não amplie a remoção de pacotes para todas as contas.
- Não desative mecanismos de segurança, atualizações ou políticas de execução.
- Não execute texto do XML, texto baixado ou conteúdo fornecido pelo usuário como código. Dados devem ser passados como dados.
- Nova remoção ou mudança nativa exige teste em VM descartável e registro da build, edição e resultado. Simulação não demonstra compatibilidade real.
- Para bugs, acrescente um teste que reproduza a falha antes da correção, quando houver comportamento significativo a proteger.

## Privacidade e revisão

Não inclua históricos reais, XML de instalação, perfis de usuário, certificados privados, instaladores, SDKs ou tokens. Imagens públicas devem usar `--self-test-ui`, com dados sintéticos. Não publique detalhes de vulnerabilidades em issues abertas; consulte [SECURITY.md](SECURITY.md).

Contribuições aceitas são distribuídas sob a licença MIT do projeto. Componentes de terceiros devem manter seus próprios avisos.
