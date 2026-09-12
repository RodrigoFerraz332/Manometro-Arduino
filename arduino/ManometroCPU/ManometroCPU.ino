#include <SPI.h>
#include <math.h>
#include <stdlib.h>
#include <string.h>

// Uno: CS=D10, SCK=D13, SDI=D11. O MCP41010 recebe alimentação de 5 V e GND.
const uint8_t CS_PIN = 10;
const unsigned long BAUD_RATE = 9600;
const unsigned long FAILSAFE_MS = 10000;
// currentValue é a posição enviada ao chip; targetValue é o destino da rampa.
// safeValue é usado na inicialização e quando faltam comandos de posição por 10 s.
uint8_t currentValue = 25;
uint8_t targetValue = 25;
uint8_t safeValue = 25;
unsigned long stepInterval = 120;
unsigned long lastStep = 0;
unsigned long lastSetpoint = 0;
bool failsafe = true;
// Buffer fixo evita alocação dinâmica na RAM limitada do Uno. Cada linha termina em LF.
char buffer[48];
uint8_t used = 0;
bool overflow = false;

void writeMcp(uint8_t value) {
  // CS baixo seleciona o chip; transmite comando e posição antes de encerrar a transação.
  SPI.beginTransaction(SPISettings(1000000, MSBFIRST, SPI_MODE0));
  digitalWrite(CS_PIN, LOW);
  SPI.transfer(0x11); // Comando de escrita no potenciômetro 0.
  SPI.transfer(value);
  digitalWrite(CS_PIN, HIGH);
  SPI.endTransaction();
}

uint8_t temperatureToMcp(float temperature) {
  // Tabela padrão para TEMP, TEST e números sem prefixo.
  // No modo CalibratedMcp, o PC aplica sua própria tabela e envia a posição pronta.
  const float temperatures[] = {30,40,50,60,70,75,80,90,95};
  const uint8_t values[] = {25,22,18,14,10,8,6,2,0};
  if (temperature <= temperatures[0]) return values[0];
  for (uint8_t i = 1; i < 9; ++i) {
    if (temperature <= temperatures[i]) {
      // Interpolação entre vizinhos: aproxima a resposta não linear por pequenos trechos.
      float fraction = (temperature-temperatures[i-1]) / (temperatures[i]-temperatures[i-1]);
      return (uint8_t)floor(values[i-1] + fraction * ((int)values[i] - values[i-1]) + 0.5f);
    }
  }
  return values[8];
}

bool parseNumber(const char* text, float& result) {
  // Exige um número completo e finito: não aceita sufixos inválidos, NaN ou infinito.
  if (!*text) return false;
  char* end;
  result = strtod(text, &end);
  return end != text && *end == '\0' && isfinite(result);
}

void setTarget(uint8_t value) {
  // Só posições válidas renovam o watchdog; a agulha chega ao alvo gradualmente em loop().
  targetValue = value;
  lastSetpoint = millis();
  failsafe = false;
}

void handleLine(char* line) {
  // PING e VERSION confirmam comunicação, mas não mantêm a agulha fora do failsafe.
  // F() guarda os textos constantes na flash para economizar RAM.
  if (!strcmp(line, "PING")) { Serial.println(F("PONG")); return; }
  if (!strcmp(line, "VERSION")) { Serial.println(F("VERSION:ManometroCPU:1.0")); return; }
  const char* argument = strchr(line, ':');
  float value;
  if (argument) {
    if (!parseNumber(argument+1, value)) { Serial.println(F("ERR:NUMBER")); return; }
    if (!strncmp(line, "MCP:", 4)) {
      if (value < 0 || value > 255 || value != floor(value)) { Serial.println(F("ERR:RANGE")); return; }
      setTarget((uint8_t)value);
    } else if (!strncmp(line, "TEMP:", 5) || !strncmp(line, "TEST:", 5)) {
      if (value < 0 || value > 125) { Serial.println(F("ERR:RANGE")); return; }
      setTarget(temperatureToMcp(value));
    } else if (!strncmp(line, "STEP:", 5)) {
      if (value < 1 || value > 1000 || value != floor(value)) { Serial.println(F("ERR:RANGE")); return; }
      stepInterval = (unsigned long)value;
    } else if (!strncmp(line, "SAFE:", 5)) {
      if (value < 0 || value > 255 || value != floor(value)) { Serial.println(F("ERR:RANGE")); return; }
      safeValue = (uint8_t)value;
      if (failsafe) { currentValue = targetValue = safeValue; writeMcp(currentValue); }
    } else { Serial.println(F("ERR:COMMAND")); return; }
  } else {
    // Números sem prefixo sempre significam Celsius, mantendo compatibilidade com o Python antigo.
    if (!parseNumber(line, value) || value < 0 || value > 125) { Serial.println(F("ERR:COMMAND")); return; }
    setTarget(temperatureToMcp(value));
  }
  // Confirma que o comando foi aceito, não que o movimento da agulha já terminou.
  Serial.println(F("OK"));
}

void setup() {
  // Começa em posição segura antes de receber qualquer dado do computador.
  digitalWrite(CS_PIN, HIGH);
  pinMode(CS_PIN, OUTPUT);
  SPI.begin();
  writeMcp(safeValue);
  Serial.begin(BAUD_RATE);
}

void loop() {
  // Limita o trabalho serial por ciclo para não impedir a rampa e o watchdog em caso de ruído.
  for (uint8_t budget = 0; budget < 32 && Serial.available(); ++budget) {
    char c = Serial.read();
    if (c == '\r') continue;
    if (c == '\n') {
      // Linhas maiores que o buffer são descartadas integralmente até o próximo LF.
      if (overflow) Serial.println(F("ERR:TOO_LONG"));
      else if (used) { buffer[used] = '\0'; handleLine(buffer); }
      used = 0; overflow = false;
    } else if (c < 32 || c > 126) { overflow = true; }
    else if (!overflow && used < sizeof(buffer)-1) buffer[used++] = c;
    else overflow = true;
  }
  unsigned long now = millis();
  if (!failsafe && (unsigned long)(now-lastSetpoint) >= FAILSAFE_MS) {
    // A segurança tem prioridade: retorna imediatamente, sem aguardar a rampa inteira.
    failsafe = true; currentValue = targetValue = safeValue; writeMcp(currentValue);
  }
  // Subtração sem sinal continua válida quando millis() dá a volta; não há delay bloqueante.
  if ((unsigned long)(now-lastStep) >= stepInterval) {
    lastStep = now;
    if (currentValue < targetValue) { ++currentValue; writeMcp(currentValue); }
    else if (currentValue > targetValue) { --currentValue; writeMcp(currentValue); }
  }
}
