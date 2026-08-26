# Plano de desenvolvimento — My Frame

## Objetivo

Aplicativo Windows local para transformar os dados já mantidos pelo AlecaFrame em uma visão de inventário, coleção/maestria, plano de farm e recomendações de venda por platinum ou troca por ducats.

O My Frame será somente leitura: não altera o AlecaFrame, não automatiza o jogo e não cria, edita ou apaga ordens no Warframe.Market.

## Escopo da primeira versão

- .NET 10, .NET MAUI e Windows.
- Leitura de `%LOCALAPPDATA%\AlecaFrame\lastData.dat`.
- Catálogos em `%LOCALAPPDATA%\AlecaFrame\cachedData`.
- Token de `%LOCALAPPDATA%\AlecaFrame\WFMarketToken.tk` somente em memória.
- Consultas HTTP `GET` ao Warframe.Market.
- Cache próprio apenas de cotações públicas.
- Dashboard, coleção, farm, vendas/ducats e configurações.
- Atualização manual e automática por mudanças nos arquivos.
- Testes com dados sintéticos; nenhum dado privado no Git.

Ficam fora desta fase: escrita no mercado, modificação de arquivos do AlecaFrame, leitura de memória/injeção no jogo, nuvem e plataformas além de Windows.

## Fases

### 1. Fundação

- Solução com `MyFrame.App`, `MyFrame.Core` e `MyFrame.Core.Tests`.
- Modelos, contratos, DI e documentação.
- Configuração Windows e dependências MAUI/LiveCharts.

Aceite: solução restaura e compila com o workload `maui-windows`.

### 2. Dados locais

- Descriptografar e interpretar `lastData.dat`.
- Suportar formato atual e formato antigo com `InventoryJson` encapsulado.
- Ler itens, componentes, relíquias, ducats, tradable, vaulted e IDs do mercado nos catálogos.
- Tolerar arquivo bloqueado, gravação parcial e campos opcionais.

Aceite: snapshots sintéticos e o snapshot local válido geram modelos consistentes sem escrita na pasta do AlecaFrame.

### 3. Warframe.Market

- Verificar expiração do JWT antes da chamada.
- Consultar perfil e ordens próprias com Bearer.
- Consultar melhores ordens públicas por slug.
- Respeitar no máximo três requisições por segundo e usar `User-Agent` descritivo.
- Funcionar offline com cache e sinalização de preço antigo.

Aceite: token ausente/expirado ou API indisponível não impedem o inventário local.

### 4. Recomendações

- Calcular coleção, maestria, peças existentes e faltantes.
- Reservar peças necessárias antes de classificar excedentes.
- Opcionalmente reservar um conjunto extra de Warframe Prime não vaulted.
- Considerar ordens existentes.
- Comparar venda, ducats e conjunto completo versus peças.
- Usar razão configurável, inicialmente `1 platinum = 10 ducats`.

Aceite: nenhuma peça necessária é recomendada para venda/ducats, e toda decisão tem justificativa.

### 5. Interface

- Navegação lateral desktop em tema escuro.
- Indicadores de platinum, ducats, maestria e inventário.
- Gráficos LiveCharts.
- Listas de coleção, farm e vendas.
- Estados vazio, carregando, offline, token expirado e erro recuperável.
- Configuração da razão ducats/platinum.

Aceite: interface útil com dados reais, dados vazios e mercado offline em 1050×700 e 1440×900.

### 6. Qualidade e entrega

- Testes de criptografia/parsing, catálogo, HTTP e regras.
- Build completo e validação visual.
- Revisão do diff para impedir dados privados.
- Commit na `main`, sem push automático.

## Definição de pronto

- O app encontra a instalação local sem configuração manual.
- Nunca modifica as fontes nem ordens do usuário.
- Mantém utilidade offline e identifica cache antigo.
- Recomendações são determinísticas e explicáveis.
- Build e testes passam; o app inicia e foi validado visualmente.
- Token, snapshot real e dados pessoais não estão no repositório.

