# Versionamento e publicação

Destino configurado: `https://github.com/TiagoEstetele/estetele-format-reset.git`.

O repositório Git local usa a branch `main`, autor Tiago Estetele e o e-mail informado pelo mantenedor. Nome/e-mail em commits públicos também ficam públicos. Uma configuração local do Git não cria o repositório no GitHub e não autentica a conta.

## Primeira publicação

1. Entre no GitHub com `TiagoEstetele` e crie um repositório **público** chamado `estetele-format-reset`. Deixe-o vazio: README, licença e `.gitignore` já existem neste projeto.
2. Na pasta do projeto, confira `git status` e `git remote -v`.
3. Publique os commits com `git push -u origin main`. O Git Credential Manager pode abrir a autenticação oficial. Não cole tokens ou senhas em commits, comandos compartilhados ou chats.
4. Em **Actions**, aguarde o workflow `Windows build and tests`. Ele compila, executa os testes isolados, renderiza a interface em simulação e produz o ZIP. A execução remota só estará validada após esse workflow passar no GitHub.
5. Em **Settings → Code security**, habilite o canal de relato privado de vulnerabilidades, se disponível. Proteja a branch principal exigindo o CI nas contribuições, conforme os recursos disponíveis na conta.

Se já houver conteúdo no repositório remoto, consulte e integre o histórico antes de enviar. Não use `push --force` para substituir arquivos existentes.

Alternativa com GitHub CLI já autenticado, caso o repositório ainda não exista:

```powershell
gh auth login
gh repo create TiagoEstetele/estetele-format-reset --public --source . --remote github-publish --push
```

O nome alternativo `github-publish` evita conflito com o `origin` já configurado. GitHub CLI é opcional e não é dependência do aplicativo.

## Distribuir o aplicativo

O `.exe` tem aproximadamente 134 MiB e deve ser anexado a uma **Release**, não incluído no histórico Git. O GitHub bloqueia arquivos maiores que 100 MiB no Git comum. [Limites oficiais](https://docs.github.com/en/repositories/working-with-files/managing-large-files/about-large-files-on-github).

Execute `./tools/build.ps1`. Para uma primeira distribuição de avaliação, crie uma tag como `v0.1.0-preview.1`, usando o commit revisado. Em **Releases → Draft a new release**, selecione a tag, marque **pre-release** e anexe:

- `dist/estetele-format-reset-win-x64.zip` — recomendado; inclui executável, documentação e licenças.
- `dist/SHA256SUMS.txt` — hashes do ZIP e do executável extraído.

Descreva as limitações em `docs/VALIDACAO.md` e não anuncie uma versão estável enquanto os testes nativos em VM estiverem pendentes. As instruções de [criação de Releases](https://docs.github.com/en/repositories/releasing-projects-on-github/managing-releases-in-a-repository) explicam como preparar um rascunho antes da publicação.

## Próximas versões

```powershell
git switch -c fix/descricao
# Edite, execute tools/build.ps1 e confira os arquivos.
git diff --check
git add src tests docs README.md CHANGELOG.md
git commit -m "fix: descreva o comportamento corrigido"
git push -u origin fix/descricao
```

Abra um pull request para `main`. Atualize a versão do projeto e o changelog quando preparar uma nova distribuição. Não versione `.tools`, `dist`, `artifacts`, históricos reais ou `autounattend.xml`.

O CI segue o fluxo oficial de [compilação e testes .NET no GitHub Actions](https://docs.github.com/en/actions/tutorials/build-and-test-code/net). Não publica Releases automaticamente e não executa otimizações reais no runner.
