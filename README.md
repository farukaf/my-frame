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
- [Validação F20 — status da inbox no MCP](docs/validacoes/2026-09-13-f20.md)
- [Validação F119 — App e MCP no mesmo SQLite](docs/validacoes/2026-09-13-f119.md)
- [Validação F120 — desempenho com App e dois MCP](docs/validacoes/2026-09-13-f120.md)
- [Validação F121 — matriz completa do contrato MCP](docs/validacoes/2026-09-13-f121.md)
- [Validação F122 — atualização com MCP ativo](docs/validacoes/2026-09-13-f122.md)
- [Validação F123 — readiness não-mutante dos clientes MCP](docs/validacoes/2026-09-13-f123.md)
- [Validação F124 — gate externo Codex/Claude](docs/validacoes/2026-09-13-f124.md)
- [Validação F125 — regressão Core/MCP](docs/validacoes/2026-09-13-f125.md)
- [Validação F126 — runner sequencial da regressão](docs/validacoes/2026-09-13-f126.md)
- [Validação F127 — distribuição 0.0.8 e pacote Overwolf](docs/validacoes/2026-09-13-f127.md)
- [Validação F128 — tentativa F1 com Warframe em execução](docs/validacoes/2026-09-13-f128.md)
- [Validação F129 — consolidação dos gates F10](docs/validacoes/2026-09-13-f129.md)
- [Validação F130 — sincronização manual do Public Export no App](docs/validacoes/2026-09-13-f130.md)
- [Validação F131 — smoke da fonte oficial Public Export](docs/validacoes/2026-09-13-f131.md)
- [Validação F132 — decoder LZMA no fluxo do App](docs/validacoes/2026-09-13-f132.md)
- [Validação F133 — aliases localizados do Public Export](docs/validacoes/2026-09-13-f133.md)
- [Validação F134 — executor local de sincronização Public Export](docs/validacoes/2026-09-13-f134.md)
- [Validação F135 — status operacional via CLI](docs/validacoes/2026-09-13-f135.md)
- [Validação F136 — executor World State](docs/validacoes/2026-09-13-f136.md)
- [Validação F137 — orquestrador Public Export compartilhado](docs/validacoes/2026-09-13-f137.md)
- [Validação F138 — orquestrador World State compartilhado](docs/validacoes/2026-09-13-f138.md)
- [Validação F139 — sincronização conjunta das fontes oficiais](docs/validacoes/2026-09-13-f139.md)
- [Validação F140 — runbook de agendamento Windows](docs/validacoes/2026-09-13-f140.md)
- [Validação F141 — consolidação do aceite F2](docs/validacoes/2026-09-13-f141.md)
- [Validação F142 — cobertura do Public Export](docs/validacoes/2026-09-13-f142.md)
- [Validação F143 — cobertura de fontes no MCP](docs/validacoes/2026-09-13-f143.md)
- [Validação F144 — busca local no Public Export pelo MCP](docs/validacoes/2026-09-13-f144.md)
- [Validação F145 — procedência na busca Public Export do MCP](docs/validacoes/2026-09-13-f145.md)
- [Validação F146 — inbox sugerida no coletor Overwolf](docs/validacoes/2026-09-13-f146.md)
- [Validação F147 — gate read-only de prontidão Overwolf](docs/validacoes/2026-09-13-f147.md)
- [Validação F148 — preflight reproduzível do coletor Overwolf](docs/validacoes/2026-09-13-f148.md)
- [Validação F149 — heartbeat sanitizado da sessão Overwolf](docs/validacoes/2026-09-13-f149.md)
- [Validação F150 — gate de runtime baseado em heartbeat](docs/validacoes/2026-09-13-f150.md)
- [Validação F151 — sincronização Public Export por arquivo local](docs/validacoes/2026-09-13-f151.md)
- [Validação F152 — sincronização World State por arquivo local](docs/validacoes/2026-09-13-f152.md)
- [Validação F153 — agregação de múltiplos Public Exports locais](docs/validacoes/2026-09-13-f153.md)
- [Validação F21 — detecção automática de novas capturas](docs/validacoes/2026-09-13-f21.md)
- [Validação F22 — restore SQLite](docs/validacoes/2026-09-13-f22.md)
- [Validação F23 — histórico de tentativas de sincronização](docs/validacoes/2026-09-13-f23.md)
- [Validação F24 — histórico de sincronização no MCP](docs/validacoes/2026-09-13-f24.md)
- [Validação F25 — execução do gate estrutural F10](docs/validacoes/2026-09-13-f25.md)
- [Validação F26 — artefato distribuível Windows](docs/validacoes/2026-09-13-f26.md)
- [Validação F27 — smoke de instalação limpa](docs/validacoes/2026-09-13-f27.md)
- [Validação F28 — gate MCP read-only](docs/validacoes/2026-09-13-f28.md)
- [Validação F29 — persistência de cobertura do inventário](docs/validacoes/2026-09-13-f29.md)
- [Validação F30 — cobertura do inventário no MCP](docs/validacoes/2026-09-13-f30.md)
- [Validação F31 — fluxo ponta a ponta sintético](docs/validacoes/2026-09-13-f31.md)
- [Validação F32 — migração de schema legado](docs/validacoes/2026-09-13-f32.md)
- [Validação F33 — equipamentos instanciados no MCP](docs/validacoes/2026-09-13-f33.md)
- [Validação F34 — preservação de upgrades atribuídos](docs/validacoes/2026-09-13-f34.md)
- [Validação F35 — consulta de mods no MCP](docs/validacoes/2026-09-13-f35.md)
- [Validação F36 — loadout agrupado por instância](docs/validacoes/2026-09-13-f36.md)
- [Validação F37 — retenção segura de revisões SQLite](docs/validacoes/2026-09-13-f37.md)
- [Validação F38 — compatibilidade explícita de schema](docs/validacoes/2026-09-13-f38.md)
- [Validação F39 — manutenção do SyncHost](docs/validacoes/2026-09-13-f39.md)
- [Validação F40 — preflight sem mutação de schema futuro](docs/validacoes/2026-09-13-f40.md)
- [Validação F41 — publicação integrada do Public Export](docs/validacoes/2026-09-13-f41.md)
- [Validação F42 — publicação integrada do World State](docs/validacoes/2026-09-13-f42.md)
- [Validação F43 — resolução do índice Public Export](docs/validacoes/2026-09-13-f43.md)
- [Validação F44 — leitura completa de bounties World State](docs/validacoes/2026-09-13-f44.md)
- [Validação F45 — bounties World State no MCP](docs/validacoes/2026-09-13-f45.md)
- [Validação F46 — estado de disponibilidade de bounties](docs/validacoes/2026-09-13-f46.md)
- [Validação F47 — skill de farm alinhada ao MCP](docs/validacoes/2026-09-13-f47.md)
- [Validação F48 — skill de builds alinhada ao loadout](docs/validacoes/2026-09-13-f48.md)
- [Validação F49 — paridade World State SQLite→MCP](docs/validacoes/2026-09-13-f49.md)
- [Validação F50 — paridade documental das ferramentas MCP](docs/validacoes/2026-09-13-f50.md)
- [Validação F51 — teste automatizado de paridade documental](docs/validacoes/2026-09-13-f51.md)
- [Validação F52 — World State completo no MCP](docs/validacoes/2026-09-13-f52.md)
- [Validação F53 — limite de resposta do World State](docs/validacoes/2026-09-13-f53.md)
- [Validação F54 — filtro de sindicato no World State](docs/validacoes/2026-09-13-f54.md)
- [Validação F55 — inicialização sem dependência de AlecaFrame](docs/validacoes/2026-09-13-f55.md)
- [Validação F56 — metadados de sincronização no MCP](docs/validacoes/2026-09-13-f56.md)
- [Validação F57 — sincronização manual do World State](docs/validacoes/2026-09-13-f57.md)
- [Validação F58 — cobertura explícita de Mother Token](docs/validacoes/2026-09-13-f58.md)
- [Validação F59 — cobertura World State persistida no MCP](docs/validacoes/2026-09-13-f59.md)
- [Validação F60 — substituição de cobertura entre revisões](docs/validacoes/2026-09-13-f60.md)
- [Validação F61 — substituição de cobertura do inventário](docs/validacoes/2026-09-13-f61.md)
- [Validação F62 — revisão ativa no World State](docs/validacoes/2026-09-13-f62.md)
- [Validação F63 — gate MCP read-only em data root limpo](docs/validacoes/2026-09-13-f63.md)
- [Validação F64 — pacote e testes do coletor Overwolf](docs/validacoes/2026-09-13-f64.md)
- [Validação F65 — distribuição Windows 0.0.7](docs/validacoes/2026-09-13-f65.md)
- [Validação F66 — matriz F10 validada](docs/validacoes/2026-09-13-f66.md)
- [Validação F67 — tentativa interativa F1](docs/validacoes/2026-09-13-f67.md)
- [Validação F68 — prioridade SQLite sobre legado AlecaFrame](docs/validacoes/2026-09-13-f68.md)
- [Validação F69 — instalação nova sem caminho AlecaFrame](docs/validacoes/2026-09-13-f69.md)
- [Validação F70 — UX explicita importação legada opcional](docs/validacoes/2026-09-13-f70.md)
- [Validação F71 — skill de farm alinhada ao World State v2](docs/validacoes/2026-09-13-f71.md)
- [Validação F72 — identificador do mercado alinhado no sync status](docs/validacoes/2026-09-13-f72.md)
- [Validação F73 — limites e filtros World State no stdio](docs/validacoes/2026-09-13-f73.md)
- [Validação F74 — skill de builds alinhada à cobertura v2](docs/validacoes/2026-09-13-f74.md)
- [Validação F75 — consulta de atividades no MCP](docs/validacoes/2026-09-13-f75.md)
- [Validação F76 — filtro de recompensa nas atividades](docs/validacoes/2026-09-13-f76.md)
- [Validação F77 — filtro de recompensa consistente](docs/validacoes/2026-09-13-f77.md)
- [Validação F78 — adaptador World State oficial](docs/validacoes/2026-09-13-f78.md)
- [Validação F79 — procedência do parser no status](docs/validacoes/2026-09-13-f79.md)
- [Validação F80 — identificação automática da fonte World State](docs/validacoes/2026-09-13-f80.md)
- [Validação F81 — fallback controlado do World State](docs/validacoes/2026-09-13-f81.md)
- [Validação F82 — procedência do parser na UI](docs/validacoes/2026-09-13-f82.md)
- [Validação F83 — procedência no resultado do sync](docs/validacoes/2026-09-13-f83.md)
- [Validação F84 — proteção contra fallback em schema inválido](docs/validacoes/2026-09-13-f84.md)
- [Validação F85 — fluxo oficial até SQLite](docs/validacoes/2026-09-13-f85.md)
- [Validação F86 — verificador do endpoint World State](docs/validacoes/2026-09-13-f86.md)
- [Validação F87 — verificador offline por fixture](docs/validacoes/2026-09-13-f87.md)
- [Validação F88 — detecção explícita de Mother Token](docs/validacoes/2026-09-13-f88.md)
- [Validação F89 — leitura concorrente app/MCP](docs/validacoes/2026-09-13-f89.md)
- [Validação F90 — skill de farm com procedência de fonte](docs/validacoes/2026-09-13-f90.md)
- [Validação F91 — skill de builds com procedência do arsenal](docs/validacoes/2026-09-13-f91.md)
- [Validação F92 — skill de economia com procedência do mercado](docs/validacoes/2026-09-13-f92.md)
- [Validação F93 — migração de parserVersion em SQLite legado](docs/validacoes/2026-09-13-f93.md)
- [Validação F94 — suíte completa da solução](docs/validacoes/2026-09-13-f94.md)
- [Validação F95 — rollback da migração SQLite](docs/validacoes/2026-09-13-f95.md)
- [Validação F96 — MCP ativo após atualização do banco](docs/validacoes/2026-09-13-f96.md)
- [Validação F97 — gate reproduzível do contrato MCP](docs/validacoes/2026-09-13-f97.md)
- [Validação F98 — interoperabilidade local do coletor e probe](docs/validacoes/2026-09-13-f98.md)
- [Validação F99 — desempenho de consulta MCP em fixture grande](docs/validacoes/2026-09-13-f99.md)
- [Validação F100 — gate operacional de processo MCP somente leitura](docs/validacoes/2026-09-13-f100.md)
- [Validação F101 — schema mínimo de resultados da avaliação F10](docs/validacoes/2026-09-13-f101.md)
- [Validação F102 — dois servidores MCP em paralelo](docs/validacoes/2026-09-13-f102.md)
- [Validação F103 — memória de dois processos MCP](docs/validacoes/2026-09-13-f103.md)
- [Validação F104 — upgrade lado a lado da distribuição](docs/validacoes/2026-09-13-f104.md)
- [Validação F105 — configuração dos clientes MCP](docs/validacoes/2026-09-13-f105.md)
- [Validação F106 — MCP Inspector CLI](docs/validacoes/2026-09-13-f106.md)
- [Validação F107 — schemas MCP nullable portáveis](docs/validacoes/2026-09-13-f107.md)
- [Validação F108 — chamadas MCP pelo Inspector](docs/validacoes/2026-09-13-f108.md)
- [Validação F109 — paridade do snapshot entre UI e MCP](docs/validacoes/2026-09-13-f109.md)
- [Validação F110 — limite de retenção dos snapshots](docs/validacoes/2026-09-13-f110.md)
- [Validação F111 — falha MCP sem escrita ou vazamento](docs/validacoes/2026-09-13-f111.md)
- [Validação F112 — reconciliação da matriz](docs/validacoes/2026-09-13-f112.md)
- [Validação F113 — caches de mercado no SQLite](docs/validacoes/2026-09-13-f113.md)
- [Validação F114 — settings no SQLite](docs/validacoes/2026-09-13-f114.md)
- [Validação F115 — instalação limpa sem caches legados](docs/validacoes/2026-09-13-f115.md)
- [Validação F116 — credencial WFM protegida por usuário Windows](docs/validacoes/2026-09-13-f116.md)
- [Validação F117 — serviço local de credencial WFM](docs/validacoes/2026-09-13-f117.md)
- [Validação F118 — UX de credencial WFM](docs/validacoes/2026-09-13-f118.md)

As skills versionadas para análise de builds, farm/progressão e economia ficam em
[`skills/`](skills/README.md). Elas orientam o cliente LLM a respeitar snapshots,
cobertura, fontes e incerteza; não substituem os dados do MCP.
