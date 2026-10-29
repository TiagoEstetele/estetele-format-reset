# Segurança

## Estado do projeto

A série 0.1.x é experimental. O foco é a versão mais recente do código; não há garantia de suporte a versões antigas ou prazo fixo de correção. O executável atual não possui assinatura de distribuição.

## Relatar vulnerabilidades

Use a opção **Security → Report a vulnerability** no repositório, quando habilitada. Se ela não estiver disponível, abra apenas uma issue pedindo um canal privado de contato, sem incluir exploit, dados de terceiros ou detalhes sensíveis. Não envie tokens, senhas ou seu histórico completo.

Informe privadamente a versão, build/edição do Windows, impacto e um exemplo mínimo em ambiente descartável. Separe falhas de segurança de erros comuns de configuração.

## Proteções e limites

- Operações são explícitas, com revisão, pré-validação e checagem posterior.
- Downloads exigem HTTPS, origens permitidas e assinatura Authenticode do publicador esperado. Notepad++ também exige hash da release oficial.
- Backups são locais, vinculados à conta e ao computador, sem criptografia adicional implementada pelo EFR. Proteja sua conta e a pasta de histórico.
- A restauração detecta divergências, mas não substitui backup completo nem controla outros processos administrativos.
- SHA-256 verifica integridade em relação ao manifesto obtido de fonte confiável; não substitui assinatura de código.
- Não desative Defender, SmartScreen ou políticas corporativas para executar o programa.

Antes de distribuição ampla, execute a [matriz de VM](docs/VALIDACAO.md). Testes automatizados em memória não equivalem a uma auditoria de segurança ou validação de todas as builds.
