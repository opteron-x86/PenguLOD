#pragma once
#include <array>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <fstream>
#include <iterator>
#include <string>
#include <vector>
// The file readers only need logging from SKSE; tests exercise their actual binary parser.
namespace logger {
    template<class... T> void error(T&&...) {}
    template<class... T> void info(T&&...) {}
}
