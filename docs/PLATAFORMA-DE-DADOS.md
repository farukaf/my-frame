# Plano de entrega — plataforma local de dados Warframe

Data: 12/09/2026. Estado: F0 em validação; demais fases ainda não implementadas.
Execução: [relatório F0](validacoes/2026-09-12-f0.md),
[auditoria](AUDITORIA-ALECAFRAME.md) e [decisões abertas](DECISOES-PLATAFORMA.md).

Este é o roteiro de evolução do My Frame. O comportamento existente permanece
documentado em [MCP.md](MCP.md) e [ARQUITETURA.md](ARQUITETURA.md).
As evidências e limitações da pesquisa estão em [FONTES-DE-DADOS.md](FONTES-DE-DADOS.md).
A execução dos testes segue [VALIDACAO-PLATAFORMA.md](VALIDACAO-PLATAFORMA.md).

## 1. Resultado de produto

Entregar uma aplicação Windows que coleta o inventário pelo Overwolf, sincroniza
dados públicos de Warframe, mantém dados consultáveis em SQLite, mostra o estado
de cada sincronização e oferece uma API de domínio via MCP para qualquer LLM.

A inteligência de recomendar builds, selecionar prioridades e elaborar planos é
da LLM. A aplicação fornece fatos, relações, cálculos verificáveis, referências,
cobertura e idade dos dados. Skills orientam a pesquisa e a composição das respostas.

Perguntas de aceite:

- Quais builds consigo montar com meus mods e equipamentos atuais?
- Como adaptar uma build de referência ao meu inventário?
- O que falta para um objetivo, quais pré-requisitos existem e onde obter cada item?
- Quais bounties estão disponíveis e quais recompensas oferecem?
- Qual equipamento estou visualizando e quais referências existem para ele?
- O que mudou no meu inventário desde a última sincronização?
- De onde veio uma informação e qual sua versão?
- Quais dados estão incompletos e como atualizá-los?

Disponibilidade de dados melhora as respostas, mas não garante correção da LLM.
Os testes de respostas são parte do aceite, junto dos testes de infraestrutura.

## 2. Decisões e limites

1. Remover AlecaFrame como dependência obrigatória. Overwolf permanece como canal
   proposto para inventário; validar acesso, distribuição e captura na fase F1.
2. Usar fontes públicas de Warframe para catálogo, estado do mundo e drops;
   enriquecimentos comunitários preservam autoria e procedência próprias.
3. Usar SQLite para dados relacionados, gerações e histórico limitado. JSON continua
   adequado para settings e mensagens de transporte; não haverá um projeto paralelo
   de caches JSON com manifesto para substituir posteriormente pelo banco.
4. Manter um único escritor coordenado por usuário Windows. App e MCP consultam
   projeções compartilhadas. O MCP não faz migração, coleta de rede ou escrita.
5. Preservar dados públicos de origem permitidos para reprocessar parsers. Inventário
   bruto privado tem retenção curta, acesso restrito e não é exposto em logs/MCP.
6. Modelos internos evoluem sem sufixo obrigatório v2. Versões de armazenamento,
   parser, regras e contrato MCP são independentes.
7. Não introduzir um motor que declare a melhor build. Cálculos de capacidade,
   diferenças e probabilidades podem ser determinísticos e explicar suas hipóteses.
8. Wiki e Overframe são adaptadores de fontes, com acesso e licença verificados;
   não assumir a existência de API pública ou permissão de cópia integral.
9. A migração termina com instalação e uso sem AlecaFrame, inclusive sem seus
   catálogos, token de mercado ou diretórios. Importação de legado pode ser opcional.
10. Chat fica fora da primeira entrega: evento existe, mas coleta, retenção e envio
    à LLM precisam de opção explícita de produto. Nenhuma automação de mensagens.

Não fazem parte deste marco: automação do jogo, anúncios escritos no mercado,
leitura de memória, extração de sessão do jogo, serviço remoto multiusuário,
simulador completo de combate ou estimativa garantida de tokens por hora.

## 3. Evidência disponível e dúvidas que afetam o desenho

