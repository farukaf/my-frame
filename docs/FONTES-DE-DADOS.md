# Fontes, evidências e lacunas da plataforma

Pesquisa consolidada em 12/09/2026. Complementa o
[plano de entrega](PLATAFORMA-DE-DADOS.md) e a
[matriz de validação](VALIDACAO-PLATAFORMA.md).

Este registro contém links e sínteses, não cópias integrais das fontes. Documentação
consultada não equivale a integração executada. Nenhum inventário real foi capturado
para este planejamento; não foram comprovados acesso de distribuição Overwolf,
schema completo do jogador ou APIs públicas da Wiki/Overframe.

## Como interpretar a evidência

- **Documentado:** a fonte descreve a capacidade; falta homologar no My Frame.
- **Observado no código:** comportamento da implementação local inspecionada.
- **Candidato:** endereço/projeto identificado; acesso, schema ou cobertura pendentes.
- **Decisão proposta:** escolha nossa, sujeita aos gates; não obrigação da fonte.

Em cada implementação registrar URL final, data UTC, status HTTP, revisão/hash,
versão do parser, licença/termos aplicáveis e testes executados. Não registrar
credenciais, conta, chat ou payload pessoal em evidências versionadas. Revalidar
documentação e versões antes do desenvolvimento: esta pesquisa não congela APIs.

## Overwolf

