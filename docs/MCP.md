# MCP local — arquitetura e contrato v1

Este é o contrato atual. A expansão de domínios e a migração para SQLite estão no
[plano da plataforma](PLATAFORMA-DE-DADOS.md), com testes de compatibilidade na
[matriz de validação](VALIDACAO-PLATAFORMA.md). Não há mudança automática de versão
do contrato por causa da troca de armazenamento.

Exemplo executável de leitura segura de erros/páginas:
[consumo MCP](../examples/mcp/README.md). Nunca converter ausência de
`structuredContent` em inventário vazio; verificar `isError` primeiro.

## Resultado pretendido

Uma IA conectada ao servidor local deve conseguir responder perguntas como:

- “O que eu tenho no inventário?”
- “Quais itens Prime estão perto de completar?”
- “O que vale vender por platinum ou trocar por ducats?”
- “Quais relíquias devo abrir?”
- “Mostre as peças excedentes de itens já dominados.”

As respostas devem vir dos mesmos dados e regras da interface, em JSON estruturado,
sem exigir cópia manual, autenticação ou acesso direto da IA aos arquivos do AlecaFrame.

Este documento registra a arquitetura implementada e os critérios de evolução. A v1
expõe inventário agregado dos tipos suportados pelo leitor, com cobertura declarada;
não promete representação completa de todas as instâncias do inventário do jogo.
O servidor é local, mas o cliente pode enviar os resultados ao seu provedor de IA.

## Estado da implementação

A v1 inclui o console self-contained em `stdio`, oito ferramentas somente leitura, dois
recursos, um prompt, DTOs e schemas estruturados, snapshots/cursores, paginação por bytes,
limites de concorrência, configuração compartilhada, migração idempotente e onboarding no
aplicativo. Testes cobrem o processo real `stdio`, schemas, paginação, cursores, dados
parciais, reservas e fallback do mesmo contexto.

Gerações transacionais que agrupem todos os arquivos de mercado em um único manifesto e
benchmarks de grande volume permanecem como hardening posterior. Na v1, cada arquivo é
substituído atomicamente e o snapshot declara a versão/idade de cada fonte separadamente;
ele não afirma que cotações, índice e ordens foram obtidos na mesma transação remota.

## Decisão de arquitetura

Criar um projeto console `MyFrame.Mcp` e usar o transporte `stdio`.

```text
Codex / Claude
      │ inicia e conversa por stdin/stdout
      v
MyFrame.Mcp
      │ consultas read-only
      v
MyFrameReadModel / SnapshotProvider (Core)
      ├── AlecaFrameReader + AlecaCatalogReader
      ├── caches locais do My Frame
      ├── RecommendationEngine
      └── configuração compartilhada
                 ^
                 │ mesmos serviços e contratos
             MyFrame.App
```

O servidor não ficará embutido no processo MAUI. Dessa forma, o cliente MCP controla o
ciclo de vida, não há porta ou conflito de porta, e o app gráfico pode estar aberto ou
fechado. Quando o app estiver aberto, ele continuará atualizando os caches de mercado;
o MCP lerá esses caches e obterá inventário diretamente da fonte local. Consultas novas
usam a última versão válida disponível; consultas fixadas em um snapshot preservam sua
versão até expirar. Falhas e idade da origem acompanham as respostas.

O MCP não terá autenticação porque `stdio` cria uma sessão privada entre o cliente e o
processo filho e não aceita conexões de rede. Se um transporte HTTP for adicionado no
futuro, autenticação e autorização deverão ser redesenhadas antes de habilitá-lo.

## Paridade com a interface

O `DashboardViewModel` deriva sua tela de `DashboardSnapshot`, enquanto a fronteira local
compartilhada fica no Core:

- `IMyFrameSnapshotProvider`: entrega snapshots imutáveis, versionados e com estado das fontes;
- `MyFrameSettings`: pasta de dados, razão ducats/platinum e sets Prime reservados;
- contratos separados para leitura de settings/caches e escrita exclusiva pelo app;
- `MyFrameReadModel`: projeções estáveis para overview, inventário e recomendações;
- DTOs próprios do MCP, sem reutilizar diretamente records de UI.

O app e o MCP consomem a mesma implementação do provedor, em instâncias independentes.
Compartilhar código não compartilha memória: ambos devem observar as mesmas versões dos
arquivos. A paridade é verificável com as mesmas fontes, settings e instante de avaliação,
além de resultados esperados calculados independentemente do motor.

