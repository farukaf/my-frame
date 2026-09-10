# Arquitetura

## Componentes atuais

```text
lastData.dat ─────> AlecaFrameReader ─────┐
cachedData\*.json -> AlecaCatalogReader ──┼-> RecommendationEngine
WFMarketToken.tk -> WarframeMarketClient ─┘          │
Warframe.Market API <───────────────┘                v
cache público de preços <──────────────── DashboardService
                                                     v
                                      DashboardViewModel -> MAUI
```

- `MyFrame.Core` contém leitores, integração, modelos e regras.
- `MyFrame.App` contém composição de DI, configurações MAUI, view model e XAML.
- `MyFrame.Core.Tests` valida o Core somente com dados sintéticos.

O `DashboardService` lê o inventário, carrega o catálogo, recupera caches, tenta atualizar
conta/ordens/preços, executa as regras e publica um `DashboardSnapshot` atômico. Um watcher
com debounce observa snapshot, token e catálogos.

## Fontes locais

### `lastData.dat`

O formato observado é AES-CBC/PKCS7:

- chave UTF-8: `LEO-ALEC\tEO-ALEC`;
- IV: `49,50,70,71,66,51,54,45,76,69,51,45,113,61,57,0`;
- resultado: JSON;
- formato atual: coleções no objeto raiz;
- formato antigo: JSON interno na string `InventoryJson`.

O arquivo é aberto somente para leitura com compartilhamento. Falhas transitórias são
repetidas e apenas snapshots completos são publicados.

### Catálogos

Os JSONs em `cachedData` relacionam identificador, nome, categoria, componentes,
quantidade, ducats, tradable, relíquias, raridade, chance, vaulted e identidade do
Warframe.Market. O parser aceita campos desconhecidos/opcionais e isola as variantes no
`AlecaCatalogReader`.

### Warframe.Market

`WFMarketToken.tk` é um JWT em texto, não uma base local de preços. Ele é lido somente em
memória e nunca copiado, persistido ou registrado.

O cliente implementado oferece apenas leituras:

- `GET /v2/me`;
- `GET /v2/orders/my`;
- consulta de catálogo de itens;
- consulta das melhores ordens públicas por slug.

Bearer segue apenas para endpoints autenticados. O contrato não oferece operações de
criação, alteração ou exclusão de anúncios.

## Cache e falhas

O cache próprio guarda cotações públicas e estado de mercado sem o token. Após 15 minutos,
uma cotação é marcada como antiga. Inventário e catálogos continuam úteis sem internet.

Falhas previstas:

- pasta ausente: orientar o usuário a iniciar AlecaFrame/Warframe ou configurar a pasta;
- escrita parcial: repetir e manter o último snapshot válido;
- token ausente/expirado: omitir dados autenticados sem derrubar o restante;
- API offline: usar cache com aviso;
- mudança de catálogo: ignorar entrada defeituosa quando seguro e registrar diagnóstico
  sem payload privado.

## Arquitetura alvo do MCP

```text
Codex / Claude ──stdio──> MyFrame.Mcp ──> MyFrameReadModel
                                              │
MyFrame.App ──────────────────────────────────┤
                                              v
                                  SnapshotProvider (Core)
                                   ├── leitores AlecaFrame
                                   ├── caches read-only
                                   ├── settings compartilhados
                                   └── RecommendationEngine
```

`MyFrame.Mcp` é um console separado, iniciado sob demanda pelo cliente. Não há
listener de rede nem autenticação MCP. O processo expõe somente consultas e pode funcionar
sem a janela MAUI aberta.

Para garantir que “o que a IA vê” seja “o que a tela vê”, configuração, composição do
snapshot e projeções deixam de depender do projeto MAUI. O app e o servidor consomem a
mesma implementação de provedor no Core, em instâncias independentes. DTOs MCP próprios
estabilizam o contrato externo e impedem que campos internos, como caminhos locais,
vazem por serialização acidental.

O app migra settings e caches para a localização compartilhada, substitui cada arquivo
por escrita atômica e persiste validade/contexto das ordens. O MCP só lê;
seu container de DI não registra cliente de mercado, leitor de token ou stores escritores.
O caminho offline é o `MyFrameSnapshotProvider`, não `RefreshAsync(false)`.

Snapshots têm identidade, versões das fontes/settings/regras e instante de avaliação.
Cursores e consultas relacionadas podem fixar uma versão com retenção limitada. Watchers
observam todas as fontes, com reconciliação e expiração temporal; falhas de leitura não
equivalem a dados vazios. Troca de origem invalida estado e cursores do contexto anterior.

O Core/UI compartilha as correções de estimativas parciais, farm e distinção entre
excedente de coleção e disponibilidade para venda. O inventário v1 é agregado e
declara sua cobertura, sem inventar quantidades de equipamento perdidas pelo leitor atual.
Os testes usam expectativas independentes das regras e o mesmo provedor usado pela interface.

O desenho completo está em [MCP.md](MCP.md).
