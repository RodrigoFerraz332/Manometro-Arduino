# Validação — 7 de setembro de 2026

## Atualização após diagnóstico na máquina alvo

O PawnIO 2.1.0.0 foi instalado com autorização explícita do usuário. As 86 verificações automatizadas passaram e as três telas foram renderizadas em `artifacts/validation/`. O bloqueio inicial descrito abaixo não ocorreu nesta execução.

A leitura real do Ryzen funcionou com privilégios de administrador: sensor `/amdcpu/0/temperature/2`, `Core (Tctl/Tdie)`, aproximadamente 51,1 °C. Sem elevação, permaneceu indisponível. O Windows identifica o CH340 em COM3, porém o handshake VERSION do firmware não foi confirmado. A aplicação foi reaberta como administrador. A gravação/versão do firmware e o funcionamento físico do manômetro continuam pendentes.

## Verificado

- MVP compilado em Release, sem erros/avisos, antes das funcionalidades adicionais.
- Versão ampliada compilada em Release, sem erros/avisos.
- Publicação self-contained Windows x64 gerada em `artifacts/win-x64`.
- Firmware compilado pelo Arduino CLI oficial para `arduino:avr:uno`, core AVR 1.8.8: 6090 bytes de flash (18%) e 343 bytes de RAM estática (16%). Nenhum upload realizado.
- Instalador PawnIO incorporado: assinatura Authenticode válida, signatário namazso.eu.

## Bloqueado / não verificado

O projeto de testes compilou, mas o carregamento da DLL foi bloqueado pelo Controle de Aplicativos do Windows com `0x800711C7`. Isso aconteceu antes da entrada no programa de testes; **nenhuma asserção nem renderização de tela foi executada**. Não há evidência de teste passando. A política não foi alterada.

Não foram validados neste ambiente: execução da interface, temperatura real do Ryzen, latência/consumo, criação/remoção de startup, instalação PawnIO, comunicação com placa física, calibração elétrica e comportamento real da agulha. Nenhum driver foi instalado por esta sessão.

## Verificação de bancada restante

1. Executar testes em ambiente que permita o código local e revisar as renderizações das telas.
2. Preparar PawnIO e confirmar nome/leitura do sensor na máquina alvo.
3. Gravar o sketch, confirmar conexão e observar posições com o circuito devidamente verificado.
4. Retirar/reconectar USB e verificar COM diferente sem reiniciar a aplicação.
5. Confirmar que leitura ausente ou fechamento forçado provoca valor seguro após 10 s.
6. Confirmar que uma temperatura estável por mais de 10 s não dispara failsafe.
7. Testar tabela personalizada, valores MCP extremos, EMA, média móvel e retorno da tela de teste.
8. Testar startup no logon e remoção da opção, inclusive no modo elevado quando necessário.

Compilação bem-sucedida não substitui essas verificações de execução e hardware.