O modo MCP é cache-first e sem escrita: relê inventário e catálogo, lê configurações,
ordens e preços já armazenados, e reexecuta o mesmo `RecommendationEngine`. Ele não chama
operações mutáveis e não atualiza ordens. Cada resposta informa quando inventário e preços
foram obtidos, sua cobertura e validade. `DashboardService.RefreshAsync(false)` não serve
como caminho offline: hoje ainda consulta conta e ordens. Sua composição local deve ser
extraída; o container de DI do MCP não registra cliente HTTP de mercado, leitor de token,
stores de escrita ou migração.

### Armazenamento compartilhado e migração

- Centralizar settings e caches em `%LOCALAPPDATA%\MyFrame`, com nomes e versões de
  formato documentados. A localização não depende do diretório de execução.
- O app migra `Preferences` e os caches de `FileSystem.Current.AppDataDirectory`.
  A migração é idempotente, validada antes de publicar, preserva as origens e não
  sobrescreve um destino válido mais recente. Se origem e destino coincidirem, não copia.
- `settings.v1.json` é a barreira obrigatória de setup e revisão. Os caches opcionais são
  copiados individualmente por substituição atômica; uma migração repetida preserva destinos.
- Antes da migração, o MCP mantém descoberta e diagnóstico disponíveis e retorna
  `SETUP_REQUIRED` nas consultas dependentes, orientando abrir o app. Não adota defaults
  silenciosos que possam ignorar a pasta ou reservas já configuradas pelo usuário.
- Separar versão de armazenamento, versão do contrato MCP e versão das regras. Versões
  incompatíveis geram `STORAGE_VERSION_UNSUPPORTED`; nunca resetam dados para defaults.
- Escritas usam temporários únicos e substituição atômica, com coordenação entre escritores
  do app. Um `SemaphoreSlim` por processo não protege duas instâncias escritoras.
- Cada arquivo de mercado usa temporário único e substituição atômica. Uma futura versão
  poderá agrupá-los em gerações imutáveis com manifesto; até lá, settings têm revisão
  própria e cada fonte registra sua idade/validade no snapshot.

## Correção e significado dos dados — requisito anterior aos endpoints

### Cobertura e identidade do inventário

O leitor atual agrega stackables por tipo e representa equipamentos em um conjunto de
identificadores, perdendo multiplicidade e detalhes por instância. Na v1:

- declarar `inventoryCoverage`, coleções suportadas e limitações do parser;
- usar `quantity: null` quando só houver evidência de presença de equipamento, junto de
  `owned: true` e `quantityKnown: false`; nunca transformar presença em quantidade um;
- filtros de quantidade excluem quantidade desconhecida, salvo inclusão explícita;
- manter entradas sem correspondência no catálogo com `catalogMatched: false`;
- todas as listas retornam `itemId` aceito por `get_item`, com tipo de entidade explícito
  para equipamento, blueprint, componente, set e relíquia; nomes são busca, não identidade;
- sets são entidades derivadas com composição, não novas cópias físicas do inventário.
  Nomes ambíguos retornam candidatos e não selecionam silenciosamente o primeiro.

### Preços, estimativas e relíquias

O limite atual de 100 slugs antecede inclusive a leitura de cache. Separar o orçamento
da sincronização online da composição local: ler todas as cotações relevantes já disponíveis,
desserializando o cache uma vez por composição, sem rede no MCP.

- Cada cotação declara `retrievedAt`, `status` (`available`, `stale`, `missing`), plataforma
  e base de preço (`lowestSell` ou `highestBuy`). Ausência não significa preço zero.
- Manter a política inicial de frescor de 15 minutos, avaliada no instante de cada consulta,
  inclusive sem alterações de arquivo. Declarar o limiar no schema.
- Estimativas informam cobertura, quantidade de preços ausentes/antigos e se o valor é
  completo ou parcial. O menor anúncio é uma referência, não receita garantida.
- No valor esperado de relíquias, preço ausente não vale zero: expor o subtotal conhecido
  separadamente; o valor esperado completo é `null` quando faltarem preços necessários.
  Sem comparação completa e fresca, a recomendação comparativa é `insufficientData`.
- Declarar refinamento e hipótese de abertura, probabilidades e base de preço usadas.
  Só calcular a modalidade sustentada pelo catálogo; não inferir resultados de squad
  ou refinamentos sem dados. Validar probabilidades e cobertura das recompensas.
- Aplicar a mesma exigência de cobertura à comparação set versus peças e platinum versus
  ducats. Dados parciais não justificam uma preferência econômica definitiva.
- Corrigir essas regras no Core consumido pela UI e pelo MCP, mantendo a paridade.

### Reservas, excedentes e ordens

`list_surplus` responde sobre necessidade de coleção; `list_sales` orienta disponibilidade
para venda. Hoje excedentes ignoram reservas. O novo read model deve expor separadamente
`surplusForCollection`, `reservedQuantity`, a decomposição das reservas e `availableToSell`.
As listas se sobrepõem e seus totais não podem ser somados. Uma recomendação em set deve
identificar as peças alocadas para impedir contagem dupla com vendas avulsas.