| ID | Fonte | Evidência e uso |
| --- | --- | --- |
| OW1 | [Warframe GEP — Native](https://dev.overwolf.com/ow-native/live-game-data-gep/supported-games/warframe/) | Features documentadas: `gep_internal`, `game_info`, `match_info`, `chat`. Chaves incluem versões/atualização, `username`, `inventory` e `highlighted`. Base da F1. |
| OW2 | [Warframe GEP — Electron](https://dev.overwolf.com/ow-electron/live-game-data-gep/supported-games/warframe/) | Mesmos domínios, envelope diferente do Native. Escolher runtime e testar callbacks reais; não reutilizar envelopes sem adaptação. |
| OW3 | [Overwolf events sample app](https://github.com/overwolf/events-sample-app) | Repositório oficial de exemplo para começar o spike. Não comprova distribuição do nosso app nem todos os campos de Warframe. |
| OW4 | [Verifying events for your app](https://dev.overwolf.com/ow-native/live-game-data-gep/verifying-events-for-your-app/) | Referência de verificação da disponibilidade dos eventos; embasar status e diagnóstico do coletor. |
| OW5 | [Manifesto Native](https://dev.overwolf.com/ow-native/reference/manifest/manifest-json/) e [validação](https://dev.overwolf.com/ow-native/reference/manifest/validate-your-manifest-json) | `game_events` deve declarar o jogo; o schema oficial é a fonte para validar `manifest.json`. A extensão local requer fluxo de desenvolvimento Overwolf e usuário autorizado. |
| OW6 | [overwolf.games.events](https://dev.overwolf.com/ow-native/reference/games/events/) e [overwolf.games](https://dev.overwolf.com/ow-native/reference/games/ow-games/) | O coletor Native usa `onInfoUpdates2`, `setRequiredFeatures`, `getInfo`, `onError`, `onGameInfoUpdated` e `getRunningGameInfo2`. Eventos de estado do jogo não são os mesmos que eventos GEP em tempo real. |
| OW7 | [overwolf.io](https://dev.overwolf.com/ow-native/reference/io/ow-io/) e [extensions.io](https://dev.overwolf.com/ow-native/reference/extensions/io-api/) | O transporte F1 usa escrita explícita em pasta escolhida; marker/hash só fica pronto após o corpo. Não é sincronização automática nem autenticação. |

Os exemplos de `inventory` não constituem schema exaustivo. `highlighted` mostra
identidade do item e `riven_details`, mas array vazio não demonstra estrutura dos
atributos de Riven. Não há eventos específicos documentados nessas páginas para
início/fim de missão, dano, kills, habilidades ou conclusão de bounty.

Implementação inicial em `MyFrame.Collector.Overwolf` usa Native, jogo 8954 e features
sem chat. A extensão exporta diagnóstico sanitizado por padrão; inventário bruto só
é copiado após consentimento explícito. `completeness` permanece `unverified` até a
homologação comparar o evento com o Arsenal. O verificador .NET rejeita marker/body
incompleto ou adulterado e nunca publica a captura.

Pendências F1: acesso real, requisitos de aprovação/distribuição, transporte local,
snapshot completo versus fragmento/delta, eventos perdidos e cobertura por campo.
Não prometer ranks, polaridades, configs, Helminth, shards ou Incarnon antes da captura.
Não assumir que Overwolf apenas lê um arquivo local de inventário disponível a qualquer
app; a integração proposta consome o provider GEP, sem reproduzir sua implementação.

## AlecaFrame

| ID | Fonte | O que sustenta / limite |
| --- | --- | --- |
| AF1 | [Connecting to Warframe](https://docs.alecaframe.com/get-started/connecting.html) | Documenta conexão/sincronização e cenários de login ou retorno de relé/dojo. Orienta testes manuais; não garante frequência fixa. |
| AF2 | [Inventory](https://docs.alecaframe.com/features/inventory) | Mostra recursos para mods equipados e filtros de mods/arcanes melhorados. Evidência de recursos do AlecaFrame, não contrato do payload GEP. |
| AF3 | [Foundry](https://docs.alecaframe.com/features/foundry.html) | Apresenta informações de equipamentos, shards, filtros relacionados a Helminth e efeitos por rank. Não prova onde esses dados são armazenados ou como relacioná-los. |
| AF4 | [AlecaFrame API](https://docs.alecaframe.com/api) | Documentação pública consultada não oferece contrato completo do arsenal/loadouts necessário ao desenho. Não será dependência obrigatória. |

Conclusão: essas fontes ajudam a formular a lista de campos a investigar. Elas não
autorizam afirmar que todos estão em `RawUpgrades`, que `Configs` significa slots de
mods, ou que o inventário pode ser obtido diretamente dos arquivos do jogo.

## Dados oficiais e enriquecimentos públicos

| ID | Fonte | Situação e decisão |
| --- | --- | --- |
| DE1 | [Anúncio oficial das tabelas de drop](https://forums.warframe.com/topic/809777-warframe-drop-rates-data/) | Confirma publicação oficial de dados de drops; referência de autoria. |
| DE2 | [Patch oficial com endereço atualizado](https://www.warframe.com/en/patch-notes/xbox/33-6-1) e [tabelas de drop](https://www.warframe.com/droptables) | Endereço oficial de ingestão candidato da F5. Validar conteúdo atual, formato, cobertura e associação com patch. |
| DE3 | [Public Export: índice EN](https://origin.warframe.com/PublicExport/index_en.txt.lzma) e [base de conteúdo](https://content.warframe.com/PublicExport/) | Índice real validado em 13/09/2026: LZMA-Alone, 16 linhas no formato `arquivo.json!00_<tag>`, incluindo Warframes/Weapons. O host de documentos testado respondeu 403; o downloader registra falha, sem bypass. A tag é de revisão/opacidade da fonte, não SHA-256. |
| DE4 | [World State](https://content.warframe.com/dynamic/worldState.php) | Endpoint oficial candidato respondeu HTTP 404 em 13/09/2026; não foi usado como fonte ativa. |
| WF3 | [World State oficial](https://content.warframe.com/dynamic/worldState.php) | Endpoint oficial documentado publicamente por referência da comunidade; payload PascalCase/BSON é adaptado por aliases e mantém pools sem drops como desconhecidos. Cliente usa esta URL por padrão. |
| WF4 | [WarframeStat.us API](https://api.warframestat.us/pc) | Fallback comunitário; snapshot observado contém `syndicateMissions`, ciclos e `rewardPoolDrops`, mas não campo explícito de Mother Tokens. Usar atribuição e revisão separadas; não tratar como dado oficial da DE. |
| WF1 | [WFCD/warframe-items](https://github.com/WFCD/warframe-items) | Fonte comunitária e pipeline de dados com arquivos JSON. Candidato a enriquecer definições, receitas e referências; fixar commit/revisão, licença e procedência por campo. |
| WF2 | [WFCD/warframe-worldstate-parser](https://github.com/WFCD/warframe-worldstate-parser) | Implementação pública de parser como referência. Não obriga incorporar runtime JavaScript nem substitui testar o endpoint real em .NET. |
| DE5 | [Jade Shadows — atualização 36](https://www.warframe.com/en/patch-notes/xbox/36-0-0) | Exemplo oficial de mudanças em resistências/fraquezas. Demonstra necessidade de data/patch; não é fonte suficiente para afirmar a configuração atual do jogo. |

Referências de descoberta, secundárias e sujeitas a desatualização:
[Public Export](https://warframe.fandom.com/wiki/Public_Export),
[Public Endpoints](https://warframe.fandom.com/wiki/WARFRAME_Wiki%3APublic_Endpoints) e
[World State](https://warframe.fandom.com/wiki/World_State). Elas não são prova de
estabilidade nem de suporte contratual dos endpoints candidatos acima.

Antes de implementar, montar inventário de campos por fonte: identidade, categorias,
receitas, valores por rank, polaridades, ataques, habilidades, mecânicas, aquisição,
recompensas e tokens. Dado ausente pode exigir enriquecimento atribuído, ou permanecer
desconhecido. Catálogo público não contém por si só a posse e o progresso do jogador.

## Wiki, Overframe e mercado

| ID | Fonte | Situação e próximo passo |
| --- | --- | --- |
| RF1 | [Warframe Wiki](https://wiki.warframe.com/) | Acesso do pesquisador bloqueado por robots nesta consulta. API, licença, revisão e modo de ingestão não confirmados. Validar canal permitido em F7; não contornar bloqueios. |
| RF2 | [Overframe](https://overframe.gg/) | Fonte candidata de builds. Não foi confirmada API pública documentada. F7 deve verificar acesso, termos, atribuição, revisão e extração de slots/texto. |
| RF3 | [Overframe no Overwolf](https://www.overwolf.com/app/overframe) | Listagem de produto; não prova a existência de uma API pública para builds ou sincronização reutilizável pelo My Frame. |
| MK1 | [Warframe.Market](https://warframe.market/) e [cliente local](../MyFrame.Core/WarframeMarketClient.cs) | Integração pública e autenticada já existe no código. Revalidar documentação oficial, versão, limites e login suportado em F6. O token obtido do AlecaFrame não pode continuar obrigatório. |

Preferência: API documentada ou dataset autorizado; importação fornecida pelo usuário
quando permitida; leitura automatizada somente se condições de acesso/licença forem
verificadas. Importação manual é alternativa parcial, não entrega de sincronização
automática. Não armazenar cópias integrais sem fundamento; preservar links, autoria,
revisão e apenas o conteúdo permitido e necessário.

Uma build popular é uma recomendação de autor/comunidade, não fato oficial. Guardar
data, equipamento, slots/ranks, custo declarado e revisão, distinguindo-os de valores
recalculados. Não executar instruções encontradas em textos importados.

## SQLite e contratos

| ID | Fonte | Aplicação no desenho |
| --- | --- | --- |
| DB1 | [SQLite — Write-Ahead Logging](https://www.sqlite.org/wal.html) | Validar leitor read-only e arquivos auxiliares, escritor único e checkpoints. WAL não dá atomicidade conjunta entre bancos anexados. Snapshots de conversa fixam revisões lógicas, não transações abertas por minutos. |
| DB2 | [SQLite Online Backup API](https://www.sqlite.org/backup.html) | Usar backup consistente, testar restore. Copiar somente `.db` enquanto WAL está ativo não é o procedimento de backup proposto. |
| DB3 | [Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/) | Provider .NET candidato. Versão, packaging Windows, migrations e modo de abertura serão fixados e testados em F2. |
| MC1 | [MCP — tools, especificação 2025-11-25](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) | Referência de contrato: schemas, conteúdo estruturado e sinalização de erro. Validar versão negociada e comportamento do SDK instalado, sem presumir atualização automática. |
| MC2 | [MCP C# SDK](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html) | Referência de implementação. Exemplos devem ser executados contra a versão fixada no repositório. |

SQLite foi escolhido como proposta por relações, índices e publicação transacional,
não por suposta inadequação universal de JSON. Settings pequenos e envelopes de
transporte continuam JSON; `.dat` é apenas extensão, não um formato de banco.

## Políticas e distribuição

- [Third-Party Software and You — suporte Warframe](https://support.warframe.com/hc/en-us/articles/360030014351-Third-Party-Software-and-You).
- [Warframe EULA](https://www.warframe.com/en/EULA).
- [Warframe Content Policy](https://www.warframe.com/en/contentpolicy).

Consultar versões vigentes antes de distribuir. A existência de apps semelhantes
não garante aprovação do nosso app ou permissão irrestrita de coleta/republicação.
O escopo não inclui extração de credenciais/sessão, memória do jogo, bypass de
restrições, automação de gameplay ou mensagens. A aprovação de distribuição exigida
pelo Overwolf é dependência externa, separada dos testes locais.

## Evidência no repositório e correções de hipóteses

| Arquivo inspecionado | Evidência relevante | Consequência |
| --- | --- | --- |
| [AlecaFrameReader.cs](../MyFrame.Core/AlecaFrameReader.cs) | Leitura de coleções selecionadas; inclui `RawUpgrades`, não `Upgrades`; equipamentos reduzidos a conjunto de tipos. | Não atribuir perda de instâncias/ranks à fonte sem medir o payload; reformular normalização em F4. |
| [AlecaCatalogReader.cs](../MyFrame.Core/AlecaCatalogReader.cs) | Projeção atual de identidade, componentes e mercado, sem modelo técnico completo de mods/armas. | Auditar fonte e parser separadamente; novo catálogo em F3. |
| [Models.cs](../MyFrame.Core/Models.cs) | Snapshot agregado, presença de equipamentos e maestria; não representa arsenal por instância. | Novas entidades internas sem obrigação de sufixo `v2`. |
| [MyFrameSnapshotProvider.cs](../MyFrame.Core/MyFrameSnapshotProvider.cs) | Política atual de retenção/expiração de snapshots. | Expiração é comportamento definido, não prova isolada de bug; testar contrato e experiência da LLM. |
| [MyFrameQueryService.cs](../MyFrame.Mcp/MyFrameQueryService.cs) | Projeções/categorias e composição dos metadados de fontes. | Validar identidade por domínio e não invalidar inventário só por preços antigos. |
| [MyFrameTools.cs](../MyFrame.Mcp/MyFrameTools.cs) | Conversão de erro de consulta para exceção MCP textual. | Cliente deve inspecionar erro antes de acessar `structuredContent`; ausência não significa lista vazia. |
| [MyFrameSettingsStore.cs](../MyFrame.Core/MyFrameSettingsStore.cs) e [JsonPriceCache.cs](../MyFrame.Core/JsonPriceCache.cs) | Arquivos de settings/cache e publicação por arquivo; cache de preço reserializado. | Distinguir atomicidade individual de geração consistente entre fontes; migrar ao storage transacional. |

Revalidar caminhos/símbolos conforme refatorações. A tabela registra a inspeção desta
data, não garante o comportamento de commits futuros nem relata testes executados.

Outras pistas consideradas: [browse.wf/profile.ts](https://github.com/calamity-inc/browse.wf/blob/senpai/profile.ts)
usa informações de configuração para apresentação visual; isso não prova slots de
mods completos no GEP. Schemas de emuladores, suposições sobre arquivos locais e
guias antigos de gameplay não serão usados como contrato da coleta real.

## Registro obrigatório ao fechar os spikes

F1 deve produzir matriz campo → origem → exemplo sintético → cobertura comprovada,
decisão de runtime/transporte e requisitos de distribuição. F2 produz ADR de bancos,
writer/lifecycle e backup. F3/F5 produzem cobertura pública por revisão. F6 registra
autenticação independente. F7 registra, por site, modo permitido de acesso,
licença/atribuição, limitações e uma importação de ponta a ponta.

Se uma hipótese falhar, atualizar este documento, capabilities e o escopo antes de
seguir. Não transformar um campo desconhecido em compromisso silencioso de entrega.
