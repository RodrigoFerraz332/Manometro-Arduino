# Manometro CPU

Aplicativo Windows em C#/.NET 10 + firmware Arduino Uno para representar a temperatura da CPU em um manômetro com MCP41010.

- Executável gerado: [artifacts/win-x64/ManometroCPU.exe](artifacts/win-x64/ManometroCPU.exe)
- [Guia completo de montagem, configuração e compilação](docs/README.md)
- [Firmware](arduino/ManometroCPU/ManometroCPU.ino)
- [Resultados e limites da validação](docs/VALIDATION.md)

Não depende de Python, HWiNFO ou Arduino IDE durante o uso. O driver de sensores PawnIO é instalado uma vez pelo botão em Configurações; seu instalador oficial está incorporado ao executável. O firmware precisa ser gravado uma vez no Uno.

Compilação Windows e firmware validados. A execução dos testes foi bloqueada pela política de Controle de Aplicativos deste Windows; o funcionamento no sensor e no circuito físico ainda precisa de validação.