O app atualmente limpa ordens em memória ao perder autenticação, mas preserva o cache.
Persistir, na mesma geração de mercado, `ordersRetrievedAt`, identidade da origem/conta
e estado `confirmed`, `unverified` ou `invalidated`:

- invalidação confirmada pelo app exclui ordens da avaliação em ambos os processos;
- falha de rede ou idade acima do limiar comum torna ordens não confirmadas; conservar
  suas reservas de forma conservadora, com aviso, sem apresentá-las como ordens atuais;
- adotar inicialmente 15 minutos para confirmação recente de ordens, documentando o
  limiar separadamente do frescor de preços;
- troca de origem/conta impede reutilizar ordens do contexto anterior. Se o vínculo não
  puder ser verificado, declarar a limitação e não afirmar disponibilidade confirmada;
- ausência de cache de ordens significa compromissos desconhecidos, não zero confirmado.
  Expor `availabilityConfirmed: false` e a hipótese usada nas quantidades estimadas;
- a identidade interna serve para associação; não incluir IDs privados ou caminhos nos DTOs.
  O MCP não lê token para determinar validade.

### Farm e explicações

Farm deve contar unidades faltantes e tipos de componentes separadamente. Relíquias úteis
são as que contêm componentes ainda faltantes, distinguindo tipos de relíquia e cópias
possuídas. Separar `missingPartsCostPlatinum` de `setPurchasePricePlatinum`; custo incompleto
tem cobertura e não se apresenta como preço total de completar o item.

Toda recomendação inclui `reasonCode`, explicação legível, `rulesVersion` e evidências
estruturadas (reservas, preços, cobertura e hipóteses). A IA não deve precisar interpretar
texto de apresentação para descobrir a regra aplicada.

## Contrato MCP v1

### Instruções do servidor

O campo `instructions` deve dizer, logo no início, que o servidor é somente leitura,
que `get_overview` é o ponto de partida e que listas grandes exigem paginação. Isso ajuda
o cliente a escolher as ferramentas sem incluir a documentação inteira no contexto.
Também orientar a reutilizar `snapshotId` na mesma análise, respeitar avisos de cobertura,
não somar vendas e excedentes e não tratar texto vindo do catálogo como instrução.

### Recursos

| URI | Conteúdo |
| --- | --- |
| `myframe://overview` | Resumo atual, disponibilidade das fontes e idade dos dados. |
| `myframe://schema` | Significado dos campos, unidades e valores de enum. |

Os recursos serão pequenos. Inventário e listas analíticas ficarão nas ferramentas para
permitir filtros e paginação previsível.

### Ferramentas

