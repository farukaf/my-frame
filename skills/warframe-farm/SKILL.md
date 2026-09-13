---
name: warframe-farm
version: 1
description: Montar planos de farm e progressão rastreáveis por aquisição, atividade e inventário.
---

# Farm and progression

## Pré-condições

- Execute o contrato comum.
- Fixe `snapshotId`, horário e plataforma.
- Para atividades, use apenas bounties cuja ativação/expiração cubra o horário da consulta.

## Procedimento

1. Normalize o objetivo para um `itemId` técnico e consulte posse/quantidade.
2. Liste pré-requisitos e componentes faltantes; diferencie tipo desconhecido de quantidade desconhecida.
3. Consulte `get_bounties` e verifique `state` antes de usar a lista. Só use
   bounties quando `state=available` e a ativação/expiração cobrir o horário;
   `not_initialized`/`failed` exige sincronização ou confirmação externa.
4. Relacione cada recompensa a sua fonte, tier, chance, quantidade e condição.
   `chance` não é garantia nem taxa de tokens por hora.
5. Compare alternativas por restrições do usuário (solo, tempo, MR, equipamento,
   rotação), sem converter chance em tokens/hora.
6. Para Mother Tokens, só use quantidade explicitamente atribuída a uma fonte. Se ausente, diga que a taxa/quantidade precisa ser confirmada no jogo ou em tabela permitida.

## Saída

Entregue passos ordenados, pré-requisitos, dados observados, lacunas, validade da atividade e como atualizar. Nunca trate bounty vencida como atual.
