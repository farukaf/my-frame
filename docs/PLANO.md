# Roteiro do My Frame

## Objetivo

O My Frame transforma os dados locais já mantidos pelo AlecaFrame em uma visão de
inventário, coleção/maestria, plano de farm e recomendações de venda por platinum ou
troca por ducats. Ele permanece somente leitura: não altera o AlecaFrame, não automatiza
o jogo e não cria, edita ou apaga ordens no Warframe.Market.

## Estado atual

A primeira versão funcional está concluída:

- solução .NET 10 com `MyFrame.App`, `MyFrame.Core` e testes;
- leitura tolerante dos formatos atual e antigo de `lastData.dat`;
- leitura e alinhamento dos catálogos do AlecaFrame e Warframe.Market;
- consulta read-only de conta, ordens e preços, com cache e funcionamento offline;
- regras de coleção, maestria, farm, venda, ducats, relíquias e excedentes;
- configuração da pasta do AlecaFrame, razão ducats/platinum e reserva de sets Prime;
- atualização manual e automática, estados de erro e logs sem conteúdo privado;
- interface desktop com dashboard, busca, filtros, detalhes e indicadores visuais;
- testes sintéticos para parsing, mercado, alinhamento e recomendações.

O antigo backlog de ajustes visuais foi encerrado: pill de quantidade possuída, imagem
específica das peças e controle de conversão com duas cores e dois campos já estão no
código. O arquivo [todo.md](../todo.md) acompanha o hardening ainda aberto.

## Marco entregue: MCP local read-only

`MyFrame.Mcp` é um servidor local via `stdio` que permite a Codex, Claude e
outros clientes MCP consultar as mesmas regras e fontes que alimentam a interface, além
do inventário agregado dos tipos suportados, com cobertura e limitações explícitas.

Decisões principais:

- processo console separado, iniciado e encerrado pelo cliente MCP;
- nenhuma porta HTTP, conta, API key ou autenticação do MCP;
- nenhuma ferramenta capaz de alterar arquivos, configurações, mercado ou inventário;
- regras, projeções, configurações e estado das fontes compartilhados com o app;
- respostas JSON estruturadas, pequenas, pesquisáveis e paginadas por snapshot;
- correção de estimativas parciais, reservas e significado dos dados antes dos endpoints;
- migração de settings/caches exclusiva do app e descoberta MCP disponível mesmo sem fontes;
- distribuição do executável MCP junto com o aplicativo e comandos prontos para cadastro.

O desenho técnico, os contratos e os critérios de aceite estão em
[MCP.md](MCP.md).

## Sequência de entrega executada

0. Fechar contratos de dados e corrigir análises no Core/UI com expectativas independentes.
1. Extrair configuração e composição local; implementar migração e armazenamento versionado.
2. Criar `MyFrame.Mcp` via `stdio`, com schemas, ferramentas, totais e cursores por snapshot.
3. Validar atualização de todas as fontes, expiração temporal, fallback e limites operacionais.
4. Criar distribuição com caminho estável, onboarding e testes de upgrade/rollback.
5. Validar a matriz de regressão, privacidade, desempenho e uso real em Codex e Claude.

## Critérios de aceite e hardening do marco MCP

O núcleo funcional da v1 está implementado. Esta lista permanece como régua de fechamento
do hardening; [todo.md](../todo.md) distingue o que já foi demonstrado do que ainda exige
matriz ampliada, benchmark ou validação manual em clientes externos.

- O usuário cadastra o servidor com um comando copiado da tela de configurações.
- Codex e Claude iniciam o processo sem credenciais e listam as capacidades disponíveis.
- Uma IA consulta resumo, inventário e todas as seções analíticas da interface.
- Resultados grandes são filtráveis e paginados, sem truncamento silencioso.
- Um mesmo snapshot pode ser usado entre páginas e ferramentas; expiração tem erro explícito.
- Mudanças de inventário, catálogo, settings e mercado aparecem sem reinício; expiração de
  preços/ordens é reconhecida mesmo sem mudança de arquivo.
- Dados de origem sanitizados, cobertura, idade e validade acompanham cada resposta;
  desconhecido não vira zero, e presença de equipamento não vira quantidade um.
- Estimativas parciais não fundamentam vantagem econômica definitiva; farm considera peças
  faltantes e separa seu custo do preço do set. Recomendações têm código e evidências.
- Excedente de coleção, reservas e disponibilidade para venda são distintos; totais não
  contam as mesmas peças duas vezes e ordens antigas/invalidadas têm política comum.
- Falhas de leitura mantêm estado válido do mesmo contexto com aviso, sem zerar os dados.
- Token, cabeçalho de autorização, caminhos locais e payload bruto nunca aparecem no MCP.
- Nome de conta é omitido por padrão; onboarding explica possível envio ao provedor de IA.
- App e MCP produzem resultados equivalentes para as mesmas fontes, settings e instante,
  e os resultados também passam por expectativas independentes do motor.
- Descoberta/overview funcionam sem setup completo; MCP não migra, escreve nem acessa rede.
- Migração repetida/interrompida e upgrade/rollback preservam dados e caminho cadastrado.
- Limites de bytes, retenção, concorrência, cancelamento e latência de MCP.md são medidos.
- Build, testes automatizados, MCP Inspector e smoke tests em Codex e Claude passam.

## Fora do escopo deste marco

- ferramentas de escrita ou automação do jogo/mercado;
- acesso remoto, nuvem, Streamable HTTP, OAuth ou servidor multiusuário;
- captura da janela ou transmissão da interface como imagem;
- chat embutido no My Frame;
- suporte oficial a plataformas além de Windows;
- simulações de settings, agregações configuráveis e inventário por instância, registrados
  como evoluções em MCP.md; totais filtrados e farm por objetivo já pertencem à v1.