| Ferramenta | Finalidade e filtros principais |
| --- | --- |
| `get_capabilities` | Capacidades disponíveis, parciais e pendentes de validação externa. |
| `get_market_credential_status` | Estado/expiração da credencial independente do Warframe Market; nunca retorna o token. |
| `get_sync_status` | Estado somente leitura das fontes, última tentativa, revisão ativa, versão do parser, contagens aceitas/rejeitadas e erro sanitizado. |
| `get_capture_inbox_status` | Status read-only da inbox Overwolf: estado, processos, heartbeat/frescor e marcadores válidos/inválidos; não retorna caminho nem payload. |
| `get_sync_history` | Tentativas recentes por fonte, com limite e estado sanitizado. |
| `get_inventory_coverage` | Cobertura por campo do inventário (`Known`, `NotObserved`, `Invalid` etc.). |
| `get_inventory_history` | Revisões recentes do inventário, com sequência, completude, modo de captura e retenção. |
| `get_inventory_changes` | Compara duas revisões completas e retorna somente alterações de equipamento/quantidade, sem payload bruto. |
| `get_source_coverage` | Estado, revisão/parser ativos e cobertura por campo de `public-export`, `worldstate-pc` ou `overwolf-inventory`, sem payload bruto. No Public Export inclui `components`, `relics`, `marketIdentity`, `imageName`, `productCategory`, `localizedNames` e `technicalMetadata`. |
| `search_public_export` | Busca local no catálogo oficial por uniqueName, nome, alias ou categoria, retornando revisão/parser/cobertura e metadados técnicos, componentes de receita, identidade de mercado e fontes de relíquia observadas, sem rede e sem raw JSON. |
| `get_public_export_item` | Consulta um item Public Export por uniqueName, nome ou alias sem exigir inventário, retornando metadados normalizados e estado/revisão da fonte. |
| `search_references` | Busca referências Wiki/Overframe importadas localmente, com URL, revisão, autoria/licença e marcação de conteúdo não confiável para fatos. |
| `get_reference_section` | Recupera uma seção atribuída de uma referência importada, sem rede e sem aceitar URL fora das fontes permitidas. |
| `get_equipment` | Instâncias observadas, tipo, rank/configuração e estados de cobertura; filtro por tipo e limite. |
| `get_mods` | Upgrades/mods observados, filtráveis por `ownerInstanceId` e campo de origem; sem inferir capacidade. |
| `get_loadout` | Equipamento agrupado por instância com configuração e upgrades atribuídos; filtro por tipo e limite. |
| `get_acquisition` | Consolida componentes, fontes de relíquia e bounties ativas para um itemId, preservando revisões e estados de disponibilidade. |
| `get_bounties` | Bounties World State ativas, jobs, estágios, recompensas e estado/última tentativa da fonte; aceita filtros opcionais por sindicato e texto da recompensa. |
| `get_world_state` | Estado da fonte, revisão ativa, bounties, ciclos planetários e cobertura observada; limite de 1–200 bounties e filtros opcionais por sindicato e texto da recompensa. |
| `get_activity` | Atividades atuais (bounties e ciclos) com a mesma revisão, validade e cobertura do World State; pode filtrar texto da recompensa. |
| `get_overview` | Totais, nível, trades, maestria, cobertura, estimativas, fontes e configurações ativas; conta somente com inclusão explícita. |
| `search_inventory` | Busca inventário agregado suportado por texto, tipo, categoria, quantidade conhecida e estado built/stackable. |
| `get_item` | Detalhe por `itemId`, com descrição atribuída do catálogo, posse, componentes, maestria, preços, relíquias e evidências; coleções aninhadas paginadas. |
| `list_collection` | Lista coleção por estado, Prime, vaulted, categoria e progresso. |
| `list_farm` | Lista metas por texto, `targetItemId`, vaulted e unidades/tipos de peças faltantes; custos com cobertura. |
| `list_sales` | Lista decisões de manter, vender ou trocar, incluindo reservas e justificativa. |
| `list_relics` | Lista relíquias por ação, vaulted, posse e `targetItemId`, com valor selado, valor esperado e hipóteses. |
| `list_surplus` | Lista excedentes de coleção por motivo e cobertura de preço, distinguindo reservas e disponibilidade para venda. |

Todas terão anotações equivalentes a `readOnlyHint=true`, `destructiveHint=false`,
`idempotentHint=true` e `openWorldHint=false`. Não haverá `set_*`, `update_*`, `sell_*`,
`delete_*` nem uma ferramenta de atualização de mercado na v1.

As ferramentas de estado (`get_*` de plataforma, inventário rico e bounties) retornam
listas estruturadas sem paginação por snapshot; seus limites são explícitos no schema.
As ferramentas analíticas abaixo usam `snapshotId` e cursores assinados para manter
uma geração consistente durante a paginação.

### Prompt opcional

`review_inventory` orientará o cliente a consultar o overview e, conforme o objetivo do
usuário, paginar vendas, farm, relíquias e excedentes. Ele melhora a descoberta, mas não
é requisito para acessar os dados.

### Envelope e paginação

Todas as respostas compartilham metadados, mas listas usam `items`, detalhe usa `item`
e overview usa `overview`. Exemplo de inventário vazio válido, com mercado indisponível:

```json
{
  "schemaVersion": "1",
  "rulesVersion": "1",
  "snapshotId": "opaque-snapshot-id",
  "generatedAt": "2026-09-09T21:00:00Z",
  "evaluatedAt": "2026-09-09T21:00:00Z",
  "servedAt": "2026-09-09T21:00:01Z",
  "inventoryCapturedAt": "2026-09-09T20:59:00Z",
  "sources": {
    "inventory": { "state": "valid", "retrievedAt": "2026-09-09T21:00:00Z" },
    "catalog": { "state": "valid", "retrievedAt": "2026-09-09T20:50:00Z" },
    "prices": { "state": "missing", "retrievedAt": null },
    "orders": { "state": "missing", "retrievedAt": null }
  },
  "warnings": [{ "code": "MARKET_MISSING", "message": "Preços e ordens indisponíveis; sincronize pelo aplicativo." }],
  "complete": false,
  "count": 0,
  "totalCount": 0,
  "totals": null,
  "nextCursor": null,
  "items": []
}
```

- `limit` padrão de 50 e máximo de 200;
- `count` é a quantidade devolvida; `totalCount` é o total do conjunto filtrado antes da
  paginação, ou `null` se indisponível. Inventário ausente não equivale a inventário vazio;
- `complete` indica que todas as fontes declaradas na resposta estão disponíveis e válidas,
  sem fallback. Frescor e cobertura de cotações são informados separadamente; a falta de
  fonte opcional pode tornar a resposta parcial sem invalidar o inventário conhecido;
