cmake_minimum_required(VERSION 3.24)
project(Yorehold LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# The framework lives next to this repo.
set(YOREHOLD_FRAMEWORK_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../yorehold-framework" CACHE PATH "Path to yorehold-framework")
add_subdirectory(${YOREHOLD_FRAMEWORK_DIR} yorehold-framework)

add_executable(yorehold src/main.cpp)
target_link_libraries(yorehold PRIVATE yorehold::framework)

if(MSVC)
    target_compile_options(yorehold PRIVATE /W4)
    set_property(DIRECTORY PROPERTY VS_STARTUP_PROJECT yorehold)
else()
    target_compile_options(yorehold PRIVATE -Wall -Wextra)
endif()
