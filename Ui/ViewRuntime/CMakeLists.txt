cmake_minimum_required(VERSION 3.16)
project(ViewRuntime VERSION 0.1.0 LANGUAGES C CXX)

include(CTest)
include(CMakePackageConfigHelpers)
include(GNUInstallDirs)

option(VIEWRUNTIME_BUILD_TESTS "Build the ViewRuntime native test executables" "${BUILD_TESTING}")

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

add_library(viewruntime_core SHARED
    src/abi/abi.cpp
    src/primitives/geometry.cpp
    src/css/css_length.cpp
    src/css/color_rgba.cpp
    src/android/android_views.cpp
    src/android/android_inflate.cpp
    src/android/android_measure_layout.cpp
    src/android/android_list.cpp
    src/android/constraint_solver.cpp
    src/android/constraint_widget.cpp
    src/android/stb_text_measurer.cpp
    src/android/android_image_decode.cpp
    src/android/viewruntime_backend.cpp
    src/android/android_grid_layout.cpp
    src/android/android_display_list.cpp
    src/rendering/display_list_commands.cpp
    src/rendering/transform_matrix.cpp
    src/rendering/render_plan.cpp
)

add_library(ViewRuntime::Core ALIAS viewruntime_core)

target_include_directories(viewruntime_core
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
)

target_compile_definitions(viewruntime_core PRIVATE EXPORTS)

set_target_properties(viewruntime_core PROPERTIES
    EXPORT_NAME Core
    VERSION "${PROJECT_VERSION}"
    SOVERSION "${PROJECT_VERSION_MAJOR}"
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)

if(MSVC)
    target_compile_options(viewruntime_core PRIVATE /W4 /utf-8)
    target_compile_definitions(viewruntime_core PRIVATE _CRT_SECURE_NO_WARNINGS NOMINMAX)
else()
    target_compile_options(viewruntime_core PRIVATE -Wall -Wextra -pedantic)
    target_compile_options(viewruntime_core PRIVATE -ffp-contract=off)
endif()

if(VIEWRUNTIME_BUILD_TESTS)
    enable_testing()

    add_executable(smoke_test tests/test_smoke.cpp)
    target_link_libraries(smoke_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.smoke COMMAND smoke_test)

    add_executable(abi_c_smoke_test tests/test_abi_c.c)
    target_link_libraries(abi_c_smoke_test PRIVATE ViewRuntime::Core)
    set_target_properties(abi_c_smoke_test PROPERTIES
        C_STANDARD 11
        C_STANDARD_REQUIRED YES
    )
    add_test(NAME viewruntime.abi-c COMMAND abi_c_smoke_test)

    add_executable(render_plan_test tests/test_render_plan.cpp)
    target_link_libraries(render_plan_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.render-plan COMMAND render_plan_test)

    add_executable(android_measure_test tests/test_android_measure.cpp)
    target_link_libraries(android_measure_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-measure COMMAND android_measure_test)

    add_executable(android_layout_test tests/test_android_layout.cpp)
    target_link_libraries(android_layout_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-layout COMMAND android_layout_test)

    add_executable(android_display_list_test tests/test_android_display_list.cpp)
    target_link_libraries(android_display_list_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-display-list COMMAND android_display_list_test)

    add_executable(android_api_test tests/test_android_api.cpp)
    target_link_libraries(android_api_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-api COMMAND android_api_test)

    add_executable(android_inflate_test tests/test_android_inflate.cpp)
    target_link_libraries(android_inflate_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-inflate COMMAND android_inflate_test)

    add_executable(android_grid_test tests/test_android_grid.cpp)
    target_link_libraries(android_grid_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-grid COMMAND android_grid_test)

    add_executable(android_list_test tests/test_android_list.cpp)
    target_link_libraries(android_list_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.android-list COMMAND android_list_test)

    add_executable(viewruntime_constraint_solver_test tests/test_constraint_solver.cpp src/android/constraint_solver.cpp)
    target_link_libraries(viewruntime_constraint_solver_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.constraint-solver COMMAND viewruntime_constraint_solver_test)

    add_executable(viewruntime_constraint_widget_test tests/test_constraint_widget.cpp
                   src/android/constraint_solver.cpp src/android/constraint_widget.cpp)
    target_link_libraries(viewruntime_constraint_widget_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.constraint-widget COMMAND viewruntime_constraint_widget_test)

    add_executable(viewruntime_text_measurer_test tests/test_text_measurer.cpp)
    target_link_libraries(viewruntime_text_measurer_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.text-measurer COMMAND viewruntime_text_measurer_test)

    add_executable(viewruntime_backend_test tests/test_viewruntime_backend.cpp)
    target_link_libraries(viewruntime_backend_test PRIVATE ViewRuntime::Core)
    add_test(NAME viewruntime.backend COMMAND viewruntime_backend_test)
endif()

set(VIEWRUNTIME_INSTALL_CMAKEDIR
    "${CMAKE_INSTALL_LIBDIR}/cmake/ViewRuntime"
    CACHE STRING "Install location for ViewRuntime CMake package files"
)

install(
    TARGETS viewruntime_core
    EXPORT ViewRuntimeTargets
    RUNTIME DESTINATION "${CMAKE_INSTALL_BINDIR}"
    LIBRARY DESTINATION "${CMAKE_INSTALL_LIBDIR}"
    ARCHIVE DESTINATION "${CMAKE_INSTALL_LIBDIR}"
)

install(
    DIRECTORY include/
    DESTINATION "${CMAKE_INSTALL_INCLUDEDIR}"
    FILES_MATCHING PATTERN "*.h"
)

configure_package_config_file(
    cmake/ViewRuntimeConfig.cmake.in
    "${CMAKE_CURRENT_BINARY_DIR}/ViewRuntimeConfig.cmake"
    INSTALL_DESTINATION "${VIEWRUNTIME_INSTALL_CMAKEDIR}"
)

write_basic_package_version_file(
    "${CMAKE_CURRENT_BINARY_DIR}/ViewRuntimeConfigVersion.cmake"
    VERSION "${PROJECT_VERSION}"
    COMPATIBILITY SameMinorVersion
)

install(
    EXPORT ViewRuntimeTargets
    FILE ViewRuntimeTargets.cmake
    NAMESPACE ViewRuntime::
    DESTINATION "${VIEWRUNTIME_INSTALL_CMAKEDIR}"
)

install(
    FILES
        "${CMAKE_CURRENT_BINARY_DIR}/ViewRuntimeConfig.cmake"
        "${CMAKE_CURRENT_BINARY_DIR}/ViewRuntimeConfigVersion.cmake"
    DESTINATION "${VIEWRUNTIME_INSTALL_CMAKEDIR}"
)

file(
    RELATIVE_PATH VIEWRUNTIME_PKGCONFIG_PREFIX_RELATIVE
    "${CMAKE_CURRENT_BINARY_DIR}/${CMAKE_INSTALL_LIBDIR}/pkgconfig"
    "${CMAKE_CURRENT_BINARY_DIR}"
)

configure_file(
    cmake/viewruntime-core.pc.in
    "${CMAKE_CURRENT_BINARY_DIR}/viewruntime-core.pc"
    @ONLY
)

install(
    FILES "${CMAKE_CURRENT_BINARY_DIR}/viewruntime-core.pc"
    DESTINATION "${CMAKE_INSTALL_LIBDIR}/pkgconfig"
)