- `totals` agrega o conjunto filtrado inteiro no servidor, com unidade e cobertura por
  estimativa; não é a soma da página nem soma de listas sobrepostas;
- ordenação explícita e determinística com desempate por `itemId` e comparação ordinal;
- cursor opaco validado, vinculado ao snapshot, ferramenta, filtros normalizados, ordenação
  e projeção. Mudanças incompatíveis geram `INVALID_CURSOR`, sem reinício silencioso;
- `snapshotId` pode ser passado a outras ferramentas para manter a análise na mesma versão;
  o ID é opaco e restrito à sessão/processo, sem caminhos ou identidade privada embutidos;
- reter snapshots por até 5 minutos, com máximo inicial de 8 versões e 128 MiB por processo;
  pressão de memória pode antecipar descarte. Versão expirada gera `SNAPSHOT_EXPIRED`;
  cursor cuja versão expirou gera `CURSOR_EXPIRED`, orientando recomeçar pelo overview;
- itens, ordenação, totais e recomendações ficam fixos no snapshot. `evaluatedAt` identifica
  o instante das regras; `servedAt` e avisos de idade são atualizados a cada resposta.
  Ao vencer preço/ordem, uma consulta sem snapshot cria nova avaliação mesmo sem evento
  de arquivo. Consulta fixada mantém os valores históricos e sinaliza a idade atual;
- avisos e totais acompanham todas as páginas; `nextCursor: null` significa fim do conjunto
  disponível, não prova que todas as fontes estejam completas;
- strings e enums estáveis; números permanecem números, não textos formatados;
- datas em ISO 8601 e valores monetários explicitamente nomeados em platinum/ducats.

`inventoryCapturedAt` deriva inicialmente da modificação do arquivo de origem, não de
uma garantia de sincronização com o jogo; `generatedAt` é a composição pelo My Frame.
`sources` inclui também revisão de settings, catálogo e geração de mercado. Preços têm
datas individuais e resumo com mais antigo/mais recente, contagens frescas/antigas/ausentes;
um timestamp agregado nunca substitui a cobertura. Os estados das fontes e a condição
temporal dos dados são dimensões distintas. Exemplos e contratos de cada ferramenta
devem fechar os campos obrigatórios antes de implementar seus handlers.

### Schemas e erros

- Publicar `inputSchema` e `outputSchema` explícitos. Validar limites, enums, tamanho de
  strings e cursores, rejeitando parâmetros desconhecidos e identificadores ambíguos.
- Retornar o envelope em `structuredContent` e JSON equivalente em `content` textual
  para compatibilidade. Testar o formato emitido pelo SDK, não só o DTO interno.
- Diferenciar erros JSON-RPC de protocolo de falhas de execução com `isError: true`.
  Definir schema de erro próprio e sua compatibilidade com o `outputSchema`/SDK escolhido.
- Códigos de execução: `SETUP_REQUIRED`, `STORAGE_VERSION_UNSUPPORTED`,
  `SOURCE_UNAVAILABLE`, `ITEM_NOT_FOUND`, `INVALID_ARGUMENT`, `INVALID_CURSOR`,
  `CURSOR_EXPIRED`, `SNAPSHOT_EXPIRED`, `TIMEOUT`, `SERVER_BUSY` e `RESULT_TOO_LARGE`.
  Retornar mensagem curta, indicação de retry e ação corretiva, sem exceção interna.
- Falta de fonte opcional gera resposta parcial com avisos; indisponibilidade da fonte
  obrigatória gera erro acionável quando não houver fallback. Overview continua disponível.
- Versionar contrato e enums; documentar campos opcionais, `null` e compatibilidade.
  Acréscimos compatíveis não mudam o significado de campos existentes; mudanças de
  significado ou enums incompatíveis exigem nova versão do contrato.
- Fixar versão do SDK na implementação e registrar versões de protocolo/clientes testadas.

### Limites operacionais

- Listas usam projeções compactas. `get_item` aceita seleção de seção (`components`,
  `relics`, `recommendations`) com cursor próprio quando necessário, além do resumo.
- Orçamento inicial: 128 KiB por resposta MCP serializada, contando cópia textual e
  estruturada. Reduzir página e devolver continuação; nunca cortar strings/arrays em
  silêncio. Um único registro que exceda o orçamento gera `RESULT_TOO_LARGE` com orientação.
- Até 4 consultas simultâneas e uma reconstrução compartilhada do snapshot por processo;
  fila limitada a 16 pedidos. Excesso recebe `SERVER_BUSY`. Cancelar um consumidor não
  cancela a reconstrução ainda necessária a outros; EOF cancela todo o trabalho.
