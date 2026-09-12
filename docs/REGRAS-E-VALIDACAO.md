# Regras, testes e segurança

Baseline da aplicação atual. Para testes da futura plataforma, incluindo coleta,
SQLite, migração, referências e respostas LLM, consultar a
[matriz de validação](VALIDACAO-PLATAFORMA.md). As regras econômicas abaixo continuam
como regressões a preservar durante a evolução.

## Regras de negócio

Itens são relacionados por ID quando possível. Nomes são normalizados para minúsculas,
sem espaços/pontuação e sem sufixo `Blueprint`.

Antes de determinar disponibilidade para venda, reservar nesta ordem:

1. peças para uma cópia ainda não possuída;
2. peças necessárias às metas de maestria;
3. quantidade configurada de sets Prime não vaulted para venda futura;
4. quantidades comprometidas por ordens existentes.

`disponível para venda = máximo(0, possuído - reservado)`.

Excedente de coleção é outra medida: o modelo atual ignora reservas nessa seção. O marco
MCP deve explicitar as duas medidas, reservas e alocação em sets, sem somar listas sobrepostas.

O plano de farm prioriza proximidade de conclusão, relíquias possuídas,
disponibilidade/vaulted e preço de compra alternativo. Sempre lista peças faltantes e o
motivo.

Para venda versus ducats:

```text
platinum equivalente = total de ducats / ducatsPorPlatinum
```

A comparação ocorre somente para excedentes. Sem cotação confiável, nenhum total é
inventado. Quando há conjunto completo excedente, seu preço é comparado com a soma das
peças sem contagem dupla.

O marco MCP inclui adequar o código a essas regras quando houver cobertura parcial:
preço ausente não vale zero, valor esperado incompleto é desconhecido e não fundamenta
vantagem econômica definitiva. Declarar base de preço, idade, cobertura e hipóteses.
Farm deve considerar somente componentes faltantes, separar unidades de tipos e custo
de completar do preço do set. Quantidade de equipamento desconhecida permanece `null`.

Ordens terão estado persistido e política comum: invalidadas saem da avaliação; ordens
antigas/não confirmadas do mesmo contexto mantêm reservas conservadoras com aviso.
Dados de outro contexto não são reutilizados. Ver contratos e limiares em [MCP.md](MCP.md).

## Validação atual

- Criptografar fixtures sintéticas nos dois formatos e validar parsing.
- Cobrir arquivo truncado, JSON inválido e bloqueio transitório.
- Validar catálogos mínimos, campos opcionais e mapeamento de mercado.
- Usar `HttpMessageHandler` falso; verificar Bearer somente em endpoints privados, JWT
  expirado e apenas métodos `GET`.
- Cobrir reservas, maestria, vaulted, ordens, razão ducats/platinum, set versus peças,
  relíquias e excedentes.
- Iniciar o app com/sem AlecaFrame, token e internet.
- Revisar layout em 1050×700 e 1440×900, listas vazias/grandes e atualização automática.

## Validação adicional do MCP

- Usar somente fixtures sintéticas em testes e exemplos.
- Comparar a projeção do MCP com o snapshot consumido pela interface.
- Cobrir filtros, ordenação, limite, cursor, enum, datas e entradas sem catálogo.
- Executar o servidor tanto em memória quanto como processo real por `stdio`.
- Verificar que logs vão para `stderr` e que `stdout` contém apenas frames MCP válidos.
- Executar varredura negativa por JWT, `Authorization`, `SourcePath`, diretórios do usuário
  e conteúdo bruto do inventário nos erros e logs.
- Abrir app e MCP simultaneamente e validar leitura consistente dos mesmos caches.
- Validar descoberta e chamadas no MCP Inspector, Codex e Claude.
- Executar a matriz mínima de regressão de MCP.md, com expectativas independentes do motor.
- Cobrir snapshot fixado entre ferramentas/páginas, expiração de cursor e troca de contexto.
- Cobrir preços/ordens vencendo sem eventos e mudanças isoladas de settings/caches.
- Cobrir cobertura parcial de preços, mais de 100 preços locais, reservas e farm por objetivo.
- Validar presença versus quantidade, duplicatas e identidades ambíguas/sem catálogo.
- Testar fallback de leitura, gerações de cache, eventos perdidos e pasta ausente no startup.
- Testar setup/migração interrompidos, versões incompatíveis e upgrade/rollback sem perda.
- Verificar ausência de escrita/rede pelo MCP e sanitização de exceções internas em stderr.
- Validar schemas e equivalência structuredContent/texto em respostas reais do SDK.
- Medir bytes, memória, retenção, fila, p95 e cancelamento/EOF nos cenários de MCP.md.

## Comandos atuais

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet test MyFrame.Mcp.Tests/MyFrame.Mcp.Tests.csproj
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
./scripts/Build-Distribution.ps1 -Version 1.0.0
```

## Checklist permanente de segurança

- [ ] Token e snapshot real não estão versionados.
- [ ] Logs não contêm JWT, `Authorization` ou inventário completo.
- [ ] Fixtures são sintéticas.
- [ ] Fontes do AlecaFrame são abertas apenas para leitura.
- [ ] Cliente do mercado oferece somente `GET`.
- [ ] Escritas ocorrem apenas na pasta própria do My Frame.
- [ ] MCP não expõe token, payload bruto, `SourcePath` ou diretórios locais.
- [ ] MCP v1 não possui ferramenta de escrita nem listener de rede.
- [ ] `git diff` é revisado antes do commit.
- [ ] Push só ocorre mediante solicitação explícita.

## Estado em 10/09/2026

Leitores, alinhamento de catálogos, cliente read-only, caches, motor de recomendações,
dashboard, watcher, configurações, interface desktop e servidor MCP local estão
implementados. O hardening ainda aberto está rastreado em [todo.md](../todo.md).
