---
name: warframe-farm
description: Montar planos de farm e progressão rastreáveis por aquisição, atividade e inventário.
metadata:
  version: "5"
---

# Farm and progression

## Pré-condições

- Execute o contrato comum.
- Fixe `snapshotId`, horário e plataforma.
- Consulte `get_capabilities` e `get_sync_status` antes dos dados; registre `activeRevisionId` e `parserVersion` da fonte World State.
- Se o plano usar posse ou peças faltantes, consulte `get_capture_inbox_status`;
  sem `state=ready`, `heartbeatFresh=true` e `validMarkers>0`, trate o
  inventário como `unverified` e não calcule déficit pessoal.
- Para atividades, use apenas bounties cuja ativação/expiração cubra o horário da consulta.

## Procedimento

1. Normalize o objetivo para um `itemId` técnico e consulte posse/quantidade.
2. Se a pergunta não depender de posse, consulte `get_public_export_item` pelo
   `itemId`, nome ou alias para obter receita e relíquias sem exigir captura
   Overwolf. Consulte `get_source_coverage("public-export")` antes de listar pré-requisitos;
   só use componentes e relíquias quando esses campos estiverem `Known`.
   Diferencie tipo desconhecido de quantidade desconhecida.
3. Para um item com `itemId` estável, prefira `get_acquisition` para consolidar componentes, relíquias e bounties na mesma resposta; registre `worldStateRevisionId` e `worldStateParserVersion` antes de atribuir as bounties. Quando precisar explorar atividades, consulte `get_world_state` ou `get_activity` (preferencialmente com `syndicate`, `reward` e `limit` quando procurar uma recompensa específica)
   e verifique `state`, `activeRevisionId` e `coverage` antes de usar a lista.
   Só use bounties quando `state=available` e a ativação/expiração cobrir o
   horário; `not_initialized`/`failed` exige sincronização ou confirmação
   externa. `get_bounties` continua como compatibilidade para somente bounties.
   Se `parserVersion=worldstate-community-1`, identifique a resposta como
   fallback comunitário; se `parserVersion=worldstate-1`, identifique-a como
   fixture/adaptador genérico; nenhuma das duas deve ser apresentada como
   confirmação oficial da DE. `worldstate-official-1` identifica o parser do
   endpoint oficial, mas ainda cite `activeRevisionId` e o horário servido.
4. Relacione cada recompensa a sua fonte, tier, chance, quantidade e condição.
   `chance` não é garantia nem taxa de tokens por hora.
5. Compare alternativas por restrições do usuário (solo, tempo, MR, equipamento,
   rotação), sem converter chance em tokens/hora.
6. Para Mother Tokens, consulte `coverage.motherTokens`: `Known` permite usar
   somente a recompensa explicitamente atribuída na revisão `activeRevisionId`;
   `NotObserved` exige dizer que quantidade e taxa precisam ser confirmadas no
   jogo ou em tabela permitida. Nunca derive tokens/hora de `chance`.
7. Se o objetivo envolver progresso desde a última captura, consulte
   `get_inventory_history` e `get_inventory_changes`; só trate adições,
   remoções ou alterações como completas quando as duas revisões forem
   `complete`, nunca quando a resposta estiver `partial`.

## Saída

Entregue passos ordenados, pré-requisitos, dados observados, lacunas, validade da atividade e como atualizar. Nunca trate bounty vencida como atual.