- Orçamento de chamada de 10 segundos. Metas iniciais na máquina de referência documentada:
  descoberta em até 2 segundos, primeira consulta de dados em até 5 segundos e consultas
  aquecidas com p95 de até 300 ms, sobre fixture sintética com 20 mil entradas de inventário
  e 50 mil entradas de catálogo. Medir com dois servidores ativos e registrar memória/bytes.
- Essas são metas de aceite locais, não garantias para toda máquina. Ajustes exigem
  benchmark e atualização explícita do plano antes de declarar a etapa concluída.

## Atualização dos dados

1. Inicialização MCP, descoberta e schema independem de AlecaFrame e mercado disponíveis.
   Carregar fontes em paralelo à disponibilidade do protocolo; overview informa estado.
2. Leitores retornam estado `missing`, `valid`, `invalid` ou `unavailable`, diagnóstico
   sanitizado e versão. Não converter erro de JSON/IO em cache vazio ou inventário zerado.
3. Observar `lastData.dat`, catálogos, settings e manifesto de mercado com debounce de
   750 ms. Eventos invalidam a composição; pedidos novos aguardam uma reconstrução
   compartilhada dentro do timeout ou recebem fallback identificado.
4. Revalidar metadados das fontes em consultas novas e reconciliar watchers a cada
   30 segundos, incluindo pasta inicialmente ausente, recriada, renomeada ou alterada nas
   settings. Tratar overflow, eventos perdidos e rajadas sem reconstruções ilimitadas.
5. Ler a geração de mercado pelo manifesto. Para fontes externas, comparar identidade,
   tamanho e modificação antes/depois da leitura, repetir mudanças e validar estrutura.
   Isso detecta instabilidade, mas não prova uma transação entre arquivos do AlecaFrame;
   registrar a procedência sem prometer atomicidade que a origem não oferece.
6. Publicar uma composição imutável somente após validação. Falha transitória conserva
   o último conteúdo válido por fonte e declara fallback, idade e causa. Nunca reutilizar
   inventário/ordens de outra origem/conta após troca de contexto; invalidar também cursores.
7. Expiração temporal produz nova avaliação nas consultas novas, sem precisar de rede ou
   escrita. O encerramento de `stdin` encerra watchers, fila e tarefas pendentes.

Não haverá dependência de rede para responder. Preços ausentes ou antigos serão marcados,
nunca inventados. A sincronização online permanece responsabilidade do aplicativo nesta
primeira versão.

## Privacidade e limites

Apesar de não haver login no MCP, o contrato é explicitamente limitado:

- não serializar `WFMarketToken.tk`, JWT, `Authorization` ou conteúdo bruto dos arquivos;
- não devolver `InventorySnapshot.SourcePath`, diretórios de cache ou nome de usuário do Windows;
- não registrar argumentos ou resultados completos das ferramentas;
- omitir nome e ID privado da conta por padrão; `get_overview(includeAccount: true)` pode
  incluir nome e plataforma com informação de procedência/idade, nunca token ou ID privado;
- usar somente leitores e stores read-only no processo MCP;
- escrever logs apenas em `stderr`; `stdout` fica exclusivo para mensagens MCP;
- não herdar comportamento de escrita do cliente de mercado por acidente.

Sanitizar centralmente mensagens de erro, exceções internas e logs: leitores atuais podem
incluir caminhos em exceções. `stderr` também é recebido pelo cliente. Registrar somente
operação, duração, contagens, código de erro e ID de correlação; validar com sentinelas
sintéticas de caminhos e segredos. Conteúdo de catálogo é dado não confiável, com tamanho
limitado, e não deve virar instrução, caminho de arquivo ou URL a acessar automaticamente.

`stdio` evita exposição de rede, mas qualquer processo local iniciado pelo usuário já opera
com as permissões desse usuário. O instalador e a documentação devem deixar claro que o
MCP dá à IA acesso ao inventário coberto pelo My Frame. Execução local não implica modelo
local nem ausência de envio ao provedor do cliente. As anotações read-only descrevem o
comportamento; a restrição real depende da implementação e dos testes de ausência de
escritas e chamadas de rede.

## Instalação e uso

A publicação do aplicativo inclui um executável self-contained `MyFrame.Mcp.exe` ao
lado dos binários do My Frame. A tela Settings contém uma seção “AI access (MCP)” com o
caminho detectado, uma explicação do acesso read-only e botões para copiar comandos.

`scripts/Build-Distribution.ps1` publica app e MCP self-contained na mesma pasta e versão.
O cadastro usa esse caminho estável; depois de uma troca de diretório de instalação, o
cliente deve ser recadastrado. Upgrade/rollback mantêm settings e caches versionados fora
da pasta dos binários. Os comandos abaixo seguem as CLIs oficiais atuais.

