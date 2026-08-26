# My Frame

Aplicativo Windows em .NET MAUI para visualizar o inventário local do AlecaFrame,
acompanhar coleção e mastery, planejar farm e separar excedentes entre platinum e
ducats.

## Planejamento

- [Plano completo](docs/PLANO.md)
- [Arquitetura e engenharia reversa](docs/ARQUITETURA.md)
- [Regras, testes e segurança](docs/REGRAS-E-VALIDACAO.md)

## Segurança

- A pasta do AlecaFrame é sempre aberta em modo somente leitura.
- `WFMarketToken.tk` é lido somente em memória e nunca é copiado ou registrado.
- A autenticação do Warframe.Market é usada apenas para consultar o perfil e as
  ordens do próprio usuário. O aplicativo não cria, altera ou remove anúncios.
- Dados privados e snapshots reais não fazem parte do repositório.

## Desenvolvimento

Requisitos: Windows, .NET 10 SDK e workload `maui-windows`.

```powershell
dotnet restore MyFrame.slnx
dotnet build MyFrame.slnx
dotnet test MyFrame.Core.Tests/MyFrame.Core.Tests.csproj
dotnet run --project MyFrame.App/MyFrame.App.csproj -f net10.0-windows10.0.19041.0
```
