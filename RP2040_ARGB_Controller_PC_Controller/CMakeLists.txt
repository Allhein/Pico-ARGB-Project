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

project(RP2040_ARGB_Controller_PC_Controller C CXX ASM)

# Initialise the Raspberry Pi Pico SDK
pico_sdk_init()

# Add executable. Default name is the project name, version 0.1

add_executable(RP2040_ARGB_Controller_PC_Controller 
    RP2040_ARGB_Controller_PC_Controller.cpp
    ws2812.pio
    tusb_config.h
)

pico_generate_pio_header(RP2040_ARGB_Controller_PC_Controller ${CMAKE_CURRENT_LIST_DIR}/ws2812.pio)
pico_set_program_name(RP2040_ARGB_Controller_PC_Controller "RP2040_ARGB_Controller_PC_Controller")
pico_set_program_version(RP2040_ARGB_Controller_PC_Controller "0.1")

# Modify the below lines to enable/disable output over UART/USB
pico_enable_stdio_uart(RP2040_ARGB_Controller_PC_Controller 0)
pico_enable_stdio_usb(RP2040_ARGB_Controller_PC_Controller 1)

# Add the standard library to the build
target_link_libraries(RP2040_ARGB_Controller_PC_Controller
        pico_stdlib
        hardware_pll
        hardware_pio
        hardware_clocks
        tinyusb_device
        tinyusb_board
    )

# Add the standard include files to the build
target_include_directories(RP2040_ARGB_Controller_PC_Controller PRIVATE
        ${CMAKE_CURRENT_LIST_DIR}
)

pico_add_extra_outputs(RP2040_ARGB_Controller_PC_Controller)

