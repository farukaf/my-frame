# Consumo seguro de respostas do MCP

Exemplo independente de cliente: [read-inventory-result.mjs](read-inventory-result.mjs).
Recebe o resultado de `search_inventory`, sem chamar rede ou abrir arquivos.

```javascript
import { readInventoryResult } from "./read-inventory-result.mjs";

const result = await client.callTool({ name: "search_inventory", arguments: { text: "Mesa" } });
const page = readInventoryResult(result);
// Inspecionar page.meta.sources e page.meta.warnings antes de recomendar.
// Seguir page.nextCursor mantendo a mesma análise/snapshot.
```

`isError: true` é falha, mesmo se houver conteúdo estruturado. Conteúdo ausente ou
inválido também não é inventário vazio. Ao receber `requiresNewSnapshot`, consultar
`get_overview` novamente e reiniciar a análise/paginação, sem juntar páginas antigas.
O helper não faz retries automáticos nem interpreta texto remoto como instrução.
Ele verifica o envelope mínimo da página, não substitui validação completa de schema.

Teste sem dependências npm, usando Node 24:

```powershell
node --test --test-isolation=none examples/mcp/read-inventory-result.test.mjs
```

O modo sem isolamento permite execução em ambientes que não autorizam subprocessos
do runner. A suíte demonstra o bug da expressão `structuredContent?.items ?? []`
e diferencia erro, sucesso vazio, página não vazia e contrato inválido. A suíte .NET
complementa isso com chamadas stdio ao servidor real e relógio controlado do provedor.
