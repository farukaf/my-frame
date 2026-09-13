# My Frame

Aplicativo Windows em .NET MAUI para visualizar o inventário local do AlecaFrame,
acompanhar coleção e maestria, planejar farm e separar excedentes entre platinum e
ducats.

O aplicativo é local e somente leitura. A distribuição inclui um servidor MCP local
para que clientes como Codex e Claude consultem, em dados estruturados, o mesmo
inventário e as mesmas análises exibidas na interface.

## Estado do projeto

O fluxo principal já está implementado: leitura dos dados do AlecaFrame, catálogo,
integração read-only com Warframe.Market, cache, recomendações, atualização automática,
configurações e interface desktop.

- [Roteiro da versão atual](docs/PLANO.md)
- [Plano detalhado do MCP](docs/MCP.md)
- [Arquitetura](docs/ARQUITETURA.md)
- [Regras, testes e segurança](docs/REGRAS-E-VALIDACAO.md)
- [Checklist de entrega e hardening](todo.md)

### Próxima evolução — planejada, não implementada

Plataforma local de dados com captura via Overwolf sem dependência do AlecaFrame,
SQLite, sincronização observável, fontes públicas, referências Wiki/Overframe e MCP
de domínio. A aplicação fornece os dados; a LLM compõe recomendações e planos.

- [Plano detalhado: arquitetura, fases, dependências e critérios de entrega](docs/PLATAFORMA-DE-DADOS.md)
- [Pesquisa: documentação, fontes, evidências e lacunas](docs/FONTES-DE-DADOS.md)
- [Testes: matriz, homologação e gates de aprovação](docs/VALIDACAO-PLATAFORMA.md)
- [F0: auditoria e evidências de execução](docs/validacoes/2026-09-12-f0.md)
- [F1: coletor Overwolf e roteiro de homologação](docs/validacoes/2026-09-13-f1.md)

A captura completa do inventário e o acesso permitido às fontes comunitárias ainda
precisam ser comprovados. Os recursos descritos abaixo continuam sendo os atuais.

## Segurança

- A pasta do AlecaFrame é sempre aberta em modo somente leitura.
- `WFMarketToken.tk` é lido somente em memória e nunca é copiado ou registrado.
- A autenticação do Warframe.Market é usada apenas para consultar o perfil e as
  ordens do próprio usuário. O aplicativo não cria, altera ou remove anúncios.
- Dados privados e snapshots reais não fazem parte do repositório.
- O MCP usa `stdio`, sem porta de rede e sem autenticação própria, e expõe
  apenas operações declaradas como somente leitura.

## MCP local

Depois de publicar ou instalar o My Frame, abra **Settings > AI access (MCP)**. A tela
mostra o caminho de `MyFrame.Mcp.exe` e oferece botões para copiar os comandos completos.
Os equivalentes no PowerShell são:

```powershell
codex mcp add my-frame -- "C:\caminho\do\My Frame\MyFrame.Mcp.exe"
claude mcp add --transport stdio --scope user my-frame -- "C:\caminho\do\My Frame\MyFrame.Mcp.exe"
```

Verifique com `codex mcp list` ou `claude mcp get my-frame`. Para remover, use
`codex mcp remove my-frame` ou `claude mcp remove --scope user my-frame`.

O servidor não usa o token do Warframe.Market, não faz chamadas de rede e não escreve
nos arquivos. Ele lê o inventário do AlecaFrame e os caches que o aplicativo atualiza.
Preços ausentes ou antigos, ordens não confirmadas e cobertura incompleta aparecem nos
metadados; não são convertidos em zero. A conta só é incluída quando `get_overview`
recebe `includeAccount=true`. Embora o processo seja local, o cliente conectado pode
enviar os resultados ao provedor de IA usado por ele.

## Desenvolvimento

Requisitos: Windows, .NET 10 SDK e workload `maui-windows`.

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet test MyFrame.Mcp.Tests/MyFrame.Mcp.Tests.csproj
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
```

Para gerar uma pasta self-contained com o app e o MCP lado a lado:

```powershell
./scripts/Build-Distribution.ps1 -Version 1.0.0
```

## Logs

O aplicativo grava eventos estruturados em JSON Lines em
`%LOCALAPPDATA%\MyFrame\logs\my-frame-AAAAmmddHH.json`. Um novo arquivo é criado
a cada hora, com retenção máxima de 168 arquivos e limite de 25 MB por arquivo.
Os logs registram etapas de inicialização, contagens, resultados de sincronização
e falhas, sem gravar JWT, cabeçalho `Authorization` ou o inventário completo.
- [Validação F2 — SQLite e publicação transacional](docs/validacoes/2026-09-13-f2.md)
- [Validação F3 — adaptador Public Export](docs/validacoes/2026-09-13-f3.md)
- [Validação F4 — inventário rico e cobertura](docs/validacoes/2026-09-13-f4.md)
- [Validação F5 — World State e recompensas](docs/validacoes/2026-09-13-f5.md)
- [Validação F6 — credencial de mercado independente](docs/validacoes/2026-09-13-f6.md)
- [Validação F7 — referências Wiki/Overframe](docs/validacoes/2026-09-13-f7.md)
- [Validação F8 — capacidades e status MCP](docs/validacoes/2026-09-13-f8.md)
- [Validação F9 — skills de domínio para clientes LLM](docs/validacoes/2026-09-13-f9.md)
- [Avaliação F10 — casos reproduzíveis de respostas MCP](docs/avaliacao/2026-09-13-f10.md)
- Verificador local: `./scripts/Test-F10Evaluation.ps1`
- [Validação F11 — página visual de status de sincronização](docs/validacoes/2026-09-13-f11.md)
- [Validação F12 — snapshot preferencial do SQLite sincronizado](docs/validacoes/2026-09-13-f12.md)
- [Validação F13 — catálogo Public Export rico e preservado](docs/validacoes/2026-09-13-f13.md)
- [Validação F14 — importação de captura validada para SQLite](docs/validacoes/2026-09-13-f14.md)
- [Validação F15 — comando operacional de importação](docs/validacoes/2026-09-13-f15.md)
- [Validação F16 — preflight do pacote Overwolf](docs/validacoes/2026-09-13-f16.md)
- [Validação F17 — inbox de captura e importação em lote](docs/validacoes/2026-09-13-f17.md)
- [Validação F18 — integração da inbox no aplicativo](docs/validacoes/2026-09-13-f18.md)
- [Validação F19 — falhas da inbox persistidas no SQLite](docs/validacoes/2026-09-13-f19.md)

As skills versionadas para análise de builds, farm/progressão e economia ficam em
[`skills/`](skills/README.md). Elas orientam o cliente LLM a respeitar snapshots,
cobertura, fontes e incerteza; não substituem os dados do MCP.
