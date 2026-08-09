#include <viewruntime/viewruntime.h>

#include <stdio.h>

int main(void) {
    if (abi_version() != ABI_VERSION_CURRENT) {
        fputs("installed header and runtime ABI versions differ\n", stderr);
        return 1;
    }
    if (paint_command_size() != sizeof(paint_command_t)) {
        fputs("installed header and runtime command sizes differ\n", stderr);
        return 1;
    }

    puts("OK: installed ViewRuntime::Core package is consumable");
    return 0;
}