Exemplo para Codex:

```powershell
codex mcp add my-frame -- "C:\caminho\para\MyFrame.Mcp.exe"
codex mcp list
```

Exemplo para Claude Code:

```powershell
claude mcp add --transport stdio --scope user my-frame -- "C:\caminho\para\MyFrame.Mcp.exe"
claude mcp get my-frame
```

O caminho real é preenchido pelo app, portanto o usuário não precisa editá-lo. A v1
não altera automaticamente os arquivos de configuração de outros programas.

## Plano de implementação

### 0. Contratos de dados e correção das análises

- Fechar identidade, cobertura, unidades, desconhecidos e exemplos de DTOs por ferramenta.
- Corrigir preços parciais, valor esperado, set versus peças, farm e distinção de excedentes.
- Definir ordens confirmadas/não confirmadas/invalidadas e vínculo com contexto de origem.
- Acrescentar códigos de justificativa e evidências estruturadas no read model compartilhado.
- Criar fixtures com resultados esperados independentes, incluindo duas cópias do mesmo
  equipamento, componente reservado e preço de recompensa ausente.

Aceite: o Core não transforma desconhecido em zero/um, não recomenda vantagem econômica
sem cobertura suficiente e diferencia excedente de coleção de disponibilidade para venda.
As mesmas correções chegam à interface antes de expor contratos externos.

### 1. Fundação compartilhada

- Extrair caminhos e settings do MAUI para abstrações do Core.
- Implementar leitura com estados explícitos, manifesto de mercado, revisão de settings
  e migração idempotente pelo app, preservando arquivos anteriores.
- Separar composição local/cacheada da sincronização online de `DashboardService`.
- Criar read model e testes de paridade antes de expor qualquer endpoint.

Aceite: app e console obtêm resultados equivalentes com as mesmas versões de fontes,
settings e relógio. Migração repetida/interrompida recupera sem perder preferências;
MCP iniciado antes dela diagnostica setup sem escrever ou buscar rede.

### 2. Servidor e contratos

- Criar `MyFrame.Mcp` como console `net10.0` e adicionar à solução.
- Usar o pacote `ModelContextProtocol` e `Microsoft.Extensions.Hosting`.
- Configurar `WithStdioServerTransport`, DI e descoberta explícita das ferramentas.
- Implementar schemas de entrada/saída, recursos, ferramentas, filtros, totais do conjunto
  filtrado, cursores vinculados a snapshots e erros tipados.
- Direcionar logs para `stderr` e preencher as instruções globais do servidor.

Aceite: MCP Inspector lista e chama capacidades sem tráfego inválido em `stdout`;
descoberta e overview funcionam com fontes ausentes. Respostas reais contêm JSON
estruturado/textual equivalente, com limite de bytes e continuidade verificáveis.

### 3. Atualização e robustez

- Implementar retenção limitada de snapshots, relógio injetável e reconstrução compartilhada.
- Observar inventário, catálogos, settings e gerações de mercado, com reconciliação de
  eventos perdidos e recuperação de pasta ausente.
- Manter fontes válidas anteriores em falhas transitórias do mesmo contexto, com avisos.
- Validar cancelamento, encerramento por EOF e duas instâncias simultâneas.
- Impedir escrita nos caches pelo processo MCP.

Aceite: alterações isoladas de inventário, cache e settings aparecem sem reiniciar; preço
vence sem evento de arquivo; gravação parcial não zera dados. Paginação permanece fixa
durante atualização e expiração/contexto incompatível gera erro explícito. Cumprir os
limites operacionais documentados e demonstrar ausência de rede/escrita no processo MCP.

### 4. Distribuição e onboarding

- Publicar o servidor self-contained na mesma arquitetura do app.
- Criar o fluxo de publicação/distribuição, com caminho estável e upgrade de app/MCP juntos.
- Adicionar comandos prontos na tela Settings e documentação de solução de problemas.
- Validar caminhos com espaços, caracteres especiais e Unicode, execução fora do diretório
  do repositório, atualização com processo ativo e compatibilidade/rollback de armazenamento.

Aceite: após setup pelo app, uma instalação limpa conecta copiando um comando; atualização
preserva o cadastro e os dados. Onboarding explica cobertura, acesso à conta sob demanda
e possibilidade de envio dos resultados ao provedor de IA do cliente.

### 5. Qualidade final

- Testes unitários de cada filtro e projeção.
- Testes de contrato para schemas, enums, limites, paginação e erros.
- Teste de paridade UI/MCP sobre o mesmo `DashboardSnapshot`.
- Testes de resultados esperados independentes: paridade não substitui correção das regras.
- Testes de privacidade procurando segredos e caminhos nos resultados e logs.
- Integração em memória e processo real via `stdio`.
- Smoke tests manuais no Codex e Claude no Windows.
- Restore, build e suíte completa da solução.

