# Generated Cmake Pico project file

cmake_minimum_required(VERSION 3.13)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_EXPORT_COMPILE_COMMANDS ON)

# Initialise pico_sdk from installed location
# (note this can come from environment, CMake cache etc)

# == DO NOT EDIT THE FOLLOWING LINES for the Raspberry Pi Pico VS Code Extension to work ==
if(WIN32)
    set(USERHOME $ENV{USERPROFILE})
else()
    set(USERHOME $ENV{HOME})
endif()
set(sdkVersion 2.2.0)
set(toolchainVersion 14_2_Rel1)
set(picotoolVersion 2.2.0-a4)
set(picoVscode ${USERHOME}/.pico-sdk/cmake/pico-vscode.cmake)
if (EXISTS ${picoVscode})
    include(${picoVscode})
endif()
# ====================================================================================
set(PICO_BOARD pico CACHE STRING "Board type")

# Pull in Raspberry Pi Pico SDK (must be before project)
include(pico_sdk_import.cmake)

project(PicoARGB_Firmware C CXX ASM)

# Initialise the Raspberry Pi Pico SDK
pico_sdk_init()

# Add executable. Default name is the project name, version 0.1

add_executable(PicoARGB_Firmware 
    main.cpp
    config.cpp
    led_driver.cpp
    effects.cpp
    protocol.cpp
    ws2812.pio
    tusb_config.h
)

pico_generate_pio_header(PicoARGB_Firmware ${CMAKE_CURRENT_LIST_DIR}/ws2812.pio)
pico_set_program_name(PicoARGB_Firmware "PicoARGB_Firmware")
pico_set_program_version(PicoARGB_Firmware "0.1")

# Modify the below lines to enable/disable output over UART/USB
pico_enable_stdio_uart(PicoARGB_Firmware 0)
pico_enable_stdio_usb(PicoARGB_Firmware 1)

# Add the standard library to the build
target_link_libraries(PicoARGB_Firmware
        pico_stdlib
        hardware_pll
        hardware_pio
        hardware_clocks
        tinyusb_device
        tinyusb_board
    )

# Add the standard include files to the build
target_include_directories(PicoARGB_Firmware PRIVATE
        ${CMAKE_CURRENT_LIST_DIR}
)

pico_add_extra_outputs(PicoARGB_Firmware)

