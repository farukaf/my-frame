# Arquitetura e engenharia reversa

## Fluxo

```text
lastData.dat ─────> AlecaFrameReader ─────┐
cachedData\*.json -> AlecaCatalogReader ──┼-> RecommendationEngine
WFMarketToken.tk -> WarframeMarketClient ─┘          │
Warframe.Market API <───────────────┘                v
cache público de preços <──────────────── DashboardService
                                                     v
                                      DashboardViewModel -> MAUI
```

`MyFrame.Core` mantém o domínio, contratos, regras, sincronização, repositórios de arquivos/cache e integração HTTP. A organização física é:

```text
MyFrame.Core/
  Models/                         snapshots, recomendações e eventos de mudança
  Abstractions/                   contratos públicos (um por arquivo)
  Rules/                          RecommendationEngine e alinhamento inventário/catálogo
  Services/                       DashboardService e monitor de alterações
  Repositories/AlecaFrame/        lastData.dat, catálogo e diretório atual
  Repositories/Market/            cache de preços e estado de ordens
  Integrations/WarframeMarket/    cliente HTTP e autenticação JWT
```

Os tipos continuam no namespace `MyFrame.Core` para manter compatibilidade entre as camadas.
`MyFrame.App` contém a composição de DI, páginas, componentes, view models e adaptadores MAUI
(preferências, seletor de pasta e launcher). `MyFrame.Core.Tests` valida regras, persistência,
sincronização e monitoramento com dados sintéticos.

## `lastData.dat`

O formato observado é AES-CBC/PKCS7:

- chave UTF-8: `LEO-ALEC\tEO-ALEC`;
- IV: `49,50,70,71,66,51,54,45,76,69,51,45,113,61,57,0`;
- resultado: JSON;
- formato atual: coleções no objeto raiz;
- formato antigo: JSON interno na string `InventoryJson`.

O arquivo deve ser aberto somente para leitura com compartilhamento, repetindo falhas transitórias e publicando apenas snapshots completos.

## Catálogos

Os JSONs em `cachedData` relacionam identificador, nome, categoria, componentes, quantidade, ducats, tradable, relíquias, raridade, chance, vaulted e `warframeMarket { id, urlName }`. O parser deve aceitar campos desconhecidos/opcionais e isolar variantes no `AlecaCatalogReader`.

## Token e API

`WFMarketToken.tk` é JWT em texto, não uma base local de preços. Ele só é lido em memória e nunca copiado, persistido ou registrado.

Endpoints planejados:

- `GET /v2/me`;
- `GET /v2/orders/my`;
- `GET /v2/orders/item/{slug}/top`.

Bearer vai apenas para endpoints autenticados. O contrato do cliente não oferece operações de escrita.

## Atualização e cache

O `DashboardService` implementa `IDashboardService`: lê inventário, carrega catálogo, recupera cache,
tenta atualizar conta/ordens/preços, executa regras e publica um `DashboardSnapshot` atômico. O
`FileSystemAlecaFrameChangeMonitor` implementa `IAlecaFrameChangeMonitor`, encapsulando o
`FileSystemWatcher`, os filtros de `lastData.dat`, `WFMarketToken.tk` e JSONs, recriação após erro e
debounce de 750 ms. Alterações de catálogo invalidam o catálogo carregado antes do próximo refresh.

O cache próprio guarda somente slug, menor venda, maior compra e instante da consulta. Após 15 minutos, a cotação é marcada como antiga. Inventário, perfil e ordens não são persistidos nesse cache.

## Falhas previstas

- Pasta ausente: orientar o usuário a iniciar AlecaFrame/Warframe.
- Escrita parcial: repetir e manter o último snapshot válido.
- Token ausente/expirado: desabilitar dados autenticados sem derrubar o restante.
- API offline: usar cache com aviso.
- Mudança de catálogo: ignorar entrada defeituosa quando seguro e manter diagnóstico sem payload privado.

## Contratos e composição

O Core não acessa APIs MAUI. `IDashboardService` e `IAlecaFrameChangeMonitor` são registrados como
singletons pela App; o dashboard recebe o monitor por injeção e o descarta junto com o serviço.
Preferências, seleção de pasta e abertura de links devem ser adaptadas atrás de contratos da App/Core
para que os view models permaneçam testáveis.
