# Regras, testes e segurança

## Regras de negócio

Itens são relacionados por ID quando possível. Nomes são normalizados para minúsculas, sem espaços/pontuação e sem sufixo `Blueprint`.

Antes de determinar excedentes, reservar nesta ordem:

1. peças para uma cópia ainda não possuída;
2. peças necessárias às metas de maestria;
3. conjunto extra de Warframe Prime não vaulted, se habilitado;
4. quantidades comprometidas por ordens existentes.

`excedente = máximo(0, possuído - reservado)`.

O plano de farm prioriza proximidade de conclusão, relíquias possuídas, disponibilidade/vaulted e preço de compra alternativo. Sempre lista peças faltantes e motivo.

Para venda versus ducats:

```text
platinum equivalente = total de ducats / ducatsPorPlatinum
```

Comparar esse valor com a cotação disponível, somente para excedentes. Sem cotação confiável, não inventar total. Quando houver conjunto completo excedente, comparar seu preço com a soma das peças sem contagem dupla.

## Testes

- Criptografar fixtures sintéticas nos dois formatos e validar parsing.
- Cobrir arquivo truncado, JSON inválido e bloqueio transitório.
- Validar catálogos mínimos, campos opcionais e mapeamento de mercado.
- Usar `HttpMessageHandler` falso; verificar Bearer somente em endpoints privados, JWT expirado e apenas `GET`.
- Cobrir reservas, maestria, vaulted, ordens existentes, razão ducats/platinum e conjunto versus peças.
- Iniciar o app com/sem AlecaFrame, token e internet.
- Revisar layout em 1050×700 e 1440×900, listas vazias/grandes e atualização automática.

## Comandos

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
```

## Checklist de segurança

- [ ] Token e snapshot real não estão versionados.
- [ ] Logs não contêm JWT, Authorization ou inventário completo.
- [ ] Fixtures são sintéticas.
- [ ] Cache contém somente preços públicos.
- [ ] Fontes do AlecaFrame são abertas apenas para leitura.
- [ ] Cliente do mercado oferece somente `GET`.
- [ ] Escritas ocorrem apenas na pasta própria do My Frame.
- [ ] `git diff` é revisado antes do commit.
- [ ] Push só ocorre mediante solicitação explícita.

## Estado atual (28/08/2026)

A solução, leitores, cliente read-only, cache, motor de recomendações,
dashboard, watcher, view model e interface desktop estão implementados. O app usa
Serilog em JSON com rotação horária na pasta própria, sem registrar credenciais ou
payloads privados. Há testes sintéticos para snapshots, catálogo, HTTP/JWT e regras
de recomendação, incluindo regressão para nomes duplicados de relíquias.

Build, testes e sincronização com a instalação local foram validados. Ainda resta
uma revisão visual humana completa em 1050×700 e 1440×900 antes da primeira entrega
formal e do commit funcional.
