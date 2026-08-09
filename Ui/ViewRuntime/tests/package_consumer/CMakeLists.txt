cmake_minimum_required(VERSION 3.16)
project(ViewRuntimePackageConsumer LANGUAGES C)

find_package(ViewRuntime 0.1 CONFIG REQUIRED)

add_executable(package_consumer main.c)
target_link_libraries(package_consumer PRIVATE ViewRuntime::Core)

if(WIN32)
    add_custom_command(
        TARGET package_consumer
        POST_BUILD
        COMMAND "${CMAKE_COMMAND}" -E copy_if_different
                "$<TARGET_FILE:ViewRuntime::Core>"
                "$<TARGET_FILE_DIR:package_consumer>"
        VERBATIM
    )
endif()

enable_testing()
add_test(NAME viewruntime.package-consumer COMMAND package_consumer)
