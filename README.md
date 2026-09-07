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

## CI/CD e distribuição

Todo pull request executa todos os testes e gera artefatos `win-x64`
autocontidos com o Velopack, que não exigem a instalação do .NET SDK ou do
runtime:

- `MyFrame-win-Portable.zip`: versão portátil;
- `MyFrame-win-Setup.exe`: instalador one-click por usuário.

Os arquivos podem ser baixados pelo link que o bot publica no PR ou pela seção
**Artifacts** da execução do GitHub Actions. O preview exige login no GitHub e
expira após 30 dias. Tags no formato
`vMAJOR.MINOR.PATCH` (por exemplo, `v1.2.3`) criam ou
atualizam uma GitHub Release e anexam os mesmos arquivos a ela, junto com os
metadados e pacotes necessários para implementar atualização automática depois.

O instalador não pede privilégios administrativos: instala em `%LOCALAPPDATA%`,
cria atalhos no Desktop e no Menu Iniciar e registra a desinstalação no Windows.
Enquanto os binários não tiverem assinatura de código, o Windows SmartScreen
poderá exibir um aviso ao baixá-los pela primeira vez.

[Baixar o instalador da versão estável mais recente](https://github.com/farukaf/my-frame/releases/latest/download/MyFrame-win-Setup.exe)

[Baixar a versão portátil mais recente](https://github.com/farukaf/my-frame/releases/latest/download/MyFrame-win-Portable.zip)

Para reproduzir o empacotamento localmente, execute:

```powershell
./scripts/Build-Distribution.ps1 -Version 1.0.0
```

## Logs

O aplicativo grava eventos estruturados em JSON Lines em
`%LOCALAPPDATA%\MyFrame\logs\my-frame-AAAAmmddHH.json`. Um novo arquivo é criado
a cada hora, com retenção máxima de 168 arquivos e limite de 25 MB por arquivo.
Os logs registram etapas de inicialização, contagens, resultados de sincronização
e falhas, sem gravar JWT, cabeçalho Authorization ou o inventário completo.
