cmake_minimum_required(VERSION 3.22)

set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(MSVC)
  add_compile_options(/utf-8)
endif()

set(BUILD_SHARED_LIBS OFF CACHE BOOL "Build third-party UI dependencies as static libraries" FORCE)
set(FT_DISABLE_ZLIB TRUE CACHE BOOL "Disable optional FreeType zlib dependency" FORCE)
set(FT_DISABLE_BZIP2 TRUE CACHE BOOL "Disable optional FreeType bzip2 dependency" FORCE)
set(FT_DISABLE_PNG TRUE CACHE BOOL "Disable optional FreeType png dependency" FORCE)
set(FT_DISABLE_HARFBUZZ TRUE CACHE BOOL "Disable optional FreeType harfbuzz dependency" FORCE)
set(FT_DISABLE_BROTLI TRUE CACHE BOOL "Disable optional FreeType brotli dependency" FORCE)
set(SKIP_INSTALL_ALL TRUE CACHE BOOL "Do not install vendored FreeType artifacts" FORCE)

add_subdirectory("${PIXELENGINE_FREETYPE_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/freetype")
if(NOT TARGET Freetype::Freetype)
  add_library(Freetype::Freetype ALIAS freetype-interface)
endif()

set(RMLUI_FONT_ENGINE "freetype" CACHE STRING "Use FreeType font engine" FORCE)
set(RMLUI_SAMPLES OFF CACHE BOOL "Disable RmlUi samples" FORCE)
set(RMLUI_LUA_BINDINGS OFF CACHE BOOL "Disable RmlUi Lua bindings" FORCE)
set(RMLUI_LOTTIE_PLUGIN OFF CACHE BOOL "Disable RmlUi Lottie plugin" FORCE)
set(RMLUI_SVG_PLUGIN OFF CACHE BOOL "Disable RmlUi SVG plugin" FORCE)
set(RMLUI_PRECOMPILED_HEADERS OFF CACHE BOOL "Disable RmlUi precompiled headers in superbuild" FORCE)
set(RMLUI_WARNINGS_AS_ERRORS OFF CACHE BOOL "Do not treat third-party warnings as errors" FORCE)
set(RMLUI_INSTALL_LICENSES_AND_BUILD_INFO OFF CACHE BOOL "Do not install RmlUi build info" FORCE)

add_subdirectory("${PIXELENGINE_RMLUI_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/rmlui")

add_library(pixelengine_ui_native SHARED
  PixelEngineUiNative.cpp
  "${PIXELENGINE_RMLUI_SOURCE_DIR}/Backends/RmlUi_Renderer_GL3.cpp"
)

target_include_directories(pixelengine_ui_native
  PRIVATE
    "${PIXELENGINE_RMLUI_SOURCE_DIR}/Backends"
)

target_link_libraries(pixelengine_ui_native
  PRIVATE
    rmlui_core
    rmlui_debugger
)

target_compile_features(pixelengine_ui_native PRIVATE cxx_std_17)
target_compile_definitions(pixelengine_ui_native PRIVATE PIXELENGINE_UI_NATIVE_EXPORTS)

set_target_properties(pixelengine_ui_native PROPERTIES
  OUTPUT_NAME "PixelEngine.UI.Native"
  RUNTIME_OUTPUT_DIRECTORY "${PIXELENGINE_NATIVE_OUT_DIR}/shared"
  LIBRARY_OUTPUT_DIRECTORY "${PIXELENGINE_NATIVE_OUT_DIR}/shared"
  ARCHIVE_OUTPUT_DIRECTORY "${PIXELENGINE_NATIVE_OUT_DIR}/shared"
)
