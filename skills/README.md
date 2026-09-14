# My Frame skills

Estas skills são procedimentos versionados para clientes LLM que usam o MCP do
My Frame. Elas não contêm meta fixa nem substituem raciocínio do modelo.

Contrato comum:

1. chamar `get_capabilities`;
2. chamar `get_sync_status`;
3. chamar `get_overview` e guardar `snapshotId`;
4. tratar `isError`, `problem`, `coverage`, `sources` e frescor antes de usar os dados;
5. repetir `snapshotId` e seguir todos os `nextCursor` necessários;
6. separar fatos observados, cálculos determinísticos, referência comunitária e hipótese;
7. citar fonte/revisão e declarar o que falta confirmar.

Para decisões de aquisição/build, `get_source_coverage("public-export")` é a
verificação de cobertura do catálogo: `NotObserved` para componentes, relíquias,
categoria ou identidade de mercado impede uma conclusão definitiva.

Skills disponíveis: `warframe-builds`, `warframe-farm` (v3, World State com
revisão/cobertura/procedência), `warframe-economy` (v2, mercado com procedência)
e `warframe-research` (v1, referências importadas com atribuição).