Aceite: todos os itens da definição de pronto em [PLANO.md](PLANO.md) são demonstrados.

### Matriz mínima de regressão

| Cenário | Resultado exigido |
| --- | --- |
| Inventário muda entre páginas e entre overview/detalhe | Snapshot fixado não duplica/omite registros; consulta nova recebe nova versão. |
| Cursor adulterado, outra consulta, expiração ou limite de memória | Erro tipado, sem reiniciar paginação silenciosamente. |
| Preço vence sem mudança em arquivo | Nova avaliação sinaliza idade; snapshot fixado preserva cálculo e avisa. |
| Mais de 100 preços já cacheados; recompensa/peça sem cotação | Todos os relevantes são lidos; estimativa parcial não vira comparação definitiva. |
| Ordens offline, invalidadas ou de outra origem/conta | UI/MCP aplicam a mesma política, sem misturar contextos. |
| Peça excedente com reserva e venda por set | Disponibilidade explicada e ausência de contagem dupla. |
| Equipamentos duplicados, nome ambíguo e item sem catálogo | Cobertura e quantidade desconhecida explícitas; identidade não depende do nome. |
| Catálogo/JSON parcialmente escrito, cache bloqueado ou manifesto substituído | Retry/fallback com aviso; falha não vira conjunto vazio válido. |
| Pasta aparece após startup; watcher perde evento; só settings/cache muda | Recuperação e nova versão sem reinício. |
| MCP inicia antes de setup, com cache vazio ou formato incompatível | Descoberta/overview disponíveis e erro acionável nas consultas dependentes. |
| Migração interrompida/repetida e upgrade/rollback | Dados preservados, formato incompatível não é sobrescrito. |
| Dois servidores e app ativo; chamadas paralelas, cancelamento e EOF | Limites respeitados, shutdown limpo e nenhuma escrita/rede pelo MCP. |
| Item com muitos componentes/relíquias e inventário grande | Orçamento inclui ambos os formatos MCP; continuações e p95 demonstrados. |
| Exceção com caminho, segredo ou texto de catálogo malicioso | Respostas e stderr sanitizados, sem execução nem acesso derivado do conteúdo. |

## Riscos e mitigação

| Risco | Mitigação |
| --- | --- |
| App e MCP calcularem números diferentes | Fontes/regras/settings versionados, mesma política de ordens e testes com relógio controlado. |
| Ambos reproduzirem uma recomendação incorreta | Fixtures com expectativas independentes, cobertura e justificativas estruturadas. |
| Páginas misturarem estados | Cursor vinculado a snapshot imutável e expiração explícita. |
| Inventário exceder o contexto da IA | Projeções compactas, totais no servidor, limite de bytes e paginação aninhada. |
| Cache sofrer corrida entre processos | Gerações imutáveis, manifesto atômico, coordenação dos escritores e fallback. |
| Catálogo mudar enquanto é lido | Validação de estabilidade e procedência, sem prometer transação da fonte externa. |
| Dados ficarem antigos sem eventos | Expiração temporal e reconciliação de watchers/fontes. |
| Erro revelar caminhos ou identidade privada | Sanitização central também em stderr e conta omitida por padrão. |
| Saída de log quebrar o protocolo | `stdout` exclusivo do MCP e logging configurado para `stderr`. |
| Cliente não localizar o executável após upgrade | Caminho estável, comando escapado e teste de atualização/reinício. |

## Evoluções posteriores

Somente depois da v1 estabilizada considerar:

- simulações read-only de razão ducats/platinum e reserva de sets, sem persistir settings,
  com snapshot-base e parâmetros simulados explícitos;
- agregações configuráveis e rankings por objetivo; os totais filtrados básicos já são v1;
- comparação de cenários de farm e abertura/refinamento de relíquias, quando houver dados
  suficientes para sustentar as hipóteses;
- inventário por instância e quantidades de equipamento, ampliando o leitor e seu contrato;
- notificações/subscriptions de mudança de snapshot;
- mais prompts de análise;
- transporte Streamable HTTP local ou remoto com um novo modelo de autenticação;
- clientes além de Windows;
- ações de escrita, sempre em um marco separado com consentimento e confirmações próprias.

## Referências oficiais

- [MCP — ferramentas, schemas, resultados estruturados e erros](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP — transporte stdio](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports)
- [MCP no Codex](https://developers.openai.com/codex/mcp)
- [MCP no Claude Code](https://code.claude.com/docs/en/mcp)
- [SDK C# oficial — primeiros passos](https://csharp.sdk.modelcontextprotocol.io/concepts/getting-started.html)
- [SDK C# oficial — transportes](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md)
