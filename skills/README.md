# My Frame skills

Estas skills são procedimentos versionados para clientes LLM que usam o MCP do
My Frame. Elas não contêm meta fixa nem substituem raciocínio do modelo.

Contrato comum:

1. chamar `get_capabilities`;
2. chamar `get_sync_status`;
3. quando a pergunta depender do inventário, chamar `get_capture_inbox_status`;
4. chamar `get_overview` e guardar `snapshotId`;
5. tratar `isError`, `problem`, `coverage`, `sources` e frescor antes de usar os dados;
6. repetir `snapshotId` e seguir todos os `nextCursor` necessários;
7. separar fatos observados, cálculos determinísticos, referência comunitária e hipótese;
8. citar fonte/revisão e declarar o que falta confirmar.

Se `get_capture_inbox_status.state` não for `ready`, ou se `heartbeatFresh` for
falso/`validMarkers` for zero, o inventário não deve ser descrito como captura
atual: use `unverified` e peça sincronização/confirmação no jogo.

Para decisões de aquisição/build, `get_source_coverage("public-export")` é a
verificação de cobertura do catálogo: `NotObserved` para componentes, relíquias,
categoria ou identidade de mercado impede uma conclusão definitiva.
Quando houver um `itemId` estável, `get_acquisition` consolida componentes,
relíquias e bounties ativas; seu `state` e as revisões ainda precisam ser
verificados antes de recomendar uma atividade.

Skills disponíveis: `warframe-builds`, `warframe-farm` (v4, World State com
revisão/cobertura/procedência), `warframe-economy` (v2, mercado com procedência)
e `warframe-research` (v1, referências importadas com atribuição).
