# Manometro CPU — guia de uso e desenvolvimento

## Objetivo e arquitetura

`ManometroCPU.exe → LibreHardwareMonitorLib → temperatura CPU → filtro → calibração → USB serial → Arduino Uno → MCP41010 → manômetro`.

WinForms/.NET 10, sem console, com biblioteca oficial LibreHardwareMonitorLib 0.9.6 incorporada. Apenas sensores de CPU são habilitados. Um trabalhador em segundo plano executa leitura, descoberta e comunicação serial; a interface consulta um estado imutável a cada 250 ms. Todas as operações seriais passam pelo mesmo trabalhador para evitar concorrência de comandos. A leitura normal e o envio usam 1000 ms, configuráveis independentemente entre 500 e 3000 ms. Descoberta/reconexão pode temporariamente atrasar leituras enquanto o Uno reinicia, sem bloquear a janela.

O Python anterior fica preservado na raiz como referência; não participa da aplicação nova.

## Requisitos e preparação inicial

- Windows x64 compatível com .NET 10; SDK .NET 10 somente para compilar. A publicação self-contained inclui o runtime.
- Arduino Uno, cabo USB de dados e driver USB da placa (clones CH340/FTDI podem exigir o driver de seu fabricante).
- Firmware abaixo gravado no Uno, uma vez.
- Driver de sensores PawnIO, usado pelo LibreHardwareMonitor oficial. Seu instalador assinado está incorporado ao aplicativo; não há download em tempo de execução.

1. Coloque o executável em uma pasta permanente.
2. Abra `ManometroCPU.exe`. Em **Configurações**, use **Instalar driver de sensores** caso indicado. O Windows solicita UAC porque instalar driver exige administrador.
3. Após preparar o driver, use **Sair** na bandeja e abra o aplicativo novamente. A biblioteca carrega seus módulos na abertura.
4. A versão ajustada para este Ryzen solicita administrador ao abrir: os testes nesta máquina confirmaram que o sensor fica indisponível sem elevação. Aceite o UAC para permitir a leitura.
5. Conecte o Uno com o firmware ManometroCPU. A identificação padrão exige a resposta `VERSION:ManometroCPU:1.0` antes de enviar posições.
6. Confira sensor, temperatura, COM e status. Faça a calibração de bancada antes do uso permanente.

O driver fica instalado no Windows; não precisa manter um aplicativo PawnIO aberto. A aplicação não abre HWiNFO, Arduino IDE, Python ou terminal. Esta entrega é portátil; não inclui MSI. O `.pdb` gerado ao lado do executável serve apenas para depuração e não é necessário para executar.

## Montagem Arduino/MCP41010

Confira a pinagem do encapsulamento no datasheet. Para MCP41010 DIP-8:

| MCP41010 | Arduino Uno |
|---|---|
| VDD/VCC, pino 8 | 5 V |
| VSS/GND, pino 4 | GND |
| CS, pino 1 | D10 |
| SCK, pino 2 | D13 |
| SI/SDI, pino 3 | D11 |

Use desacoplamento de 100 nF próximo de VDD/VSS. A interface SPI usa modo 0, 1 MHz, comando `0x11` e um byte MCP. Para resistência crescente com o código, a referência é o caminho **PW0–PB0** (pinos 6–5), conforme circuito aprovado para seu manômetro. PA0 é o pino 7. O código zero não representa um curto ideal: há resistência do cursor.

