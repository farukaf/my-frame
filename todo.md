# Ideias de Features não refinadas

- Criar ordem de venda/compra no warframe market e a partir dos eventos do overwolf remover a ordem automaticamente.
- Colocar na interface condições de remoção da ordem (tempo, inventario conter um dos itens (item da ordem, outro item feito com o item da ordem)



# Entrega — plataforma de dados e hardening do MCP

Plano novo, ainda não implementado: [plataforma de dados](docs/PLATAFORMA-DE-DADOS.md).
Evidências: [fontes](docs/FONTES-DE-DADOS.md) e
[testes/gates](docs/VALIDACAO-PLATAFORMA.md). Marcar conclusão apenas com evidência
do aceite; dependência externa bloqueada não equivale a conclusão.

## Próxima evolução — F0 a F10

- [x] F0: registrar baseline de build/testes e auditar todas as dependências AlecaFrame ([evidências](docs/validacoes/2026-09-12-f0.md)).
- [x] F0: reproduzir erro MCP interpretado como inventário vazio e criar regressão.
- [x] F0: registrar capacidades mínimas, decisões abertas e mapeamento das pendências; PR #6 com base em `feat/local-mcp`.
- [x] F1: implementar spike Native GEP, manifest 8954, probe estrutural e transporte marker/hash; testes sintéticos aprovados.
- [ ] F1: carregar extensão no Overwolf e provar captura real sem ler AlecaFrame ([roteiro](docs/validacoes/2026-09-13-f1.md)).
- [ ] F1: documentar schema/cobertura real, snapshots/deltas e requisitos de distribuição.
- [ ] F2: validar SQLite read-only/WAL, escritor único, backup e decisão de um/dois bancos.
- [x] F2: validar backup/reabertura e serialização do escritor em testes concorrentes.
- [ ] F2: implementar migrations, revisões, publicação, retenção e lifecycle do SyncHost.
- [ ] F2: entregar página inicial de status com tentativas, erros e ação corretiva.
- [ ] F3: sincronizar Public Export e enriquecimentos atribuídos, sem catálogo AlecaFrame.
- [ ] F3: normalizar identidade PT/EN, categorias, receitas e definições técnicas confirmadas.
- [x] F3: criar parser seguro do índice/documento Public Export e contrato de decoder LZMA.
- [x] F3: integrar decoder LZMA-Alone com limite de saída e teste HTTP → parser.
- [x] F3: adaptar o índice real `arquivo.json!00_<tag>` e registrar a evidência de 16 entradas.
- [x] F3: publicar registros Public Export normalizados junto da revisão SQLite em transação única.
- [x] F4: contrato de envelope/projeção de inventário com cobertura e instâncias desconhecidas.
- [x] F4: persistir inventário rico e campos desconhecidos na revisão SQLite.
- [x] F5: parser/client de World State com validade, bounties, ciclos e recompensas atribuídas.
- [x] F5: publicar World State em revisão SQLite independente.
- [x] F6: separar cliente de mercado do caminho AlecaFrame e usar armazenamento My Frame.
- [x] F7: contrato de referências atribuídas, busca e isolamento de conteúdo não confiável.
- [x] F8: expor capacidades e status de sincronização somente leitura no MCP.
- [ ] F4: preservar instâncias, mods/ranks/configs comprovados e cobertura por campo.
- [ ] F4: validar contextos, deltas e conferência manual com o jogo.
- [ ] F5: integrar World State, drops, aquisição, bounties e mecânicas com revisão.
- [ ] F6: migrar settings/caches com rollback e resolver autenticação WFM independente.
- [ ] F6: comprovar instalação limpa sem AlecaFrame nem caches legados.
- [ ] F7: comprovar acesso permitido, licença e ingestão Wiki e Overframe separadamente.
- [ ] F7: oferecer referências/builds offline com autoria, revisão e conteúdo não confiável isolado.
- [ ] F8: preservar tools atuais e adicionar consultas de domínio, capacidades e lotes.
- [ ] F8: validar erros, snapshots, limites, paridade UI e clientes reais sem escrita/rede.
- [ ] F9: criar skills de builds, farm/progressão e economia baseadas nos dados disponíveis.
- [ ] F9: executar avaliação antes/depois, com fontes, incerteza e zero falhas críticas.
- [ ] F10: executar desempenho, segurança, falhas, instalação, upgrade e restore.
- [ ] F10: cumprir distribuição Overwolf e publicar runbook/cobertura/documentação atualizados.

## Baseline e pendências do MCP atual

O checklist abaixo é histórico de implementação do [MCP v1](docs/MCP.md).
Marcações existentes não certificam a plataforma futura. Pendências foram mapeadas
para a nova matriz e continuam abertas até validação.

## Base compartilhada

- [x] Fechar cobertura do inventário, quantidade desconhecida e identidade por tipo de entidade.
- [x] Corrigir estimativas parciais, relíquias e comparação set/peças sem tratar preço ausente como zero.
- [x] Corrigir farm por peças faltantes, unidades/tipos e custo de completar versus preço do set.
- [x] Distinguir excedente de coleção, reservas e disponibilidade; incluir reasonCode e evidências.
- [x] Persistir validade/idade/contexto das ordens e aplicar política comum na UI e no MCP.
- [x] Criar um provedor de snapshot read-only reutilizável pela interface e pelo MCP.
- [x] Centralizar pasta do AlecaFrame e preferências de recomendação em configuração comum.
- [x] Migrar Preferences e caches pelo app, de forma idempotente e recuperável, preservando origens.
- [x] Versionar settings/regras/contrato e publicar cada arquivo por substituição atômica.
- [ ] Garantir gerações consistentes de mercado: proposta de manifesto JSON substituída por SQLite em F2 (DB02–DB06); objetivo ainda pendente.
- [x] Separar interfaces leitoras/escritoras e remover dependência de rede/token da composição MCP.
- [x] Ler todas as cotações locais relevantes em lote, separando o orçamento online de 100 slugs.
- [x] Definir DTOs MCP versionados, sem propriedades de apresentação nem caminhos locais.
- [x] Garantir leitura concorrente segura dos caches quando app e MCP estiverem ativos.

## Servidor

- [x] Adicionar `MyFrame.Mcp` à solução como console `net10.0` referenciando o Core.
- [x] Adicionar o SDK oficial C# do MCP e configurar transporte `stdio`.
- [x] Enviar todo log para `stderr` e reservar `stdout` exclusivamente ao protocolo.
- [x] Publicar instruções do servidor e capacidades somente leitura.
- [x] Manter inicialização, descoberta, schema e overview disponíveis sem AlecaFrame/setup completo.
- [x] Definir inputSchema/outputSchema, structuredContent/texto equivalente e erros tipados.
- [x] Implementar resumo, busca do inventário e detalhe consolidado de item.
- [x] Implementar consultas paginadas de coleção, farm, vendas, relíquias e excedentes.
- [x] Vincular cursores a snapshot/filtros/ordenação e aceitar snapshotId entre ferramentas.
- [x] Limitar retenção de snapshots e sinalizar cursores/versões expirados explicitamente.
- [x] Calcular totais do conjunto filtrado e paginar coleções aninhadas, respeitando bytes de resposta.
- [x] Observar inventário, catálogo, settings e mercado; reconciliar eventos perdidos/pastas ausentes.
- [x] Reavaliar preços/ordens ao vencerem sem evento de arquivo, com relógio injetável.
- [x] Diferenciar fonte ausente, inválida e indisponível; manter fallback apenas do mesmo contexto.
- [x] Limitar fila/concorrência e compartilhar reconstruções, com timeout, cancelamento e EOF.
- [x] Sanitizar erros/stderr centralmente e omitir conta salvo solicitação explícita no overview.

## Instalação e experiência

- [x] Distribuir `MyFrame.Mcp.exe` junto com o aplicativo.
- [x] Criar fluxo de publicação com caminho estável e upgrade de app/MCP juntos.
- [ ] Validar migração, formato incompatível, rollback e atualização com servidor ativo.
- [x] Adicionar à tela de configurações uma seção MCP com comandos para Codex e Claude.
- [x] Oferecer botão para copiar cada comando e mostrar o caminho do executável.
- [x] Documentar cadastro, verificação, atualização e remoção do servidor.
- [x] Explicar cobertura, dados parciais, acesso opcional à conta e envio potencial ao provedor de IA.

## Validação

- [x] Testar projeções, paginação, cursores e limites com fixtures sintéticas.
- [ ] Testar paridade entre o snapshot usado pela interface e as respostas MCP.
- [x] Testar resultados esperados independentes: preço parcial, reservas, farm e quantidade desconhecida.
- [ ] Executar toda a matriz mínima de regressão de docs/MCP.md, incluindo troca de contexto.
- [x] Testar paginação durante atualização e assinatura/vínculo de cursores.
- [ ] Testar expiração temporal e descarte por limite de memória.
- [ ] Medir latência, memória e bytes com fixture grande, dois servidores e app ativo.
- [x] Testar que logs `stdio` não contêm autorização e que DTOs omitem token/caminhos.
- [ ] Demonstrar ausência de escrita e rede no processo MCP, inclusive setup e falhas.
- [ ] Testar app e MCP lendo os mesmos arquivos/cache ao mesmo tempo.
- [ ] Validar o protocolo com MCP Inspector.
- [ ] Executar smoke tests reais em Codex e Claude no Windows.
- [x] Executar restore, build, publicação e suíte completa antes da entrega.
- [x] F2: fundação SQLite com staging, publicação transacional, idempotência e status
- [x] F2: lifecycle mínimo do SyncHost e registro de falhas sem perder revisão ativa
- [ ] F2: conectar publicação SQLite ao fluxo real do coletor após homologação GEP F1
