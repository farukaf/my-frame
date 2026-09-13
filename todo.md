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
- [x] F9: criar skills de builds, farm/progressão e economia baseadas nos dados disponíveis ([skills](skills/README.md), [evidências](docs/validacoes/2026-09-13-f9.md)).
- [x] F10: definir casos e protocolo reproduzível de avaliação antes/depois ([fixtures](docs/avaliacao/f10-cases.json), [protocolo](docs/avaliacao/2026-09-13-f10.md)).
- [x] F11: exibir status de sincronização SQLite em página somente leitura ([evidências](docs/validacoes/2026-09-13-f11.md)).
- [x] F12: preferir snapshot SQLite no MCP/app com fallback legado e cobertura parcial ([evidências](docs/validacoes/2026-09-13-f12.md)).
- [x] F13: preservar raw JSON e projetar campos ricos do Public Export ([evidências](docs/validacoes/2026-09-13-f13.md)).
- [x] F14: importar captura Overwolf validada no SQLite com consentimento/idempotência ([evidências](docs/validacoes/2026-09-13-f14.md)).
- [x] F15: expor importação validada como comando operacional do probe ([evidências](docs/validacoes/2026-09-13-f15.md)).
- [x] F16: validar pacote Overwolf, arquivos do manifesto e ícone antes da homologação ([evidências](docs/validacoes/2026-09-13-f16.md)).
- [x] F17: criar inbox Core para importar capturas validadas em lote, com consentimento e idempotência ([evidências](docs/validacoes/2026-09-13-f17.md)).
- [x] F18: integrar importação da inbox à página de status do aplicativo, com consentimento explícito ([evidências](docs/validacoes/2026-09-13-f18.md)).
- [x] F19: persistir falhas/rejeições da inbox como tentativas de sincronização no SQLite ([evidências](docs/validacoes/2026-09-13-f19.md)).
- [x] F20: expor metadados da inbox e último erro no MCP, sem payload nem escrita ([evidências](docs/validacoes/2026-09-13-f20.md)).
- [x] F21: detectar novas capturas na inbox e avisar a UI sem importar automaticamente ([evidências](docs/validacoes/2026-09-13-f21.md)).
- [x] F22: implementar restore SQLite a partir de backup validado e testar reabertura ([evidências](docs/validacoes/2026-09-13-f22.md)).
- [x] F23: exibir histórico recente de tentativas e falhas na página de sincronização ([evidências](docs/validacoes/2026-09-13-f23.md)).
- [x] F24: expor histórico sanitizado de sincronização no MCP, com filtro e limite ([evidências](docs/validacoes/2026-09-13-f24.md)).
- [x] F25: executar o gate estrutural da matriz F10 e registrar a ausência do baseline real ([evidências](docs/validacoes/2026-09-13-f25.md)).
- [x] F26: gerar e validar artefato Windows App/MCP + pacote Overwolf ([evidências](docs/validacoes/2026-09-13-f26.md)).
- [x] F27: executar smoke do App/MCP distribuídos com data root temporário ([evidências](docs/validacoes/2026-09-13-f27.md)).
- [x] F28: automatizar gate MCP com stdin EOF, data root limpo e stdout vazio ([evidências](docs/validacoes/2026-09-13-f28.md)).
- [x] F29: persistir e consultar cobertura por campo do inventário Overwolf ([evidências](docs/validacoes/2026-09-13-f29.md)).
- [x] F30: expor cobertura por campo do inventário no MCP sem transformar ausência em zero ([evidências](docs/validacoes/2026-09-13-f30.md)).
- [x] F31: validar fluxo sintético coletor → inbox → SQLite → leitor/MCP stdio ([evidências](docs/validacoes/2026-09-13-f31.md)).
- [x] F32: testar upgrade de banco legado com migração compatível sem perda de schema ([evidências](docs/validacoes/2026-09-13-f32.md)).
- [x] F33: expor equipamentos instanciados, rank/config e cobertura no MCP ([evidências](docs/validacoes/2026-09-13-f33.md)).
- [x] F34: preservar upgrades/mods atribuídos sem inferir semântica ([evidências](docs/validacoes/2026-09-13-f34.md)).
- [x] F35: consultar mods/upgrades observados no MCP por instância ([evidências](docs/validacoes/2026-09-13-f35.md)).
- [x] F36: combinar equipamento, configuração e upgrades em loadout por instância ([evidências](docs/validacoes/2026-09-13-f36.md)).
- [x] F37: podar revisões retidas por fonte sem remover a revisão ativa ([evidências](docs/validacoes/2026-09-13-f37.md)).
- [x] F38: rejeitar schema SQLite futuro sem resetar dados ([evidências](docs/validacoes/2026-09-13-f38.md)).
- [x] F39: conectar retenção à fronteira de manutenção do SyncHost ([evidências](docs/validacoes/2026-09-13-f39.md)).
- [x] F40: verificar schema futuro antes de qualquer mutação SQLite ([evidências](docs/validacoes/2026-09-13-f40.md)).
- [x] F41: ligar cliente Public Export ao SyncHost e publicar registros normalizados ([evidências](docs/validacoes/2026-09-13-f41.md)).
- [x] F42: ligar cliente World State ao SyncHost e publicar revisão validada ([evidências](docs/validacoes/2026-09-13-f42.md)).
- [x] F43: resolver índice Public Export antes de baixar e publicar documento ([evidências](docs/validacoes/2026-09-13-f43.md)).
- [x] F44: preservar jobs e recompensas na leitura SQLite do World State ([evidências](docs/validacoes/2026-09-13-f44.md)).
- [x] F45: expor bounties ativas e recompensas no MCP ([evidências](docs/validacoes/2026-09-13-f45.md)).
- [x] F46: retornar estado/última tentativa junto de bounties no MCP ([evidências](docs/validacoes/2026-09-13-f46.md)).
- [x] F47: alinhar skill de farm ao contrato `get_bounties` e estados World State ([evidências](docs/validacoes/2026-09-13-f47.md)).
- [x] F48: alinhar skill de builds às consultas `get_loadout`/`get_mods` ([evidências](docs/validacoes/2026-09-13-f48.md)).
- [x] F49: validar paridade da projeção World State entre SQLite e MCP ([evidências](docs/validacoes/2026-09-13-f49.md)).
- [x] F50: atualizar contrato docs/MCP.md para as 17 ferramentas implementadas ([evidências](docs/validacoes/2026-09-13-f50.md)).
- [x] F51: automatizar regressão de ferramentas MCP documentadas ([evidências](docs/validacoes/2026-09-13-f51.md)).
- [x] F52: expor bounties e ciclos em uma consulta World State no MCP ([evidências](docs/validacoes/2026-09-13-f52.md)).
- [ ] F10: executar avaliação antes/depois, com fontes, incerteza e zero falhas críticas.
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
