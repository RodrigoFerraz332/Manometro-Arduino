# Componentes de terceiros

Biblioteca oficial não modificada: LibreHardwareMonitorLib 0.9.6, MPL-2.0 e avisos adicionais preservados em `licenses/`. Fonte: https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/tree/v0.9.6 . Dependências transitivas são restauradas pelo NuGet, com versões em `src/ManometroCPU/packages.lock.json`.

Instalador PawnIO incorporado, sem modificações:

- Origem: https://raw.githubusercontent.com/LibreHardwareMonitor/LibreHardwareMonitor/v0.9.6/LibreHardwareMonitor/Resources/PawnIO_setup.exe
- SHA-256: `A3A46226C5E2824F4CDD42BE0EECBABFC672C86F7889710F5AB1E6AD385B47A0`
- Assinatura Authenticode verificada: válida, `E=admin@namazso.eu, CN=namazso.eu, O=namazso, L=Debrecen, C=HU`.
- Projeto/fonte do driver: https://github.com/namazso/PawnIO . GPL-2.0-or-later com exceção descrita pelo projeto; texto da licença em `licenses/PawnIO-COPYING.txt` e aviso do projeto em `licenses/PawnIO-README.md`.

O instalador só é executado quando o usuário aciona o botão em Configurações. Ele instala um driver compartilhado do Windows. A aplicação não o remove ao desativar startup, pois outros aplicativos podem utilizá-lo.

O runtime .NET e bibliotecas System.* são distribuídos pela Microsoft. As dependências e seus metadados/licenças podem ser consultados nos respectivos pacotes NuGet. Ao redistribuir, preserve estes avisos, as licenças e a disponibilidade dos fontes correspondentes.
