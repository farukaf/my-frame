# Autonomia do agente neste repositório

O agente tem autonomia ampla para desenvolver, testar, executar, manter e
finalizar tarefas neste repositório sem solicitar confirmação a cada etapa.

## Arquivos e código

Dentro deste repositório, o agente está autorizado a:

- Ler, criar, editar, mover, renomear e excluir arquivos e diretórios.
- Implementar funcionalidades, corrigir defeitos e realizar refatorações.
- Criar ou alterar testes, scripts, configurações, documentação e arquivos de
  automação.
- Fazer alterações arquiteturais necessárias para concluir a tarefa solicitada,
  mantendo compatibilidade com os requisitos e padrões existentes.
- Remover arquivos obsoletos, gerados, duplicados ou substituídos quando isso fizer
  parte do trabalho solicitado.
- Excluir arquivos e diretórios, inclusive de forma recursiva, quando necessário
  para desenvolver, corrigir, limpar, reconstruir ou testar o projeto. Essas
  exclusões dentro do repositório estão previamente autorizadas e não exigem uma
  confirmação adicional do usuário.

O agente não precisa pedir confirmação para essas operações, desde que seus
alvos estejam resolvidos e permaneçam dentro da raiz deste repositório. Mudanças
preexistentes do usuário devem ser preservadas e nunca descartadas sem necessidade
direta para a tarefa.

## Execução e validação

O agente está autorizado a:

- Instalar, atualizar e restaurar dependências necessárias ao projeto.
- Executar a aplicação, serviços locais, scripts, migrações locais e ferramentas
  de desenvolvimento.
- Executar builds, testes unitários, testes de integração, testes end-to-end,
  linters, formatadores, type-checkers, analisadores estáticos e verificadores de
  segurança.
- Iniciar e encerrar processos locais necessários para testar o projeto.
- Diagnosticar falhas, corrigir os problemas encontrados e repetir o ciclo de
  execução e validação até que os critérios relevantes sejam satisfeitos.
- Usar rede quando necessário para baixar dependências ou acessar serviços que
  pertençam ao fluxo normal de desenvolvimento do projeto.

Essas ações não exigem confirmação intermediária quando forem locais,
reversíveis e relacionadas à tarefa atual.

## Git

O agente tem autonomia para usar Git no repositório, incluindo:

- Inspecionar status, histórico, branches, tags, diffs e arquivos rastreados.
- Criar e alternar branches de trabalho.
- Adicionar arquivos ao index e criar commits relacionados à tarefa.
- Fazer merge, rebase, cherry-pick e resolver conflitos quando necessário.
- Buscar e sincronizar referências remotas quando isso fizer parte da tarefa.
- Reverter commits ou alterações produzidas pelo próprio agente durante a tarefa.

O agente deve preservar trabalho preexistente que não pertença à tarefa e não
deve descartar alterações do usuário. Exclusões necessárias dentro do repositório
não precisam de confirmação; seus alvos devem apenas ser explicitamente resolvidos
e verificados antes da execução.

## Limites

Esta autorização se aplica apenas a este repositório e ao ambiente local necessário
para executar o projeto. Ela não autoriza acesso ou alteração de arquivos pessoais
fora do repositório.

O agente somente deve interromper o trabalho para pedir confirmação quando uma
ação:

- Exigir permissão obrigatória do ambiente ou da plataforma que não possa ser
  concedida por este arquivo.
- Puder causar impacto irreversível fora deste repositório.
- Envolver publicação, implantação em produção, compra, cobrança ou uso de
  credenciais não fornecidas para a tarefa.
- Exigir uma decisão de produto que altere materialmente o objetivo solicitado e
  que não possa ser inferida com segurança a partir do código e do contexto.

Fora desses casos, o agente deve tomar decisões razoáveis, continuar trabalhando
de forma autônoma e entregar a tarefa validada, informando ao final as alterações
realizadas e as verificações executadas.
