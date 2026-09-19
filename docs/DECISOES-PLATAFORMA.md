# Registro de decisões — aberto na F0

Data: 12/09/2026. Estado de cada decisão é independente do estado da implementação.
Escopo e fontes: [plano](PLATAFORMA-DE-DADOS.md), [pesquisa](FONTES-DE-DADOS.md).

## Decisões aceitas de escopo

- Dados, procedência e cálculos verificáveis pertencem à aplicação; recomendação
  contextual e planejamento pertencem à LLM. Não criar motor universal de melhor build.
- Substituir dependência obrigatória AlecaFrame, incluindo catálogo e autenticação.
- MCP somente leitura e cache-first, sem rede/migration/SQL arbitrário.
- Chat e automação de gameplay fora da primeira entrega.
- Uma fase por PR; primeira fase baseada em `feat/local-mcp`, demais na fase anterior.
  A documentação previamente produzida entra na F0, sem um PR de fase artificial.
- Não publicar dados pessoais nas evidências ou fixtures.

## Decisões técnicas a fechar por evidência

| ID | Questão e alternativas | Responsável/fase | Evidência exigida / bloqueia |
| --- | --- | --- | --- |
| ADR-001 | Coletor Native ou Electron; canal de distribuição Overwolf. | Integração, F1 | Callback real, requisitos de acesso/aprovação, instalação e lifecycle. Bloqueia captura/release; exemplo oficial não é homologação. |
| ADR-002 | Transporte bridge/.NET: inbox atômica ou IPC local suportado. | Integração, F1/F2 | Capacidade real do runtime, framing, tamanho, identidade de remetente, ACL, ordem, confirmação e recuperação. Não abrir endpoint remoto. |
| ADR-003 | Dois SQLite ou um banco com separação lógica. | Storage, F2 | DB04–DB07: leitor read-only offline, WAL, revisão composta, retenção e backup. Não presumir transação atômica entre dois bancos. |
| ADR-004 | Lifecycle: SyncHost independente e quem o inicia/encerra. | App/storage, F2 | Escritor único, UI fechada, shutdown/cancelamento, leitor funcionando sem host. |
| ADR-005 | Public Export direto e enriquecimentos WFCD por campo. | Dados, F3/F5 | Inventário de campos, endpoints/revisões válidos, licença, aliases e conflitos. Não tratar JSON comunitário inteiro como dado oficial. |
| ADR-006 | Login WFM sem token do AlecaFrame. | Integração, F6 | Fluxo suportado, expiração/revogação, armazenamento de segredo e MIG04. Sem isso, ordens pessoais permanecem parciais. |
| ADR-007 | Wiki: API/dataset permitido ou importação limitada. | Dados, F7 | Acesso permitido, licença/atribuição e uma ingestão offline por revisão. Bloqueio de acesso não deve ser contornado. |
| ADR-008 | Overframe: acesso permitido e schema de builds. | Dados, F7 | Identidade, slots/ranks, autor/data e limites de reutilização demonstrados. Não assumir API pública. |
| ADR-009 | Envelope MCP compatível, leases e orçamento total. | MCP, F8 | Clientes reais, isError/structuredContent, revisão fixa sem txn longa, desempenho medido; DTOs atuais mantidos até lá. |

Ao fechar uma decisão, acrescentar alternativas rejeitadas e motivo, teste/relatório,
versões e impactos de migração. Não apenas trocar o estado para “aceita”.

## Capacidades mínimas a comprovar

| Pergunta do usuário | Dados necessários | Gate |
| --- | --- | --- |
| O que tenho e quais fontes faltam? | Identidade, posse/cobertura, revisões e status por fonte. | E1 |
| Qual build posso montar? | Instâncias/mods/ranks suportados, requisitos do equipamento, capacidade/polaridades conhecidas e limitações. | E1/E3; ausência de campo impede certificar equipabilidade. |
| Como farmar Mother Tokens? | Arsenal observado, atividade ativa, recompensas/bonificação e mecânicas atribuídas. | E2/E3; tokens/hora não inferidos só de drops. |
| Como adaptar esta build? | Referência permitida, slots/ranks, posse e verificação de requisitos. | E3 |
| O que falta para meu objetivo? | Receitas, aquisição, posse e pré-requisitos; progresso pessoal pode ser desconhecido. | E2 |
| O que vale vender? | Preços/idade, reservas e cobertura de ordens pessoais. | E2 e regressões econômicas. |

## Organização das stacked PRs

```text
feat/local-mcp (base solicitada, existente)
  -> feat/data-platform-f0 (baseline, auditoria, regressões e planejamento)
    -> feat/data-platform-f1 (captura Overwolf)
      -> ... uma branch/PR por fase até F10
```

Usar `gh stack`, extensão oficial instalada, para tracking e submissão. Com somente
F0 há uma primeira camada/PR; o agrupamento remoto de múltiplos PRs poderá ser criado
quando F1 tiver conteúdo real. Não incluir o PR preexistente da base como nova fase,
não abrir PR vazio e não alterar a base para `main`.

Submeter fase com relatório de aceite. Aprovação, merge e aprovação externa de
distribuição são estados separados; criar PR não autoriza fazer merge automaticamente.
Antes de restack/rebase, preservar alterações locais e verificar mudanças remotas.

Referências oficiais consultadas em 12/09/2026:
[sobre stacks](https://docs.github.com/en/pull-requests/get-started/about-stacked-prs),
[comandos](https://docs.github.com/en/pull-requests/reference/stacked-prs-cli-commands),
[guia](https://docs.github.com/en/pull-requests/how-tos/stacked-pull-requests).
