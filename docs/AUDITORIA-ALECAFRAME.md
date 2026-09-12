# F0 — auditoria de dependências do AlecaFrame

Data: 12/09/2026. Baseline: `d35eb1f`, branch base `feat/local-mcp`.
Escopo: código e fixtures do repositório, sem ler inventário, catálogo ou token reais.
Relaciona o [plano](PLATAFORMA-DE-DADOS.md) com as mudanças necessárias por fase.

## Dependências diretas e indiretas

| Área | Ponto atual | Dependência e saída planejada |
| --- | --- | --- |
| Inventário | [AlecaFrameReader](../MyFrame.Core/AlecaFrameReader.cs) | `lastData.dat`, formato direto ou `InventoryJson`, leitura de coleções selecionadas. F1 prova GEP; F4 substitui aquisição e normalização. |
| Catálogo | [AlecaCatalogReader](../MyFrame.Core/AlecaCatalogReader.cs) | `cachedData/json/*.json`: nomes, componentes, ducats, relíquias e identidade de mercado. F3 substitui por fontes independentes; migrar somente inventário não basta. |
| Posse e receitas | [InventoryCatalogAlignment](../MyFrame.Core/InventoryCatalogAlignment.cs) | Reconcilia IDs de receitas/partes entre dois formatos Aleca. F3/F4 preservam semântica com mapeamentos canônicos explícitos. |
| Mercado | [WarframeMarketClient](../MyFrame.Core/WarframeMarketClient.cs) | Construtor com `IAlecaFramePath` resolve `WFMarketToken.tk`. Preços/índice públicos não exigem token; perfil/ordens exigem. F6 precisa de login suportado independente. |
| Tradabilidade | [CatalogMarketAlignment](../MyFrame.Core/CatalogMarketAlignment.cs) | Corrige divergências do catálogo com índice WFM. Não remover regra de precedência ao trocar a origem. |
| Identidade do contexto | [MyFrameContext](../MyFrame.Core/MyFrameContext.cs) | Contexto associado ao diretório. F4 deve separar sessão/jogador da localização dos arquivos; nunca mesclar contas por nome. |
| Settings compartilhados | [MyFrameSettingsStore](../MyFrame.Core/MyFrameSettingsStore.cs) | `AlecaFrameDirectory` em `settings.v1.json`, default local e root substituível para testes. F6 retira exigência, mantém importação legada opcional. |
| Setup e seleção | [AlecaFrameDirectorySettings](../MyFrame.App/AlecaFrameDirectorySettings.cs), [LocalSettings](../MyFrame.App/LocalSettings.cs) | Setup valida `lastData.dat` e catálogo; preferência de diretório também controla mercado e monitoramento. F4/F6 substituem fluxo por status de coleta. |
| Composição da UI | [MauiProgram](../MyFrame.App/MauiProgram.cs) | Injeta leitores e token por pasta. F2/F6 separam SyncHost escritor e read model. |
| Migração existente | [SharedDataMigration](../MyFrame.App/SharedDataMigration.cs) | Preferences MAUI e três caches antigos copiados para armazenamento comum. F6 preserva origem, idade e recuperação. |
| Dashboard e watchers | [DashboardService](../MyFrame.Core/DashboardService.cs), [DashboardViewModel](../MyFrame.App/DashboardViewModel.cs) | Eventos de arquivos/pasta e mensagens de setup pressupõem Aleca. F2/F4 substituem por eventos/status do SyncHost; não apenas renomear rótulos. |
| Snapshot compartilhado | [MyFrameSnapshotProvider](../MyFrame.Core/MyFrameSnapshotProvider.cs) | Leitores Aleca, fingerprints, watcher, fallback por contexto e retenção local. F2/F8 migram consistência para revisões persistidas. |
| Composição MCP | [Program](../MyFrame.Mcp/Program.cs) | Registra ambos os leitores Aleca. Não registra cliente HTTP de mercado/token, mas ainda depende dos dados Aleca. F8 troca composição preservando read-only. |
| Contratos/mensagens | [MyFrameQueryService](../MyFrame.Mcp/MyFrameQueryService.cs), [MyFrameResources](../MyFrame.Mcp/MyFrameResources.cs) | Inventário agregado, fontes e instruções de setup. F8 mantém compatibilidade e diferencia ausência, erro, parcial e vazio. |

A auditoria não copia o conteúdo de segredos do leitor/token. Referências a arquivos
acima são pontos de implementação, não autorização para publicar dados privados.

## O que a baseline representa — e o que perde

`InventorySnapshot` mantém stackables agregados, conjunto de equipamentos e XP de
maestria por tipo. Não mantém identidade das cópias, slots, mods equipados, polaridades
ou configurações completas. O parser inclui `RawUpgrades` e não inclui `Upgrades`.
Isso demonstra uma limitação do leitor, não prova o conteúdo completo da fonte.

`CatalogItem` não é um modelo completo de mecânicas/efeitos por rank. Categoria
inferida da existência de receita/componentes no MCP também não é taxonomia completa.
O próximo catálogo deve preservar equipamentos sem receita e componentes sem confundir
seus tipos. Campos desconhecidos terão cobertura explícita, conforme o plano.

## Falha de interpretação MCP reproduzível

Cadeia atual: provedor lança `MyFrameSnapshotException` → query converte para
`QueryProblemException` → ferramenta converte para `McpException` → SDK responde
`isError: true` com texto, sem `structuredContent`. O consumidor anterior usava
`structuredContent?.items ?? []`, apagando a diferença entre erro e lista vazia.

F0 fixa essa expectativa com teste pelo transporte stdio, relógio controlado e
[exemplo executável](../examples/mcp/README.md). Atualiza instruções do servidor,
sem mudar DTOs/outputSchema ou antecipar o envelope estruturado da F8.
Busca legítima sem matches continua retornando página vazia com metadados.

Retenção atual: cinco minutos a partir de retenção, no máximo oito snapshots;
leituras por ID não renovam o prazo. O teste temporal não espera cinco minutos reais.
F0 não certifica todas as condições de descarte concorrente/memória da F8.

## Saída mínima para declarar independência

1. Captura GEP e catálogo público funcionam sem pasta AlecaFrame.
2. Settings e contexto não exigem esse diretório no caminho padrão.
3. App e MCP leem a mesma revisão normalizada pelo novo storage.
4. Mercado público continua funcionando; ordens pessoais só são anunciadas depois
   de autenticação independente homologada.
5. Instalação limpa e upgrade preservam dados/registro MCP; leitor/importador legado
   opcional não é necessário para operar.

Os pontos acima continuam pendentes para F1–F8. A F0 entrega o mapa e a baseline,
não declara que o AlecaFrame já foi removido.