O GEP documenta `inventory`, `highlighted`, `username`, dados de versão e `chat`.
Não documenta eventos específicos de missão, dano ou conclusão de bounty.
Detalhes e fontes: [OW1–OW4](FONTES-DE-DADOS.md#overwolf).

O payload completo do inventário não foi capturado nesta pesquisa. Ranks,
configurações, polaridades, Helminth, shards e Incarnon são campos a verificar,
não capacidades já demonstradas do futuro coletor. A documentação do AlecaFrame
mostra recursos de mods equipados/melhorados e shards, mas não especifica um
schema completo do GEP. [AF1–AF4](FONTES-DE-DADOS.md#alecaframe).

No código atual, `AlecaFrameReader` percorre apenas uma lista de coleções e reduz
equipamentos a presença; `InventorySnapshot` não possui instâncias. O leitor inclui
`RawUpgrades`, mas não inclui `Upgrades`. Não concluir que todos os mods melhorados
estão em `RawUpgrades`: a relação entre ambas deve ser medida na captura.

As buscas MCP anteriores também foram prejudicadas por tratamento no cliente:
`structuredContent?.items ?? []` transformou ausência de conteúdo estruturado em
aparente inventário vazio. Parte dos resultados vazios era erro de execução.
A correção deve cobrir servidor, clientes de exemplo e instruções das skills.

## 4. Arquitetura proposta

```text
Warframe -> Overwolf GEP -> MyFrame.Collector.Overwolf
                                      |
                         transporte local validado
                                      |
Fontes públicas -> adaptadores -> MyFrame.SyncHost
                                      |
                         staging -> validação -> publicação
                                      |
                    catalog.db + player.db
                                      |
                        read model compartilhado
                             /                 \
                       MyFrame.App          MyFrame.Mcp
                                                |
                                          Skills -> LLM
```

Os nomes representam responsabilidades; não é obrigatório criar um projeto para
cada interface. Extrair assemblies somente quando houver composição/distribuição
independente. Manter `MyFrame.Core` livre de dependências da UI e do runtime Overwolf.

| Componente | Responsabilidade | Escrita/rede |
| --- | --- | --- |
| Collector.Overwolf | Assinar features e entregar envelopes de eventos | Transporte local; sem acesso direto ao banco |
| SyncHost | Agendar, importar, validar, migrar e publicar gerações | Escritor único; rede para adaptadores habilitados |
| Storage | Migrations, transações, índices e repositórios | Interfaces separadas de leitura/escrita |
| Core/read model | Identidade, projeções, cobertura e cálculos | Sem coleta de rede |
| App | Estado de sincronização e exploração dos dados | Solicita operações ao SyncHost |
| MCP | Consultas por domínio e geração | Leitura local; sem SQL arbitrário |
| Skills | Procedimento de investigação e planejamento | Usam as ferramentas disponíveis no cliente |

O SyncHost é executado em segundo plano, sem janela, quando habilitado. Definir na
F2 quem o inicia, quem o encerra e se continua ativo com a janela fechada. O MCP
precisa consultar o último estado válido mesmo com coletor e SyncHost encerrados.

## 5. Persistência e consistência

### 5.1 Bancos e identidade

Proposta inicial: `catalog.db` para fatos públicos e `player.db` para inventário,
ordens pessoais e preferências analíticas. SQLite não criptografa esses arquivos
automaticamente. Proteger dados pessoais por permissões do usuário Windows;
segredos de autenticação ficam em armazenamento de credenciais separado.

| Banco | Grupos de tabelas propostos |
| --- | --- |
| Ambos | `schema_migrations`, `sources`, `source_revisions`, `sync_runs`, `coverage`, `diagnostics` |
| Catálogo | `items`, `aliases`, `recipes`, `recipe_components`, `mod_definitions`, `mod_rank_effects`, `weapon_attacks`, `abilities`, `arcane_definitions` |
| Catálogo | `missions`, `reward_tables`, `reward_entries`, `acquisition_edges`, `worldstate_revisions`, `bounties`, `enemies`, `mechanic_documents` |
| Catálogo | `source_documents`, `document_sections`, `community_builds`, `build_slots`, índices de busca textual |
| Jogador | `player_contexts`, `inventory_revisions`, `equipment_instances`, `mod_instances`, `stackables`, `configs`, `config_slots`, `progress`, `inventory_deltas` |
| Jogador | `market_account_state`, `market_orders`, `analysis_views`, `analysis_view_sources` |

Usar identificador canônico do jogo quando disponível; mapeamentos para Wiki,
Overframe e mercado são relações separadas. Nome traduzido não é chave primária.
Instâncias têm identidade diferente do tipo. IDs pessoais externos não são
devolvidos ao MCP; referências opacas locais preservam relacionamentos.

Campos opcionais retornam valor e cobertura, distinguindo `known`, `notObserved`,
`notSupported`, `invalid` e `notApplicable`. Coleção ausente não é coleção vazia.
Rank máximo de catálogo não é rank possuído; maestria histórica não é XP atual.

### 5.2 Publicação e snapshots de análise

1. Baixar/receber uma revisão e calcular hash do conteúdo.
2. Registrar tentativa e origem, sem substituir a geração ativa.
3. Validar formato, limites, contagens, identidade e relações em staging.
4. Normalizar e gravar a revisão numa transação curta do banco correspondente.
5. Publicar o ponteiro de revisão ativa após concluir a validação.
6. Falha mantém a revisão anterior e atualiza o diagnóstico.

Revisões de fontes distintas não precisam ter sido coletadas no mesmo instante.
Cada análise fixa um vetor de revisões: inventário, catálogo, mercado, world state
e documentos. Cada registro deve permitir consultar a revisão selecionada.

Dois bancos SQLite em WAL não fornecem commit atômico conjunto. Não depender de
`ATTACH` para essa garantia. Publicar primeiro a revisão pública; depois gravar em
`player.db` a visão de análise que a referencia. Validar referências ao abrir a
visão e reter revisões públicas enquanto referenciadas. Testar falha entre commits.
Se a complexidade não se justificar, F2 pode escolher um banco com separação lógica,
registrando a decisão antes da implementação dependente.

Snapshots MCP não mantêm transações de leitura abertas durante a conversa. Guardam
IDs de revisões retidas; consultas usam transações curtas. Isso evita impedir
checkpoints por minutos. Garbage collection considera referências e leases.

Usar WAL somente após demonstrar acesso simultâneo e operação offline com leitores
read-only. O SyncHost prepara arquivos auxiliares/checkpoints necessários; não usar
`immutable=1` num banco que ainda é atualizado. [DB1](FONTES-DE-DADOS.md#sqlite-e-contratos).

### 5.3 Origem, retenção e busca

Guardar `sourceId`, `sourceRecordId`, URL pública quando aplicável, revisão/hash,
`retrievedAt`, data de publicação quando conhecida, versão do parser, idioma,
licença e cobertura. Hash e ETag não são versões do jogo. Só preencher `gameVersion`
quando houver associação verificável.

Valores conflitantes permanecem atribuídos às fontes. A precedência é por campo e
atualidade: voto de build não substitui atributo mecânico; guia antigo não substitui
patch mais recente; desconhecido não é preenchido por inferência silenciosa.

Políticas iniciais, ajustáveis após medições:

- Raw público permitido: duas revisões por documento, teto global de 1 GiB.
- Raw de inventário: até três revisões ou 24 horas, o que expirar primeiro; opção
  de desligar retenção. Não guardar chat nem segredos no raw.
- Histórico normalizado: 30 dias, teto inicial de 250 MiB por contexto; manter
  revisão atual e revisões com lease até seu limite absoluto.
- Execuções de sync/logs sanitizados: 30 dias com rotação por tamanho.
- Busca textual por FTS, nomes PT/EN e aliases; consultas limitadas e parametrizadas.

Se limites impedirem nova ingestão, mostrar condição de armazenamento. Não apagar
revisão ativa para recuperar espaço. Backup usa mecanismo consistente com WAL,
seguido de teste de restauração; copiar apenas o arquivo `.db` ativo não é o fluxo
de backup. [DB2](FONTES-DE-DADOS.md#sqlite-e-contratos).

## 6. Política de sincronização e página de status

| Fonte | Política inicial proposta | Condição de atualização |
| --- | --- | --- |
| Inventário GEP | Por evento, deduplicação por hash | Recepção de snapshot válido; não forçar chamadas privadas |
| Item visualizado | Por evento, último valor com horário | Expirar contexto ao encerrar sessão |
| Catálogo público | Verificar revisão a cada 6 h e na inicialização | Baixar apenas revisão alterada |
| Drops e patches | Verificar a cada 6 h | ETag/hash e revisão de origem |
| World State | 60–120 s, conforme limites da fonte | Conservar expiração original dos eventos |
| Mercado público | Fila e cache; alvo de frescor de 15 min | Orçamento/rate limit por fonte |
| Ordens pessoais | Atualização autenticada independente | Não supor ordens vazias em falha de login |
| Wiki/Overframe | Sob demanda, atualização após 24 h como ponto inicial | Somente mecanismo permitido e fonte habilitada |

Esses intervalos são decisões de produto, não limites oficiais. Aplicar timeout,
backoff com jitter, `Retry-After`, limite de concorrência por host e cancelamento.
Uma fonte lenta não bloqueia as demais. Resposta 304 mantém o conteúdo, mas registra
`checkedAt`; não falsifica a data de publicação ou versão.

A página de sincronização deve existir desde a fundação e ganhar fontes por fase:

- Resumo: atualizado, parcial, offline ou aguardando jogo; hora do último inventário.
- Por fonte: habilitada, estado, última tentativa/sucesso, próxima tentativa,
  etapa atual, duração, registros recebidos/aceitos/rejeitados e ação corretiva.
- Coletor: conectado, jogo detectado, aguardando evento, provider desatualizado,
  último evento recebido e perda de conexão.
- Ações: sincronizar fontes públicas, tentar novamente, cancelar, habilitar fonte,
  abrir detalhes, exportar diagnóstico sanitizado e gerenciar armazenamento.
- Detalhes técnicos recolhidos: versão GEP/parser, revisão, hash, cobertura e erros.
- “Sincronizar inventário” explica que aguarda evento do jogo; não promete forçar
  atualização do GEP. Progresso indeterminado quando a fonte não informa total.
- Geração antiga continua consultável com aviso enquanto uma nova sincroniza.

## 7. Fases executáveis

Cada fase deve entregar código, documentação atualizada e evidências de validação.
Os responsáveis abaixo são papéis de trabalho, não solicitação de agentes paralelos.

### F0 — Consolidar baseline e decisões de integração

Dependências: nenhuma. Responsáveis: Core, MCP e integração.

1. Registrar build, versão do SDK, protocolo e baseline de testes existentes.
2. Auditar dependências do AlecaFrame em inventário, catálogo, settings e token WFM.
3. Reproduzir erro de snapshot e separar falhas servidor/cliente; criar caso de
   regressão para erro interpretado como lista vazia.
4. Registrar perguntas e capacidades mínimas; abrir decisões para bridge, bancos,
   autoria/licença das fontes e distribuição do coletor.
5. Mapear os testes pendentes de `todo.md` para a nova matriz sem marcá-los concluídos.

Aceite: baseline reprodutível e lista de dependências sem pressupor campos privados.
Evidência: relatório F0 com comandos, resultados e pendências reais.

### F1 — Provar a captura Overwolf sem AlecaFrame

Dependências: F0. Responsável: integração.

1. Usar documentação/sample oficiais para app mínimo, comparar Native e Electron e
   escolher runtime. Confirmar acesso ao GEP de Warframe e requisitos de distribuição.
2. Assinar inventário, identificação e versão; validar `highlighted` separadamente.
3. Demonstrar captura com AlecaFrame fechado e sem ler sua pasta.
4. Executar cenários: login, relé/retorno, reinício do jogo, coletor iniciado antes
   e depois do jogo, desconexão e mudança de usuário.
5. Criar schema probe local: nomes/tipos de campos, coleções, contagens, nulos e
   relações, sem exportar valores privados. Listar campos presentes, ausentes e
   codificados; `Configs` deve ser verificado como modificação ou cosmético.
6. Distinguir snapshot completo, fragmento e delta. Rejeitar publicação quando não
   for possível comprovar integridade. Verificar envelopes reais Native/Electron.
7. Validar transporte permitido bridge/.NET; inbox atômica é candidata, não garantia
   de capacidade da plataforma. Documentar tamanho, framing, ordem e confirmação.
8. Gerar fixtures sintéticas equivalentes e um relatório de cobertura por coleção.

Aceite: inventário real recebido independentemente do AlecaFrame; esquema e fluxo
de distribuição demonstrados. Falta de acesso ao GEP é bloqueio desta integração,
não motivo para substituir silenciosamente por extração de memória/sessão.
Catálogo e storage podem avançar usando fixtures enquanto o acesso é resolvido.

### F2 — Fundação SQLite e SyncHost

Dependências: F0; fronteira do coletor depende de F1. Responsáveis: storage/Core.

1. Fazer spike de SQLite/.NET: versões fixadas, WAL, migrations, read-only e backup.
2. Confirmar um ou dois bancos com teste de revisão composta; registrar ADR.
3. Implementar escritor único entre processos, operações idempotentes e fila limitada.
4. Implementar schema mínimo: fontes, revisões, tentativas, cobertura e visão de análise.
5. Implementar publicação transacional, retenção, recuperação após encerramento e
   recusa de schema incompatível sem reset de dados.
6. Definir lifecycle do host, exclusão mútua, IPC local e comportamento com UI fechada.
7. Criar primeira página de status com uma fonte sintética controlada.

Aceite: rollback preserva dados, repetição não duplica registros, MCP não cria banco
nem arquivos auxiliares, UI continua responsiva durante ingestão.
Testes: DB01–DB08 e SY01–SY04 da matriz.

### F3 — Catálogo público e identidade canônica

Dependências: F2. Responsável: dados públicos.

1. Implementar descoberta e download do Public Export com revisão fixa por execução.
2. Inventariar cobertura; usar WFCD como enriquecimento explícito por revisão quando
   necessário. Não depender dos arquivos de catálogo instalados pelo AlecaFrame.
3. Normalizar itens, aliases PT/EN, categorias, receitas, recursos, relíquias, mods,
   efeitos por rank, arcanes, ataques e habilidades conforme campos confirmados.
4. Separar fatos numéricos de descrição textual; preservar texto quando o efeito não
   puder ser traduzido para cálculo com segurança.
5. Criar mapeamentos para paths StoreItems, item do inventário, mercado e referências.
6. Indexar busca e adicionar estados da fonte na página de sincronização.

Aceite: buscar Mesa, Voidrig, mod e componente retorna entidades distintas com
proveniência; nomes ambíguos retornam candidatos; reimportar revisão é idempotente.
Testes: SRC01–SRC05, IN04 e casos de categoria do MCP.

### F4 — Inventário rico e contexto do jogador

Dependências: F1, F2 e F3. Responsáveis: integração/Core.

1. Implementar envelope do coletor: ID de sessão, sequência, horário, hash e versão
   de protocolo. Validar mensagens, limitar tamanho e deduplicar na importação.
2. Preservar tipos e instâncias; normalizar `RawUpgrades` e `Upgrades` conforme a
   evidência de F1, sem somar a mesma cópia duas vezes.
3. Resolver configurações e referências aos mods; guardar rank atual e polaridades
   somente se observados/decodificados com teste independente.
4. Implementar fingerprints, armas Kuva/Tenet, arcanes, shards, Helminth e Incarnon
   progressivamente, cada recurso protegido por capability e cobertura.
5. Tratar itens desconhecidos como dados válidos sem correspondência; não descartá-los.
6. Versionar contexto do jogador; nome não é identificador estável. Se a associação
   estiver ambígua, não mesclar históricos automaticamente.
7. Produzir diferenças de inventário entre revisões compatíveis, sem atribuir causa.
8. Mostrar equipamento/mod/configuração na UI com a mesma projeção do futuro MCP.

Aceite: comparação com Arsenal de uma amostra de Warframe, arma, exaltada, Necramech,
mod melhorado e duplicado coincide nos campos suportados. Campos não suportados são
explicitamente desconhecidos. Não declarar capacidade equipável sem dados suficientes.
Testes: IN01–IN08; coleta real sanitizada separada dos testes automatizados.

### F5 — Atividades, recompensas e mecânicas

Dependências: F3. Responsável: dados públicos.

1. Sincronizar World State e tabelas de drops de fontes públicas, com revisões distintas.
2. Relacionar missões, rotações, recompensas, bounties e intervalos de validade.
3. Distinguir chance de recompensa, quantidade, recompensa garantida e bônus; não
   confundir pool completo com oferta ativa no servidor.
4. Mapear especificamente recompensas de Mother Tokens; se a quantidade não estiver
   na tabela oficial, registrar outra fonte verificável ou deixar desconhecida.
5. Importar pré-requisitos e relações de aquisição; registrar ciclos e alternativas.
6. Incorporar dados de inimigos/mecânicas e referências de patch com escopo e versão.

Aceite: um caminho de aquisição pode ser rastreado à fonte; bounty vencida não aparece
como atual; não se inventa duração ou taxa de farm a partir de drop chance.
Testes: SRC06–SRC09 e cenário LLM01.

### F6 — Mercado independente e migração do legado

Dependências: F2 e F3; retirar o leitor de inventário exige F4. Responsáveis: Core/app.

1. Migrar settings, cotações e ordens por importador idempotente com backup consistente.
2. Manter timestamps/cobertura originais; não tornar dados antigos recentes na migração.
3. Substituir leitura obrigatória de `WFMarketToken.tk` por autenticação de mercado
   suportada e validada. Segredos ficam fora do SQLite/MCP. Confirmar contrato antes
   de implementar; acesso público funciona sem conta.
4. Se não houver fluxo suportado de ordens pessoais, desabilitar essa capacidade com
   aviso e reservas não confirmadas. Registrar perda funcional, não ocultá-la.
5. Adaptar regras econômicas e UI ao read model SQLite, mantendo semântica testada.
6. Remover exigência de pasta AlecaFrame e suas dependências no caminho padrão.
7. Validar fallback/importação de legado opcional e rollback de versão.

Aceite: instalação limpa sem AlecaFrame sincroniza catálogo/inventário e consulta
mercado público. Ordens pessoais só são declaradas disponíveis após teste do novo login.
Testes: MIG01–MIG05, regressões econômicas e instalação limpa.

### F7 — Wiki e builds comunitárias

Dependências: F2 e F3. Responsável: adaptadores de conteúdo.

1. Confirmar interfaces de acesso, termos, licença e limites de Wiki e Overframe.
   Registrar decisão e mecanismos permitidos por fonte.
2. Implementar busca/importação por entidade ou URL suportada com cache. Não fornecer
   fetch arbitrário de endereços locais/privados pelo MCP.
3. Wiki: título, URL, revisão, idioma, seções, referências e trechos permitidos.
4. Overframe: ID, equipamento, autor, URL, atualização, mods/ranks, Forma e hipóteses
   presentes. Votos são métrica comunitária, não prova de qualidade ou atualidade.
5. Associar referências canônicas; detectar guia antigo, item removido e revisão ambígua.
6. Conteúdo remoto é dado não confiável; preservar limites entre texto e instruções.
7. Se o acesso automatizado não puder ser viabilizado, manter links/importação
   permitida e registrar integração como parcial; não marcar o conector completo.

Aceite: fontes acessíveis offline após sincronização, com atribuição e limites de uso;
falha externa não compromete inventário. Wiki/Overframe completos exigem acesso testado.
Testes: DOC01–DOC05 e LLM02/LLM05.

### F8 — MCP de domínio sobre o banco

Dependências: F2–F4; demais grupos habilitados conforme F5–F7. Responsável: MCP.

1. Preservar as oito ferramentas atuais com adaptador de compatibilidade e testes.
2. Criar recursos de descoberta: capacidades, categorias, cobertura e fontes habilitadas.
3. Implementar consultas em lote, paginação e projeções por seção com orçamento total.
4. Expor status de sync, dados do arsenal, aquisição, atividades e referências.
5. Implementar envelope de erro estruturado e textual equivalente, validado pelo SDK.
6. Fixar vetor de revisões por snapshot; expor prazo e limite absoluto do lease.
7. Atualizar avisos de idade no instante da resposta sem recalcular fatos históricos.
8. Publicar schema e exemplos executáveis, incluindo erro, vazio, parcial e ausência.
9. Fazer paridade entre UI e MCP por dados e revisão, não somente por código compartilhado.

Aceite: o fluxo Mother Tokens usa poucas consultas por domínio, todas rastreáveis;
expiração e ausência são distinguíveis; MCP permanece sem escrita e rede.
Testes: MCP01–MCP09 e ensaios reais em clientes.

### F9 — Skills e avaliação de respostas

Dependências: F8; referências comunitárias dependem de F7. Responsável: experiência LLM.

1. Criar skills versionadas no repositório para builds, farm/progressão e economia.
2. Cada skill consulta capacidades, cobertura e frescor antes dos dados específicos.
3. Ensinar lotes, continuidade de snapshot, tratamento de `isError`, interpretação
   de campos desconhecidos e citação de fontes/revisões.
4. Build: consultar arsenal, atividade e referências; comparar requisitos com posse;
   separar sugestão de validação matemática, declarar o que falta confirmar.
5. Farm/progressão: consultar aquisição, pré-requisitos e disponibilidade; explicitar
   hipóteses de tempo e restrições do usuário.
6. Economia: manter bases de preço, reservas, probabilidade e ausência de dupla contagem.
7. Não embutir listas de meta ou números voláteis; usar dados sincronizados.
8. Executar conjunto de perguntas antes/depois das skills, em clientes distintos.
9. Documentar instalação manual e compatibilidade; MCP prompts complementam, mas
   não implicam que todas as aplicações aceitem o mesmo formato de skill.

Aceite: respostas apoiadas em dados e incerteza correta; nenhuma resposta certifica
uma build inválida ou um dado ausente. Relatório de avaliação revisado por humano.
Testes: LLM01–LLM06; seguir skill de criação aplicável durante a implementação.

### F10 — Homologação, distribuição e retirada da dependência

Dependências: F1–F9 para escopo integral. Responsáveis: QA/integração/release.

1. Rodar matriz completa, cenários de falha, desempenho e uso prolongado.
2. Distribuir App, MCP e SyncHost com versões compatíveis; testar instalação/update
   do coletor pelo fluxo exigido pelo Overwolf. Aprovação externa é gate de release.
3. Homologar máquina sem AlecaFrame, sem caches legados e com jogo/Overwolf reiniciados.
4. Validar preservação do cadastro MCP, permissões locais e backup/restore.
5. Testar atualização com leitor ativo e versão antiga; não substituir schema em uso
   sem estratégia explícita de compatibilidade ou reinício comunicado.
6. Preparar runbook de suporte, cobertura conhecida, desinstalação e exclusão de dados.
7. Atualizar README e arquitetura para comportamento entregue; arquivar decisões
   superadas e registrar changelog.

Aceite: todos os gates obrigatórios passam; conectores parciais são nomeados no
release. A entrega integral não é declarada enquanto Wiki/Overframe ou ordens
prometidas estiverem bloqueadas. Uma entrega reduzida exige escopo explícito.

## 8. Contrato MCP proposto

Agrupar capacidades para evitar dezenas de chamadas exploratórias:

| Grupo | Ferramentas candidatas | Dados/dependências |
| --- | --- | --- |
| Descoberta | `get_capabilities`, `get_sync_status`, `get_overview` | Cobertura, revisões, fontes e estado |
| Catálogo | `search_items`, `get_item`, `get_items` | Identidade, categorias, definições e lotes |
| Jogador | `search_inventory`, `list_owned_mods`, `get_equipment`, `get_loadout` | Instâncias, configs e ranks observados |
| Contexto | `get_current_context`, `get_inventory_changes` | Item visualizado e diferenças com intervalo |
| Aquisição | `get_acquisition_sources`, `get_activity`, `list_current_bounties` | Pré-requisitos, drops e World State |
| Referências | `search_references`, `get_reference_section`, `search_community_builds`, `get_community_build` | Wiki/Overframe sincronizados |
| Verificação | `compare_build_requirements`, `calculate_mod_capacity` | Operações determinísticas com cobertura explícita |
| Economia | Ferramentas atuais de coleção, farm, venda, relíquias e excedentes | Compatibilidade das regras existentes |

Nomes finais definidos em F8. Não expor execução de SQL nem uma ferramenta “melhor
build” que esconda critérios. Cálculos retornam entradas, resultado e versão das
regras; falta de polaridade/rank retorna resultado parcial, não capacidade inventada.

Envelope novo conceitual (não substituir DTOs v1 sem compatibilidade):

```json
{
  "meta": {
    "schemaVersion": "1",
    "snapshotId": "opaque",
    "servedAt": "2026-09-12T15:00:00Z",
    "expiresAt": "2026-09-12T15:15:00Z"
  },
  "data": null,
  "coverage": {"inventory": "notObserved"},
  "sources": [],
  "warnings": [],
  "problem": {
    "code": "SOURCE_UNAVAILABLE",
    "retryable": true,
    "action": "wait_for_inventory_event"
  }
}
```

Sucesso vazio contém coleção vazia e `problem: null`. Erros de execução usam também
`isError: true` no resultado MCP; testar união de schemas e clientes. Erros de
protocolo continuam distintos. `retryable` significa repetir a mesma chamada;
snapshot expirado pede nova análise, não retry automático no mesmo ID.

Proposta de lease: 15 min de inatividade, máximo absoluto de 60 min; limite de
revisões/bytes medido em F8. Renovação nunca troca conteúdo. Não usar WAL como
retenção de snapshots. Disponibilidade de inventário não depende de frescor de preços.

## 9. Dependências, entregas e acompanhamento

| Entrega | Conteúdo | Gate |
| --- | --- | --- |
| E0 — Viabilidade | F0/F1 | Captura GEP e distribuição viáveis |
| E1 — Dados locais | F2/F3/F4 | SQLite, catálogo e inventário visíveis com sync status |
| E2 — Independência | F5/F6/F8 básico | Instalação limpa e MCP sem AlecaFrame |
| E3 — Pesquisa assistida | F7/F8 completo/F9 | Referências e skills avaliadas |
| E4 — Release | F10 | Matriz, desempenho, migração e documentação aprovados |

F3, F5 e F7 admitem trabalho independente da captura após seus contratos mínimos;
não remover o leitor legado antes de E1 validado. Previsão de calendário só depois
do spike, pois acesso GEP e interfaces de Wiki/Overframe são dependências externas.
Cada fase deve ser dividida em mudanças revisáveis: contrato/fixture, implementação,
integração, testes e documentação. Evitar uma migração integral em um único PR.

Papéis: integração captura e distribuição; dados cuida de fontes; storage/Core de
consistência; UI/MCP de acesso; QA valida comportamento e respostas. Um desenvolvedor
pode assumir vários papéis; a evidência de aceite continua necessária.

## 10. Riscos e critérios de decisão

| Risco | Resposta e condição de avanço |
| --- | --- |
| GEP indisponível/restrito | Provar acesso em F1; registrar dependência externa; não prometer substituição pronta |
| Campos privados ausentes | Capability parcial por campo; não inventar loadouts completos |
| Reinício/troca de usuário | Novas sessões/contextos; nunca reutilizar inventário de outra conta |
| Catálogo incompatível com inventário | Identificadores sem match preservados e contabilizados |
| API de Wiki/Overframe não confirmada | Spike de acesso; importação permitida como opção parcial |
| Guia antigo ou incorreto | Revisão, autoria e conflitos visíveis; skill verifica mecânicas |
| Crescimento de banco/WAL | Retenção, leituras curtas, checkpoints e carga medida |
| Publicação entre dois bancos falha | Revisões imutáveis e validação da visão composta |
| Fonte muda schema | Quarentena da revisão, último válido e diagnóstico acionável |
| Token WFM ainda depende do Aleca | Resolver F6 antes de declarar independência integral |
| LLM ignora erro ou cobertura | Contrato explícito, exemplos e testes adversariais |

O plano substitui as propostas anteriores de “Inventory v2” obrigatório, motor de
melhor build e manifesto JSON como próximo marco. Mantém seus objetivos úteis:
dados ricos, rastreabilidade e consistência; a implementação seguirá os gates acima.
