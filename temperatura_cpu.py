# Script legado: lê a memória compartilhada do HWiNFO e envia a temperatura inteira.
# A aplicação C# substitui este fluxo; este arquivo permanece como referência do sistema antigo.
import ctypes
from ctypes import wintypes
import struct
import time

import serial

PORTA = "COM3"
BAUD = 9600

SHM_NAME = "Global\\HWiNFO_SENS_SM2"
ASSINATURA_ATIVA = b"HWiS"

FILE_MAP_READ = 0x0004
TIPO_TEMPERATURA = 1
LABELS_CPU_PREFERIDOS = (
    "CPU (Tctl/Tdie)",
    "CPU Package",
    "Core Temperatures",
    "CPU",
)

kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

OpenFileMappingW = kernel32.OpenFileMappingW
OpenFileMappingW.argtypes = [
    wintypes.DWORD,
    wintypes.BOOL,
    wintypes.LPCWSTR,
]
OpenFileMappingW.restype = wintypes.HANDLE

MapViewOfFile = kernel32.MapViewOfFile
MapViewOfFile.argtypes = [
    wintypes.HANDLE,
    wintypes.DWORD,
    wintypes.DWORD,
    wintypes.DWORD,
    ctypes.c_size_t,
]
MapViewOfFile.restype = ctypes.c_void_p

UnmapViewOfFile = kernel32.UnmapViewOfFile
UnmapViewOfFile.argtypes = [ctypes.c_void_p]

CloseHandle = kernel32.CloseHandle
CloseHandle.argtypes = [wintypes.HANDLE]


def abrir_hwinfo():
    # Abre o bloco de memória criado pelo HWiNFO; exige que ele esteja em execução.
    handle = OpenFileMappingW(FILE_MAP_READ, False, SHM_NAME)

    if not handle:
        raise ctypes.WinError(ctypes.get_last_error())

    ptr = MapViewOfFile(handle, FILE_MAP_READ, 0, 0, 0)

    if not ptr:
        CloseHandle(handle)
        raise ctypes.WinError(ctypes.get_last_error())

    return handle, ptr


def ler_bytes(ptr, offset, tamanho):
    return ctypes.string_at(ptr + offset, tamanho)


def limpar_string(data):
    return data.split(b"\x00", 1)[0].decode("latin-1", errors="ignore")


def ler_header(ptr):
    # O cabeçalho informa onde começam e qual o tamanho das tabelas de sensores e leituras.
    header = ler_bytes(ptr, 0, 48)
    valores = struct.unpack("<4sIIqIIIIIII", header)

    return {
        "status": valores[0],
        "version": valores[1],
        "revision": valores[2],
        "last_update": valores[3],
        "sensor_offset": valores[4],
        "sensor_size": valores[5],
        "sensor_count": valores[6],
        "reading_offset": valores[7],
        "reading_size": valores[8],
        "reading_count": valores[9],
        "polling_period_ms": valores[10],
    }


def ler_sensores(ptr, header):
    sensores = []

    for i in range(header["sensor_count"]):
        offset = header["sensor_offset"] + i * header["sensor_size"]
        raw = ler_bytes(ptr, offset, header["sensor_size"])

        nome_original = limpar_string(raw[8:136])
        nome_usuario = limpar_string(raw[136:264])
        sensores.append(nome_usuario or nome_original)

    return sensores


def extrair_leitura(raw):
    return {
        "tipo": struct.unpack_from("<I", raw, 0)[0],
        "sensor_index": struct.unpack_from("<I", raw, 4)[0],
        "nome_original": limpar_string(raw[12:140]),
        "nome_usuario": limpar_string(raw[140:268]),
        "unidade": limpar_string(raw[268:284]),
        "valor": struct.unpack_from("<d", raw, 284)[0],
    }


def eh_celsius(unidade):
    return "C" in unidade or "°" in unidade


def pontuar_temperatura_cpu(leitura, sensores):
    # Descarta medições incompatíveis e prioriza nomes conhecidos de temperatura da CPU.
    if leitura["tipo"] != TIPO_TEMPERATURA:
        return -1

    if not eh_celsius(leitura["unidade"]):
        return -1

    valor = leitura["valor"]
    if not 0 < valor < 120:
        return -1

    label = leitura["nome_usuario"] or leitura["nome_original"]
    sensor_nome = ""

    if leitura["sensor_index"] < len(sensores):
        sensor_nome = sensores[leitura["sensor_index"]]

    texto = f"{sensor_nome} {label}".lower()

    for prioridade, alvo in enumerate(LABELS_CPU_PREFERIDOS):
        if alvo.lower() in texto:
            return len(LABELS_CPU_PREFERIDOS) - prioridade

    if "cpu" in texto:
        return 1

    return -1


def ler_temperatura_cpu(ptr, header, sensores):
    # Percorre todas as leituras e mantém a de maior prioridade.
    melhor = None
    melhor_pontuacao = -1

    for i in range(header["reading_count"]):
        offset = header["reading_offset"] + i * header["reading_size"]
        raw = ler_bytes(ptr, offset, header["reading_size"])
        leitura = extrair_leitura(raw)
        pontuacao = pontuar_temperatura_cpu(leitura, sensores)

        if pontuacao > melhor_pontuacao:
            melhor = leitura
            melhor_pontuacao = pontuacao

    return melhor


# Neste script antigo a COM é fixa, ao contrário da descoberta feita pela aplicação C#.
arduino = serial.Serial(PORTA, BAUD, timeout=1)
time.sleep(2)

try:
    handle, ptr = abrir_hwinfo()

    print("Shared Memory do HWiNFO aberta.")
    print("Arduino conectado em", PORTA)

    while True:
        header = ler_header(ptr)

        if header["status"] != ASSINATURA_ATIVA:
            print("HWiNFO Shared Memory nao esta ativa.")
            time.sleep(1)
            continue

        sensores = ler_sensores(ptr, header)
        leitura_cpu = ler_temperatura_cpu(ptr, header, sensores)

        if leitura_cpu is not None:
            temperatura = leitura_cpu["valor"]
            temp_int = round(temperatura)
            label = leitura_cpu["nome_usuario"] or leitura_cpu["nome_original"]

            print(f"{label}: {temperatura:.1f} C -> Arduino: {temp_int}")
            arduino.write(f"{temp_int}\n".encode())
        else:
            print("Sensor de temperatura da CPU nao encontrado.")

        time.sleep(1)

finally:
    # Libera a memória compartilhada e a porta mesmo se ocorrer erro ou interrupção.
    if "ptr" in locals():
        UnmapViewOfFile(ptr)

    if "handle" in locals():
        CloseHandle(handle)

    arduino.close()
