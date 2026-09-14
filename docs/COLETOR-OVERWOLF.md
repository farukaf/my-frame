# Contrato atual do coletor Overwolf

Este documento descreve o contrato implementado pelo spike Native GEP. Ele não é
uma afirmação de que o GEP real já forneceu todos os campos: a coluna “evidência”
separa testes sintéticos de homologação com Warframe em execução.

| Campo/evento | Contrato implementado | Evidência | Estado para recomendações |
| --- | --- | --- | --- |
| `gameId=8954` | Manifesto e envelope exigem Warframe | testes do coletor/probe | conhecido |
| `game_info.username` | Identidade é observada em memória e nunca gravada no payload | testes sintéticos | não expor sem política de conta |
| `match_info.inventory` | Evento é convertido em envelope snapshot/delta e hash | F98, F163–F165 | `unverified` até captura real |
| `highlighted` | Estrutura é inspecionada sem armazenar valores | testes do probe | `notObserved` para fatos |
| `gep_internal`/`game_info`/`match_info` | Features solicitadas sem chat | F0/F1 e testes do manifesto | aguardando callback real |
| `chat` | Não assinado e descartado | testes de rejeição | não suportado |
| rank/config/mods | Campos aceitos no envelope quando presentes | parser sintético | `NotObserved` até comparar Arsenal |
| upgrades/RawUpgrades | Relação atribuída por `ownerInstanceId` quando presente | testes Core/MCP | cobertura depende da captura |

| diagnóstico de callbacks GEP | Heartbeat preserva estado sanitizado, features suportadas, contagem por feature e último evento | F284 (sintético); callback real ainda pendente | evidência de transporte, não de completude |

## Envelope e retenção

O transporte local usa `schemaVersion`, `sessionId`, `eventId`, `sequence`,
`captureMode`, `completeness`, `contentHash` e raw privado com retenção curta. O
marker `.ready.json` contém apenas nome, tamanho e SHA-256; não contém inventário,
username ou token. A importação exige consentimento explícito e é idempotente.

## Gate que ainda falta

Para promover qualquer campo a fato observado, executar a extensão no Overwolf
com Warframe aberto, capturar pelo menos um snapshot, comparar com o Arsenal e
registrar `OVERWOLF_COLLECTOR_READY=1`, heartbeat válido e marker aceito. Até lá,
o MCP deve preservar `NotObserved`/`unverified` e não calcular builds completas,
polaridades, shards, Helminth ou Incarnon.
