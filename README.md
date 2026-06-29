# Pico ARGB Project

**Pico ARGB Project** es un proyecto experimental para utilizar un microcontrolador **Raspberry Pi Pico / RP2040** como controlador ARGB externo para ventiladores y tiras LED compatibles con señal tipo **WS2812/ARGB**.

El proyecto nació por la necesidad de controlar la iluminación de ventiladores **Aigo AR12** cuando no se cuenta con un controlador ARGB dedicado. Partiendo de eso se me ocurrio usar el RP2040 como una alternativa económica, programable y personalizable para enviar efectos de iluminación y comandos desde una aplicación de PC.

---

## Objetivo del proyecto

El objetivo principal es convertir el **RP2040** en un controlador ARGB funcional que pueda recibir comandos desde Windows mediante USB y aplicar efectos de iluminación sobre LEDs ARGB.

Este proyecto incluye dos partes principales:

1. **Firmware para RP2040**

   * Controla la salida de datos hacia los LEDs ARGB.
   * Usa PIO para generar la señal compatible con WS2812.
   * Recibe comandos desde la PC mediante USB HID.
   * Ejecuta efectos de iluminación en tiempo real.

2. **Aplicación de PC**

   * Permite conectarse al RP2040 por USB.
   * Envía comandos HID al firmware.
   * Permite seleccionar colores y modos de iluminación.
   * Incluye soporte inicial para modo reactivo a música.

---

## ¿Por qué usar un RP2040?

El RP2040 es una buena opción para este tipo de proyecto porque:

* Es económico y fácil de conseguir.
* Tiene soporte para USB nativo.
* Cuenta con módulos PIO, útiles para generar señales precisas.
* Puede controlar LEDs WS2812/ARGB sin depender completamente de la CPU.
* Permite crear un controlador personalizado sin usar hardware ARGB propietario.

Y la razón más importante: lo vi y me gusto :p
---

## Hardware utilizado

* Raspberry Pi Pico o placa compatible con RP2040.
* Ventiladores Aigo AR12 o LEDs ARGB compatibles.
* Cable USB para conectar el RP2040 a la PC.
* Fuente de alimentación adecuada para los LEDs o ventiladores.
* Cableado para señal, voltaje y tierra.

> **Nota:** Este proyecto está pensado como una solución experimental. Antes de conectar ventiladores o tiras LED, verifica el pinout, voltaje y consumo eléctrico para evitar daños en el RP2040 o en los LEDs.

---

## Firmware RP2040

El firmware está diseñado para controlar LEDs mediante una salida de datos conectada al GPIO del RP2040.

Configuración actual del firmware:

| Parámetro         | Valor actual |
| ----------------- | ------------ |
| Pin de datos ARGB | GPIO 0       |
| LED de debug      | GPIO 25      |
| Cantidad de LEDs  | 8            |
| Comunicación USB  | HID          |
| VID               | `0x20A0`     |
| PID               | `0x423D`     |

El firmware usa **TinyUSB HID** para comunicarse con el programa de PC y **PIO** para generar la señal de control hacia los LEDs WS2812/ARGB.

---

## Comandos HID soportados

El firmware recibe reportes HID de 64 bytes. El formato usado es:

```txt
[0] = Report ID
[1] = Comando
[2..] = Datos del comando
```

Comandos principales:

| Comando          | Código | Descripción                                                 |
| ---------------- | -----: | ----------------------------------------------------------- |
| `PING`           | `0xAA` | Comprueba la comunicación. El firmware responde con `PONG`. |
| `SET_COLOR`      | `0x03` | Establece el color RGB base.                                |
| `OFF`            | `0x04` | Apaga todos los LEDs.                                       |
| `SET_MODE`       | `0x05` | Cambia el modo de iluminación activo.                       |
| `MUSIC_LEVEL`    | `0x06` | Actualiza el nivel usado por el modo música.                |
| `SET_BRIGHTNESS` | `0x07` | Comando reservado para control de brillo.                   |

---

## Modos de iluminación

| Modo | Efecto         |
| ---: | -------------- |
|  `0` | Apagado        |
|  `2` | Rainbow        |
|  `3` | Breathing      |
|  `4` | Chase          |
|  `5` | Music reactive |
|  `6` | Color cycle    |

---

## Aplicación de PC

La aplicación de PC está desarrollada para Windows y permite controlar el RP2040 mediante USB HID.

Funciones principales:

* Detección del dispositivo por VID/PID.
* Conexión y desconexión manual.
* Envío de comandos al firmware.
* Selección de modo de iluminación.
* Selección de color base.
* Vista previa visual de ventiladores.
* Guardado básico de configuración.
* Captura de audio para modo reactivo a música.

---

## Estructura recomendada del repositorio

```txt
Pico-ARGB-Project/
│
├─ firmware-rp2040/
│  ├─ RP2040_ARGB_Controller.cpp
│  ├─ CMakeLists.txt
│  ├─ ws2812.pio
│  └─ tusb_config.h
│
├─ desktop-controller/
│  ├─ PicoARGBControl.csproj
│  ├─ MainWindow.xaml
│  ├─ MainWindow.xaml.cs
│  ├─ HidManager.cs
│  ├─ AudioAnalyzer.cs
│  └─ Assets/
│
├─ docs/
│  ├─ protocol.md
│  └─ wiring.md
│
├─ README.md
├─ .gitignore
└─ LICENSE
```

---

## Instalación del firmware

1. Descarga el archivo `.uf2` desde la sección de Releases.
2. Mantén presionado el botón **BOOTSEL** del Raspberry Pi Pico.
3. Conecta el Pico a la PC por USB.
4. Suelta el botón BOOTSEL.
5. Copia el archivo `.uf2` al dispositivo que aparece como unidad USB.
6. El Pico se reiniciará automáticamente con el firmware cargado.

---

## Uso básico

1. Conecta el RP2040 a la PC.
2. Conecta la señal de datos ARGB al pin configurado en el firmware.
3. Abre la aplicación de PC.
4. Conecta con el dispositivo usando el VID/PID configurado.
5. Selecciona un modo de iluminación.
6. Elige un color base si el modo lo requiere.
7. Aplica los cambios.

---

## Estado actual del proyecto

Este proyecto se encuentra en una etapa inicial funcional. El firmware ya permite recibir comandos HID y controlar efectos básicos de iluminación, mientras que la aplicación de PC permite enviar comandos y probar la comunicación con el RP2040.

Actualmente está orientado a pruebas, depuración y validación del concepto.

---

## Limitaciones actuales

* El número de LEDs está definido directamente en el firmware.
* El soporte de brillo está iniciado, pero aún debe integrarse completamente en la aplicación de PC.
* El modo música depende del envío de niveles desde el programa de PC.
* El firmware todavía contiene mensajes y funciones de depuración.
* La compatibilidad con Aigo AR12 puede depender del cableado, alimentación y tipo exacto de LEDs usados por el modelo.

---

## Advertencia

Este proyecto implica conectar hardware externo al RP2040. Una conexión incorrecta de voltaje, tierra o señal puede dañar el microcontrolador, los ventiladores o los LEDs.

Usa una fuente de alimentación adecuada y revisa el pinout antes de conectar cualquier dispositivo ARGB.

---

## Licencia

Este proyecto se distribuye bajo la licencia incluida en el repositorio.
