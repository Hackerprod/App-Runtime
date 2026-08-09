#pragma once
#include <viewruntime/viewruntime.h>
#include <vector>

/* Retained paint-command list. The list owns the heap storage of every
 * command it carries (strings, gradient stops, filter lists) and releases it
 * on destruction. */
struct display_list_s {
    std::vector<paint_command_t> commands;

    ~display_list_s() {
        for (auto& cmd : commands) {
            paint_command_free(&cmd);
        }
    }
};