**A ligação ao manômetro automotivo depende do circuito elétrico.** O MCP41010 não é um resistor passivo tolerante a 12 V: os terminais analógicos precisam respeitar a faixa VSS–VDD e os limites de corrente do datasheet. Não ligue uma entrada automotiva de 12 V diretamente ao cursor. Meça tensão/corrente e use uma interface apropriada se necessário. O software não valida essa compatibilidade. Consulte o [datasheet oficial Microchip](https://ww1.microchip.com/downloads/en/DeviceDoc/11195c.pdf).

## Gravar o firmware uma vez

Na Arduino IDE, abra `arduino/ManometroCPU/ManometroCPU.ino`, selecione **Arduino Uno**, selecione a COM atual e clique em Upload. A pasta tem o mesmo nome do sketch, como exigido pelo ambiente Arduino. Depois feche a IDE/monitor serial para liberar a porta.

Alternativa para desenvolvedores com [Arduino CLI oficial](https://docs.arduino.cc/arduino-cli/installation):

```powershell
arduino-cli core update-index
arduino-cli core install arduino:avr
arduino-cli compile --fqbn arduino:avr:uno arduino/ManometroCPU
arduino-cli board list
arduino-cli upload -p COM3 --fqbn arduino:avr:uno arduino/ManometroCPU
```

Troque `COM3` pela porta encontrada **apenas no comando de gravação**. A aplicação detecta portas em tempo de execução. Nenhum upload é feito automaticamente por este projeto. O HEX compilado também está em `artifacts/firmware/`.

## Sensor e portas

Prioridade automática: `CPU (Tctl/Tdie)`, `Core (Tctl/Tdie)`, `CPU Package`, `CPU Die (average)`, `Core (Tdie)`, `Core Average`, depois demais temperaturas do hardware identificado como CPU. O Ryzen 3700X pode aparecer como `Core (Tctl/Tdie)` na biblioteca. A janela mostra o nome realmente selecionado; a escolha manual usa identificador estável. Somente valores finitos entre 0 e 125 °C são aceitos; um sensor manual ausente não é substituído silenciosamente.

As portas são enumeradas por System.IO.Ports e enriquecidas com informações PnP via WMI. São priorizados Arduino, VID 2341/2A03 e adaptadores comuns 1A86/0403, a identidade PnP anterior e a última COM. VID de adaptador não prova que é Arduino: o handshake confirma o firmware. Em placas com identidade USB instável, o handshake ainda permite reencontrar a placa em outra COM. Com vários Unos, selecione manualmente o desejado na configuração inicial.

Reconexão é tentada aproximadamente a cada 5 s; abrir a COM espera 2 s pelo reset do Uno. Timeouts de leitura/escrita e respostas inválidas provocam desconexão e nova tentativa. **Desconectar** suspende as tentativas até **Conectar**. O menu da bandeja também permite conectar.

## Calibração e suavização

O modo padrão `CalibratedMcp` calcula a tabela no PC e envia `MCP:n`. A faixa inicial é 30–95 °C. Valores fora dela são limitados aos extremos.

```text
30 = 25
40 = 22
50 = 18
60 = 14
70 = 10
75 = 8
80 = 6
90 = 2
95 = 0
```

Esses valores são um ponto de partida fornecido no pedido, não uma calibração elétrica medida. A tabela aceita múltiplos pontos e faz interpolação linear por segmento, com arredondamento para o MCP mais próximo. As temperaturas devem ser únicas e os valores MCP devem diminuir ou permanecer iguais com o aquecimento. A tabela deve cobrir a faixa mínima/máxima.

Abra **Testar manômetro**, envie uma temperatura simulada ou um MCP explícito (0–255), observe a agulha e anote pontos de referência. Envio MCP é por botão, evitando varredura acidental ao editar o número. Fechar a janela encerra o teste e retoma a CPU. Os testes simulados respeitam a faixa mínima/máxima e o protocolo selecionado. O teste MCP exige protocolo confirmado. Os comandos continuam sendo reenviados durante o teste para manter o watchdog alimentado.

Em Configurações, edite a tabela com um ponto por linha, usando ponto como separador decimal. A tabela personalizada é usada em `CalibratedMcp`; os modos `TemperatureDecimal/Integer` usam a tabela padrão compilada no firmware.

- `None`: sem filtro.
- `Ema`: `filtrado = alfa × leitura + (1 − alfa) × filtrado anterior`. Alfa menor deixa a resposta mais lenta; o primeiro valor inicializa o filtro.
- `MovingAverage`: média das últimas N leituras; a janela inicial usa as amostras disponíveis.
- Deadband: varia a saída apenas quando a diferença acumulada em relação ao último valor aceito alcança o limiar; padrão 0,3 °C.
- Velocidade: milissegundos por passo MCP no Uno; padrão 120. Menor significa mais rápido.

Mesmo com temperatura estável, o aplicativo reenvia o valor aceito. Isso evita disparar o failsafe por causa da suavização. A temperatura exibida é a leitura da CPU; a saída passa pelo filtro, deadband e calibração.

## Protocolo serial 1.0

ASCII, 9600 baud, 8N1, sem controle de fluxo. Uma linha por comando, terminada em LF (`\n`); o firmware também aceita CRLF. O PC mantém um comando pendente por vez. Confirmações indicam aceitação do alvo, não que a agulha já chegou ao destino.

| Comando | Resposta | Efeito |
|---|---|---|
| `VERSION` | `VERSION:ManometroCPU:1.0` | Identificação exclusiva |
| `PING` | `PONG` | Verifica enlace sem renovar watchdog |
| `MCP:12` | `OK` | Define alvo inteiro 0–255 |
| `TEMP:47.5` | `OK` | Converte °C pela tabela padrão do firmware |
| `TEST:50` | `OK` | Equivalente a TEMP |
| `STEP:120` | `OK` | Define 1–1000 ms por passo |
| `SAFE:25` | `OK` | Define posição segura 0–255 |
| `45.8` ou `46` | `OK` | Temperatura sem prefixo, nunca MCP |
| Inválido | `ERR:NUMBER`, `ERR:RANGE`, `ERR:COMMAND`, `ERR:TOO_LONG` | Não renova watchdog |

Os formatos `TemperatureDecimal` e `TemperatureInteger` enviam `TEMP:` com uma casa ou inteiro. Os modos `LegacyDecimal` e `LegacyInteger` enviam apenas `45.8\n` / `46\n`, sem handshake/confirmação; exigem porta manual. Servem para o firmware antigo, mas nesse modo uma escrita bem-sucedida não comprova recepção e as funções STEP/SAFE não são enviadas. O firmware fornecido aceita números sem prefixo como Celsius para evitar ambiguidade.

Se mudar o baud no aplicativo, mude também `BAUD_RATE` no firmware e grave novamente. STEP e SAFE são reaplicados a cada conexão e não são gravados em EEPROM; após reset, o Uno começa com MCP 25 e passo 120 ms. Se 25 não for seguro no seu circuito, ajuste também o valor padrão no sketch antes da gravação.

## Failsafe

Após 10 s sem um comando válido de posição/temperatura, o Uno grava imediatamente o MCP seguro. O movimento normal usa `millis()` e um passo por vez, sem `delay()`. No failsafe, a escrita imediata tem prioridade para não demorar minutos a sair de uma posição extrema. PING, VERSION, STEP, SAFE e mensagens malformadas não alimentam o watchdog.

Quando o sensor fica indisponível, o PC deixa de enviar posições e usa apenas PING no protocolo confirmado. Portanto, a saída vai para failsafe. Ao encerrar normalmente, tenta enviar o MCP seguro; se o PC travar/desconectar, o watchdog do firmware assume. Esse mecanismo cobre perda de comunicação enquanto o Uno continua funcionando; não cobre falta de alimentação, falha elétrica ou travamento físico do microcontrolador.

## Bandeja e início com Windows

Minimizar ou fechar pelo X mantém o aplicativo funcionando quando **Minimizar para bandeja** está habilitado. Para encerrar, use **Sair** na bandeja. O menu oferece Abrir, Mostrar temperatura, Conectar Arduino, Configurações e Sair. O tooltip mostra CPU e conexão. Uma segunda instância é ignorada; use a bandeja para abrir a existente.

**Iniciar junto com o Windows** registra o executável atual com `--minimized`:

- Nesta versão, o manifesto exige administrador para acessar o sensor Ryzen. Ao salvar a opção habilitada, a aplicação cria uma tarefa do Agendador para seu SID, com token interativo, no logon e nível mais alto; remove a entrada Run para evitar duplicação. Não armazena senha, não inicia antes do logon e não exige novo UAC a cada início pela tarefa.
- O serviço mantém suporte à entrada HKCU Run para builds sem exigência de elevação; essa alternativa não é usada pelo executável atual.

Para remover uma tarefa elevada, abra como administrador, desmarque e salve. Se mover o executável, reabra no novo caminho e salve a opção novamente com os privilégios usados na criação. Configurar startup não instala nem executa o aplicativo como serviço. O driver PawnIO é separado da tarefa de logon.

## Arquivos locais e logs

Configuração: `%LOCALAPPDATA%\ManometroCPU\config.json`. Logs: `%LOCALAPPDATA%\ManometroCPU\logs\manometrocpu-AAAA-MM-DD.log`.

O diretório por usuário permite executar em uma pasta sem permissão de escrita. Na primeira abertura, gera padrões. JSON inválido é preservado em `.invalid-AAAAMMDDhhmmss` antes de restaurar padrões; falta de permissão é exibida como erro. A gravação usa arquivo temporário e substituição. Não edite o JSON com a aplicação aberta.

Logs registram inicialização, sensor selecionado, descoberta, conexão, reconexão, erros e encerramento. Retenção de 14 dias; dois segmentos de aproximadamente 2 MB por dia. Leituras normais não são registradas individualmente. O serviço evita derrubar o controle se o disco de logs estiver indisponível.

## Compilar Windows x64

Na raiz com SDK .NET 10:

```powershell
dotnet restore ManometroCPU.slnx
dotnet build ManometroCPU.slnx -c Release
dotnet publish src/ManometroCPU/ManometroCPU.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
```

O projeto já configura `PublishSingleFile`, extração de bibliotecas nativas e desativa trimming. O resultado é `artifacts/win-x64/ManometroCPU.exe`, cerca de 120 MB devido ao runtime incluído. As bibliotecas nativas podem ser extraídas pelo .NET para sua área temporária na execução; isso não requer programa auxiliar. O driver permanece uma instalação de sistema.

Neste workspace também existe `.tools/dotnet/dotnet.exe`, SDK local oficial usado na validação. Essa pasta e os compiladores Arduino são ferramentas de desenvolvimento, não fazem parte da distribuição. Para usar o SDK local, substitua `dotnet` nos comandos pelo caminho `& .\.tools\dotnet\dotnet.exe`.

Testes:

```powershell
dotnet run --project tests/ManometroCPU.Tests -c Release
```

O executável de testes verifica interpolação, limites, monotonicidade, filtros, configurações inválidas e JSON em cultura pt-BR; prepara renderizações de três telas em `artifacts/validation/`. Não envia comandos à placa. `-- --probe` acrescenta somente leitura de sensores e enumeração de portas. Veja [VALIDATION.md](VALIDATION.md) para o que foi efetivamente executado neste ambiente.

## Solução de problemas

| Sintoma | Ação |
|---|---|
| Sensor indisponível | Instale/repare PawnIO pelo botão, reabra e confira privilégios. O sensor manual selecionado pode estar ausente. |
| Driver instalado mas sem leitura | Confira permissões e bloqueios do Windows; consulte logs. Não há fallback para HWiNFO. |
| Arduino não conecta | Grave este firmware, confirme baud e selecione COM manual se a descrição USB for incomum. Feche IDE/monitor serial e outros usuários da porta. |
| Reconecta continuamente | Verifique cabo, alimentação, baud, confirmação VERSION e erros nos logs. |
| Agulha oscila | Reduza alfa, amplie janela média móvel ou aumente deadband/tempo por passo. |
| Agulha não acompanha a tabela | Use CalibratedMcp. Confira PW–PB, circuito, limites e faixa do manômetro. |
| Agulha para após 10 s | Verifique leitura CPU e comandos válidos. PING sozinho não mantém posição. |
| Aplicativo desapareceu | Abra pelo ícone da bandeja, inclusive na área de ícones ocultos. |
| Startup elevado não salva | Abra como administrador para criar/remover a tarefa e consulte a mensagem exibida. |
| Windows bloqueia EXE/DLL com 0x800711C7 | Política de Controle de Aplicativos bloqueou código local. Em máquina gerenciada, solicite a liberação/assinatura aprovada ao administrador; não desative a proteção como solução. |

## Organização

`src/ManometroCPU/`: Program, AppController, Hardware, Serial, Calibration, Settings, UI, Logging e Resources. `arduino/ManometroCPU/`: sketch. `config/`: exemplo JSON. `tests/`: verificações sem hardware. `docs/`: guia, validação e licenças. `artifacts/`: publicação e firmware compilado.

## Fontes e componentes incorporados

- [LibreHardwareMonitor oficial](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor), [pacote 0.9.6](https://www.nuget.org/packages/LibreHardwareMonitorLib/0.9.6), [fonte correspondente v0.9.6](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6). Sem modificações na biblioteca.
- [PawnIO](https://github.com/namazso/PawnIO), driver utilizado pela biblioteca. Instalador incorporado obtido de `LibreHardwareMonitor/Resources/PawnIO_setup.exe` da tag v0.9.6; assinatura Authenticode verificada como válida, signatário namazso.eu.
- [Notas e licenças de terceiros](THIRD-PARTY-NOTICES.md), incluindo origem e hash do instalador.
- [Arduino CLI](https://docs.arduino.cc/arduino-cli/getting-started/) e [MCP41010 Microchip](https://www.microchip.com/en-us/product/MCP41010).
