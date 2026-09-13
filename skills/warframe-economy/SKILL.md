---
name: warframe-economy
version: 1
description: Avaliar preços, reservas, venda e custo de completar sem dupla contagem.
---

# Economy

## Pré-condições

- Execute o contrato comum e registre idade/fonte de cada preço.
- `market.private` é opcional; ausência de conta não significa ausência de ordens.
- Preço ausente, stale ou parcial permanece `null`/incerto.

## Procedimento

1. Consulte inventário e coleção no mesmo snapshot.
2. Separe reserva para construção, coleção, ordens existentes e venda futura.
3. Use `list_sales`, `list_surplus` e `list_farm` como projeções diferentes; nunca some seus totais.
4. Informe unidade (platinum, ducats, quantidade) e data de atualização.
5. Calcule custo de completar somente com preços conhecidos; reporte cobertura e componentes sem cotação.
6. Cite o motivo determinístico (`reasonCode`) e não substitua dados ausentes por zero.

## Saída

Mostre decisão, quantidade livre, reserva, evidências, preço/idade, incerteza e ação reversível. Não crie ordem, não autentique e não envie dados ao mercado.
